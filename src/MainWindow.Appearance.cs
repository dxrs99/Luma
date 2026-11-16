using System;
using System.Windows;
using System.Windows.Media;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private Color PageBackgroundColor => ((SolidColorBrush)ColorBrush(settings.NightMode ? settings.NightBackground : settings.DayBackground)).Color;

    private void ApplyTheme() {
        Color background = PageBackgroundColor;
        Color automaticInk = ContrastingInk(background);
        Color accent = ((SolidColorBrush)ColorBrush(settings.AccentColor, "#8AA675")).Color;
        Color ink = automaticInk;
        bool customText = settings.NightMode ? settings.CustomNightText : settings.CustomDayText;
        if (customText) {
            ink = ((SolidColorBrush)ColorBrush(settings.NightMode ? settings.NightTextColor : settings.DayTextColor)).Color;
        }
        bool dark = RelativeLuminance(background) < 0.25;
        Resources["Ink"] = new SolidColorBrush(ink);
        Resources["Muted"] = new SolidColorBrush(Mix(background, automaticInk, 0.65));
        Resources["Accent"] = new SolidColorBrush(accent);
        Resources["OnAccent"] = new SolidColorBrush(ContrastingInk(accent));
        Resources["AccentSoft"] = new SolidColorBrush(Mix(background, accent, 0.16));
        Resources["AccentInk"] = new SolidColorBrush(ReadableAccent(accent, background, automaticInk));
        Resources["Line"] = new SolidColorBrush(Color.FromArgb(27, automaticInk.R, automaticInk.G, automaticInk.B));
        Resources["Surface"] = new SolidColorBrush(Color.FromArgb((byte)Math.Round(settings.BackgroundOpacity * 255), background.R, background.G, background.B));
        Resources["Card"] = new SolidColorBrush(Mix(background, Colors.White, dark ? 0.065 : 0.68));
        Resources["Input"] = new SolidColorBrush(Mix(background, Colors.White, dark ? 0.045 : 0.8));
        Resources["SwitchOff"] = new SolidColorBrush(Mix(background, automaticInk, 0.16));
        Foreground = (Brush)Resources["Ink"];
        MarkdownView.Ink = (Brush)Resources["Ink"];
        MarkdownView.Muted = (Brush)Resources["Muted"];
        MarkdownView.Accent = (Brush)Resources["AccentInk"];
        MarkdownView.CodeSurface = (Brush)Resources["Card"];
        ApplyWindowMaterial();
    }

    private void ApplyWindowMaterial() {
        // 透明客户区由系统模糊，表面画刷负责唯一一层颜色和透明度，避免叠加变黑或变实。
        desktopHost?.ApplyGlass(settings.BackgroundOpacity < 0.995, interactive: windowManipulating || edgeAnimating);
    }

    private static Color ContrastingInk(Color background) {
        Color light = Color.FromRgb(248, 249, 251);
        Color dark = Color.FromRgb(23, 26, 32);
        return Contrast(background, light) > Contrast(background, dark) ? light : dark;
    }
    private static Color ReadableAccent(Color accent, Color background, Color ink) {
        Color result = accent;
        for (int index = 0; index < 8 && Contrast(result, background) < 4.5; index++) {
            result = Mix(result, ink, 0.2);
        }
        return result;
    }
    private static Color Mix(Color first, Color second, double amount) {
        return Color.FromRgb((byte)Math.Round(first.R + (second.R - first.R) * amount), (byte)Math.Round(first.G + (second.G - first.G) * amount), (byte)Math.Round(first.B + (second.B - first.B) * amount));
    }
    private static double Contrast(Color first, Color second) {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static double RelativeLuminance(Color color) {
        return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
    }
    private static double LinearChannel(byte value) {
        double channel = value / 255.0;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private void UpdateRoundedClip() {
        if (root == null || ActualWidth < 1 || ActualHeight < 1) { return; }
        root.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 24 * uiScale, 24 * uiScale);
        if (desktopHost?.IsAlive == true) {
            desktopHost.RoundCorners((int)Math.Round(24 * uiScale * VisualTreeHelper.GetDpi(this).DpiScaleX));
        }
    }
}
