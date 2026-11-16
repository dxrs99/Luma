using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopNotes;

/// <summary>维护卡片与桌面的所属关系，保留顶层绘制以支持原生背景材质。</summary>
public sealed class DesktopHost {
    private const int GwlExStyle = -20;
    private readonly IntPtr handle;
    private IntPtr parent;
    internal int GlassAccent { get; private set; } = -1;
    private int glassTint;

    /// <summary>绑定当前卡片的原生窗口句柄，供桌面挂载和物理像素布局使用。</summary>
    public DesktopHost(IntPtr handle) {
        this.handle = handle;
    }

    public bool IsAlive => IsWindow(handle);
    public bool IsAttached => parent != IntPtr.Zero && IsWindow(parent) && GetWindow(handle, 4) == parent;
    public bool IsTopmost => (GetWindowLongPtr(handle, GwlExStyle).ToInt64() & 0x00000008L) != 0;
    public IntPtr ParentHandle => parent;

    /// <summary>检查当前是否有其他应用窗口显示；排除 Luma、桌面、任务栏及已收起的窗口。</summary>
    public static bool HasVisibleApplication() {
        bool found = false;
        EnumWindows((window, _) => {
            if (!IsVisibleApplication(window)) { return true; }
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    internal static bool IsVisibleApplication(IntPtr window) {
        // 隐藏、最小化、其他虚拟桌面及本进程的小条不属于当前展示的外部应用。
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) { return false; }
        GetWindowThreadProcessId(window, out uint process);
        if (process == (uint)Environment.ProcessId) { return false; }
        if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) { return false; }
        long style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        if ((style & 0x00000080L) != 0) { return false; }
        if (GetWindow(window, 4) != IntPtr.Zero && (style & 0x00040000L) == 0) { return false; }

        // Explorer 的桌面和任务栏一直存在，不能据此把桌面模式误切成置顶。
        StringBuilder name = new(256);
        GetClassName(window, name, name.Capacity);
        string kind = name.ToString();
        if (kind == "Progman" || kind == "WorkerW" || kind == "Shell_TrayWnd" || kind == "Shell_SecondaryTrayWnd") { return false; }
        if (!GetWindowRect(window, out NativeRect bounds)) { return false; }
        System.Drawing.Rectangle rectangle = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        return rectangle.Width > 0 && rectangle.Height > 0 && rectangle.IntersectsWith(System.Windows.Forms.SystemInformation.VirtualScreen);
    }
    /// <summary>检查窗口中心是否实际露出，避免把被其他应用遮挡的桌面窗口误判为已经显示。</summary>
    public bool IsExposed {
        get {
            if (!IsAlive || !IsWindowVisible(handle) || IsIconic(handle)) { return false; }
            NativeRect bounds = Bounds;
            NativePoint point = new() { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 };
            return WindowFromPoint(point) == handle;
        }
    }

    /// <summary>找到承载桌面图标的 Explorer 窗口后挂载；失败时抛出原生错误。</summary>
    public void Attach() {
        if (IsAttached) { return; }
        IntPtr desktop = FindDesktopHost();
        if (desktop == IntPtr.Zero) { throw new InvalidOperationException("暂时找不到桌面宿主，请等待资源管理器恢复。"); }
        NativeRect bounds = Bounds;
        // 保留顶层窗口，使用桌面所属关系而非 WS_CHILD，让 DWM 毛玻璃在桌面模式也能工作。
        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(handle, -8, desktop);
        int error = Marshal.GetLastPInvokeError();
        if (GetWindow(handle, 4) != desktop) { throw new Win32Exception(error, "无法关联桌面窗口。"); }
        parent = desktop;
        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr((style & ~0x00040000L) | 0x00000080L));
        Move(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    /// <summary>以屏幕物理像素移动或缩放，转换成宿主客户区坐标后交给 Win32。</summary>
    public void Move(int left, int top, int width, int height) {
        NativePoint point = new() { X = left, Y = top };
        // 桌面模式仍保留顶层句柄，坐标始终为屏幕像素，移动不改变窗口层级。
        // 位置变化不重建非客户区、不重复显示窗口；原来的 FRAMECHANGED 会在每个鼠标事件重算边框。
        uint flags = 0x0010 | 0x0004 | 0x0200;
        NativeRect bounds = Bounds;
        if (bounds.Width == width && bounds.Height == height) { flags |= 0x0001; }
        if (!SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, width, height, flags)) {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>解除桌面父子关系，恢复独立工具窗口以支持真正置顶。</summary>
    public void Detach() {
        if (parent == IntPtr.Zero) { return; }
        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(handle, -8, IntPtr.Zero);
        int error = Marshal.GetLastPInvokeError();
        if (GetWindow(handle, 4) != IntPtr.Zero) { throw new Win32Exception(error, "无法解除桌面关联。"); }
        parent = IntPtr.Zero;
    }

    /// <summary>解除桌面挂载后，将原生窗口放到置顶层并显示。</summary>
    public void ShowTopmost() {
        // SWP_SHOWWINDOW 只显示窗口，不能解除最小化，必须先恢复再调整层级。
        if (IsIconic(handle)) { ShowWindow(handle, 9); }
        const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0040;
        if (!SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, flags)) {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法显示置顶窗口。");
        }
    }

    /// <summary>启用 Windows 原生背景模糊；返回系统是否接受该效果。</summary>
    public bool ApplyGlass(bool enabled, int tint = 0, bool interactive = false) {
        // Win10 Acrylic 会在圆角外留下合成底色并拖慢移动，使用可正确裁切的普通毛玻璃。
        // Win11 静止时使用 Acrylic；交互期间同样使用普通模糊。
        int accent = 0;
        if (enabled) {
            accent = 3;
            if (!interactive && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { accent = 4; }
        }
        if (GlassAccent == accent && glassTint == tint) { return true; }
        // 为普通 WPF 窗口扩展透明客户区，避免只调整画刷 Alpha 却得到黑底。
        GlassMargins margins = new() { Left = enabled ? -1 : 0, Right = enabled ? -1 : 0, Top = enabled ? -1 : 0, Bottom = enabled ? -1 : 0 };
        DwmExtendFrameIntoClientArea(handle, ref margins);
        AccentPolicy policy = new() { Accent = accent, Flags = 2, Color = tint == 0 && enabled ? 0x01000000 : tint };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try {
            Marshal.StructureToPtr(policy, memory, false);
            CompositionData data = new() { Attribute = 19, Data = memory, Size = Marshal.SizeOf<AccentPolicy>() };
            bool applied = SetWindowCompositionAttribute(handle, ref data) != 0;
            if (applied) { GlassAccent = accent; glassTint = tint; }
            return applied;
        } finally {
            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>以物理像素设置四角圆形裁切，避免原生模糊背景在透明角落露出方角。</summary>
    public void RoundCorners(int radius) {
        NativeRect bounds = Bounds;
        IntPtr region = CreateRoundRectRgn(0, 0, bounds.Width, bounds.Height, radius * 2, radius * 2);
        if (region == IntPtr.Zero) { throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法创建窗口圆角区域。"); }
        // 成功后区域由系统接管；失败时由本方法释放，避免 GDI 句柄泄漏。
        if (SetWindowRgn(handle, region, true) == 0) {
            DeleteObject(region);
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "无法更新窗口圆角裁切。");
        }
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int Accent; public int Flags; public int Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GlassMargins { public int Left; public int Right; public int Top; public int Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref GlassMargins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr window, ref CompositionData data);

    public NativeRect Bounds {
        get {
            if (!GetWindowRect(handle, out NativeRect rectangle)) {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            return rectangle;
        }
    }

    /// <summary>获取拖动时的物理屏幕坐标，避免多显示器坐标与 WPF 单位混用。</summary>
    public static NativePoint CursorPosition() {
        GetPhysicalCursorPos(out NativePoint point);
        return point;
    }

    /// <summary>主动唤出桌面模式时呈现桌面；MinimizeAll 为幂等操作，避免切换桌面时反向恢复窗口。</summary>
    public static void ShowDesktop() {
        Type shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("无法连接 Windows 桌面。");
        object shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("无法打开 Windows 桌面。");
        try {
            // Windows Shell 提供的是 COM 自动化接口，dynamic 仅用于调用其公开的 MinimizeAll 方法。
            ((dynamic)shell).MinimizeAll();
        } finally {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>检查当前父窗口属于 Explorer，供首次运行验证桌面挂载结果。</summary>
    public string ParentClass() {
        StringBuilder name = new(256);
        GetClassName(parent, name, name.Capacity);
        return name.ToString();
    }

    private static IntPtr FindDesktopHost() {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) => {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) {
                return true;
            }
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
}
