using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopNotes;

/// <summary>验收仅操作 tests 下的隔离数据，截图只包含测试窗口。</summary>
internal static class FeatureVerification {
    internal static int RunScreenColorTarget() {
        // 独立进程作为实际外部应用，颜色固定，标题显示收到的点击数。
        System.Drawing.Rectangle work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        using var form = new System.Windows.Forms.Form {
            Text = "__LUMA_TEST__ COLOR TARGET 0", TopMost = true, ShowInTaskbar = true,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(work.Right - 360, work.Top + 120),
            ClientSize = new System.Drawing.Size(280, 220),
            BackColor = System.Drawing.Color.FromArgb(35, 106, 183)
        };
        int clicks = 0;
        form.MouseDown += (_, _) => { clicks++; form.Text = "__LUMA_TEST__ COLOR TARGET " + clicks; };
        using var timer = new System.Windows.Forms.Timer { Interval = 45000 };
        timer.Tick += (_, _) => form.Close();
        timer.Start();
        System.Windows.Forms.Application.Run(form);
        return 0;
    }

    internal static int VerifyTrayIcon() {
        // 使用与托盘相同的内嵌资源和系统尺寸，检查实际加载后的 Alpha，而非只检查源 PNG。
        using Stream stream = typeof(Program).Assembly.GetManifestResourceStream("Luma.TrayIcon")
            ?? throw new IOException("缺少托盘图标资源。");
        using System.Drawing.Icon icon = new(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        using System.Drawing.Bitmap bitmap = icon.ToBitmap();
        Assert(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0
            && bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0, "托盘图标四角完全透明");
        Assert(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A > 240, "图标主体保留");
        Console.WriteLine($"PASS: 内嵌托盘图标 {bitmap.Width}x{bitmap.Height}，外侧透明，主体保留");
        return 0;
    }

    internal static int CheckAll(string projectDirectory, string verification = "--acceptance-test") {
        Console.OutputEncoding = Encoding.UTF8;
        string directory = Path.Combine(projectDirectory, "tests", "acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        DesktopHost.NativePoint cursor = DesktopHost.CursorPosition();
        Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try {
            AppSettings settings = AppSettings.Load(Path.Combine(directory, "config"), Path.Combine(directory, "notes"));
            settings.EdgeAutoHideEnabled = false;
            settings.AlwaysOnTop = true;
            settings.Width = 720;
            settings.Height = 900;
            settings.Left = 300;
            settings.Top = 120;
            settings.DrawerVersion = 1;
            window = new MainWindow(settings, Path.Combine(directory, "config"), () => { });
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            switch (verification) {
                case "--native-core-test": window.VerifyNativeRenderAndDelay(); break;
                case "--edge-hide-test": window.VerifyAutomaticEdges(); break;
                case "--edge-restore-test": window.VerifyEdgeRestoreMode(); break;
                case "--reader-drag-test": window.VerifyReaderDrag(directory, projectDirectory); break;
                case "--todo-interaction-test": window.VerifyTodoInteractions(directory, projectDirectory); break;
                case "--rounded-corners-test": window.VerifyRoundedCorners(projectDirectory); break;
                case "--screen-picker-test": window.VerifyExternalScreenPicker(); break;
                case "--inline-image-test": window.VerifyInlineImages(directory, projectDirectory); break;
                case "--window-input-test": window.VerifyWindowInput(projectDirectory); break;
                case "--ui-layout-test": window.VerifyUiLayout(projectDirectory); break;
                case "--reader-tools-test": window.VerifyReaderTools(directory); break;
                default: window.VerifyProduct(directory, projectDirectory); break;
            }
            return 0;
        } catch (Exception error) {
            window?.CapturePreview(Path.Combine(projectDirectory, "assets", "acceptance-failure.png"));
            Console.Error.WriteLine(error);
            return 1;
        } finally {
            if (window != null) { window.AllowExit = true; window.Close(); }
            application.Shutdown();
            SetCursorPos(cursor.X, cursor.Y);
            string tests = Path.GetFullPath(Path.Combine(projectDirectory, "tests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(directory).StartsWith(tests, StringComparison.OrdinalIgnoreCase)) {
                Directory.Delete(directory, true);
                Console.WriteLine("已删除隔离技术验证目录：" + directory);
            }
        }
    }

    internal static void Assert(bool condition, string description) {
        if (!condition) { throw new InvalidOperationException("验收失败：" + description); }
    }
    internal delegate bool WindowVisitor(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowVisitor visitor, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SetPhysicalCursorPos")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(DesktopHost.NativePoint point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out DesktopHost.NativeRect bounds);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

public sealed partial class MainWindow {
    internal void VerifyTodoInteractions(string directory, string projectDirectory) {
        byte[] pixels = Enumerable.Repeat((byte)180, 640 * 1280 * 4).ToArray();
        BitmapSource bitmap = BitmapSource.Create(640, 1280, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4);
        string image = ImageStore.SaveClipboard(settings.NotesDirectory, bitmap);
        string body = "说明正文\n- [ ] 第一项\n- [ ] 第二项\n\n![图片](" + image + ")\n"
            + string.Concat(Enumerable.Repeat("这是用于滚轮验证的长正文。\n", 60));
        TodoEntry entry = todoLibrary.Create("__LUMA_TEST__交互", body);
        VerifyTodoCheckboxes(entry.Path);
        VerifyTodoImageInteractions(projectDirectory, ImageStore.Resolve(settings.NotesDirectory, image));
        ToggleLock();
        PumpVerification(100);
        FeatureVerification.Assert(settings.LayoutLocked && moveGrip.Cursor == Cursors.Arrow && pinButton.Content is System.Windows.Shapes.Path, "锁定时矢量闭锁图标和普通指针");
        FeatureVerification.Assert(root.Children.OfType<System.Windows.Controls.Primitives.Thumb>().All(item => !item.IsHitTestVisible), "锁定后边角不提示可调整大小");
        CapturePreview(Path.Combine(projectDirectory, "assets", "luma-locked.png"));
        ToggleLock();
        PumpVerification(100);
        FeatureVerification.Assert(!settings.LayoutLocked && moveGrip.Cursor == Cursors.SizeAll, "解锁后恢复拖动提示");
        CapturePreview(Path.Combine(projectDirectory, "assets", "luma-unlocked.png"));
        VerifyEditableCheckboxes();
        Console.WriteLine("PASS: 子待办独立勾选/撤销、详情/编辑保存、原图系统查看器、正文/编辑滚轮滚动");
    }

    private void VerifyTodoCheckboxes(string path) {
        OpenTodo(path);
        PumpVerification(300);
        RichTextBox document = FindVisual<RichTextBox>(contentArea, item => item.IsReadOnly)!;
        FeatureVerification.Assert(document.IsDocumentEnabled, "详情正文中的复选框与图片允许交互");
        CheckBox first = FindVisual<CheckBox>(document, item => System.Windows.Automation.AutomationProperties.GetName(item) == "第一项")!;
        CheckBox second = FindVisual<CheckBox>(document, item => System.Windows.Automation.AutomationProperties.GetName(item) == "第二项")!;
        ToggleVerificationCheckbox(first);
        FeatureVerification.Assert(TodoLibrary.Read(path).Done == 1 && second.IsChecked == false, "第一项完成不影响第二项");
        ToggleVerificationCheckbox(second);
        ToggleVerificationCheckbox(first);
        FeatureVerification.Assert(TodoLibrary.Read(path).Done == 1 && first.IsChecked == false && second.IsChecked == true, "可自由勾选第二项并撤销第一项");
        ScrollViewer scroll = ((Grid)contentArea.Children[0]).Children.OfType<ScrollViewer>().Single();
        scroll.ScrollToVerticalOffset(90);
        PumpVerification(100);
        double offset = scroll.VerticalOffset;
        ToggleVerificationCheckbox(first);
        FeatureVerification.Assert(TodoLibrary.Read(path).Done == 2 && !TodoLibrary.Read(path).Completed, "子项独立完成，父待办不被自动改变");
        FeatureVerification.Assert(Math.Abs(scroll.VerticalOffset - offset) < 1, "勾选后保留当前滚动位置");
        Console.WriteLine("PASS: 详情实际复选框操作、独立状态、撤销和浏览位置保留");
    }

    private void VerifyTodoImageInteractions(string projectDirectory, string imagePath) {
        RichTextBox document = FindVisual<RichTextBox>(contentArea, item => item.IsReadOnly)!;
        Border picture = (Border)document.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Single(item => item.Child is Border).Child;
        ScrollViewer pageScroll = ((Grid)contentArea.Children[0]).Children.OfType<ScrollViewer>().Single();
        double offset = pageScroll.VerticalOffset;
        picture.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        PumpVerification(100);
        FeatureVerification.Assert(pageScroll.VerticalOffset > offset, "鼠标位于详情图片上时滚轮仍滚动正文");
        pageScroll.ScrollToVerticalOffset(picture.TranslatePoint(new Point(), pageScroll).Y + pageScroll.VerticalOffset - 12);
        PumpVerification(100);
        OpenTodoImage(imagePath);
        FeatureVerification.Assert(inlineOverlay == null, "系统查看器调用不创建 Luma 图片浮层");
        IntPtr viewer = IntPtr.Zero;
        string filename = Path.GetFileName(imagePath);
        // 根据本次唯一的图片文件名确认真正打开的外部窗口，不把进程启动无异常当成验收通过。
        for (int attempt = 0; attempt < 60 && viewer == IntPtr.Zero; attempt++) {
            FeatureVerification.EnumWindows((candidate, _) => {
                StringBuilder title = new(512);
                FeatureVerification.GetWindowText(candidate, title, title.Capacity);
                FeatureVerification.GetWindowThreadProcessId(candidate, out uint process);
                if (process != Environment.ProcessId && FeatureVerification.IsWindowVisible(candidate) && title.ToString().Contains(filename, StringComparison.OrdinalIgnoreCase)) {
                    viewer = candidate;
                    Console.WriteLine("系统图片查看器：" + title);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            PumpVerification(200);
        }
        FeatureVerification.Assert(viewer != IntPtr.Zero, "系统查看器实际显示本次原图：" + filename);
        // 只关闭显示本次隔离图片的窗口，不影响用户其他图片窗口。
        FeatureVerification.PostMessage(viewer, 0x0010, IntPtr.Zero, IntPtr.Zero);
        PumpVerification(500);
        Console.WriteLine("PASS: 打开原始图片的系统关联应用、正文滚轮滚动");
    }

    private void VerifyEditableCheckboxes() {
        EditTodo();
        PumpVerification(150);
        FeatureVerification.Assert(FindVisual<Button>(contentArea, item => item.Content is string label && (label == "图片" || label == "粘贴图片")) == null, "编辑页不再显示图片按钮");
        var paragraphs = todoEditor!.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        var checks = paragraphs.Select(item => item.Inlines.FirstInline).OfType<System.Windows.Documents.InlineUIContainer>().Select(item => item.Child).OfType<CheckBox>().ToList();
        FeatureVerification.Assert(checks.Count == 2 && checks.All(item => item.IsEnabled), "编辑页直接显示可勾选的子待办");
        ToggleVerificationCheckbox(checks[0]);
        FeatureVerification.Assert(dirty && SaveCurrent() && selectedTodo!.Done == 1, "编辑页勾选更新正文并保存");
        string markdown = todoEditor.Markdown;
        todoEditor.Markdown = markdown;
        FeatureVerification.Assert(todoEditor.Markdown == markdown, "复选框与图片的 Markdown 往返不丢失正文");
        todoEditor.ScrollToHome();
        PumpVerification(100);
        todoEditor.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        PumpVerification(100);
        FeatureVerification.Assert(todoEditor.VerticalOffset > 0, "编辑正文可以滚轮上下滚动");
        todoEditor.CaretPosition = todoEditor.Document.ContentEnd;
        InsertChecklist();
        FeatureVerification.Assert(todoEditor.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Last().Inlines.FirstInline is System.Windows.Documents.InlineUIContainer, "添加子待办按钮立即创建复选框");
        FeatureVerification.Assert(SaveCurrent(), "新增复选框后仍能保存");
        IDataObject? previousClipboard = Clipboard.GetDataObject();
        try {
            int before = todoEditor.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Count();
            Clipboard.SetImage(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8));
            PasteClipboardImage();
            FeatureVerification.Assert(todoEditor.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Count() == before + 1 && SaveCurrent(), "剪贴板图片直接显示并能保存，无需图片按钮");
        } finally {
            if (previousClipboard == null) { Clipboard.Clear(); } else { Clipboard.SetDataObject(previousClipboard, true); }
        }
        Console.WriteLine("PASS: 编辑页真实复选框、草稿保存、Markdown 兼容、滚轮与新增子待办");
    }

    private void ToggleVerificationCheckbox(CheckBox checkbox) {
        FeatureVerification.Assert(checkbox.IsEnabled, "复选框启用且可操作");
        var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(checkbox);
        var toggle = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
        toggle.Toggle();
        PumpVerification(100);
    }

    internal void VerifyReaderDrag(string directory, string projectDirectory) {
        ShowTodos();
        VerifyHeaderWidths(settings.UiSize);
        FeatureVerification.Assert(headerHost.RowDefinitions[0].Height.Value == 23 && Math.Abs(moveGrip.ActualHeight - 23) < 1, "顶部拖动区高度从 46 减为 23");
        FeatureVerification.Assert(pinButton.ActualHeight < 24 && pinButton.Margin.Top == 0, "锁定按钮完整放入紧凑顶栏");
        desktopHost!.Move(300, 350, 720, 900);
        string source = Path.Combine(directory, "__LUMA_TEST__reader-drag.txt");
        File.WriteAllText(source, "第一章 阅读拖动测试\n" + string.Concat(Enumerable.Repeat("正文仅用于隔离验证，拖动窗口不应触发翻页或改变阅读进度。\n", 200)));
        NovelBook book = novelStorage.Import(source);
        foreach (bool scroll in new[] { true, false }) {
            settings.ScrollReading = scroll;
            OpenBook(book);
            PumpVerification(250);
            FeatureVerification.Assert(moveGrip.IsVisible && pinButton.IsVisible && Math.Abs(headerHost.ActualHeight - 23) < 1, "阅读页常驻紧凑拖动条和锁定按钮");
            FeatureVerification.Assert(!desktopButton.IsVisible && !booksButton.IsVisible, "阅读页不展开重复的模块切换");
            int offset = book.CharacterOffset;
            int chapter = book.ChapterIndex;
            DragWithNativeInput(60, 30);
            FeatureVerification.Assert(book.CharacterOffset == offset && book.ChapterIndex == chapter, "拖动阅读窗口不改变进度或章节");
            Press(pinButton);
            FeatureVerification.Assert(settings.LayoutLocked && moveGrip.Cursor == Cursors.Arrow, "阅读页可以锁定窗口位置");
            Press(pinButton);
            FeatureVerification.Assert(!settings.LayoutLocked, "阅读页可以解除锁定");
        }
        SetReaderChrome(false);
        PumpVerification(200);
        CapturePreview(Path.Combine(projectDirectory, "assets", "luma-reader-drag.png"));
        ShowBooks();
        PumpVerification(150);
        FeatureVerification.Assert(desktopButton.IsVisible && booksButton.IsVisible && headerHost.RowDefinitions[1].Height.IsAuto, "返回书架后恢复两组切换布局");
        Console.WriteLine("PASS: 拖动区减半、三档窗口宽度对齐、滚动/分页阅读页真实鼠标拖动、进度保留、锁定与书架返回");
    }

    internal void VerifyEdgeRestoreMode() {
        using var external = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--screen-color-target") { UseShellExecute = false })!;
        try {
            for (int attempt = 0; attempt < 40 && external.MainWindowHandle == IntPtr.Zero; attempt++) {
                PumpVerification(100);
                external.Refresh();
            }
            IntPtr target = external.MainWindowHandle;
            FeatureVerification.Assert(target != IntPtr.Zero && DesktopHost.IsVisibleApplication(target), "识别真实独立进程的可见应用窗口");
            FeatureVerification.Assert(!DesktopHost.IsVisibleApplication(handle) && !DesktopHost.IsVisibleApplication(IntPtr.Zero), "排除 Luma 自身和空窗口");

            PrepareEdgeRestore(false);
            FeatureVerification.Assert(!DesktopHost.IsVisibleApplication(desktopHost!.ParentHandle), "桌面宿主不计为外部应用");
            RestoreEdgeForVerification();
            PumpVerification(350);
            FeatureVerification.Assert(settings.AlwaysOnTop && Topmost && desktopHost.IsTopmost && !desktopHost.IsAttached, "有外部应用时小条恢复自动切为原生置顶");
            FeatureVerification.Assert(topButton.FontWeight == FontWeights.SemiBold && desktopButton.FontWeight == FontWeights.Normal, "顶部按钮同步为置顶模式");
            FeatureVerification.Assert(AppSettings.Load(configDirectory, settings.NotesDirectory).AlwaysOnTop, "置顶模式保存到设置");
            FeatureVerification.Assert(FeatureVerification.IsWindowVisible(target) && !FeatureVerification.IsIconic(target), "恢复时不会最小化外部应用");

            PrepareEdgeRestore(true);
            RestoreEdgeForVerification();
            PumpVerification(350);
            FeatureVerification.Assert(settings.AlwaysOnTop && desktopHost.IsTopmost, "原本置顶时恢复后保持置顶");
            FeatureVerification.ShowWindow(target, 6);
            PumpVerification(100);
            FeatureVerification.Assert(!DesktopHost.IsVisibleApplication(target), "最小化应用不计入当前展示的应用");
            FeatureVerification.ShowWindow(target, 0);
            FeatureVerification.Assert(!DesktopHost.IsVisibleApplication(target), "隐藏应用不计入当前展示的应用");

            PrepareEdgeRestore(false);
            RestoreFromEdge(false);
            PumpVerification(450);
            FeatureVerification.Assert(!settings.AlwaysOnTop && desktopHost.IsAttached, "非主动恢复保留桌面模式");
            Console.WriteLine("PASS: 小条恢复自动置顶、按钮与配置同步、保留外部应用、原置顶保持、桌面/隐藏/最小化过滤");
        } finally {
            if (!external.HasExited) {
                external.CloseMainWindow();
                if (!external.WaitForExit(2000)) { external.Kill(); }
            }
        }
    }

    private void PrepareEdgeRestore(bool top) {
        settings.EdgeAutoHideEnabled = false;
        settings.AlwaysOnTop = top;
        EnsureAttached(revealTopmost: top);
        UpdateModeButtons();
        System.Drawing.Rectangle work = SelectedScreen().WorkingArea;
        desktopHost!.Move(work.Right - 480, work.Top + 100, 480, 500);
        settings.EdgeAutoHideEnabled = true;
        UpdateDockEdge();
        HideAtEdge();
        PumpVerification(450);
        FeatureVerification.Assert(edgeHidden && !edgeAnimating && edgeTab?.IsVisible == true, "待恢复的边缘小条已显示");
        settings.EdgeAutoHideEnabled = false;
    }

    internal void VerifyRoundedCorners(string projectDirectory) {
        settings.EdgeAutoHideEnabled = false;
        settings.NightMode = true;
        settings.BackgroundOpacity = 0.7;
        settings.NightBackground = "#1D1C28";
        settings.AlwaysOnTop = true;
        EnsureAttached(true);
        desktopHost!.Move(300, 180, 600, 650);
        PumpVerification(200);
        DesktopHost.NativeRect bounds = desktopHost.Bounds;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Color backdrop = Color.FromRgb(35, 106, 183);
        Window sample = new() {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true,
            Left = (bounds.Left - 40) / dpi.DpiScaleX, Top = (bounds.Top - 40) / dpi.DpiScaleY,
            Width = (bounds.Width + 80) / dpi.DpiScaleX, Height = (bounds.Height + 80) / dpi.DpiScaleY,
            Background = new SolidColorBrush(backdrop)
        };
        try {
            sample.Show();
            ApplyTheme();
            UpdateRoundedClip();
            desktopHost.ShowTopmost();
            Activate();
            PumpVerification(450);
            EnsureAttached(true);
            PumpVerification(100);
            Color center = ReadScreenColor(new DesktopHost.NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height / 2 });
            FeatureVerification.Assert(desktopHost.IsExposed, $"测试窗口实际显示在纯色背景上：center={center}, topmost={desktopHost.IsTopmost}, bounds={bounds.Left},{bounds.Top},{bounds.Width},{bounds.Height}, dpi={dpi.DpiScaleX}");
            for (int size = 0; size < 3; size++) {
                ChangeUiSize(size);
                EnsureAttached(true);
                PumpVerification(200);
                FeatureVerification.Assert(desktopHost.IsExposed, "UI 档位切换后测试卡片仍可见");
                Color measuredBackdrop = ReadScreenColor(new DesktopHost.NativePoint { X = bounds.Left - 25, Y = bounds.Top - 25 });
                Console.WriteLine($"UI={settings.UiSize}, backdrop={measuredBackdrop}, native={desktopHost.GlassAccent}");
                int radius = (int)Math.Round(24 * uiScale * dpi.DpiScaleX);
                foreach (int offset in new[] { 2, radius / 5 }) {
                    VerifyCornerPixels(bounds, offset, measuredBackdrop);
                }
            }
            SaveScreen(Path.Combine(projectDirectory, "assets", "luma-rounded-corners.png"));
            Console.WriteLine("PASS: 三档 UI 大小、四角内外实际像素检查，无方角底色");
        } finally { sample.Close(); }
    }

    private void VerifyCornerPixels(DesktopHost.NativeRect bounds, int offset, Color backdrop) {
        foreach (Point corner in new[] { new Point(bounds.Left + offset, bounds.Top + offset), new Point(bounds.Right - offset - 1, bounds.Top + offset), new Point(bounds.Left + offset, bounds.Bottom - offset - 1), new Point(bounds.Right - offset - 1, bounds.Bottom - offset - 1) }) {
            Color actual = ReadScreenColor(new DesktopHost.NativePoint { X = (int)corner.X, Y = (int)corner.Y });
            int difference = Math.Max(Math.Abs(actual.R - backdrop.R), Math.Max(Math.Abs(actual.G - backdrop.G), Math.Abs(actual.B - backdrop.B)));
            FeatureVerification.Assert(difference <= 2, $"圆角外侧不残留底色：point={corner}, actual={actual}, backdrop={backdrop}, difference={difference}");
        }
    }

    private static int RestingGlassAccent => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? 4 : 3;

    internal void VerifyNativeRenderAndDelay() {
        // 材质与边缘等待不依赖物理鼠标点击，锁屏期间也能验证自身窗口状态。
        settings.BackgroundOpacity = 0.7;
        ApplyTheme();
        BeginWindowManipulation();
        FeatureVerification.Assert(desktopHost!.GlassAccent == 3, "交互开始切换为普通模糊");
        EnsureAttached();
        FeatureVerification.Assert(desktopHost.GlassAccent == 3, "窗口保活不覆盖交互材质");
        FinishDrag();
        FeatureVerification.Assert(desktopHost.GlassAccent == RestingGlassAccent, "交互结束恢复系统兼容的毛玻璃");

        System.Drawing.Rectangle work = SelectedScreen().WorkingArea;
        DesktopHost.NativePoint pointer = DesktopHost.CursorPosition();
        int left = pointer.X < work.Left + work.Width / 2 ? work.Right - 480 : work.Left;
        desktopHost.Move(left, work.Top + 100, 480, 500);
        settings.EdgeAutoHideEnabled = true;
        edgeOutsideSince = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        PumpVerification(200);
        FeatureVerification.Assert(!edgeHidden, "0.3 秒之前保持显示");
        while (!edgeHidden && watch.ElapsedMilliseconds < 650) { PumpVerification(10); }
        FeatureVerification.Assert(edgeHidden && watch.ElapsedMilliseconds >= 290 && watch.ElapsedMilliseconds < 550, "0.3 秒计时触发隐藏");
        Console.WriteLine($"PASS: 交互材质切换/恢复，贴边隐藏在 {watch.ElapsedMilliseconds} ms 触发");
        PumpVerification(450);
        FeatureVerification.Assert(!IsVisible && edgeTab?.IsVisible == true, "隐藏后显示恢复小条");
        settings.EdgeAutoHideEnabled = false;
        RestoreFromEdge(false);
        PumpVerification(450);
        FeatureVerification.Assert(IsVisible && !edgeAnimating && desktopHost.GlassAccent == RestingGlassAccent,
            $"小条恢复后回到 Acrylic：visible={IsVisible}, animating={edgeAnimating}, hidden={edgeHidden}, accent={desktopHost.GlassAccent}");
        Console.WriteLine("PASS: 小条显示、窗口恢复、材质恢复");
    }

    internal void VerifyExternalScreenPicker() {
        VerifyInlineColor();
        using var external = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--screen-color-target") { UseShellExecute = false })!;
        try {
            for (int attempt = 0; attempt < 40 && external.MainWindowHandle == IntPtr.Zero; attempt++) {
                PumpVerification(100);
                external.Refresh();
            }
            IntPtr target = external.MainWindowHandle;
            FeatureVerification.Assert(target != IntPtr.Zero, "独立外部取色窗口启动");
            string picked = "";
            foreach (bool top in new[] { true, false }) {
                SetWindowMode(top);
                PickColor("#FFFFFF", value => picked = value);
                Press(FindButton("吸管 · 取屏幕颜色"));
                FeatureVerification.ShowWindow(target, 9);
                FeatureVerification.SetForegroundWindow(target);
                PumpVerification(250);
                FeatureVerification.GetWindowRect(target, out DesktopHost.NativeRect bounds);
                FeatureVerification.SetCursorPos(bounds.Left + 70, bounds.Top + 80);
                PumpVerification(80);
                FeatureVerification.Assert(ReadScreenColor(DesktopHost.CursorPosition()) == Color.FromRgb(35, 106, 183), "实际采样点位于外部应用客户区");
                ClickScreenForVerification();
                external.Refresh();
                FeatureVerification.Assert(picked == "#236AB7" && inlineOverlay == null, "外部点击直接应用并关闭颜色面板");
                FeatureVerification.Assert(screenSampleHook == IntPtr.Zero && colorSampleTimer == null, "选中颜色后立即释放全局监听");
                FeatureVerification.Assert(external.MainWindowTitle.EndsWith(" 0"), "取色点击没有传给外部应用");
                picked = "";
            }
            // 外部应用获得焦点时也能用 Esc 取消，并且不改变颜色。
            PickColor("#FFFFFF", value => picked = value);
            Press(FindButton("吸管 · 取屏幕颜色"));
            FeatureVerification.SetForegroundWindow(target);
            FeatureVerification.keybd_event(0x1B, 0, 0, UIntPtr.Zero);
            PumpVerification(120);
            FeatureVerification.keybd_event(0x1B, 0, 2, UIntPtr.Zero);
            PumpVerification(100);
            FeatureVerification.Assert(screenSampleHook == IntPtr.Zero && picked == "", "外部焦点下 Esc 取消且不应用");
            CloseOverlay();
            PickColor("#FFFFFF", value => picked = value);
            Press(FindButton("吸管 · 取屏幕颜色"));
            FeatureVerification.mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero);
            PumpVerification(30);
            FeatureVerification.mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
            PumpVerification(150);
            FeatureVerification.Assert(screenSampleHook == IntPtr.Zero && picked == "", "右键取消且不应用");
            CloseOverlay();
            ClickScreenForVerification();
            external.Refresh();
            FeatureVerification.Assert(external.MainWindowTitle.EndsWith(" 1"), "取色结束后其他应用恢复正常点击");
            Console.WriteLine("PASS: 独立进程外部取色、桌面/置顶模式、点击立即应用、无点击穿透、Esc/右键取消、监听释放");
        } finally {
            CloseOverlay();
            if (!external.HasExited) {
                external.CloseMainWindow();
                if (!external.WaitForExit(2000)) { external.Kill(); }
            }
        }
    }

    private void ClickScreenForVerification() {
        FeatureVerification.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        PumpVerification(30);
        FeatureVerification.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        PumpVerification(250);
    }

    internal void VerifyInlineImages(string directory, string projectDirectory) {
        VerifyUiLayout(projectDirectory);
        VerifyTodoFlow(directory);
        BeginTodo();
        todoTitle!.Text = "__LUMA_TEST__ 内联图片";
        string plain = "前面的正文\n\n- [ ] 子待办\n后面的正文\n";
        todoEditor!.Markdown = plain;
        FeatureVerification.Assert(todoEditor.Markdown == plain, "纯文字、空行、子待办和尾部换行往返保留");

        // 仅创建隔离的技术图片，验证导入后立即显示，而非只检查 Markdown 链接。
        byte[] pixels = new byte[640 * 360 * 4];
        for (int index = 0; index < pixels.Length; index += 4) {
            pixels[index] = 170;
            pixels[index + 1] = (byte)(100 + index / (640 * 4) % 100);
            pixels[index + 2] = 70;
            pixels[index + 3] = 255;
        }
        BitmapSource sample = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4);
        string source = ImageStore.Resolve(settings.NotesDirectory, ImageStore.SaveClipboard(settings.NotesDirectory, sample));
        todoEditor.CaretPosition = todoEditor.Document.ContentStart.GetPositionAtOffset(3)!;
        InsertTodoImage(source);
        InsertTodoImage(source);
        PumpVerification(300);
        var blocks = todoEditor.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().ToList();
        FeatureVerification.Assert(blocks.Count == 2, "两张图片在编辑器中直接以独立块显示");
        string remainingText = System.Text.RegularExpressions.Regex.Replace(todoEditor.Markdown, @"!\[[^\]]*\]\([^)]+\)", "").Replace("\n", "");
        FeatureVerification.Assert(remainingText == plain.Replace("\n", ""), "光标中间插图不丢失前后文字：" + todoEditor.Markdown);
        foreach (var block in blocks) {
            FrameworkElement element = (FrameworkElement)block.Child;
            FeatureVerification.Assert(element.ActualWidth > 0 && element.ActualWidth <= todoEditor.ActualWidth && element.ActualHeight > 0,
                $"编辑页图片自适应：image={element.ActualWidth:F1}x{element.ActualHeight:F1}, editor={todoEditor.ActualWidth:F1}");
        }
        string withImages = todoEditor.Markdown;
        string first = (string)((FrameworkElement)blocks[0].Child).Tag;
        string second = (string)((FrameworkElement)blocks[1].Child).Tag;
        todoEditor.Selection.Select(blocks[0].ElementStart, blocks[0].ElementEnd);
        todoEditor.Selection.Text = "";
        FeatureVerification.Assert(!todoEditor.Markdown.Contains(first) && todoEditor.Markdown.Contains(second), "删除一张图片不影响另一张");
        todoEditor.Markdown = withImages;
        todoEditor.ScrollToHome();
        PumpVerification(200);
        SaveScreen(Path.Combine(projectDirectory, "assets", "luma-image-editor.png"));
        SaveTodoExplicitly();
        PumpVerification(350);
        FeatureVerification.Assert(selectedTodo!.Body.Contains(first) && selectedTodo.Body.Contains(second), "保存仍使用本地 Markdown 附件引用");
        var viewer = FindVisual<RichTextBox>(contentArea, item => item.IsReadOnly)!;
        var savedBlocks = viewer.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Where(block => block.Child is Border).ToList();
        FeatureVerification.Assert(savedBlocks.Count == 2 && savedBlocks.All(block => ((FrameworkElement)block.Child).ActualHeight > 0), "详情页直接展示两张图片");
        SaveScreen(Path.Combine(projectDirectory, "assets", "luma-image-detail.png"));
        EditTodo();
        PumpVerification(250);
        FeatureVerification.Assert(todoEditor!.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Count() == 2, "重新编辑图片仍直接显示");
        settings.UiSize = 1.2;
        settings.NightMode = true;
        ApplyTheme();
        desktopHost!.Move(300, 120, 420, 600);
        PumpVerification(300);
        FeatureVerification.Assert(todoEditor.Document.Foreground == Ink, "编辑正文跟随夜间文字颜色");
        foreach (var block in todoEditor.Document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>()) {
            FrameworkElement element = (FrameworkElement)block.Child;
            FeatureVerification.Assert(element.ActualWidth > 0 && element.ActualWidth <= todoEditor.ActualWidth, "最大 UI 档位与窄窗口下图片仍自适应");
        }
        FeatureVerification.Assert(SaveCurrent(), "重新编辑可保存");
        Console.WriteLine("PASS: 编辑页直接插图、纵向自适应、删除单图、Markdown 往返、详情显示、再次编辑");
    }

    internal void VerifyWindowInput(string projectDirectory) {
        OpenSettings();
        FeatureVerification.Assert(FindVisual<Button>(surface, item => Equals(item.Content, "隐藏窗口")) == null, "移除设置中的隐藏窗口按钮");
        VerifyResponsiveAndMove(projectDirectory);
        VerifyPointerTracking();
        VerifyAutomaticEdges();
        Console.WriteLine("PASS: 设置入口精简、拖动跟手、八方向缩放、四边自动隐藏与恢复");
    }

    private void VerifyPointerTracking() {
        settings.LayoutLocked = false;
        foreach (double size in new[] { 0.8, 1.0, 1.2 }) {
            settings.UiSize = size;
            UpdateUiScale();
            desktopHost!.Move(300, 120, 720, 900);
            PumpVerification(120);
            DragWithNativeInput(96, 36);
        }
        settings.LayoutLocked = true;
        DesktopHost.NativeRect before = desktopHost!.Bounds;
        DragWithNativeInput(50, 30);
        FeatureVerification.Assert(desktopHost.Bounds.Left == before.Left && desktopHost.Bounds.Top == before.Top, "锁定后不移动");
        settings.LayoutLocked = false;
        settings.UiSize = 1;
        UpdateUiScale();
        Console.WriteLine("PASS: 三个 UI 档位的真实鼠标系统拖动、交互材质切换、松手恢复、锁定保护");
    }

    private void ExposeVerificationWindow() {
        WindowState = WindowState.Normal;
        Show();
        EnsureAttached(revealTopmost: true);
        Activate();
        PumpVerification(150);
        // 自动测试进程可能没有前台激活权限；临时连接前台输入线程，只切换测试窗口焦点。
        uint foregroundThread = FeatureVerification.GetWindowThreadProcessId(FeatureVerification.GetForegroundWindow(), out _);
        uint currentThread = FeatureVerification.GetCurrentThreadId();
        bool attached = foregroundThread != currentThread && FeatureVerification.AttachThreadInput(currentThread, foregroundThread, true);
        try { FeatureVerification.SetForegroundWindow(handle); }
        finally { if (attached) { FeatureVerification.AttachThreadInput(currentThread, foregroundThread, false); } }
        desktopHost!.ShowTopmost();
        PumpVerification(100);
    }

    private void DragWithNativeInput(int dx, int dy) {
        // 后台仅发送操作系统鼠标输入并读取 Win32 边界；主线程执行真正的 DragMove 消息循环。
        ExposeVerificationWindow();
        Point origin = moveGrip.PointToScreen(new Point(moveGrip.ActualWidth / 2, moveGrip.ActualHeight / 2));
        DesktopHost.NativeRect before = desktopHost!.Bounds;
        bool locked = settings.LayoutLocked;
        bool sawInteractiveBlur = false;
        int maximumDeviation = 0;
        int sampled = 0;
        int changedPointer = 0;
        IntPtr target = handle;
        var driver = System.Threading.Tasks.Task.Run(() => {
            // 工作线程显式使用物理像素上下文，防止 Win32 边界和命中检测被 DPI 虚拟化。
            IntPtr previousDpi = FeatureVerification.SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
            System.Threading.Thread.Sleep(80);
            FeatureVerification.SetCursorPos((int)origin.X, (int)origin.Y);
            System.Threading.Thread.Sleep(40);
            DesktopHost.NativePoint point = new() { X = (int)origin.X, Y = (int)origin.Y };
            IntPtr hit = FeatureVerification.WindowFromPoint(point);
            FeatureVerification.Assert(hit == target, $"鼠标按下前确认测试窗口可见，防止点击其他应用：target={target}, hit={hit}, visible={IsVisible}, point={point.X},{point.Y}");
            FeatureVerification.mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            try {
                System.Threading.Thread.Sleep(100);
                sawInteractiveBlur = desktopHost.GlassAccent == 3;
                for (int step = 1; step <= 12; step++) {
                    int x = dx * step / 12;
                    int y = dy * step / 12;
                    FeatureVerification.SetCursorPos((int)origin.X + x, (int)origin.Y + y);
                    System.Threading.Thread.Sleep(24);
                    DesktopHost.NativePoint actualPointer = DesktopHost.CursorPosition();
                    if (Math.Abs(actualPointer.X - (int)origin.X - x) > 1 || Math.Abs(actualPointer.Y - (int)origin.Y - y) > 1) {
                        changedPointer++;
                        continue;
                    }
                    sampled++;
                    FeatureVerification.GetWindowRect(target, out DesktopHost.NativeRect current);
                    int error = Math.Max(Math.Abs(current.Left - before.Left - x), Math.Abs(current.Top - before.Top - y));
                    maximumDeviation = Math.Max(maximumDeviation, error);
                }
            } finally { FeatureVerification.mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
            } finally { FeatureVerification.SetThreadDpiAwarenessContext(previousDpi); }
        });
        while (!driver.IsCompleted) { PumpVerification(40); }
        driver.GetAwaiter().GetResult();
        PumpVerification(100);
        DesktopHost.NativeRect after = desktopHost.Bounds;
        int expectedX = locked ? 0 : dx;
        int expectedY = locked ? 0 : dy;
        FeatureVerification.Assert(Math.Abs(after.Left - before.Left - expectedX) <= 6 && Math.Abs(after.Top - before.Top - expectedY) <= 6,
            $"实际鼠标拖动：locked={locked}, top={settings.AlwaysOnTop}, expected={expectedX},{expectedY}, actual={after.Left - before.Left},{after.Top - before.Top}, down={manipulationPointer.X},{manipulationPointer.Y}, origin={origin.X},{origin.Y}");
        FeatureVerification.Assert(locked || sawInteractiveBlur, "拖动期间使用普通模糊以避开 Win10 Acrylic 卡顿");
        FeatureVerification.Assert(!windowManipulating && desktopHost.GlassAccent == RestingGlassAccent, "松手后恢复系统兼容材质并结束拖动状态");
        if (!locked) {
            FeatureVerification.Assert(sampled >= 6 && maximumDeviation <= 24, $"系统拖动跟随：deviation={maximumDeviation}, sampled={sampled}, changedPointer={changedPointer}");
            Console.WriteLine($"实际拖动：UI={settings.UiSize}, top={settings.AlwaysOnTop}, {sampled} 次采样最大位置偏差 {maximumDeviation} px");
        }
    }

    internal void VerifyAutomaticEdges() {
        settings.EdgeAutoHideEnabled = false;
        SetWindowMode(true);
        System.Drawing.Rectangle work = SelectedScreen().WorkingArea;
        int width = 480;
        int height = 500;
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) {
            // 移动测试窗口到光标之外，不占用用户的鼠标，也不受自动点击后光标位置影响。
            DesktopHost.NativePoint pointer = DesktopHost.CursorPosition();
            int left = pointer.X < work.Left + work.Width / 2 ? work.Right - width - 100 : work.Left + 100;
            int top = pointer.Y < work.Top + work.Height / 2 ? work.Bottom - height - 100 : work.Top + 100;
            if (edge == "left") { left = work.Left - 5; }
            if (edge == "right") { left = work.Right - width + 5; }
            if (edge == "top") { top = work.Top - 5; }
            if (edge == "bottom") { top = work.Bottom - height + 5; }
            desktopHost!.Move(left, top, width, height);
            PumpVerification(100);
            settings.EdgeAutoHideEnabled = true;
            edgeOutsideSince = null;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            PumpVerification(200);
            FeatureVerification.Assert(!edgeHidden, "贴边等待未达到 0.3 秒时保持显示");
            while (!edgeHidden && wait.ElapsedMilliseconds < 650) { PumpVerification(10); }
            FeatureVerification.Assert(edgeHidden && wait.ElapsedMilliseconds < 550, "0.3 秒后开始隐藏：" + wait.ElapsedMilliseconds);
            Console.WriteLine($"贴边隐藏触发：{edge} {wait.ElapsedMilliseconds} ms");
            PumpVerification(250);
            FeatureVerification.Assert(edgeHidden && edgeTab?.IsVisible == true && !IsVisible, "自动检测并隐藏：" + edge);
            RestoreEdgeForVerification();
            PumpVerification(300);
            FeatureVerification.Assert(IsVisible && !edgeHidden && !edgeAnimating, "点击边缘小条恢复：" + edge);
        }
        DesktopHost.NativePoint finalPointer = DesktopHost.CursorPosition();
        int finalTop = finalPointer.Y < work.Top + work.Height / 2 ? work.Bottom - height - 100 : work.Top + 100;
        desktopHost!.Move(work.Right - width, finalTop, width, height);
        ShowNotice("__LUMA_TEST__", "贴边时弹层不触发隐藏");
        settings.EdgeAutoHideEnabled = true;
        PumpVerification(500);
        FeatureVerification.Assert(!edgeHidden, "弹层阻止自动隐藏");
        CloseOverlay();
        PumpVerification(800);
        FeatureVerification.Assert(edgeHidden, "弹层关闭后自动继续检测，无需再进出窗口");
        RestoreEdgeForVerification();
        PumpVerification(300);
        settings.EdgeAutoHideEnabled = false;
        PumpVerification(500);
        FeatureVerification.Assert(!edgeHidden && IsVisible, "关闭开关后不会贴边隐藏");
        desktopHost.Move(300, 120, 720, 900);
        Console.WriteLine("PASS: 四边轻微越界、移出延迟、鼠标停留保护、关闭弹层重试、开关与小条恢复");
    }

    private void RestoreEdgeForVerification() {
        // Invoke 不会将光标移到小条；恢复检查期间关闭自动隐藏，避免再次进入隐藏倒计时。
        settings.EdgeAutoHideEnabled = false;
        UpdateDockEdge();
        Button tab = (Button)edgeTab!.Content;
        Press(tab);
    }

    internal void VerifyUiLayout(string projectDirectory) {
        // 验证实际按钮事件、配置持久化和不同窗口大小下的布局，不写入用户配置。
        OpenSettings();
        string[] labels = { "小\n80%", "标准\n100%", "大\n120%" };
        double[] sizes = { 0.8, 1.0, 1.2 };
        for (int index = 0; index < labels.Length; index++) {
            Press(FindButton(labels[index]));
            AppSettings saved = AppSettings.Load(configDirectory, settings.NotesDirectory);
            FeatureVerification.Assert(saved.UiSize == sizes[index], "UI 档位点击后持久保存");
            VerifyHeaderWidths(sizes[index]);
        }
        Press(FindButton("标准\n100%"));
        ShowTodos();
        SaveScreen(Path.Combine(projectDirectory, "assets", "luma-ui-layout.png"));
        Console.WriteLine("PASS: 三档 UI 大小、配置保存、左右半区居中、窄/中/宽窗口无重叠");
    }

    private void VerifyHeaderWidths(double size) {
        foreach (int width in new[] { 420, 720, 1000 }) {
            desktopHost!.Move(300, 120, width, width + 180);
            PumpVerification(350);
            Border left = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(desktopButton));
            Border right = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(booksButton));
            Point leftStart = left.TranslatePoint(new Point(), headerHost);
            Point rightStart = right.TranslatePoint(new Point(), headerHost);
            FeatureVerification.Assert(Math.Abs(leftStart.X + left.ActualWidth / 2 - headerHost.ActualWidth / 4) < 1, "桌面/置顶在左半区居中");
            FeatureVerification.Assert(Math.Abs(rightStart.X + right.ActualWidth / 2 - headerHost.ActualWidth * 3 / 4) < 1, "小说/待办在右半区居中");
            FeatureVerification.Assert(Math.Abs(leftStart.Y - rightStart.Y) < 1 && leftStart.X + left.ActualWidth < rightStart.X, "两组同高且互不重叠");
            double expected = Math.Clamp(Math.Min(ActualWidth / 480, ActualHeight / 640), 0.4, 3) * size * 1.5;
            FeatureVerification.Assert(Math.Abs(uiScale - expected) < 0.001, "手动档位与窗口响应缩放共同生效");
            ShowNotice("布局验证", "检查弹层与正文使用同一缩放比例。");
            FeatureVerification.Assert(inlineOverlay!.LayoutTransform is ScaleTransform transform && Math.Abs(transform.ScaleX - uiScale) < 0.001, "内联弹层同步缩放");
            CloseOverlay();
        }
    }

    internal void VerifyReaderTools(string directory) {
        PumpVerification(250);
        VerifyStartupRegistration();
        string source = Path.Combine(directory, "__LUMA_TEST__reading-keys.txt");
        string paragraphs = string.Concat(Enumerable.Repeat("这段文字仅用于阅读按键验证，包含足够多的行。\n", 160));
        File.WriteAllText(source, "第一章 按键验证\n" + paragraphs + "第二章 边界验证\n" + paragraphs);
        NovelBook book = novelStorage.Import(source);
        settings.ScrollReading = true;
        settings.SmoothPaging = false;
        OpenBook(book);
        PumpVerification(300);
        scrollText!.Focus();
        FeatureVerification.Assert(RaiseReaderKey(Key.NumPad2) && scrollText.VerticalOffset > 0, "小键盘 2 在滚动模式向下阅读");
        double offset = scrollText.VerticalOffset;
        FeatureVerification.Assert(RaiseReaderKey(Key.NumPad8) && scrollText.VerticalOffset < offset, "小键盘 8 在滚动模式向上阅读");
        scrollText.ScrollToEnd();
        PumpVerification(100);
        RaiseReaderKey(Key.Down);
        FeatureVerification.Assert(activeBook!.ChapterIndex == 1 && scrollText!.VerticalOffset <= 1, "向下键在章末进入下一章");
        RaiseReaderKey(Key.Up);
        FeatureVerification.Assert(activeBook.ChapterIndex == 0 && scrollText!.VerticalOffset > 0, "向上键在章首回到上一章末尾");
        LoadChapter(0, 0);
        PumpVerification(100);
        RaiseReaderKey(Key.NumPad8);
        FeatureVerification.Assert(activeBook.ChapterIndex == 0 && scrollText!.VerticalOffset <= 1, "全书开头不越界");

        settings.ScrollReading = false;
        OpenBook(book);
        PumpVerification(250);
        FeatureVerification.Assert(pageOffsets.Count > 1, "技术样本足够分页");
        RaiseReaderKey(Key.NumPad2);
        FeatureVerification.Assert(pageIndex == 1, "小键盘 2 翻下一页");
        RaiseReaderKey(Key.Up);
        FeatureVerification.Assert(pageIndex == 0, "向上键翻上一页");
        ShowNotice("__LUMA_TEST__", "验证内联面板不触发翻页。");
        FeatureVerification.Assert(!RaiseReaderKey(Key.NumPad2) && pageIndex == 0, "弹层打开时不接管阅读按键");
        CloseOverlay();
        Console.WriteLine("PASS: 小键盘与方向键、滚动/分页模式、跨章边界、弹层不拦截");

        ShowBooks();
        Press(FindButton("删除小说 " + book.Title));
        FeatureVerification.Assert(inlineOverlay != null && Application.Current.Windows.Count == 1, "删除小说在窗口内确认");
        Press(FindButton("取消"));
        FeatureVerification.Assert(novelStorage.List().Any(item => item.Id == book.Id), "取消删除保留小说");
        string otherSource = Path.Combine(directory, "__LUMA_TEST__unrelated.txt");
        File.WriteAllText(otherSource, "另一本文本，用于确认删除范围。");
        NovelBook other = novelStorage.Import(otherSource);
        ShowBooks();
        Press(FindButton("删除小说 " + book.Title));
        Press(FindButton("确认删除"));
        PumpVerification(850);
        FeatureVerification.Assert(!novelStorage.List().Any(item => item.Id == book.Id), "删除选中小说及进度，定时器不会重新创建");
        FeatureVerification.Assert(activeBook == null && settings.LastBookId != book.Id, "清理被删小说的阅读引用");
        FeatureVerification.Assert(File.Exists(source) && File.Exists(otherSource) && novelStorage.List().Any(item => item.Id == other.Id), "原始文件和其他小说保留");
        Console.WriteLine("PASS: 书架删除按钮、取消/确认、进度引用清理、保留原文件与其他小说");
    }

    private void VerifyStartupRegistration() {
        string testKey = @"Software\Luma\Tests\" + Guid.NewGuid().ToString("N");
        string executable = Environment.ProcessPath!;
        try {
            StartupRegistration.SetEnabled(true, executable, testKey);
            FeatureVerification.Assert(StartupRegistration.IsEnabled(executable, testKey), "启动项启用与路径引号写入");
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey)!) {
                FeatureVerification.Assert(Equals(key.GetValue("Luma.DesktopNotes"), "\"" + executable + "\" --autostart"), "启动命令包含完整路径和启动参数");
            }
            StartupRegistration.SetEnabled(false, executable, testKey);
            FeatureVerification.Assert(!StartupRegistration.IsEnabled(executable, testKey), "关闭启动项");
            OpenSettings();
            PumpVerification(100);
            FeatureVerification.Assert(FindVisual<CheckBox>(root, item => Equals(item.Content, "开机自启动")) != null, "设置页提供自启动开关");
        } finally {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
            Console.WriteLine("已删除隔离注册表验证项：HKEY_CURRENT_USER\\" + testKey);
        }
        Console.WriteLine("PASS: 启动开关、隔离注册表中的启用/禁用与命令格式");
    }

    private bool RaiseReaderKey(Key key) {
        KeyEventArgs args = new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        RaiseEvent(args);
        PumpVerification(120);
        return args.Handled;
    }

    internal void VerifyProduct(string directory, string projectDirectory) {
        PumpVerification(500);
        VerifyTodoFlow(directory);
        VerifyImages(directory);
        VerifyInlineColor();
        VerifyResponsiveAndMove(projectDirectory);
        VerifyDesktopLayer();
        VerifyMaterialAndScreenPicker(projectDirectory);
        VerifyEdgeAndHotkey();
        SaveScreen(Path.Combine(projectDirectory, "assets", "luma-preview.png"));
    }

    private void VerifyTodoFlow(string directory) {
        BeginTodo();
        FeatureVerification.Assert(todoTitle != null && todoEditor != null, "标题与正文分开输入");
        todoTitle!.Text = "__LUMA_TEST__ 两级待办";
        todoEditor!.Markdown = "仅用于隔离验收。\n- [ ] 子任务甲\n- [x] 子任务乙";
        SaveTodoExplicitly();
        FeatureVerification.Assert(selectedTodo != null && selectedTodo.Total == 2 && selectedTodo.Done == 1, "父待办与两个子待办");
        TodoEntry entry = selectedTodo!;
        DateTimeOffset created = entry.CreatedAt;
        DateTimeOffset childCreated = entry.Children[0].CreatedAt;
        ToggleSubtask(1, true);
        FeatureVerification.Assert(selectedTodo!.Done == 2 && selectedTodo.CreatedAt == created, "勾选子任务保留添加时间");
        FeatureVerification.Assert(selectedTodo.Children[0].CreatedAt == childCreated, "子待办保留各自添加时间");
        EditTodo();
        todoTitle!.Text = "__LUMA_TEST__ 修改标题";
        todoEditor!.AppendText("\n追加正文");
        FeatureVerification.Assert(SaveCurrent(), "标题与正文自动保存");
        FeatureVerification.Assert(TodoLibrary.Read(entry.Path).CreatedAt == created, "修改不覆盖添加时间");
        ShowTodos();
        DeleteTodo(TodoLibrary.Read(entry.Path));
        FeatureVerification.Assert(inlineOverlay != null && Application.Current.Windows.Count == 1, "删除确认在原窗口内");
        Press(FindButton("取消"));
        FeatureVerification.Assert(File.Exists(entry.Path), "取消删除保留原文件");
        DeleteTodo(TodoLibrary.Read(entry.Path));
        Press(FindButton("确认删除"));
        FeatureVerification.Assert(!File.Exists(entry.Path), "确认后删除隔离待办");
        string legacy = Path.Combine(directory, "notes", "todos", "legacy.md");
        File.WriteAllText(legacy, "- [ ] 旧版技术样本\n");
        FeatureVerification.Assert(TodoLibrary.Read(legacy).Total == 1, "旧 Markdown 兼容");
        File.Delete(legacy);
        ShowTodos();
        Console.WriteLine("PASS: 标题/正文、父子待办、添加时间、取消与确认删除、旧数据兼容");
    }

    private void VerifyImages(string directory) {
        byte[] pixels = Enumerable.Repeat((byte)180, 32 * 24 * 4).ToArray();
        BitmapSource image = BitmapSource.Create(32, 24, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
        string link = ImageStore.SaveClipboard(settings.NotesDirectory, image);
        string path = ImageStore.Resolve(settings.NotesDirectory, link);
        FeatureVerification.Assert(File.Exists(path), "图片保存到本地附件");
        System.Windows.Documents.FlowDocument document = MarkdownView.Build("![a](" + link + ") ![b](" + link + ")", 15, (_, _) => { }, BuildTodoImage);
        int blocks = document.Blocks.OfType<System.Windows.Documents.BlockUIContainer>().Count();
        FeatureVerification.Assert(blocks == 2, "同一行两张图片仍分别占行");
        bool rejected = false;
        try { ImageStore.Resolve(settings.NotesDirectory, "../config/settings.json"); }
        catch (IOException) { rejected = true; }
        FeatureVerification.Assert(rejected, "拒绝附件路径越界");
        string novel = Path.Combine(directory, "very-long-test-book-name-for-single-line-ellipsis.txt");
        File.WriteAllText(novel, "第一章 验收\n这是隔离验证正文。\n第二章 验收\n第二章正文。");
        NovelBook book = novelStorage.Import(novel);
        ShowBooks();
        PumpVerification(200);
        Button row = FindButton(book.Title);
        Grid content = (Grid)row.Content;
        TextBlock title = content.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First();
        FeatureVerification.Assert(title.TextWrapping == TextWrapping.NoWrap && title.TextTrimming == TextTrimming.CharacterEllipsis, "小说名严格单行省略");
        ShowTodos();
        Console.WriteLine("PASS: 图片存储、纵向独占块、路径校验、小说单行省略");
    }

    private void VerifyInlineColor() {
        int count = Application.Current.Windows.Count;
        string selected = "";
        PickColor("#8AA675", value => selected = value);
        PumpVerification(250);
        FeatureVerification.Assert(inlineOverlay != null && Application.Current.Windows.Count == count, "取色未创建独立弹窗");
        TextBox hex = FindVisual<TextBox>(inlineOverlay!, item => System.Windows.Automation.AutomationProperties.GetName(item) == "十六进制颜色")!;
        hex.Text = "#236AB7";
        Press(FindButton("应用颜色"));
        FeatureVerification.Assert(selected == "#236AB7" && inlineOverlay == null, "内联色值应用与关闭");
        Console.WriteLine("PASS: 内联颜色选择与应用");
    }

    private void VerifyResponsiveAndMove(string projectDirectory) {
        ShowTodos();
        foreach (int width in new[] { 420, 720, 1000 }) {
            desktopHost!.Move(300, 120, width, width + 180);
            PumpVerification(350);
            FeatureVerification.Assert(surface.LayoutTransform is ScaleTransform transform && Math.Abs(transform.ScaleX - transform.ScaleY) < 0.001, "控件与字号等比例缩放");
            Point right = booksButton.PointToScreen(new Point(booksButton.ActualWidth, booksButton.ActualHeight));
            FeatureVerification.Assert(right.X <= desktopHost.Bounds.Right && right.Y < desktopHost.Bounds.Bottom, "缩放后顶部按钮完整");
        }
        desktopHost!.Move(300, 120, 720, 900);
        PumpVerification(300);
        foreach (bool top in new[] { true, false }) {
            // 只验证层级与拖动，不发送会收起用户其他应用的“显示桌面”命令。
            settings.AlwaysOnTop = top;
            EnsureAttached(revealTopmost: top);
            UpdateModeButtons();
            PumpVerification(350);
            DesktopHost.NativeRect before = desktopHost.Bounds;
            DragWithNativeInput(50, 30);
            DesktopHost.NativeRect after = desktopHost.Bounds;
            FeatureVerification.Assert(Math.Abs(after.Left - before.Left - 50) <= 6 && Math.Abs(after.Top - before.Top - 30) <= 6,
                $"顶部留白拖动窗口：mode={top}, before={before.Left},{before.Top}, after={after.Left},{after.Top}, locked={settings.LayoutLocked}");
        }
        SetWindowMode(true);
        foreach (Point direction in new[] { new Point(-1, -1), new Point(1, -1), new Point(-1, 1), new Point(1, 1), new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) }) {
            desktopHost.Move(300, 120, 720, 900);
            PumpVerification(150);
            System.Windows.Controls.Primitives.Thumb grip = root.Children.OfType<System.Windows.Controls.Primitives.Thumb>().Single(item => Equals(item.Tag, direction));
            DragGrip(grip, (int)direction.X * 20, (int)direction.Y * 20);
            DesktopHost.NativeRect after = desktopHost.Bounds;
            FeatureVerification.Assert(Math.Abs(after.Width - 720 - Math.Abs(direction.X) * 20) <= 2 && Math.Abs(after.Height - 900 - Math.Abs(direction.Y) * 20) <= 2, "八方向调整大小：" + direction);
        }
        desktopHost.Move(300, 120, 720, 900);
        PumpVerification(250);
        Console.WriteLine("PASS: 三档尺寸等比例布局、两种层级移动、八方向缩放");
    }

    private void VerifyEdgeAndHotkey() {
        settings.EdgeAutoHideEnabled = true;
        System.Drawing.Rectangle work = SelectedScreen().WorkingArea;
        DesktopHost.NativeRect before = desktopHost!.Bounds;
        desktopHost.Move(work.Right - before.Width, work.Top + 100, before.Width, before.Height);
        FeatureVerification.SetCursorPos(work.Left + 20, work.Top + 20);
        UpdateDockEdge();
        PumpVerification(1300);
        FeatureVerification.Assert(edgeHidden && !IsVisible && edgeTab?.IsVisible == true, "靠边自动滑出并显示小条");
        Press((Button)edgeTab!.Content);
        PumpVerification(450);
        FeatureVerification.Assert(!edgeHidden && IsVisible && desktopHost.IsExposed,
            $"点击小条恢复：hidden={edgeHidden}, visible={IsVisible}, nativeTop={desktopHost.IsTopmost}, exposed={desktopHost.IsExposed}, mode={settings.AlwaysOnTop}, state={WindowState}, sliding={edgeAnimating}, tab={edgeTab.IsVisible}, bounds={desktopHost.Bounds.Left},{desktopHost.Bounds.Top}");
        settings.EdgeAutoHideEnabled = false;
        UpdateDockEdge();
        desktopHost.Move(300, 120, 720, 900);
        UnregisterHotKey(handle, 91);
        settings.HotkeyModifiers = 3;
        settings.HotkeyKey = 0x77;
        hotkeyRegistered = RegisterConfiguredHotkey();
        FeatureVerification.Assert(hotkeyRegistered, "自定义快捷键注册成功");
        PressHotkey();
        PumpVerification(400);
        FeatureVerification.Assert(!IsVisible, "全局组合键隐藏窗口");
        PressHotkey();
        PumpVerification(450);
        FeatureVerification.Assert(IsVisible, "全局组合键恢复窗口");
        Console.WriteLine("PASS: 自动边缘隐藏、小条点击恢复、自定义全局快捷键");
    }

    private void VerifyMaterialAndScreenPicker(string projectDirectory) {
        DesktopHost.NativeRect bounds = desktopHost!.Bounds;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Window sample = new() {
            Title = "__LUMA_TEST__ SCREEN COLOR", WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Topmost = true,
            Left = (bounds.Left - 20) / dpi, Top = (bounds.Top - 20) / dpi,
            Width = (bounds.Width + 40) / dpi, Height = (bounds.Height + 40) / dpi,
            Background = new SolidColorBrush(Color.FromRgb(35, 106, 183))
        };
        try {
            sample.Show();
            SetWindowMode(true);
            settings.BackgroundOpacity = 0.25;
            ApplyTheme();
            PumpVerification(350);
            Color faint = ReadScreenColor(new DesktopHost.NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height * 3 / 4 });
            settings.BackgroundOpacity = 0.95;
            ApplyTheme();
            PumpVerification(350);
            Color solid = ReadScreenColor(new DesktopHost.NativePoint { X = bounds.Left + bounds.Width / 2, Y = bounds.Top + bounds.Height * 3 / 4 });
            FeatureVerification.Assert(solid.R > faint.R + 20 && solid.G > faint.G + 10, $"透明度改变实际背景混合：low={faint}, high={solid}");
            SaveScreen(Path.Combine(projectDirectory, "assets", "luma-material.png"));
            string picked = "";
            PickColor("#FFFFFF", value => picked = value);
            PumpVerification(250);
            Press(FindButton("吸管 · 取屏幕颜色"));
            Point location = new WindowInteropHelper(sample).Handle == IntPtr.Zero ? new Point(0, 0) : sample.PointToScreen(new Point(5, 5));
            FeatureVerification.SetCursorPos((int)location.X, (int)location.Y);
            PumpVerification(80);
            ClickScreenForVerification();
            FeatureVerification.Assert(picked == "#236AB7" && screenSampleHook == IntPtr.Zero, "吸管读取主窗口以外的屏幕颜色并释放监听");
        } finally {
            CloseOverlay();
            sample.Close();
            settings.BackgroundOpacity = 0.98;
            ApplyTheme();
        }
        Console.WriteLine("PASS: 真实屏幕透明度对照、窗口外吸管取色、输入捕获释放");
    }

    private void PressHotkey() {
        FeatureVerification.keybd_event(0x11, 0, 0, UIntPtr.Zero);
        FeatureVerification.keybd_event(0x12, 0, 0, UIntPtr.Zero);
        FeatureVerification.keybd_event(0x77, 0, 0, UIntPtr.Zero);
        FeatureVerification.keybd_event(0x77, 0, 2, UIntPtr.Zero);
        FeatureVerification.keybd_event(0x12, 0, 2, UIntPtr.Zero);
        FeatureVerification.keybd_event(0x11, 0, 2, UIntPtr.Zero);
    }

    private void VerifyDesktopLayer() {
        SetWindowMode(false);
        PumpVerification(400);
        FeatureVerification.Assert(IsEmbedded && !Topmost && !desktopHost!.IsTopmost,
            $"桌面模式保持普通层级：attached={IsEmbedded}, wpf={Topmost}, native={desktopHost!.IsTopmost}, status={statusText.Text}");
        DesktopHost.NativeRect bounds = desktopHost!.Bounds;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Window cover = new() { Title = "__LUMA_TEST__ COVER", Left = bounds.Left / dpi, Top = bounds.Top / dpi, Width = bounds.Width / dpi, Height = bounds.Height / dpi, Background = Brushes.SteelBlue };
        try {
            cover.Show();
            cover.Activate();
            PumpVerification(250);
            FeatureVerification.Assert(!desktopHost.IsExposed, "普通应用可以覆盖桌面模式");
        } finally { cover.Close(); }
        SetWindowMode(true);
        PumpVerification(250);
        FeatureVerification.Assert(desktopHost.IsTopmost && desktopHost.IsExposed, "置顶模式恢复可见");
        Console.WriteLine("PASS: 桌面与置顶层级、普通窗口遮挡与恢复");
    }

    private Button FindButton(string name) {
        UpdateLayout();
        PumpVerification(100);
        return FindVisual<Button>(root, item => System.Windows.Automation.AutomationProperties.GetName(item) == name || Equals(item.Content, name))
            ?? throw new InvalidOperationException("未找到按钮：" + name);
    }
    private static T? FindVisual<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject {
        Queue<DependencyObject> queue = new();
        queue.Enqueue(parent);
        while (queue.Count > 0) {
            DependencyObject item = queue.Dequeue();
            if (item is T found && match(found)) { return found; }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++) { queue.Enqueue(VisualTreeHelper.GetChild(item, index)); }
        }
        return null;
    }

    private void Press(Button button) {
        PumpVerification(120);
        System.Windows.Automation.Peers.ButtonAutomationPeer peer = new(button);
        System.Windows.Automation.Provider.IInvokeProvider invoke = (System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
        invoke.Invoke();
        PumpVerification(250);
    }
    private void DragGrip(System.Windows.Controls.Primitives.Thumb grip, int dx, int dy) {
        // 使用 Thumb 的正式路由事件，避免桌面宿主被其他正在使用的窗口遮挡时抢走测试输入。
        DesktopHost.NativePoint origin = DesktopHost.CursorPosition();
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
        FeatureVerification.SetCursorPos(origin.X + dx, origin.Y + dy);
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(dx, dy, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
        PumpVerification(250);
    }
    internal void SaveScreen(string path) {
        DesktopHost.NativeRect bounds = desktopHost!.Bounds;
        using System.Drawing.Bitmap bitmap = new(bounds.Width, bounds.Height);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path);
    }
    private void PumpVerification(int milliseconds) {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
