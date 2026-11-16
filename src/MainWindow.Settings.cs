using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private readonly DispatcherTimer appearanceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private void OpenSettings() {
        if (!Navigate("settings")) { return; }
        StackPanel body = Page("设置", ShowTodos);
        body.Children.Add(Text("让这块小空间，变成你喜欢的样子。", 12, true));
        AddStartupSetting(body);
        Section(body, "外观");
        body.Children.Add(Text("UI 大小", 14));
        Choice(body, new[] { "小\n80%", "标准\n100%", "大\n120%" }, settings.UiSize == 0.8 ? 0 : settings.UiSize == 1.2 ? 2 : 1, ChangeUiSize);
        Choice(body, new[] { "日间", "夜间" }, settings.NightMode ? 1 : 0, ChangeAppearance);
        StackPanel theme = new();
        theme.Children.Add(Text("UI 主题色", 14));
        theme.Children.Add(Text("按钮、开关与选中状态的颜色", 11, true));
        Palette(theme, new[] { "#8AA675", "#578CEC", "#A18CD1", "#D58F9D", "#DBAB65" }, settings.AccentColor, ChangeAccent);
        ColorSetting(theme, "自定义主题色", settings.AccentColor, ChangeAccent);
        body.Children.Add(Card(theme));
        StackPanel background = new();
        background.Children.Add(Text("背景颜色", 14));
        background.Children.Add(Text("独立于 UI 主题色，日间与夜间分别保存", 11, true));
        string backgroundHex = settings.NightMode ? settings.NightBackground : settings.DayBackground;
        string[] presets = settings.NightMode ? new[] { "#181A1F", "#24372E", "#253246", "#352D42", "#402E33" } : new[] { "#F6F7F9", "#E6EDE4", "#E6EDF7", "#EEE7F5", "#F6E7EA" };
        Palette(background, presets, backgroundHex, ChangeBackground);
        ColorSetting(background, "自定义背景", backgroundHex, ChangeBackground);
        body.Children.Add(Card(background));
        StackPanel transparency = new();
        Grid opacityTitle = new();
        opacityTitle.ColumnDefinitions.Add(new ColumnDefinition());
        opacityTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityTitle.Children.Add(Text("背景透明度", 14));
        TextBlock value = Text(Math.Round((1 - settings.BackgroundOpacity) * 100) + "%", 12, true);
        Grid.SetColumn(value, 1);
        opacityTitle.Children.Add(value);
        transparency.Children.Add(opacityTitle);
        Slider slider = new() { Minimum = 0, Maximum = 80, Value = Math.Round((1 - settings.BackgroundOpacity) * 100), TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 10, 0, 3) };
        System.Windows.Automation.AutomationProperties.SetName(slider, "背景透明度");
        slider.ValueChanged += (_, args) => { value.Text = Math.Round(args.NewValue) + "%"; SetBackgroundTransparency(args.NewValue); };
        transparency.Children.Add(slider);
        transparency.Children.Add(Text("不透明  ···  更通透，文字与按钮保持清晰", 10, true));
        body.Children.Add(Card(transparency));
        StackPanel ink = new();
        Color inkColor = ((SolidColorBrush)Ink).Color;
        string inkHex = $"#{inkColor.R:X2}{inkColor.G:X2}{inkColor.B:X2}";
        ColorSetting(ink, "文字颜色", inkHex, ChangeTextColor);
        Button automatic = Button("使用自动对比色", ResetTextColor);
        automatic.Style = (Style)Resources["QuietButton"];
        ink.Children.Add(automatic);
        body.Children.Add(Card(ink));
        Section(body, "阅读与窗口");
        NavigationRow(body, "小说书架", "导入 TXT / EPUB，继续上次阅读", ShowBooks);
        NavigationRow(body, "阅读偏好", "排版、阅读配色与翻页方式", OpenReaderSettings);
        NavigationRow(body, "窗口位置", "选择显示器或回到屏幕右侧", OpenWindowSettings);
        NavigationRow(body, "本地文件", "打开待办与小说保存位置", RevealNotes);
        AddHotkeySetting(body);
        SetStatus("外观更改自动保存");
    }
    private void OpenWindowSettings() {
        if (!Navigate("window-settings")) { return; }
        StackPanel body = Page("窗口位置", OpenSettings);
        Section(body, "显示器");
        foreach (Forms.Screen screen in Forms.Screen.AllScreens) {
            string device = screen.DeviceName;
            NavigationRow(body, device, screen.Primary ? "主显示器" : "扩展显示器", () => ChangeScreen(device));
        }
        body.Children.Add(Button("回到当前显示器右侧", SnapToScreen));
        SetStatus("拖动顶部空白处移动，拖动边缘调整大小");
    }
    private void SnapToScreen() { PositionDrawer(true); UpdateRoundedClip(); SaveBounds(); }

    private void AddStartupSetting(StackPanel body) {
        Section(body, "启动");
        // 旧快捷方式也统一注册英文目录下的正式 EXE，避免启动项指向旧文件。
        string executable = Path.Combine(Path.GetDirectoryName(configDirectory) ?? AppContext.BaseDirectory, "app", "Luma.exe");
        if (!File.Exists(executable)) { executable = Environment.ProcessPath!; }
        Toggle(body, "开机自启动", StartupRegistration.IsEnabled(executable), enabled => {
            try {
                StartupRegistration.SetEnabled(enabled, executable);
                SetStatus(enabled ? "已开启，登录 Windows 后自动启动 Luma。" : "已关闭开机自启动。");
            } catch (Exception error) {
                OpenSettings();
                ShowNotice("启动设置未保存", error.Message);
            }
        });
    }
    private void OpenReaderSettings() {
        if (!Navigate("reader-settings")) { return; }
        StackPanel body = Page("阅读偏好", ReturnFromReaderSettings);
        Section(body, "文字与排版");
        NumberSetting(body, "字号", settings.ReaderFontSize, 12, 40, 1, SetReaderSize);
        NumberSetting(body, "行距", settings.ReaderLineSpacing, 1.2, 2.5, 0.1, SetReaderSpacing);
        NumberSetting(body, "左右留白", settings.ReaderMargin, 8, 60, 4, SetReaderMargin);
        Choice(body, new[] { "黑体", "宋体", "楷体" }, ReaderFontIndex(), SetReaderFontIndex);
        Section(body, "阅读配色");
        string[] readerColors = { "跟随外观", "护眼", "自定义" };
        int selected = settings.ReaderColors == "自定义" ? 2 : settings.ReaderColors == "护眼" ? 1 : 0;
        Choice(body, readerColors, selected, SetReaderColors);
        StackPanel colors = new();
        ColorSetting(colors, "正文背景", settings.ReaderBackground, SetReaderBackground);
        ColorSetting(colors, "正文文字", settings.ReaderText, SetReaderInk);
        body.Children.Add(Card(colors));
        Section(body, "翻页");
        Choice(body, new[] { "上下滚动", "左右翻页" }, settings.ScrollReading ? 0 : 1, SetReadingMode);
        Toggle(body, "滚轮翻页", settings.WheelPaging, SetWheelPaging);
        Toggle(body, "点击左右半区翻页", settings.ClickPaging, SetClickPaging);
        body.Children.Add(Text("滚动模式下，滚轮正常滚动正文。", 11, true));
        Choice(body, new[] { "无动画", "平滑滑动" }, settings.SmoothPaging ? 1 : 0, SetPageAnimation);
        Section(body, "阅读工具");
        body.Children.Add(Card(Text("鼠标靠近上方或下方，显示阅读工具。\n移回正文，工具自动淡出。\n↑ / ↓ 或小键盘 8 / 2 上下阅读。\n分页时：← / PageUp 上一页，→ / 空格下一页。", 12, true)));
        Toggle(body, "记住阅读位置", settings.RememberReading, SetRememberReading);
        body.Children.Add(Button("恢复阅读默认值", ResetReaderSettings));
        SetStatus("排版变化后，仍保持原文阅读位置");
    }
    private int ReaderFontIndex() { return settings.ReaderFontFamily == "SimSun" ? 1 : settings.ReaderFontFamily == "KaiTi" ? 2 : 0; }
    private void SetReaderFontIndex(int value) { settings.ReaderFontFamily = new[] { "Microsoft YaHei UI", "SimSun", "KaiTi" }[value]; SaveSettings(); OpenReaderSettings(); }
    private void ReturnFromReaderSettings() { if (activeBook == null) { OpenSettings(); } else { ResumeReader(); } }
    private void Section(StackPanel body, string title) {
        TextBlock heading = Text(title, 12, true);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(4, 22, 0, 10);
        body.Children.Add(heading);
    }
    private void Choice(StackPanel body, string[] values, int selected, Action<int> changed) {
        Grid choices = new();
        for (int index = 0; index < values.Length; index++) {
            choices.ColumnDefinitions.Add(new ColumnDefinition());
            int option = index;
            Button button = Button(values[index], () => changed(option));
            button.Padding = new Thickness(5, 8, 5, 8);
            button.Background = index == selected ? (Brush)Resources["Card"] : Brushes.Transparent;
            button.Foreground = index == selected ? Ink : Muted;
            button.FontWeight = index == selected ? FontWeights.SemiBold : FontWeights.Normal;
            Grid.SetColumn(button, index);
            choices.Children.Add(button);
        }
        Border segmented = new() { Child = choices, CornerRadius = new CornerRadius(13), Padding = new Thickness(3), Margin = new Thickness(0, 2, 0, 12) };
        segmented.SetResourceReference(Border.BackgroundProperty, "SwitchOff");
        body.Children.Add(segmented);
    }
    private void Toggle(StackPanel body, string title, bool value, Action<bool> changed) {
        CheckBox toggle = new() { Content = title, IsChecked = value, FontSize = 13, Style = (Style)Resources["Switch"] };
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        body.Children.Add(Card(toggle, new Thickness(16, 10, 16, 10)));
    }
    private void NumberSetting(StackPanel body, string title, double value, double minimum, double maximum, double step, Action<double> changed) {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock label = Text(title, 14);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        StackPanel controls = new() { Orientation = Orientation.Horizontal };
        TextBlock number = Text(value.ToString("0.#"), 13);
        number.VerticalAlignment = VerticalAlignment.Center;
        number.Width = 40;
        number.TextAlignment = TextAlignment.Center;
        Button minus = Button("−", () => { value = Math.Round(Math.Clamp(value - step, minimum, maximum), 1); number.Text = value.ToString("0.#"); changed(value); });
        Button plus = Button("＋", () => { value = Math.Round(Math.Clamp(value + step, minimum, maximum), 1); number.Text = value.ToString("0.#"); changed(value); });
        minus.Padding = plus.Padding = new Thickness(10, 3, 10, 3);
        minus.Background = plus.Background = (Brush)Resources["SwitchOff"];
        controls.Children.Add(minus);
        controls.Children.Add(number);
        controls.Children.Add(plus);
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        body.Children.Add(Card(row, new Thickness(16, 10, 12, 10)));
    }
    private void Palette(StackPanel body, string[] values, string selected, Action<string> changed) {
        WrapPanel palette = new() { Margin = new Thickness(0, 14, 0, 12) };
        foreach (string hex in values) {
            Button swatch = Button("", () => changed(hex));
            swatch.Style = (Style)Resources["QuietButton"];
            swatch.Padding = new Thickness(4);
            swatch.Width = 48;
            bool chosen = string.Equals(hex, selected, StringComparison.OrdinalIgnoreCase);
            Border ring = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(chosen ? 2 : 1), Padding = new Thickness(3), BorderBrush = chosen ? (Brush)Resources["AccentInk"] : Line };
            ring.Child = new Ellipse { Fill = ColorBrush(hex) };
            swatch.Content = ring;
            swatch.ToolTip = hex;
            System.Windows.Automation.AutomationProperties.SetName(swatch, "颜色 " + hex);
            palette.Children.Add(swatch);
        }
        body.Children.Add(palette);
    }
    private void ColorSetting(StackPanel body, string title, string hex, Action<string> changed) {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock label = Text(title, 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        Button color = Button("", () => PickColor(hex, changed));
        color.Padding = new Thickness(10, 6, 10, 6);
        color.Background = (Brush)Resources["SwitchOff"];
        StackPanel value = new() { Orientation = Orientation.Horizontal };
        value.Children.Add(new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(5), Background = ColorBrush(hex), BorderBrush = Line, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) });
        value.Children.Add(Text(hex.ToUpperInvariant(), 11, true));
        color.Content = value;
        System.Windows.Automation.AutomationProperties.SetName(color, title);
        Grid.SetColumn(color, 1);
        row.Children.Add(color);
        body.Children.Add(row);
    }
    private void NavigationRow(StackPanel body, string title, string description, Action action, Action? delete = null) {
        Button row = Button("", action);
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.Padding = new Thickness(16, 13, 14, 13);
        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel text = new();
        TextBlock name = Text(title, 14);
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxHeight = 24;
        text.Children.Add(name);
        TextBlock detail = Text(description, 11, true);
        detail.Margin = new Thickness(0, 4, 8, 0);
        text.Children.Add(detail);
        content.Children.Add(text);
        TextBlock chevron = Text("›", 22, true);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        row.Content = content;
        row.Margin = new Thickness(0, 0, 0, 8);
        System.Windows.Automation.AutomationProperties.SetName(row, title);
        if (delete == null) {
            body.Children.Add(row);
        } else {
            Grid container = new();
            container.ColumnDefinitions.Add(new ColumnDefinition());
            container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            container.Children.Add(row);
            Button remove = IconButton("\uE74D", delete, "删除小说 " + title);
            remove.Margin = new Thickness(8, 0, 0, 8);
            Grid.SetColumn(remove, 1);
            container.Children.Add(remove);
            body.Children.Add(container);
        }
    }
    private void ChangeUiSize(int selected) {
        // 只调整内容比例，保留窗口位置与尺寸；重建设置页以同步选中状态。
        settings.UiSize = new[] { 0.8, 1.0, 1.2 }[selected];
        UpdateUiScale();
        UpdateRoundedClip();
        SaveSettings();
        OpenSettings();
    }
    private void ChangeAppearance(int selected) { settings.NightMode = selected == 1; RefreshAppearance(); }
    private void ChangeAccent(string hex) { settings.AccentColor = hex; RefreshAppearance(); }
    private void ChangeBackground(string hex) {
        if (settings.NightMode) { settings.NightBackground = hex; settings.CustomNightText = false; } else { settings.DayBackground = hex; settings.CustomDayText = false; }
        RefreshAppearance();
    }
    private void SetBackgroundTransparency(double percent) {
        settings.BackgroundOpacity = 1 - percent / 100;
        ApplyTheme();
        appearanceSaveTimer.Stop();
        appearanceSaveTimer.Start();
    }
    private void RefreshAppearance() { ApplyTheme(); UpdateModeButtons(); SaveSettings(); OpenSettings(); }
    private void ChangeTextColor(string hex) {
        if (settings.NightMode) { settings.NightTextColor = hex; settings.CustomNightText = true; } else { settings.DayTextColor = hex; settings.CustomDayText = true; }
        RefreshAppearance();
    }
    private void ResetTextColor() {
        if (settings.NightMode) { settings.CustomNightText = false; } else { settings.CustomDayText = false; }
        RefreshAppearance();
    }
    private void ChangeScreen(string device) { settings.ScreenDevice = device; SnapToScreen(); }
    private void SetReaderSize(double value) { settings.ReaderFontSize = value; SaveSettings(); }
    private void SetReaderSpacing(double value) { settings.ReaderLineSpacing = value; SaveSettings(); }
    private void SetReaderMargin(double value) { settings.ReaderMargin = (int)value; SaveSettings(); }
    private void SetReaderColors(int value) { settings.ReaderColors = new[] { "跟随主题", "护眼", "自定义" }[value]; SaveSettings(); OpenReaderSettings(); }
    private void SetReaderBackground(string value) { settings.ReaderBackground = value; settings.ReaderColors = "自定义"; SaveSettings(); OpenReaderSettings(); }
    private void SetReaderInk(string value) { settings.ReaderText = value; settings.ReaderColors = "自定义"; SaveSettings(); OpenReaderSettings(); }
    private void SetReadingMode(int value) { settings.ScrollReading = value == 0; SaveSettings(); OpenReaderSettings(); }
    private void SetWheelPaging(bool value) { settings.WheelPaging = value; SaveSettings(); }
    private void SetClickPaging(bool value) { settings.ClickPaging = value; SaveSettings(); }
    private void SetPageAnimation(int value) { settings.SmoothPaging = value == 1; SaveSettings(); OpenReaderSettings(); }
    private void SetRememberReading(bool value) { settings.RememberReading = value; SaveSettings(); }
    private void ResetReaderSettings() {
        settings.ReaderFontSize = 18;
        settings.ReaderLineSpacing = 1.8;
        settings.ReaderMargin = 24;
        settings.ReaderFontFamily = "Microsoft YaHei UI";
        settings.ReaderColors = "跟随主题";
        settings.ScrollReading = false;
        settings.WheelPaging = true;
        settings.ClickPaging = true;
        settings.SmoothPaging = true;
        settings.RememberReading = true;
        SaveSettings();
        OpenReaderSettings();
    }
    private void RevealNotes() { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", settings.NotesDirectory) { UseShellExecute = true }); }
}
