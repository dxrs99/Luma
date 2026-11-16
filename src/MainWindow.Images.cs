using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private void InsertTodoImage(string path) {
        InsertImageLink(ImageStore.Import(settings.NotesDirectory, path));
    }
    private void InsertImageLink(string relative) {
        if (todoEditor == null) { return; }
        todoEditor.InsertImage(relative);
    }
    private void PasteTodoImage(object sender, KeyEventArgs args) {
        if (args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage()) {
            args.Handled = true;
            Run(PasteClipboardImage);
        }
    }
    private void PasteClipboardImage() {
        System.Windows.Media.Imaging.BitmapSource? bitmap = Clipboard.GetImage();
        if (bitmap == null) { SetStatus("剪贴板中没有图片。"); return; }
        InsertImageLink(ImageStore.SaveClipboard(settings.NotesDirectory, bitmap));
    }

    private UIElement BuildTodoImage(string relative) {
        try {
            string path = ImageStore.Resolve(settings.NotesDirectory, relative);
            Image image = new() { Source = ImageStore.Read(path), Stretch = Stretch.Uniform, MaxWidth = 420, MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Center };
            Border container = new() { Child = image, CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Background = (Brush)Resources["Card"], Margin = new Thickness(0, 8, 0, 12) };
            // 拦住单击的文档自动定位，避免第一下把图片滚走、第二下点不到原图。
            container.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, args) => {
                args.Handled = true;
                if (args.ClickCount != 2) { return; }
                Run(() => OpenTodoImage(path));
            }), true);
            container.Cursor = Cursors.Hand;
            container.ToolTip = "双击用系统图片查看器打开";
            return container;
        } catch (Exception error) { return Text("图片无法显示：" + error.Message, 12, true); }
    }

    private void OpenTodoImage(string path) {
        // 交给 Windows 当前关联的图片应用打开原文件，保留原始清晰度与系统缩放功能。
        if (!File.Exists(path)) { throw new IOException("原图已被移动或删除，无法打开。"); }
        using Process? viewer = Process.Start(new ProcessStartInfo {
            FileName = path, UseShellExecute = true, Verb = "open", WindowStyle = ProcessWindowStyle.Maximized
        });
        // 系统照片应用可能复用已有进程并返回 null，这仍然是正常的打开结果。
    }
}
