using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private string DraftPath => Path.Combine(configDirectory, "待办草稿.md");
    private string DraftTitlePath => Path.Combine(configDirectory, "draft-title.txt");
    private TextBox? todoTitle;
    private TextBlock? editorHint;
    private TextBlock? editorCount;

    private void ShowTodos() {
        if (!Navigate("todos")) { return; }
        StackPanel body = Page("");
        TextBlock date = Text(DateTime.Today.ToString("yyyy/MM/dd dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), 12, true);
        date.Margin = new Thickness(0, 0, 0, 16);
        body.Children.Add(date);
        foreach (TodoEntry entry in todoLibrary.List()) {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            CheckBox complete = new() { IsChecked = entry.Completed, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(complete, "完成待办 " + entry.Name);
            complete.Checked += (_, _) => Run(() => CompleteTodo(entry, true));
            complete.Unchecked += (_, _) => Run(() => CompleteTodo(entry, false));
            row.Children.Add(complete);
            StackPanel labels = new();
            TextBlock name = Text(entry.Name, 16);
            if (entry.Completed) { name.TextDecorations = TextDecorations.Strikethrough; name.Opacity = 0.55; }
            labels.Children.Add(name);
            labels.Children.Add(Text(entry.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm") + " · " + entry.Done + "/" + entry.Total + " 项", 10, true));
            Button open = Button("", () => OpenTodo(entry.Path));
            open.Content = labels;
            open.Padding = new Thickness(4, 4, 8, 4);
            open.Background = Brushes.Transparent;
            open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(open, 1);
            row.Children.Add(open);
            Button delete = IconButton("\uE74D", () => DeleteTodo(entry), "删除待办 " + entry.Name);
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            body.Children.Add(Card(row, new Thickness(12)));
        }
        Button add = PrimaryButton("＋ 添加待办", BeginTodo);
        add.Margin = new Thickness(0, 8, 0, 8);
        body.Children.Add(add);
        SetStatus("保存在本机");
    }

    private void OpenTodo(string path) {
        if (!Navigate("detail")) { return; }
        selectedTodo = TodoLibrary.Read(path);
        StackPanel body = Page(selectedTodo.Name, ShowTodos);
        body.Children.Add(Text("添加于 " + selectedTodo.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"), 11, true));
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        actions.Children.Add(Button("编辑", EditTodo));
        actions.Children.Add(Button(selectedTodo.Completed ? "取消完成" : "完成", CompleteSelectedTodo));
        actions.Children.Add(Button("删除", () => DeleteTodo(selectedTodo)));
        body.Children.Add(actions);
        RichTextBox markdown = new() {
            Document = MarkdownView.Build(selectedTodo.Body, settings.FontSize, ToggleSubtask, BuildTodoImage, selectedTodo.Children.ToDictionary(item => item.Line, item => item.CreatedAt)),
            IsReadOnly = true, IsDocumentEnabled = true, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), Margin = new Thickness(0, 8, 0, 0)
        };
        // 详情只由外层页面滚动，避免文档内置阅读器吞掉图片、复选框上的滚轮事件。
        markdown.PreviewMouseWheel += ScrollTodoDocument;
        body.Children.Add(markdown);
        SetStatus("正文与图片保存在本机");
    }

    private void DeleteTodo(TodoEntry entry) {
        ConfirmInline("删除待办", "确定删除“" + entry.Name + "”及其子待办？此操作会删除对应的 Markdown 文件。", () => {
            TodoLibrary.Delete(entry);
            selectedTodo = null;
            editing = false;
            dirty = false;
            ShowTodos();
        });
    }

    private void CompleteTodo(TodoEntry entry, bool complete) { TodoLibrary.SetCompleted(entry, complete); ShowTodos(); }
    private void CompleteSelectedTodo() {
        if (selectedTodo == null) { return; }
        TodoLibrary.SetCompleted(selectedTodo, !selectedTodo.Completed);
        OpenTodo(selectedTodo.Path);
    }
    private void ToggleSubtask(int line, bool complete) {
        Run(() => {
            if (selectedTodo == null) { return; }
            string updated = NoteStorage.ToggleTask(selectedTodo.Body, line, complete);
            selectedTodo = TodoLibrary.Update(selectedTodo, selectedTodo.Name, updated);
            // 复选框直接反映本次状态，不重建整页，以保留当前浏览位置。
        });
    }

    private void ScrollTodoDocument(object sender, MouseWheelEventArgs args) {
        DependencyObject? parent = VisualTreeHelper.GetParent((DependencyObject)sender);
        while (parent != null && parent is not ScrollViewer) { parent = VisualTreeHelper.GetParent(parent); }
        if (parent is ScrollViewer scroll) {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta / 120.0 * settings.FontSize * 3);
            args.Handled = true;
        }
    }

    private void BeginTodo() {
        if (!Navigate("edit")) { return; }
        selectedTodo = null;
        string draft = File.Exists(DraftPath) ? NoteStorage.Read(DraftPath) : "";
        string title = File.Exists(DraftTitlePath) ? File.ReadAllText(DraftTitlePath) : "";
        ShowEditor(draft, title);
    }
    private void EditTodo() {
        if (selectedTodo == null || !Navigate("edit")) { return; }
        ShowEditor(selectedTodo.Body, selectedTodo.Name);
    }

    private void ShowEditor(string text, string title) {
        editing = true;
        dirty = false;
        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        DockPanel heading = new() { Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(IconButton("\uE72B", ShowTodos, "返回并保留草稿"));
        Button save = PrimaryButton("保存", SaveTodoExplicitly);
        DockPanel.SetDock(save, Dock.Right);
        heading.Children.Add(save);
        layout.Children.Add(heading);
        todoTitle = Input(title, "待办标题");
        todoTitle.MaxLength = 80;
        todoTitle.FontSize = 18;
        todoTitle.Margin = new Thickness(0, 0, 0, 14);
        todoTitle.ToolTip = "第一行填写待办标题";
        todoTitle.TextChanged += TodoChanged;
        Grid.SetRow(todoTitle, 1);
        layout.Children.Add(todoTitle);
        Grid writing = new();
        todoEditor = new TodoEditor(BuildTodoImage) { Markdown = text };
        System.Windows.Automation.AutomationProperties.SetName(todoEditor, "待办正文");
        todoEditor.SetResourceReference(Control.ForegroundProperty, "Ink");
        todoEditor.Document.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        todoEditor.FontSize = 15;
        todoEditor.BorderThickness = new Thickness(0);
        todoEditor.Background = Brushes.Transparent;
        todoEditor.Padding = new Thickness(0);
        todoEditor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        todoEditor.TextChanged += TodoChanged;
        todoEditor.PreviewKeyDown += PasteTodoImage;
        todoEditor.AllowDrop = true;
        todoEditor.PreviewDragOver += (_, args) => { args.Effects = DragDropEffects.Copy; args.Handled = true; };
        todoEditor.PreviewDrop += (_, args) => {
            if (args.Data.GetData(DataFormats.FileDrop) is string[] files) {
                foreach (string file in files) { Run(() => InsertTodoImage(file)); }
            }
            args.Handled = true;
        };
        writing.Children.Add(todoEditor);
        editorHint = Text("在这里填写具体内容…\n\n使用“子待办”拆分步骤，按 Ctrl+V 粘贴图片，也可以拖入图片。", 14, true);
        editorHint.IsHitTestVisible = false;
        editorHint.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        writing.Children.Add(editorHint);
        Border paper = Card(writing, new Thickness(16));
        Grid.SetRow(paper, 2);
        layout.Children.Add(paper);
        WrapPanel formatting = new();
        formatting.Children.Add(Button("＋ 子待办", InsertChecklist));
        editorCount = Text(text.Length + " 字", 11, true);
        editorCount.VerticalAlignment = VerticalAlignment.Center;
        editorCount.Margin = new Thickness(10, 0, 0, 0);
        formatting.Children.Add(editorCount);
        Grid.SetRow(formatting, 3);
        layout.Children.Add(formatting);
        contentArea.Children.Add(layout);
        todoTitle.Focus();
        SetStatus("第一行标题，下方正文 · 草稿自动保存");
    }

    private void InsertChecklist() { InsertLinePrefix("- [ ] "); }
    private void InsertHeading() { InsertLinePrefix("## "); }
    private void InsertBold() {
        if (todoEditor == null) { return; }
        todoEditor.Selection.Text = "**" + (todoEditor.Selection.IsEmpty ? "加粗文字" : todoEditor.Selection.Text) + "**";
    }
    private void InsertLinePrefix(string prefix) {
        if (todoEditor == null) { return; }
        todoEditor.InsertPrefix(prefix);
    }
    private void TodoChanged(object sender, TextChangedEventArgs args) {
        if (editorHint != null) { editorHint.Visibility = todoEditor?.Markdown.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }
        if (editorCount != null) { editorCount.Text = (todoEditor?.Markdown.Length ?? 0) + " 字"; }
        dirty = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>保存正文、标题和添加时间；失败返回 false，阻止丢弃当前编辑。</summary>
    public bool SaveCurrent() {
        saveTimer.Stop();
        try { SaveReadingPosition(); }
        catch (Exception error) { SetStatus("阅读进度未保存：" + error.Message); return false; }
        if (!editing || !dirty || todoEditor == null) { return true; }
        try {
            if (selectedTodo == null) {
                Directory.CreateDirectory(configDirectory);
                NoteStorage.EnsureFile(DraftPath, "");
                NoteStorage.Save(DraftPath, NoteStorage.Read(DraftPath), todoEditor.Markdown);
                File.WriteAllText(DraftTitlePath, todoTitle?.Text ?? "");
                SetStatus("草稿已保留");
            } else {
                selectedTodo = TodoLibrary.Update(selectedTodo, todoTitle?.Text ?? selectedTodo.Name, todoEditor.Markdown);
                SetStatus("已保存");
            }
            dirty = false;
            return true;
        } catch (Exception error) { SetStatus("保存暂停，内容仍保留：" + error.Message); return false; }
    }

    private void SaveTodoExplicitly() {
        if (todoEditor == null || todoTitle == null) { return; }
        if (string.IsNullOrWhiteSpace(todoTitle.Text)) { SetStatus("请先填写第一行的待办标题。"); todoTitle.Focus(); return; }
        if (selectedTodo == null) {
            selectedTodo = todoLibrary.Create(todoTitle.Text, todoEditor.Markdown);
            if (File.Exists(DraftPath)) { File.WriteAllText(DraftPath, ""); }
            if (File.Exists(DraftTitlePath)) { File.WriteAllText(DraftTitlePath, ""); }
            dirty = false;
        } else if (!SaveCurrent()) { ShowNotice("保存暂停", statusText.Text); return; }
        editing = false;
        OpenTodo(selectedTodo.Path);
    }
}
