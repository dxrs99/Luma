using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private bool RegisterConfiguredHotkey() {
        return RegisterHotKey(handle, 91, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey);
    }

    private void AddHotkeySetting(StackPanel body) {
        Section(body, "快捷键与边缘隐藏");
        Toggle(body, "靠近屏幕边缘自动隐藏", settings.EdgeAutoHideEnabled, value => {
            settings.EdgeAutoHideEnabled = value;
            SaveSettings();
            UpdateDockEdge();
        });
        body.Children.Add(Text("点击下方输入框，按下新的组合键。", 12, true));
        TextBox capture = Input(HotkeyText(), "唤出隐藏快捷键");
        capture.IsReadOnly = true;
        capture.Margin = new Thickness(0, 10, 0, 8);
        body.Children.Add(capture);
        TextBlock hint = Text("至少包含 Ctrl、Alt 或 Win 中的一项。", 11, true);
        body.Children.Add(hint);
        uint modifiers = settings.HotkeyModifiers;
        uint key = settings.HotkeyKey;
        capture.PreviewKeyDown += (_, args) => {
            args.Handled = true;
            Key pressed = args.Key == Key.System ? args.SystemKey : args.Key;
            if (pressed == Key.LeftCtrl || pressed == Key.RightCtrl || pressed == Key.LeftAlt || pressed == Key.RightAlt || pressed == Key.LeftShift || pressed == Key.RightShift || pressed == Key.LWin || pressed == Key.RWin) { return; }
            // WPF 的修饰键位值与 Win32 不同，显式映射给 RegisterHotKey。
            modifiers = ToNativeModifiers(Keyboard.Modifiers);
            key = (uint)KeyInterop.VirtualKeyFromKey(pressed);
            capture.Text = Keyboard.Modifiers + " + " + pressed;
        };
        body.Children.Add(Button("保存快捷键", () => {
            if ((modifiers & 11) == 0 || key == 0) { hint.Text = "请使用 Ctrl、Alt 或 Win 加普通按键。"; return; }
            uint oldModifiers = settings.HotkeyModifiers;
            uint oldKey = settings.HotkeyKey;
            UnregisterHotKey(handle, 91);
            settings.HotkeyModifiers = modifiers;
            settings.HotkeyKey = key;
            hotkeyRegistered = RegisterConfiguredHotkey();
            if (!hotkeyRegistered) {
                settings.HotkeyModifiers = oldModifiers;
                settings.HotkeyKey = oldKey;
                hotkeyRegistered = RegisterConfiguredHotkey();
                hint.Text = "组合键被其他程序占用，已保留原快捷键。";
                return;
            }
            SaveSettings();
            capture.Text = HotkeyText();
            hint.Text = "快捷键已生效。";
        }));
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers) {
        uint result = 0;
        if ((modifiers & ModifierKeys.Alt) != 0) { result |= 1; }
        if ((modifiers & ModifierKeys.Control) != 0) { result |= 2; }
        if ((modifiers & ModifierKeys.Shift) != 0) { result |= 4; }
        if ((modifiers & ModifierKeys.Windows) != 0) { result |= 8; }
        return result;
    }

    private string HotkeyText() {
        string text = "";
        if ((settings.HotkeyModifiers & 2) != 0) { text += "Ctrl + "; }
        if ((settings.HotkeyModifiers & 1) != 0) { text += "Alt + "; }
        if ((settings.HotkeyModifiers & 4) != 0) { text += "Shift + "; }
        if ((settings.HotkeyModifiers & 8) != 0) { text += "Win + "; }
        return text + KeyInterop.KeyFromVirtualKey((int)settings.HotkeyKey);
    }
}
