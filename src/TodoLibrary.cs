using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopNotes;

/// <summary>一份 Markdown 文件对应一个父待办，复选清单对应其子待办。</summary>
public sealed class TodoEntry {
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Name => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;
    public string Content { get; set; } = "";
    public bool Completed { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int Total { get; set; }
    public int Done { get; set; }
    public List<TodoChildStamp> Children { get; set; } = new();
}

public sealed class TodoChildStamp {
    public string Text { get; set; } = "";
    public int Line { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>本地文件存储保持旧版兼容，元数据放在 Markdown 注释中。</summary>
public sealed class TodoLibrary {
    public const string CompletedMarker = "<!-- desktop-notes:completed -->\n";
    private readonly string directory;
    private readonly string legacyFile;

    /// <summary>使用现有笔记根目录中的英文待办分类，旧版绑定文件仍参与展示。</summary>
    public TodoLibrary(AppSettings settings) {
        directory = System.IO.Path.Combine(settings.NotesDirectory, "todos");
        legacyFile = settings.TodoFile;
        Directory.CreateDirectory(directory);
    }

    /// <summary>读取本地父待办，未完成项优先，随后按添加时间倒序。</summary>
    public List<TodoEntry> List() {
        List<TodoEntry> result = new();
        foreach (string path in Directory.EnumerateFiles(directory, "*.md")) { result.Add(Read(path)); }
        if (File.Exists(legacyFile) && !result.Exists(item => string.Equals(item.Path, legacyFile, StringComparison.OrdinalIgnoreCase))) {
            result.Add(Read(legacyFile));
        }
        result.Sort((first, second) => {
            int completed = first.Completed.CompareTo(second.Completed);
            return completed != 0 ? completed : second.CreatedAt.CompareTo(first.CreatedAt);
        });
        return result;
    }

    /// <summary>解析元数据和两级清单，旧文件没有添加时间时使用其文件创建时间。</summary>
    public static TodoEntry Read(string path) {
        TodoEntry entry = new() { Path = path, Content = NoteStorage.Read(path), CreatedAt = new DateTimeOffset(File.GetCreationTime(path)) };
        string[] lines = entry.Content.Replace("\r\n", "\n").Split('\n');
        int offset = 0;
        while (offset < lines.Length && lines[offset].StartsWith("<!--", StringComparison.Ordinal)) {
            string line = lines[offset];
            if (line == CompletedMarker.TrimEnd('\n')) { entry.Completed = true; }
            else if (line.StartsWith("<!-- luma:created=", StringComparison.Ordinal)) {
                if (DateTimeOffset.TryParse(line.Substring(18).Replace(" -->", ""), out DateTimeOffset created)) { entry.CreatedAt = created; }
            } else if (line.StartsWith("<!-- luma:title=", StringComparison.Ordinal)) {
                try { entry.Title = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(16).Replace(" -->", ""))); }
                catch (FormatException) { throw new IOException("待办标题元数据损坏，请保留原文件后检查。"); }
            } else if (line.StartsWith("<!-- luma:subtasks=", StringComparison.Ordinal)) {
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(19).Replace(" -->", "")));
                entry.Children = JsonSerializer.Deserialize<List<TodoChildStamp>>(json) ?? new();
            } else { break; }
            offset++;
        }
        entry.Body = string.Join("\n", lines, offset, lines.Length - offset);
        entry.Children = ReadChildren(entry.Body, entry.Children, entry.CreatedAt);
        bool code = false;
        foreach (string line in entry.Body.Split('\n')) {
            if (line.TrimStart().StartsWith(new string((char)96, 3), StringComparison.Ordinal)) { code = !code; continue; }
            if (!code && Regex.IsMatch(line, @"^\s*[-*+]\s+\[[ xX]\]")) {
                entry.Total++;
                if (Regex.IsMatch(line, @"^\s*[-*+]\s+\[[xX]\]")) { entry.Done++; }
            }
        }
        return entry;
    }

    /// <summary>创建包含标题、正文及不可变添加时间的父待办。</summary>
    public TodoEntry Create(string title, string body) {
        title = title.Trim();
        if (title.Length == 0 || title.Length > 80 || title.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || title.EndsWith('.')) {
            throw new IOException("标题需为 1～80 个字符，不能包含路径或文件名特殊字符。");
        }
        string path = System.IO.Path.Combine(directory, title + ".md");
        string content = Compose(title, body, DateTimeOffset.Now, false);
        if (Encoding.UTF8.GetByteCount(content) > NoteStorage.MaxFileBytes) { throw new IOException("待办正文超过 2 MB。"); }
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            using StreamWriter writer = new(stream, new UTF8Encoding(false));
            writer.Write(content);
        }
        return Read(path);
    }

    /// <summary>修改标题与正文时保留添加时间，存储层仍检查外部编辑冲突。</summary>
    public static TodoEntry Update(TodoEntry entry, string title, string body) {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 80) { throw new IOException("标题需为 1～80 个字符。"); }
        NoteStorage.Save(entry.Path, entry.Content, Compose(title.Trim(), body, entry.CreatedAt, entry.Completed, entry.Children));
        return Read(entry.Path);
    }

    private static string Compose(string title, string body, DateTimeOffset created, bool completed, List<TodoChildStamp>? previous = null) {
        string marker = completed ? CompletedMarker : "";
        List<TodoChildStamp> children = ReadChildren(body, previous ?? new(), DateTimeOffset.Now);
        string metadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(children)));
        return marker + "<!-- luma:created=" + created.ToString("O") + " -->\n"
            + "<!-- luma:title=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(title)) + " -->\n"
            + "<!-- luma:subtasks=" + metadata + " -->\n" + body;
    }

    private static List<TodoChildStamp> ReadChildren(string body, List<TodoChildStamp> previous, DateTimeOffset fallback) {
        Dictionary<string, Queue<TodoChildStamp>> dates = new(StringComparer.Ordinal);
        foreach (TodoChildStamp item in previous) {
            if (!dates.TryGetValue(item.Text, out Queue<TodoChildStamp>? queue)) { queue = new(); dates[item.Text] = queue; }
            queue.Enqueue(item);
        }
        List<TodoChildStamp> children = new();
        string[] lines = body.Replace("\r\n", "\n").Split('\n');
        bool code = false;
        for (int index = 0; index < lines.Length; index++) {
            if (lines[index].TrimStart().StartsWith(new string((char)96, 3), StringComparison.Ordinal)) { code = !code; continue; }
            Match match = Regex.Match(lines[index], @"^\s*[-*+]\s+\[[ xX]\]\s*(.*)$");
            if (code || !match.Success) { continue; }
            string text = match.Groups[1].Value;
            DateTimeOffset added = fallback;
            if (dates.TryGetValue(text, out Queue<TodoChildStamp>? queue) && queue.Count > 0) { added = queue.Dequeue().CreatedAt; }
            children.Add(new TodoChildStamp { Text = text, Line = index, CreatedAt = added });
        }
        return children;
    }

    /// <summary>父待办的完成状态独立保存，不覆盖各个子待办的完成状态。</summary>
    public static void SetCompleted(TodoEntry entry, bool completed) {
        NoteStorage.Save(entry.Path, entry.Content, Compose(entry.Name, entry.Body, entry.CreatedAt, completed, entry.Children));
    }

    /// <summary>仅在二次确认后调用，删除前核对文件仍为界面显示的版本。</summary>
    public static void Delete(TodoEntry entry) {
        if (NoteStorage.Read(entry.Path) != entry.Content) { throw new NoteConflictException(); }
        File.Delete(entry.Path);
    }
}
