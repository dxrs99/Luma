using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private Grid? inlineOverlay;
    private Action? overlayClosed;

    private void ShowOverlay(string title, FrameworkElement content, Action? closed = null, bool scrollContent = true) {
        CloseOverlay();
        modalOpen = true;
        surface.IsEnabled = false;
        overlayClosed = closed;
        inlineOverlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)) };
        Grid panel = new() { Margin = new Thickness(22) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        DockPanel header = new() { Margin = new Thickness(0, 0, 0, 16) };
        Button close = IconButton("\uE711", CloseOverlay, "关闭面板");
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        TextBlock heading = Text(title, 20);
        heading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(heading);
        panel.Children.Add(header);
        FrameworkElement contentHost = content;
        if (scrollContent) {
            contentHost = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        Grid.SetRow(contentHost, 1);
        panel.Children.Add(contentHost);
        Border sheet = new() { Child = panel, Background = new SolidColorBrush(PageBackgroundColor), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Margin = new Thickness(16, 32, 16, 32) };
        inlineOverlay.Children.Add(sheet);
        Panel.SetZIndex(inlineOverlay, 100);
        root.Children.Add(inlineOverlay);
        UpdateUiScale();
        inlineOverlay.PreviewKeyDown += (_, args) => {
            if (args.Key == Key.Escape) { CloseOverlay(); args.Handled = true; }
        };
        content.Focus();
    }

    private void CloseOverlay() {
        if (inlineOverlay == null) { return; }
        root.Children.Remove(inlineOverlay);
        inlineOverlay = null;
        surface.IsEnabled = true;
        modalOpen = false;
        Action? closed = overlayClosed;
        overlayClosed = null;
        closed?.Invoke();
    }

    internal void ShowNotice(string title, string message) {
        StackPanel body = new();
        TextBlock text = Text(message, 14);
        text.Margin = new Thickness(0, 0, 0, 20);
        body.Children.Add(text);
        body.Children.Add(PrimaryButton("知道了", CloseOverlay));
        ShowOverlay(title, body);
    }

    private void ConfirmInline(string title, string message, Action confirmed) {
        StackPanel body = new();
        body.Children.Add(Text(message, 14));
        Grid actions = new() { Margin = new Thickness(0, 22, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Button cancel = Button("取消", CloseOverlay);
        cancel.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(cancel);
        Button confirm = PrimaryButton("确认删除", () => { CloseOverlay(); Run(confirmed); });
        confirm.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(confirm, 1);
        actions.Children.Add(confirm);
        body.Children.Add(actions);
        ShowOverlay(title, body);
    }

    private void ImportFileInline(string title, string[] extensions, Action<string> import) {
        StackPanel body = new();
        body.Children.Add(Text("粘贴本机文件路径，或将文件拖到下面的输入框。", 13, true));
        TextBox path = Input("", "导入文件路径");
        path.Margin = new Thickness(0, 16, 0, 12);
        path.AllowDrop = true;
        path.PreviewDragOver += (_, args) => { args.Effects = DragDropEffects.Copy; args.Handled = true; };
        path.Drop += (_, args) => {
            if (args.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) { path.Text = files[0]; }
            args.Handled = true;
        };
        body.Children.Add(path);
        TextBlock error = Text("", 12, true);
        body.Children.Add(error);
        body.Children.Add(PrimaryButton("导入", () => {
            string selected = path.Text.Trim().Trim('"');
            if (!File.Exists(selected) || !extensions.Contains(Path.GetExtension(selected).ToLowerInvariant())) {
                error.Text = "请选择存在的 " + string.Join(" / ", extensions) + " 文件。";
                return;
            }
            CloseOverlay();
            Run(() => import(selected));
        }));
        ShowOverlay(title, body);
        path.Focus();
    }
}
