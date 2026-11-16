using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DesktopNotes;

/// <summary>初版原生 Markdown 阅读器：标题、任务、列表、引用、代码及常用行内格式。</summary>
public static class MarkdownView {
    private static readonly Regex Task = new(@"^\s*[-*+]\s+\[([ xX])\]\s*(.*)$");
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.+)$");
    private static readonly Regex InlinePattern = new(@"(\*\*(.+?)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^\s)]+)\)|(?<!\*)\*([^*]+)\*(?!\*))");
    public static Brush Ink { get; set; } = new SolidColorBrush(Color.FromRgb(53, 59, 54));
    public static Brush Muted { get; set; } = new SolidColorBrush(Color.FromRgb(130, 135, 125));
    public static Brush Accent { get; set; } = new SolidColorBrush(Color.FromRgb(86, 111, 88));
    public static Brush CodeSurface { get; set; } = new SolidColorBrush(Color.FromRgb(238, 239, 231));
    public static Action<string>? ReportError { get; set; }

    /// <summary>从文件内容构造阅读视图；复选框回调携带原文件行号。</summary>
    public static FlowDocument Build(string markdown, double fontSize, Action<int, bool> onTaskChanged, Func<string, UIElement>? imageBuilder = null, IReadOnlyDictionary<int, DateTimeOffset>? taskTimes = null) {
        FlowDocument document = new() {
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = fontSize,
            Foreground = Ink, PagePadding = new Thickness(0, 0, 4, 0),
        };
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        StringBuilder code = new();
        bool inCode = false;
        for (int index = 0; index < lines.Length; index++) {
            string line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) {
                if (inCode) {
                    AddCode(document, code.ToString(), fontSize);
                    code.Clear();
                }
                inCode = !inCode;
                continue;
            }
            if (inCode) {
                code.AppendLine(line);
                continue;
            }
            // 每张附件各占一个块，即使同一行写了多张图片也不会左右并排。
            MatchCollection images = Regex.Matches(line, @"!\[[^\]]*\]\(([^)]+)\)");
            if (imageBuilder != null && images.Count > 0) {
                foreach (Match image in images) {
                    document.Blocks.Add(new BlockUIContainer(imageBuilder(image.Groups[1].Value)));
                }
                line = Regex.Replace(line, @"!\[[^\]]*\]\(([^)]+)\)", "");
            }
            AddLine(document, line, index, fontSize, onTaskChanged, taskTimes);
        }
        if (inCode) {
            AddCode(document, code.ToString(), fontSize);
        }
        return document;
    }

    private static void AddLine(FlowDocument document, string line, int index, double fontSize, Action<int, bool> onTaskChanged, IReadOnlyDictionary<int, DateTimeOffset>? taskTimes) {
        if (string.IsNullOrWhiteSpace(line)) {
            document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 2, 0, 2), FontSize = 3 });
            return;
        }
        Match task = Task.Match(line);
        if (task.Success) {
            AddTask(document, task, index, fontSize, onTaskChanged, taskTimes);
            return;
        }
        Match heading = Heading.Match(line);
        Paragraph paragraph = new() { Margin = new Thickness(0, 2, 0, 9), LineHeight = fontSize * 1.8 };
        if (heading.Success) {
            int level = heading.Groups[1].Value.Length;
            paragraph.FontSize = Math.Max(fontSize + 1, 27 - level * 2);
            paragraph.FontWeight = FontWeights.SemiBold;
            paragraph.Margin = new Thickness(0, 11, 0, 12);
            AddInline(paragraph.Inlines, heading.Groups[2].Value);
        } else if (Regex.IsMatch(line.Trim(), @"^([-*_])(?:\s*\1){2,}$")) {
            document.Blocks.Add(new BlockUIContainer(new Border {
                Height = 1, Background = new SolidColorBrush(Color.FromRgb(225, 225, 215)), Margin = new Thickness(0, 10, 0, 12),
            }));
            return;
        } else if (line.TrimStart().StartsWith('>')) {
            paragraph.BorderBrush = Accent;
            paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
            paragraph.Padding = new Thickness(12, 4, 0, 4);
            paragraph.Foreground = Muted;
            AddInline(paragraph.Inlines, line.TrimStart().Substring(1).TrimStart());
        } else {
            string display = Regex.Replace(line, @"^\s*[-*+]\s+", "•  ");
            AddInline(paragraph.Inlines, display);
        }
        document.Blocks.Add(paragraph);
    }

    private static void AddTask(FlowDocument document, Match task, int index, double fontSize, Action<int, bool> onTaskChanged, IReadOnlyDictionary<int, DateTimeOffset>? taskTimes) {
        bool completed = task.Groups[1].Value != " ";
        TextBlock text = new() { Foreground = Ink, TextWrapping = TextWrapping.Wrap, FontSize = fontSize, LineHeight = fontSize * 1.7, Margin = new Thickness(7, -3, 0, 0) };
        AddInline(text.Inlines, task.Groups[2].Value);
        if (completed) {
            text.Foreground = Muted;
            text.TextDecorations = TextDecorations.Strikethrough;
        }
        StackPanel content = new();
        content.Children.Add(text);
        if (taskTimes != null && taskTimes.TryGetValue(index, out DateTimeOffset added)) {
            content.Children.Add(new TextBlock { Text = added.ToLocalTime().ToString("yyyy/MM/dd HH:mm"), Foreground = Muted, FontSize = Math.Max(9, fontSize * 0.65), Margin = new Thickness(7, 3, 0, 0) });
        }
        CheckBox checkbox = new() {
            Content = content, IsChecked = completed, VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 9), HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        System.Windows.Automation.AutomationProperties.SetName(checkbox, task.Groups[2].Value);
        checkbox.Checked += (_, _) => {
            onTaskChanged(index, true);
            text.Foreground = Muted;
            text.TextDecorations = TextDecorations.Strikethrough;
        };
        checkbox.Unchecked += (_, _) => {
            onTaskChanged(index, false);
            text.Foreground = Ink;
            text.TextDecorations = null;
        };
        document.Blocks.Add(new BlockUIContainer(checkbox) { Margin = new Thickness(0) });
    }

    private static void AddCode(FlowDocument document, string code, double fontSize) {
        Paragraph paragraph = new(new Run(code.TrimEnd('\r', '\n'))) {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = fontSize - 1,
            Background = CodeSurface,
            Padding = new Thickness(12), Margin = new Thickness(0, 5, 0, 12),
        };
        document.Blocks.Add(paragraph);
    }

    private static void AddInline(InlineCollection collection, string text) {
        int offset = 0;
        foreach (Match match in InlinePattern.Matches(text)) {
            if (match.Index > offset) {
                collection.Add(new Run(text.Substring(offset, match.Index - offset)));
            }
            AddMatchedInline(collection, match);
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) {
            collection.Add(new Run(text.Substring(offset)));
        }
    }

    private static void AddMatchedInline(InlineCollection collection, Match match) {
        if (match.Groups[2].Success) {
            collection.Add(new Bold(new Run(match.Groups[2].Value)));
        } else if (match.Groups[3].Success) {
            collection.Add(new Run(match.Groups[3].Value) {
                FontFamily = new FontFamily("Consolas"), Background = CodeSurface,
            });
        } else if (match.Groups[4].Success) {
            // Markdown 不能执行脚本、启动本地文件或运行 HTML；仅允许用户点击 http(s) 链接。
            if (Uri.TryCreate(match.Groups[5].Value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https")) {
                Hyperlink link = new(new Run(match.Groups[4].Value)) { NavigateUri = uri, Foreground = Accent };
                link.RequestNavigate += (_, args) => {
                    try {
                        Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
                    } catch (Exception exception) {
                        ReportError?.Invoke("无法打开链接：" + exception.Message);
                    }
                    args.Handled = true;
                };
                collection.Add(link);
            } else {
                collection.Add(new Run(match.Value));
            }
        } else {
            collection.Add(new Italic(new Run(match.Groups[6].Value)));
        }
    }
}
