using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopNotes;

/// <summary>隐藏后的屏幕边缘小条，仅用于恢复主窗口。</summary>
internal sealed class EdgeTabWindow : Window {
    /// <summary>创建恢复按钮，restore 为主窗口恢复操作，accent 为当前主题色。</summary>
    public EdgeTabWindow(Action restore, Color accent) {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = 12;
        Height = 72;
        Button button = new() { Background = new SolidColorBrush(accent), BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "显示 Luma" };
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='6'/></ControlTemplate>");
        System.Windows.Automation.AutomationProperties.SetName(button, "显示 Luma");
        button.Click += (_, _) => restore();
        Content = button;
    }

    /// <summary>按屏幕物理像素放置小条，四个参数为位置与尺寸。</summary>
    public void Place(int left, int top, int width, int height) {
        if (!IsVisible) { Show(); }
        IntPtr handle = new WindowInteropHelper(this).Handle;
        // AllowsTransparency 的 WPF 窗口拿不到 ShowInTaskbar=false 对应的隐藏 owner，
        // 会以可见置顶顶层窗口的身份留在 Alt+Tab；这里显式补上工具窗口样式并清除应用窗口标记。
        long exStyle = GetWindowLongPtr(handle, -20).ToInt64();
        if ((exStyle & 0x00000080L) == 0) {
            SetWindowLongPtr(handle, -20, new IntPtr((exStyle & ~0x00040000L) | 0x00000080L));
        }
        SetWindowPos(handle, new IntPtr(-1), left, top, width, height, 0x0010 | 0x0020 | 0x0040);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
