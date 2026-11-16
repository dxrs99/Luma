using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private DispatcherTimer? colorSampleTimer;
    private IntPtr screenSampleHook;
    private ScreenMouseCallback? screenSampleCallback;
    private bool screenSamplePressed;
    private DesktopHost.NativePoint screenSamplePoint;

    private void PickColor(string initial, Action<string> changed) {
        Color selected = ((SolidColorBrush)ColorBrush(initial)).Color;
        StackPanel body = new();
        Border preview = new() { Height = 60, CornerRadius = new CornerRadius(14), Background = ColorBrush(initial) };
        body.Children.Add(preview);
        TextBox hex = Input(initial.ToUpperInvariant(), "十六进制颜色");
        hex.Margin = new Thickness(0, 12, 0, 12);
        body.Children.Add(hex);
        Slider red = ColorSlider(body, "红", selected.R);
        Slider green = ColorSlider(body, "绿", selected.G);
        Slider blue = ColorSlider(body, "蓝", selected.B);
        TextBlock hint = Text("支持 RGB、色值与屏幕吸管。", 12, true);
        hint.Margin = new Thickness(0, 12, 0, 12);
        body.Children.Add(hint);
        bool updating = false;
        void SetColor(Color color) {
            updating = true;
            red.Value = color.R;
            green.Value = color.G;
            blue.Value = color.B;
            hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            preview.Background = new SolidColorBrush(color);
            updating = false;
        }
        void PreviewColor(object sender, RoutedPropertyChangedEventArgs<double> args) {
            if (!updating) { SetColor(Color.FromRgb((byte)red.Value, (byte)green.Value, (byte)blue.Value)); }
        }
        red.ValueChanged += PreviewColor;
        green.ValueChanged += PreviewColor;
        blue.ValueChanged += PreviewColor;
        hex.TextChanged += (_, _) => {
            if (!updating && Regex.IsMatch(hex.Text, "^#[0-9a-fA-F]{6}$")) { SetColor(((SolidColorBrush)ColorBrush(hex.Text)).Color); }
        };
        Button sample = Button("吸管 · 取屏幕颜色", () => StartScreenSample(color => {
            // 屏幕点选即应用，手动输入与滑块仍保留原有的“应用颜色”按钮。
            string value = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            CloseOverlay();
            changed(value);
        }, hint));
        sample.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(sample);
        WrapPanel presets = new() { Margin = new Thickness(0, 0, 0, 14) };
        foreach (string value in new[] { "#FFFFFF", "#181A1F", "#E6EDE4", "#E6EDF7", "#EEE7F5", "#F6E7EA" }) {
            Button swatch = Button("", () => SetColor(((SolidColorBrush)ColorBrush(value)).Color));
            swatch.Width = 42;
            swatch.Height = 36;
            swatch.Padding = new Thickness(3);
            swatch.Background = ColorBrush(value);
            swatch.ToolTip = value;
            presets.Children.Add(swatch);
        }
        body.Children.Add(presets);
        body.Children.Add(PrimaryButton("应用颜色", () => {
            if (!Regex.IsMatch(hex.Text, "^#[0-9a-fA-F]{6}$")) { hint.Text = "请输入 # 开头的六位颜色。"; return; }
            string value = hex.Text;
            CloseOverlay();
            changed(value);
        }));
        ShowOverlay("选择颜色", body, StopScreenSample);
    }

    private void StartScreenSample(Action<Color> selected, TextBlock hint) {
        StopScreenSample();
        hint.Text = "点击任意应用的颜色即应用；Esc 或右键取消。";
        // 临时低级鼠标钩子能接收其他应用上的点击，不依赖 WPF 捕获或当前焦点。
        // 只拦截本次取色的按下/松开，移动等事件继续交给系统；结束后立即释放。
        screenSampleCallback = (code, message, data) => SampleScreenMouse(code, message, data, selected, hint);
        screenSampleHook = SetWindowsHookEx(14, screenSampleCallback, GetModuleHandle(null), 0);
        if (screenSampleHook == IntPtr.Zero) {
            hint.Text = "无法启动屏幕吸管：" + new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            StopScreenSample();
            return;
        }
        Mouse.OverrideCursor = Cursors.Cross;
        colorSampleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        colorSampleTimer.Tick += (_, _) => {
            if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) {
                StopScreenSample();
                hint.Text = "已取消取色，颜色未更改。";
                return;
            }
            DesktopHost.NativePoint point = DesktopHost.CursorPosition();
            try {
                Color color = ReadScreenColor(point);
                hint.Text = $"屏幕 ({point.X}, {point.Y}) · #{color.R:X2}{color.G:X2}{color.B:X2} · 单击确认";
            } catch (Exception) { hint.Text = "暂时无法读取屏幕，Esc 取消。"; }
        };
        colorSampleTimer.Start();
    }

    private IntPtr SampleScreenMouse(int code, IntPtr message, IntPtr data, Action<Color> selected, TextBlock hint) {
        if (code < 0) { return CallNextHookEx(screenSampleHook, code, message, data); }
        int kind = message.ToInt32();
        if (kind == 0x0201) {
            screenSamplePoint = Marshal.PtrToStructure<ScreenMouseData>(data).Point;
            screenSamplePressed = true;
            return new IntPtr(1);
        }
        if (kind == 0x0202 && screenSamplePressed) {
            screenSamplePressed = false;
            DesktopHost.NativePoint point = screenSamplePoint;
            // 钩子立即返回，读色和设置更新在 UI 队列执行，避免阻塞系统鼠标输入。
            Dispatcher.BeginInvoke(new Action(() => CompleteScreenSample(point, selected, hint)));
            return new IntPtr(1);
        }
        if (kind == 0x0204) { return new IntPtr(1); }
        if (kind == 0x0205) {
            Dispatcher.BeginInvoke(new Action(() => {
                StopScreenSample();
                hint.Text = "已取消取色，颜色未更改。";
            }));
            return new IntPtr(1);
        }
        return CallNextHookEx(screenSampleHook, code, message, data);
    }

    private void CompleteScreenSample(DesktopHost.NativePoint point, Action<Color> selected, TextBlock hint) {
        if (screenSampleHook == IntPtr.Zero) { return; }
        try {
            Color color = ReadScreenColor(point);
            StopScreenSample();
            selected(color);
        } catch (Exception error) {
            StopScreenSample();
            hint.Text = "取色未应用：" + error.Message;
        }
    }

    private static Color ReadScreenColor(DesktopHost.NativePoint point) {
        using System.Drawing.Bitmap bitmap = new(1, 1);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(point.X, point.Y, 0, 0, bitmap.Size);
        System.Drawing.Color color = bitmap.GetPixel(0, 0);
        return Color.FromRgb(color.R, color.G, color.B);
    }
    private void StopScreenSample() {
        colorSampleTimer?.Stop();
        colorSampleTimer = null;
        if (screenSampleHook != IntPtr.Zero) { UnhookWindowsHookEx(screenSampleHook); screenSampleHook = IntPtr.Zero; }
        screenSampleCallback = null;
        screenSamplePressed = false;
        Mouse.OverrideCursor = null;
    }

    private delegate IntPtr ScreenMouseCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenMouseData {
        public DesktopHost.NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, ScreenMouseCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private Slider ColorSlider(StackPanel body, string name, byte value) {
        Grid row = new() { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        TextBlock label = Text(name, 12, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        Slider slider = new() { Minimum = 0, Maximum = 255, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        body.Children.Add(row);
        return slider;
    }
}
