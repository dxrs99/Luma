using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private Grid root = null!;
    private Grid shell = null!;
    private Grid contentArea = null!;
    private Grid headerHost = null!;
    private Border surface = null!;
    private TextBlock statusText = null!;
    private Button desktopButton = null!;
    private Button topButton = null!;
    private Button todosButton = null!;
    private Button booksButton = null!;
    private Button pinButton = null!;
    private Thumb moveGrip = null!;
    private bool switchingWindowMode;
    private double uiScale = 1;
    private bool windowManipulating;
    private DesktopHost.NativePoint manipulationPointer;
    private DesktopHost.NativeRect manipulationBounds;
    private Brush Ink => (Brush)Resources["Ink"];
    private Brush Muted => (Brush)Resources["Muted"];
    private Brush Accent => (Brush)Resources["Accent"];
    private Brush Line => (Brush)Resources["Line"];

    private void BuildView() {
        Title = "Luma";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // 桌面挂接采用普通 WPF 窗口，透明效果交给已有原生毛玻璃，避免分层窗口只剩背景而正文未绘制。
        AllowsTransparency = false;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 14;
        MinWidth = 260;
        MinHeight = 260;
        UseLayoutRounding = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Styles.xaml") });
        Resources[SystemParameters.VerticalScrollBarWidthKey] = 6.0;
        ApplyTheme();
        root = new Grid();
        surface = new Border { CornerRadius = new CornerRadius(24), BorderThickness = new Thickness(1), Padding = new Thickness(22, 14, 22, 14) };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition());
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        surface.Child = shell;
        root.Children.Add(surface);
        Content = root;
        AddHeader();
        contentArea = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        Grid.SetRow(contentArea, 1);
        shell.Children.Add(contentArea);
        statusText = Text("保存在本机", 10, true);
        statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        statusText.Margin = new Thickness(2, 12, 0, 0);
        Grid.SetRow(statusText, 2);
        shell.Children.Add(statusText);
        AddResizeGrip(HorizontalAlignment.Left, VerticalAlignment.Stretch, 5, double.NaN, -1, 0);
        AddResizeGrip(HorizontalAlignment.Right, VerticalAlignment.Stretch, 5, double.NaN, 1, 0);
        AddResizeGrip(HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 5, 0, -1);
        AddResizeGrip(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 5, 0, 1);
        AddResizeGrip(HorizontalAlignment.Left, VerticalAlignment.Top, 24, 24, -1, -1);
        AddResizeGrip(HorizontalAlignment.Right, VerticalAlignment.Top, 24, 24, 1, -1);
        AddResizeGrip(HorizontalAlignment.Left, VerticalAlignment.Bottom, 24, 24, -1, 1);
        AddResizeGrip(HorizontalAlignment.Right, VerticalAlignment.Bottom, 24, 24, 1, 1);
        root.MouseMove += UpdateReaderChromeFromPointer;
        root.MouseLeave += (_, _) => SetReaderChrome(false);
    }
    private void AddHeader() {
        headerHost = new Grid();
        headerHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
        headerHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerHost.ColumnDefinitions.Add(new ColumnDefinition());
        headerHost.ColumnDefinitions.Add(new ColumnDefinition());
        pinButton = IconButton("\uE785", ToggleLock, "锁定位置和大小");
        pinButton.Width = pinButton.Height = pinButton.MinHeight = 23;
        pinButton.Margin = new Thickness(0, 0, 8, 0);
        pinButton.VerticalAlignment = VerticalAlignment.Top;
        pinButton.HorizontalAlignment = HorizontalAlignment.Left;
        StackPanel modes = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        desktopButton = Button("桌面", DesktopMode);
        topButton = Button("置顶", TopMode);
        desktopButton.Padding = topButton.Padding = new Thickness(8, 6, 8, 6);
        desktopButton.Width = topButton.Width = 48;
        desktopButton.Height = topButton.Height = 36;
        desktopButton.Margin = new Thickness(0, 0, 4, 0);
        modes.Children.Add(desktopButton);
        modes.Children.Add(topButton);
        Border segmented = new() { Child = modes, CornerRadius = new CornerRadius(12), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Top };
        segmented.SetResourceReference(Border.BackgroundProperty, "SwitchOff");
        // 两个等宽半区各自居中，窗口与 UI 缩放时保持同一水平线。
        segmented.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(segmented, 1);
        headerHost.Children.Add(segmented);
        todosButton = Button("待办", ShowTodos);
        booksButton = Button("小说", ShowBooks);
        todosButton.Padding = booksButton.Padding = new Thickness(8, 6, 8, 6);
        todosButton.Width = booksButton.Width = 48;
        todosButton.Height = booksButton.Height = 36;
        booksButton.Margin = new Thickness(0, 0, 4, 0);
        StackPanel pages = new() { Orientation = Orientation.Horizontal };
        pages.Children.Add(booksButton);
        pages.Children.Add(todosButton);
        Border pageSwitch = new() { Child = pages, CornerRadius = new CornerRadius(12), Padding = new Thickness(3), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center };
        pageSwitch.SetResourceReference(Border.BackgroundProperty, "SwitchOff");
        Grid.SetColumn(pageSwitch, 1);
        Grid.SetRow(pageSwitch, 1);
        headerHost.Children.Add(pageSwitch);
        // 独立的整行拖动条不再与按钮争抢宽度，窄窗口仍有足够的命中区域。
        moveGrip = TransparentThumb();
        moveGrip.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Grid Background='Transparent'><Border Width='40' Height='4' CornerRadius='2' Background='{DynamicResource Muted}' Opacity='0.45' HorizontalAlignment='Center' VerticalAlignment='Center'/></Grid></ControlTemplate>");
        System.Windows.Automation.AutomationProperties.SetName(moveGrip, "拖动移动窗口");
        moveGrip.PreviewMouseLeftButtonDown += MoveWithSystem;
        Grid.SetColumnSpan(moveGrip, 2);
        headerHost.Children.Add(moveGrip);
        headerHost.Children.Add(pinButton);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(IconButton("\uE710", BeginTodo, "添加待办"));
        ((Button)actions.Children[0]).Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(IconButton("\uE713", OpenSettings, "设置"));
        foreach (Button action in actions.Children) {
            action.Width = action.Height = action.MinHeight = 23;
        }
        Grid.SetColumn(actions, 1);
        headerHost.Children.Add(actions);
        shell.Children.Add(headerHost);
        UpdateModeButtons();
    }
    private void DesktopMode() {
        if (switchingWindowMode) {
            return;
        }
        switchingWindowMode = true;
        try { SetWindowMode(false); } catch (Exception exception) {
            switchingWindowMode = false;
            SetWindowMode(true);
            SetStatus("无法显示桌面：" + exception.Message);
            return;
        }
        // 在 WPF 界面线程等待 Explorer 完成切换，避免从后台线程操作窗口。
        DispatcherTimer check = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(220) };
        check.Tick += (_, _) => {
            check.Stop();
            switchingWindowMode = false;
            if (!WasClosed && !settings.AlwaysOnTop && IsVisible && desktopHost?.IsExposed == false) {
                SetWindowMode(true);
                SetStatus("其他窗口仍遮住桌面，已恢复置顶。");
            }
        };
        check.Start();
    }
    private void TopMode() { SetWindowMode(true); }
    private void SetWindowMode(bool top) {
        // 显示桌面后窗口可能仍处于最小化，先恢复 WPF 状态再切父窗口，避免保存屏幕外坐标。
        if (WindowState == WindowState.Minimized) {
            WindowState = WindowState.Normal;
        }
        bool previousMode = settings.AlwaysOnTop;
        settings.AlwaysOnTop = top;
        // 只有原生窗口层级实际切换成功才保存设置，避免界面标记与窗口行为不一致。
        if (!EnsureAttached(revealTopmost: top)) {
            settings.AlwaysOnTop = previousMode;
            EnsureAttached();
            UpdateModeButtons();
            return;
        }
        UpdateModeButtons();
        UpdateRoundedClip();
        // 切入桌面层后，原先的普通窗口会遮住面板；本次主动切换同时呈现桌面。
        if (top) {
            Activate();
        } else {
            DesktopHost.ShowDesktop();
        }
        SaveBounds();
    }
    private void ToggleLock() {
        settings.LayoutLocked = !settings.LayoutLocked;
        UpdateModeButtons();
        SaveSettings();
        SetStatus(settings.LayoutLocked ? "位置和大小已锁定，点击左上角锁头解锁。" : "已解锁，可拖动顶部横条移动窗口。");
    }
    private void UpdateModeButtons() {
        desktopButton.Background = settings.AlwaysOnTop ? Brushes.Transparent : (Brush)Resources["Card"];
        topButton.Background = settings.AlwaysOnTop ? (Brush)Resources["Card"] : Brushes.Transparent;
        desktopButton.Foreground = settings.AlwaysOnTop ? Muted : Ink;
        topButton.Foreground = settings.AlwaysOnTop ? Ink : Muted;
        desktopButton.FontWeight = settings.AlwaysOnTop ? FontWeights.Normal : FontWeights.SemiBold;
        topButton.FontWeight = settings.AlwaysOnTop ? FontWeights.SemiBold : FontWeights.Normal;
        bool novels = page == "books" || page == "reader" || page == "chapters" || page == "search" || page == "reader-settings";
        todosButton.Background = novels ? Brushes.Transparent : (Brush)Resources["Card"];
        booksButton.Background = novels ? (Brush)Resources["Card"] : Brushes.Transparent;
        todosButton.Foreground = novels ? Muted : Ink;
        booksButton.Foreground = novels ? Ink : Muted;
        todosButton.FontWeight = novels ? FontWeights.Normal : FontWeights.SemiBold;
        booksButton.FontWeight = novels ? FontWeights.SemiBold : FontWeights.Normal;
        pinButton.Background = settings.LayoutLocked ? (Brush)Resources["AccentSoft"] : Brushes.Transparent;
        pinButton.Foreground = settings.LayoutLocked ? Accent : Muted;
        // 使用同一组矢量锁头表达开锁和闭锁，避免字体图标的形状与基线不一致。
        string shackle = settings.LayoutLocked ? "M 5,8 L 5,5 A 3,3 0 0 1 11,5 L 11,8" : "M 5,8 L 5,5 A 3,3 0 0 1 11,5";
        pinButton.Content = new System.Windows.Shapes.Path {
            Data = Geometry.Parse(shackle + " M 4,8 L 12,8 Q 13,8 13,9 L 13,14 Q 13,15 12,15 L 4,15 Q 3,15 3,14 L 3,9 Q 3,8 4,8 M 8,10.5 L 8,12.5"),
            Stroke = pinButton.Foreground, StrokeThickness = 1.4, Width = 16, Height = 18,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        string lockHint = settings.LayoutLocked ? "已锁定位置和大小 · 点击解锁" : "可移动和调整大小 · 点击锁定";
        pinButton.ToolTip = lockHint;
        System.Windows.Automation.AutomationProperties.SetName(pinButton, lockHint);
        moveGrip.Cursor = settings.LayoutLocked ? Cursors.Arrow : Cursors.SizeAll;
        moveGrip.Opacity = settings.LayoutLocked ? 0.25 : 0.65;
        moveGrip.ToolTip = settings.LayoutLocked ? "位置和大小已锁定，请点击左侧锁头解锁" : "按住横条移动窗口 · 拖动边缘或四角调整大小";
        // 锁定时边缘不再显示可缩放反馈，避免光标提示与实际行为相反。
        foreach (UIElement child in root.Children) {
            if (child is Thumb grip && grip.Tag is Point) { grip.IsHitTestVisible = !settings.LayoutLocked; }
        }
        surface.CornerRadius = new CornerRadius(24);
    }
    private void MoveWithSystem(object sender, MouseButtonEventArgs args) {
        if (settings.LayoutLocked || edgeAnimating || modalOpen || args.LeftButton != MouseButtonState.Pressed) { return; }
        args.Handled = true;
        // DragMove 进入 Windows 原生移动循环，避免由 WPF 鼠标事件逐帧追赶窗口位置。
        BeginWindowManipulation();
        try { DragMove(); }
        finally { FinishDrag(); }
    }
    private void BeginWindowManipulation() {
        if (settings.LayoutLocked || desktopHost == null || edgeAnimating) { return; }
        windowManipulating = true;
        manipulationPointer = DesktopHost.CursorPosition();
        manipulationBounds = desktopHost.Bounds;
        edgeOutsideSince = null;
        ApplyWindowMaterial();
    }
    private void FinishDrag() {
        windowManipulating = false;
        ApplyWindowMaterial();
        SaveBounds();
        UpdateRoundedClip();
        UpdateDockEdge();
    }
    private static Thumb TransparentThumb() {
        Thumb thumb = new() { Background = Brushes.Transparent };
        thumb.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>");
        return thumb;
    }
    private void AddResizeGrip(HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height, int x, int y) {
        Thumb grip = TransparentThumb();
        grip.HorizontalAlignment = horizontal;
        grip.VerticalAlignment = vertical;
        grip.Width = width;
        grip.Height = height;
        grip.Tag = new Point(x, y);
        grip.IsHitTestVisible = !settings.LayoutLocked;
        if (x == 0) {
            grip.Cursor = Cursors.SizeNS;
        } else if (y == 0) {
            grip.Cursor = Cursors.SizeWE;
        } else {
            grip.Cursor = x == y ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }
        grip.DragStarted += (_, _) => BeginWindowManipulation();
        grip.DragDelta += (_, args) => {
            if (!windowManipulating || settings.LayoutLocked || desktopHost == null) { return; }
            DesktopHost.NativeRect bounds = manipulationBounds;
            DesktopHost.NativePoint pointer = DesktopHost.CursorPosition();
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int nextWidth = Math.Max((int)Math.Ceiling(MinWidth * dpi.DpiScaleX), bounds.Width + (pointer.X - manipulationPointer.X) * x);
            int nextHeight = Math.Max((int)Math.Ceiling(MinHeight * dpi.DpiScaleY), bounds.Height + (pointer.Y - manipulationPointer.Y) * y);
            int left = x < 0 ? bounds.Right - nextWidth : bounds.Left;
            int top = y < 0 ? bounds.Bottom - nextHeight : bounds.Top;
            desktopHost.Move(left, top, nextWidth, nextHeight);
        };
        grip.DragCompleted += (_, _) => FinishDrag();
        root.Children.Add(grip);
    }
    private void SetStatus(string message) {
        if (statusText == null) { return; }
        statusText.Text = message;
        statusText.ToolTip = message;
    }
    private void Run(Action action) {
        try { action(); } catch (Exception exception) { SetStatus(exception.Message); ShowNotice("操作未完成", exception.Message); }
    }
    private bool Navigate(string next) {
        if (!SaveCurrent()) { return false; }
        SaveReadingPosition();
        editing = false;
        todoEditor = null;
        page = next;
        UpdateModeButtons();
        Keyboard.ClearFocus();
        contentArea.Children.Clear();
        // 阅读页保留同一个紧凑拖动条与锁定按钮，仅收起两组切换和快捷操作。
        bool reading = next == "reader";
        headerHost.Visibility = next == "edit" ? Visibility.Collapsed : Visibility.Visible;
        headerHost.RowDefinitions[1].Height = reading ? new GridLength(0) : GridLength.Auto;
        foreach (UIElement element in headerHost.Children) {
            element.Visibility = reading && element != moveGrip && element != pinButton ? Visibility.Collapsed : Visibility.Visible;
        }
        statusText.Visibility = next == "reader" ? Visibility.Collapsed : Visibility.Visible;
        contentArea.Margin = next == "reader" ? new Thickness(0) : new Thickness(0, 18, 0, 0);
        return true;
    }
    private StackPanel Page(string title, Action? back = null) {
        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        if (!string.IsNullOrWhiteSpace(title)) {
            DockPanel heading = new() { LastChildFill = true, Margin = new Thickness(0, 0, 0, 18) };
            if (back != null) { heading.Children.Add(IconButton("\uE72B", back, "返回")); }
            TextBlock label = Text(title, 22);
            label.FontWeight = FontWeights.SemiBold;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(back == null ? 0 : 8, 0, 0, 0);
            heading.Children.Add(label);
            layout.Children.Add(heading);
        }
        StackPanel body = new();
        ScrollViewer scroll = new() { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        contentArea.Children.Add(layout);
        return body;
    }
    private TextBlock Text(string value, double size = 14, bool muted = false) {
        TextBlock text = new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink");
        return text;
    }
    private Button Button(string label, Action action) {
        Button button = new() { Content = label, FontSize = 13, MinHeight = 36 };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Run(action);
        return button;
    }
    private Button PrimaryButton(string label, Action action) {
        Button button = Button(label, action);
        button.Style = (Style)Resources["PrimaryButton"];
        return button;
    }
    private Button IconButton(string glyph, Action action, string name) {
        Button button = Button(glyph, action);
        button.Style = (Style)Resources["QuietButton"];
        button.FontFamily = new FontFamily("Segoe MDL2 Assets");
        button.FontSize = 16;
        button.Width = 34;
        button.Height = 36;
        button.Padding = new Thickness(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
    private TextBox Input(string value, string name) {
        TextBox input = new() { Text = value, FontSize = 14, Tag = name };
        System.Windows.Automation.AutomationProperties.SetName(input, name);
        return input;
    }
    private Border Card(UIElement child, Thickness? padding = null) {
        Border card = new() { Child = child, CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(16), Margin = new Thickness(0, 0, 0, 10) };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        return card;
    }
    private static Brush ColorBrush(string value, string fallback = "#171717") {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); } catch (FormatException) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    private void UpdateUiScale() {
        if (surface == null || ActualWidth < 1 || ActualHeight < 1) { return; }
        // 所有内容共用同一缩放系数，字号、间距与控件同步缩放。
        uiScale = Math.Clamp(Math.Min(ActualWidth / 480.0, ActualHeight / 640.0), 0.4, 3) * settings.UiSize * 1.5;
        surface.Width = ActualWidth / uiScale;
        surface.Height = ActualHeight / uiScale;
        surface.HorizontalAlignment = HorizontalAlignment.Left;
        surface.VerticalAlignment = VerticalAlignment.Top;
        surface.LayoutTransform = new ScaleTransform(uiScale, uiScale);
        if (inlineOverlay != null) {
            inlineOverlay.Width = ActualWidth / uiScale;
            inlineOverlay.Height = ActualHeight / uiScale;
            inlineOverlay.LayoutTransform = new ScaleTransform(uiScale, uiScale);
        }
    }
}
