using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace DesktopNotes;

/// <summary>正文直接展示本地图片，保存时将图片块还原为兼容现有文件的 Markdown。</summary>
internal sealed class TodoEditor : RichTextBox {
    private static readonly Regex ImageLink = new(@"!\[[^\]]*\]\(([^)]+)\)");
    private static readonly Regex TaskLine = new(@"^(\s*[-*+]\s+\[)([ xX])(\]\s*)(.*)$");
    private readonly Func<string, UIElement> imageBuilder;

    internal TodoEditor(Func<string, UIElement> imageBuilder) {
        this.imageBuilder = imageBuilder;
        IsDocumentEnabled = true;
        AcceptsTab = true;
        Document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 15 };
        // 外部富文本只按文字粘贴，图片统一由应用导入本地附件，避免隐式保存外部对象。
        DataObject.AddPastingHandler(this, PastePlainText);
        // 在编辑器统一接收勾选事件，撤销后恢复的复选框也能继续触发保存。
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(TaskChecked));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(TaskChecked));
    }

    /// <summary>以 Markdown 读取或替换正文；图片块与文字行按原顺序保存。</summary>
    internal string Markdown {
        get {
            List<string> lines = new();
            foreach (Block block in Document.Blocks) {
                if (block is BlockUIContainer image && image.Child is FrameworkElement element && element.Tag is string relative) {
                    lines.Add("![图片](" + relative + ")");
                } else if (block is Paragraph paragraph) {
                    TextPointer start = paragraph.ContentStart;
                    string prefix = "";
                    if (paragraph.Inlines.FirstInline is InlineUIContainer inline && inline.Child is CheckBox checkbox && checkbox.Tag is string[] marker && marker.Length == 2) {
                        start = inline.ElementEnd;
                        prefix = marker[0] + (checkbox.IsChecked == true ? "x" : " ") + marker[1];
                    }
                    string text = new TextRange(start, paragraph.ContentEnd).Text.Replace("\r\n", "\n");
                    if (text.EndsWith('\n')) { text = text.Substring(0, text.Length - 1); }
                    lines.Add(prefix + text);
                }
            }
            return string.Join("\n", lines);
        }
        set {
            Document.Blocks.Clear();
            bool code = false;
            foreach (string line in value.Replace("\r\n", "\n").Split('\n')) {
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { code = !code; }
                MatchCollection images = ImageLink.Matches(line);
                if (code || images.Count == 0) {
                    Document.Blocks.Add(PlainParagraph(line, !code));
                    continue;
                }
                int offset = 0;
                foreach (Match match in images) {
                    if (match.Index > offset) { Document.Blocks.Add(PlainParagraph(line.Substring(offset, match.Index - offset))); }
                    Document.Blocks.Add(ImageBlock(match.Groups[1].Value));
                    offset = match.Index + match.Length;
                }
                if (offset < line.Length) { Document.Blocks.Add(PlainParagraph(line.Substring(offset))); }
            }
            // 图片后保留可输入位置，用户可接着写文字或再插入下一张图片。
            if (Document.Blocks.LastBlock is not Paragraph) { Document.Blocks.Add(PlainParagraph("")); }
        }
    }

    /// <summary>在光标处插入独占一行的本地图片，保留光标前后的文字。</summary>
    internal void InsertImage(string relative) {
        BeginChange();
        try {
            Selection.Text = "";
            TextPointer caret = CaretPosition;
            Paragraph? paragraph = caret.Paragraph;
            if (paragraph == null) {
                paragraph = PlainParagraph("");
                Document.Blocks.Add(paragraph);
                caret = paragraph.ContentStart;
            }
            // 将光标后的正文移到图片下方，图片永远作为独立块参与编辑与删除。
            TextRange tail = new(caret, paragraph.ContentEnd);
            string after = tail.Text;
            tail.Text = "";
            BlockUIContainer image = ImageBlock(relative);
            Document.Blocks.InsertAfter(paragraph, image);
            Paragraph next = PlainParagraph(after);
            Document.Blocks.InsertAfter(image, next);
            CaretPosition = next.ContentStart;
        } finally { EndChange(); }
        Focus();
    }

    /// <summary>在当前文字行开头添加子待办或标题标记。</summary>
    internal void InsertPrefix(string prefix) {
        if (prefix == "- [ ] ") {
            InsertTask();
            return;
        }
        TextPointer start = CaretPosition.GetLineStartPosition(0) ?? CaretPosition;
        start.InsertTextInRun(prefix);
        Focus();
    }

    private void InsertTask() {
        Paragraph? current = CaretPosition.Paragraph;
        if (current?.Inlines.FirstInline is InlineUIContainer inline && inline.Child is CheckBox) { Focus(); return; }
        BeginChange();
        try {
            string text = current == null ? "" : new TextRange(current.ContentStart, current.ContentEnd).Text;
            Paragraph task = PlainParagraph("- [ ] " + text.TrimEnd('\r', '\n'));
            if (current == null) { Document.Blocks.Add(task); }
            else { Document.Blocks.InsertBefore(current, task); Document.Blocks.Remove(current); }
            CaretPosition = task.ContentEnd;
        } finally { EndChange(); }
        Focus();
    }

    private BlockUIContainer ImageBlock(string relative) {
        FrameworkElement image = (FrameworkElement)imageBuilder(relative);
        image.Tag = relative;
        return new BlockUIContainer(image) { Margin = new Thickness(0), BreakPageBefore = false };
    }

    private Paragraph PlainParagraph(string text, bool allowTask = true) {
        Match task = TaskLine.Match(text);
        if (!allowTask || !task.Success) { return new Paragraph(new Run(text)) { Margin = new Thickness(0, 0, 0, 4) }; }
        CheckBox checkbox = new() {
            IsChecked = task.Groups[2].Value != " ", Margin = new Thickness(0, 0, 6, 0),
            // 字符串数组可由 WPF 撤销栈序列化，不把私有业务对象放入文档。
            Tag = new[] { task.Groups[1].Value, task.Groups[3].Value }
        };
        System.Windows.Automation.AutomationProperties.SetName(checkbox, "子待办 " + task.Groups[4].Value);
        Run label = new(task.Groups[4].Value) { TextDecorations = checkbox.IsChecked == true ? TextDecorations.Strikethrough : null };
        Paragraph paragraph = new() { Margin = new Thickness(0, 3, 0, 6) };
        paragraph.Inlines.Add(new InlineUIContainer(checkbox) { BaselineAlignment = BaselineAlignment.Center });
        paragraph.Inlines.Add(label);
        return paragraph;
    }

    private void TaskChecked(object sender, RoutedEventArgs args) {
        if (args.OriginalSource is not CheckBox checkbox || checkbox.Tag is not string[]) { return; }
        if (checkbox.Parent is InlineUIContainer inline && inline.Parent is Paragraph paragraph) {
            foreach (Inline item in paragraph.Inlines) {
                if (item is Run label) { label.TextDecorations = checkbox.IsChecked == true ? TextDecorations.Strikethrough : null; }
            }
        }
        // 勾选属于正文修改，沿用原有草稿和待办自动保存机制。
        RaiseEvent(new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
    }

    private static void PastePlainText(object sender, DataObjectPastingEventArgs args) {
        if (!args.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)) { args.CancelCommand(); return; }
        args.DataObject = new DataObject(DataFormats.UnicodeText, args.SourceDataObject.GetData(DataFormats.UnicodeText));
        args.FormatToApply = DataFormats.UnicodeText;
    }
}
