using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopNotes;

/// <summary>桌面窗口的模式切换、手动唤出与安全保存入口。</summary>
public sealed partial class MainWindow : Window {
    private readonly AppSettings settings;
    private readonly string configDirectory;
    private readonly Action exitApplication;
    private readonly TodoLibrary todoLibrary;
    private readonly NovelStorage novelStorage;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private DesktopHost? desktopHost;
    private IntPtr handle;
    private bool transitioning;
    private bool modalOpen;
    private bool editing;
    private bool dirty;
    private string page = "todos";
    private TodoEntry? selectedTodo;
    private TodoEditor? todoEditor;
    private bool hotkeyRegistered;
    private string lastWindowError = "";
    public bool WasClosed { get; private set; }
    public bool AllowExit { get; set; }
    public bool IsEmbedded => desktopHost?.IsAttached == true;
    public string DesktopParentClass => desktopHost?.ParentClass() ?? "";

    /// <summary>以本地配置初始化抽屉；旧笔记保留在原路径，不读取日记。</summary>
    public MainWindow(AppSettings settings, string configDirectory, Action exitApplication) {
        this.settings = settings;
        this.configDirectory = configDirectory;
        this.exitApplication = exitApplication;
        todoLibrary = new TodoLibrary(settings);
        novelStorage = new NovelStorage(settings.NotesDirectory);
        BuildView();
        MarkdownView.ReportError = message => ShowNotice("链接无法打开", message);
        Width = 440;
        Height = 580;
        Loaded += (_, _) => InitializeDrawer();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveCurrent(); };
        appearanceSaveTimer.Tick += (_, _) => { appearanceSaveTimer.Stop(); SaveSettings(); };
        resizeTimer.Tick += (_, _) => { resizeTimer.Stop(); SaveBounds(); Repaginate(); };
        SizeChanged += (_, _) => { UpdateUiScale(); UpdateRoundedClip(); resizeTimer.Stop(); resizeTimer.Start(); };
        PreviewKeyDown += HandleKey;
        Closing += CloseDrawer;
        Closed += (_, _) => DisposeDrawer();
    }

    private void InitializeDrawer() {
        // Hide/Show 可能再次触发 Loaded，已有窗口不能重复注册快捷键、重建定时器或覆盖隐藏前的位置。
        if (desktopHost != null) { return; }
        handle = new WindowInteropHelper(this).Handle;
        desktopHost = new DesktopHost(handle);
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is HwndTarget target) { target.BackgroundColor = Colors.Transparent; }
        hotkeyRegistered = RegisterConfiguredHotkey();
        if (settings.DrawerVersion == 0) {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            settings.Width = (int)(440 * dpi.DpiScaleX);
            settings.Height = (int)(580 * dpi.DpiScaleY);
            settings.DrawerVersion = 1;
            settings.Left = -1;
        }
        EnsureAttached();
        PositionDrawer(settings.Left == -1);
        UpdateUiScale();
        UpdateRoundedClip();
        ShowTodos();
        SaveSettings();
        InitializeEdgeHide();
        if (!hotkeyRegistered) {
            SetStatus("Ctrl+Shift+T 被其他程序占用；可从系统托盘唤出。");
        }
    }

    /// <summary>按当前模式恢复窗口层级；返回是否成功，失败时由切换入口保留原模式。</summary>
    public bool EnsureAttached(bool revealTopmost = false) {
        if (desktopHost == null || !desktopHost.IsAlive) {
            return false;
        }
        try {
            if (settings.AlwaysOnTop) {
                desktopHost.Detach();
                Topmost = true;
                if (IsVisible && (revealTopmost || !desktopHost.IsTopmost)) {
                    desktopHost.ShowTopmost();
                }
            } else {
                Topmost = false;
                desktopHost.Attach();
            }
            ApplyWindowMaterial();
            lastWindowError = "";
            return true;
        } catch (Exception exception) {
            string message = "窗口模式切换失败：" + exception.Message;
            if (message != lastWindowError) {
                SetStatus(message);
                lastWindowError = message;
            }
            return false;
        }
    }

    /// <summary>从托盘显示当前卡片并进入设置页，保留当前编辑内容。</summary>
    public void ShowSettings() {
        RevealDrawer(true);
        OpenSettings();
    }

    private Forms.Screen SelectedScreen() {
        foreach (Forms.Screen screen in Forms.Screen.AllScreens) {
            if (screen.DeviceName == settings.ScreenDevice) {
                return screen;
            }
        }
        return Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    }

    private void PositionDrawer(bool snap) {
        if (desktopHost == null) {
            return;
        }
        System.Drawing.Rectangle work = SelectedScreen().WorkingArea;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Min(Math.Max(settings.Width, (int)(280 * dpi.DpiScaleX)), work.Width);
        int height = Math.Min(Math.Max(settings.Height, (int)(260 * dpi.DpiScaleY)), work.Height);
        int left = Math.Clamp(settings.Left, work.Left, work.Right - width);
        int top = Math.Clamp(settings.Top, work.Top, work.Bottom - height);
        if (snap) {
            left = settings.LeftEdge ? work.Left : work.Right - width;
            top = work.Top + (work.Height - height) / 2;
        }
        desktopHost.Move(left, top, width, height);
    }

    /// <summary>显示抽屉；主动打开时呈现桌面层；窗口仅在用户明确关闭或按快捷键时隐藏。</summary>
    public void RevealDrawer(bool explicitRequest = false) {
        if (edgeHidden) { RestoreFromEdge(explicitRequest); return; }
        if (transitioning) {
            return;
        }
        if (!IsVisible) {
            Show();
        }
        EnsureAttached(revealTopmost: explicitRequest);
        // 托盘与快捷键的主动唤出也要恢复被 Windows 收起的窗口。
        if (WindowState == WindowState.Minimized) {
            WindowState = WindowState.Normal;
        }
        PositionDrawer(settings.Left == -1);
        UpdateRoundedClip();
        if (explicitRequest && !settings.AlwaysOnTop) {
            DesktopHost.ShowDesktop();
        }

        AnimateDrawer(false);
    }

    private void HideDrawer() {
        if (dockEdge.Length > 0 && settings.EdgeAutoHideEnabled) { HideAtEdge(); return; }
        if (transitioning || !SaveCurrent()) {
            return;
        }
        Keyboard.ClearFocus();
        AnimateDrawer(true);
    }

    private void AnimateDrawer(bool hide) {
        transitioning = true;
        TranslateTransform translation = new();
        root.RenderTransform = translation;
        double distance = settings.LeftEdge ? -ActualWidth : ActualWidth;
        DoubleAnimation animation = new(hide ? 0 : distance, hide ? distance : 0, TimeSpan.FromMilliseconds(210)) {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => {
            if (hide) {
                Hide();
            }
            translation.BeginAnimation(TranslateTransform.XProperty, null);
            root.RenderTransform = Transform.Identity;
            transitioning = false;
        };
        translation.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
        // 无边框玻璃客户区仍接收鼠标，移动与缩放统一由窗口内的 Thumb 处理。
        if (message == 0x0084) { handled = true; return new IntPtr(1); }
        if (message == 0x0312 && wParam.ToInt32() == 91) {
            // 桌面窗口仍为 Visible 时也可能被应用遮挡；这时快捷键应唤出，而不是先把它隐藏。
            if (IsVisible && desktopHost?.IsExposed == true) { HideDrawer(); } else { RevealDrawer(true); }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void HandleKey(object sender, KeyEventArgs args) {
        if (args.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && editing) {
            SaveTodoExplicitly();
            args.Handled = true;
            return;
        }
        if (page != "reader" || modalOpen || Keyboard.Modifiers != ModifierKeys.None) { return; }
        if (Keyboard.FocusedElement is TextBox input && !input.IsReadOnly) { return; }
        if (HandleReaderNavigation(args.Key)) { args.Handled = true; return; }
        if (!settings.ScrollReading) {
            if (args.Key == Key.Right || args.Key == Key.Space || args.Key == Key.PageDown) {
                TurnPage(1);
                args.Handled = true;
            } else if (args.Key == Key.Left || args.Key == Key.PageUp) {
                TurnPage(-1);
                args.Handled = true;
            }
        }
    }

    private void SaveSettings() {
        try {
            settings.Save(configDirectory);
        } catch (Exception exception) {
            SetStatus("设置未保存：" + exception.Message);
        }
    }

    private void SaveBounds() {
        if (desktopHost == null || transitioning || !IsVisible || edgeHidden || edgeAnimating) {
            return;
        }
        DesktopHost.NativeRect bounds = desktopHost.Bounds;
        settings.Left = bounds.Left;
        settings.Top = bounds.Top;
        settings.Width = bounds.Width;
        settings.Height = bounds.Height;
        SaveSettings();
    }

    private void CloseDrawer(object? sender, CancelEventArgs args) {
        if (!AllowExit) {
            args.Cancel = true;
            HideDrawer();
        }
    }

    private void DisposeDrawer() {
        WasClosed = true;
        if (appearanceSaveTimer.IsEnabled) { SaveSettings(); }
        appearanceSaveTimer.Stop();
        saveTimer.Stop();
        resizeTimer.Stop();
        progressTimer.Stop();
        DisposeEdgeHide();
        StopScreenSample();
        if (hotkeyRegistered) {
            UnregisterHotKey(handle, 91);
        }
    }

    /// <summary>仅导出应用自身画面，不截取用户桌面其他内容。</summary>
    public void CapturePreview(string path) {
        RenderTargetBitmap bitmap = new((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
