using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopNotes;

/// <summary>Markdown 正文的唯一存储；保存前核对磁盘内容，避免覆盖外部编辑。</summary>
public static class NoteStorage {
    public const int MaxFileBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex TaskLine = new(@"^(\s*[-*+]\s+\[)[ xX](\]\s*.*)$");

    /// <summary>创建尚不存在的笔记；已有文件始终保留。</summary>
    public static void EnsureFile(string path, string initialContent) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) {
            return;
        }
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        byte[] bytes = Utf8.GetBytes(initialContent);
        stream.Write(bytes);
    }

    /// <summary>读取 UTF-8 或带 BOM 的 Unicode 文件；大文件和无效编码明确报错。</summary>
    public static string Read(string path) {
        FileInfo file = new(path);
        if (!file.Exists) {
            throw new IOException("文件已被移动或删除，请重新选择文件。");
        }
        if (file.Length > MaxFileBytes) {
            throw new IOException("初版支持 2 MB 以内的 Markdown 文件，请选择较小的文件。");
        }
        using StreamReader reader = new(path, Utf8, true);
        return reader.ReadToEnd();
    }

    /// <summary>仅在磁盘仍与读取版本一致时写入，通过同目录替换避免留下半个文件。</summary>
    public static void Save(string path, string expectedContent, string newContent) {
        if (Utf8.GetByteCount(newContent) > MaxFileBytes) {
            throw new IOException("内容超过 2 MB，尚未保存。请先复制到其他编辑器分拆。");
        }
        string current = Read(path);
        if (current != expectedContent) {
            throw new NoteConflictException();
        }
        if (current == newContent) {
            return;
        }

        // 临时文件与目标处于同一目录；写完并刷新后再原子替换原文件。
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] bytes = Utf8.GetBytes(newContent);
                stream.Write(bytes);
                stream.Flush(true);
            }
            // 写临时文件期间若外部发生修改，保留双方内容并由界面询问。
            if (Read(path) != expectedContent) {
                throw new NoteConflictException();
            }
            File.Replace(temporary, path, null);
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>只改指定行的复选框标记，保留缩进、正文和原文件换行风格。</summary>
    public static string ToggleTask(string content, int lineIndex, bool completed) {
        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = content.Replace("\r\n", "\n").Split('\n');
        if (lineIndex < 0 || lineIndex >= lines.Length || !TaskLine.IsMatch(lines[lineIndex])) {
            throw new InvalidOperationException("该待办行已发生变化，请重新读取文件。");
        }
        Match match = TaskLine.Match(lines[lineIndex]);
        lines[lineIndex] = match.Groups[1].Value + (completed ? "x" : " ") + match.Groups[2].Value;
        return string.Join(newline, lines);
    }
}

/// <summary>磁盘文件与当前编辑基线不一致，需要用户选择要保留的版本。</summary>
public sealed class NoteConflictException : IOException {
    public NoteConflictException() : base("文件被其他软件修改，已暂停保存。你的编辑仍保留在卡片里。") { }
}

/// <summary>只保存卡片外观、位置及文件路径，不存放日记和待办正文。</summary>
public sealed class AppSettings {
    public const int MinimumWidth = 160;
    public const int MinimumHeight = 80;
    public string NotesDirectory { get; set; } = "";
    public string TodoFile { get; set; } = "";
    public string CustomDiaryFile { get; set; } = "";
    public string SelectedDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public bool DiarySelected { get; set; }
    public bool LayoutLocked { get; set; }
    public bool EdgeAutoHideEnabled { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 6;
    public uint HotkeyKey { get; set; } = 0x54;
    public double FontSize { get; set; } = 15;
    public double UiSize { get; set; } = 1;
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
    public int Width { get; set; } = 460;
    public int Height { get; set; } = 650;
    public int DrawerVersion { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool NightMode { get; set; }
    public string AccentColor { get; set; } = "#8AA675";
    public string DayBackground { get; set; } = "#F6F7F9";
    public string NightBackground { get; set; } = "#181A1F";
    public double BackgroundOpacity { get; set; } = 0.98;
    public bool CustomDayText { get; set; }
    public bool CustomNightText { get; set; }
    public string DayTextColor { get; set; } = "#171717";
    public string NightTextColor { get; set; } = "#F5F5F5";
    public string ScreenDevice { get; set; } = "";
    public bool LeftEdge { get; set; }
    public double ReaderFontSize { get; set; } = 18;
    public double ReaderLineSpacing { get; set; } = 1.8;
    public int ReaderMargin { get; set; } = 24;
    public string ReaderFontFamily { get; set; } = "Microsoft YaHei UI";
    public string ReaderColors { get; set; } = "跟随主题";
    public string ReaderBackground { get; set; } = "#F5F0DF";
    public string ReaderText { get; set; } = "#252525";
    public bool ScrollReading { get; set; }
    public bool WheelPaging { get; set; } = true;
    public bool ClickPaging { get; set; } = true;
    public bool SmoothPaging { get; set; } = true;
    public bool RememberReading { get; set; } = true;
    public string LastBookId { get; set; } = "";

    /// <summary>从指定配置目录读取设置；损坏时通知调用方，不静默覆盖原文件。</summary>
    public static AppSettings Load(string configDirectory, string notesDirectory) {
        string path = Path.Combine(configDirectory, "settings.json");
        AppSettings settings = File.Exists(path)
            ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new IOException("配置内容为空。")
            : new AppSettings();
        if (string.IsNullOrWhiteSpace(settings.NotesDirectory)) {
            settings.NotesDirectory = notesDirectory;
        }
        if (string.IsNullOrWhiteSpace(settings.TodoFile)) {
            settings.TodoFile = Path.Combine(settings.NotesDirectory, "待办.md");
        }
        settings.FontSize = Math.Clamp(settings.FontSize, 12, 24);
        if (settings.UiSize != 0.8 && settings.UiSize != 1 && settings.UiSize != 1.2) { settings.UiSize = 1; }
        settings.Width = Math.Clamp(settings.Width, MinimumWidth, 1600);
        settings.Height = Math.Clamp(settings.Height, MinimumHeight, 1600);
        settings.ReaderFontSize = Math.Clamp(settings.ReaderFontSize, 12, 40);
        settings.ReaderLineSpacing = Math.Clamp(settings.ReaderLineSpacing, 1.2, 2.5);
        settings.ReaderMargin = Math.Clamp(settings.ReaderMargin, 8, 60);
        settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.2, 1);
        return settings;
    }

    /// <summary>保存卡片设置；调用方负责将写入失败反馈到界面。</summary>
    public void Save(string configDirectory) {
        Directory.CreateDirectory(configDirectory);
        string path = Path.Combine(configDirectory, "settings.json");
        string temporary = Path.Combine(configDirectory, "settings.pending.json");
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }
}
