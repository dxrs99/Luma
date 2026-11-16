using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DesktopNotes;

public sealed class NovelChapter {
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
}

public sealed class NovelBook {
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<NovelChapter> Chapters { get; set; } = new();
    public int ChapterIndex { get; set; }
    public int CharacterOffset { get; set; }
}

public sealed class NovelSearchHit {
    public int ChapterIndex { get; set; }
    public int Offset { get; set; }
    public string Preview { get; set; } = "";
}

/// <summary>按章节分文件缓存小说；阅读与搜索每次只读一章，避免全书常驻内存。</summary>
public sealed class NovelStorage {
    private const int MaxImportBytes = 64 * 1024 * 1024;
    private const int MaxChapterCharacters = 40000;
    private readonly string directory;
    private static readonly Regex ChapterTitle = new(@"^(第[零〇一二三四五六七八九十百千万两0-9]+[章节卷回部篇集].{0,60}|[序楔终]章.{0,40}|尾声.{0,40}|Chapter\s+\d+.{0,60})$", RegexOptions.IgnoreCase);

    /// <summary>小说库位于项目笔记目录，不修改或移动用户导入的原书。</summary>
    public NovelStorage(string notesDirectory) {
        directory = Path.Combine(notesDirectory, "novels");
        Directory.CreateDirectory(directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>列出成功导入的书籍；未完成导入的目录不会进入书架。</summary>
    public List<NovelBook> List() {
        List<NovelBook> result = new();
        foreach (string folder in Directory.EnumerateDirectories(directory)) {
            string manifest = Path.Combine(folder, "book.json");
            if (!File.Exists(manifest)) {
                continue;
            }
            NovelBook book = JsonSerializer.Deserialize<NovelBook>(File.ReadAllText(manifest)) ?? throw new IOException("小说索引为空：" + manifest);
            ValidateBook(book);
            result.Add(book);
        }
        return result;
    }

    /// <summary>导入 TXT 或无加密 EPUB，成功后才写入书架索引；返回实际章节信息。</summary>
    public NovelBook Import(string path) {
        if (new FileInfo(path).Length > MaxImportBytes) {
            throw new IOException("请选择 64 MB 以内的小说文件。");
        }
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".txt" && extension != ".epub") {
            throw new IOException("目前支持 TXT 和无加密的文字 EPUB。");
        }
        NovelBook book = new() { Id = Guid.NewGuid().ToString("N"), Title = Path.GetFileNameWithoutExtension(path) };
        Directory.CreateDirectory(BookDirectory(book));
        if (extension == ".txt") {
            ImportText(path, book);
        } else {
            ImportEpub(path, book);
        }
        if (book.Chapters.Count == 0) {
            throw new IOException("没有找到可阅读的文字，可能是扫描版或加密电子书。");
        }
        SaveProgress(book);
        return book;
    }

    private void ImportText(string path, NovelBook book) {
        // 先验证 UTF-8 编码；无效时按常见中文 GB18030 重读，避免静默出现替代字符。
        Encoding encoding = new UTF8Encoding(false, true);
        try {
            using StreamReader probe = new(path, encoding, true);
            char[] buffer = new char[8192];
            while (probe.Read(buffer, 0, buffer.Length) > 0) { }
        } catch (DecoderFallbackException) {
            encoding = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        using StreamReader reader = new(path, encoding, true);
        ImportLines(reader, book, "正文");
    }

    private void ImportLines(TextReader reader, NovelBook book, string defaultTitle) {
        StringBuilder body = new();
        string title = defaultTitle;
        string? line;
        while ((line = reader.ReadLine()) != null) {
            string trimmed = line.Trim();
            bool heading = ChapterTitle.IsMatch(trimmed);
            if (heading || body.Length + line.Length > MaxChapterCharacters) {
                AddChapter(book, title, body.ToString());
                body.Clear();
                title = heading ? trimmed : title + "（续）";
            }
            if (!heading) {
                body.AppendLine(line);
            }
        }
        AddChapter(book, title, body.ToString());
    }

    private void ImportEpub(string path, NovelBook book) {
        using ZipArchive archive = ZipFile.OpenRead(path);
        long expanded = 0;
        foreach (ZipArchiveEntry entry in archive.Entries) {
            expanded += entry.Length;
            if (entry.Length > 8 * 1024 * 1024 || expanded > MaxImportBytes * 4L) {
                throw new IOException("EPUB 解压内容过大，请选择文字版小说。");
            }
        }
        if (archive.GetEntry("META-INF/encryption.xml") != null) {
            throw new IOException("暂不支持包含加密资源的 EPUB，请导入无加密版本。");
        }
        XDocument container = ReadXml(RequiredEntry(archive, "META-INF/container.xml"));
        string opfPath = container.Descendants().FirstOrDefault(element => element.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value ?? throw new IOException("EPUB 缺少阅读索引。");
        XDocument opf = ReadXml(RequiredEntry(archive, opfPath));
        string? title = opf.Descendants().FirstOrDefault(element => element.Name.LocalName == "title")?.Value;
        if (!string.IsNullOrWhiteSpace(title)) {
            book.Title = title;
        }
        Dictionary<string, string> manifest = new();
        foreach (XElement item in opf.Descendants().Where(element => element.Name.LocalName == "item")) {
            manifest[item.Attribute("id")?.Value ?? ""] = item.Attribute("href")?.Value ?? "";
        }
        foreach (XElement item in opf.Descendants().Where(element => element.Name.LocalName == "itemref")) {
            string id = item.Attribute("idref")?.Value ?? "";
            if (!manifest.TryGetValue(id, out string? href) || item.Attribute("linear")?.Value == "no") {
                continue;
            }
            // 以 OPF 的阅读顺序解析压缩包内部路径；不解压到外部目录、不加载外链与脚本。
            Uri resolved = new(new Uri("https://epub.local/" + opfPath), href);
            if (resolved.Host != "epub.local") {
                throw new IOException("EPUB 包含外部正文链接，不能离线导入。");
            }
            string entryPath = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
            ZipArchiveEntry entry = RequiredEntry(archive, entryPath);
            using StreamReader htmlReader = new(entry.Open(), Encoding.UTF8, true);
            string html = htmlReader.ReadToEnd();
            string chapterTitle = Regex.Match(html, @"<h[12]\b[^>]*>(.*?)</h[12]>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value;
            chapterTitle = HtmlText(chapterTitle).Trim();
            if (chapterTitle.Length == 0) {
                chapterTitle = "章节 " + (book.Chapters.Count + 1);
            }
            using StringReader textReader = new(HtmlText(html));
            ImportLines(textReader, book, chapterTitle);
        }
    }

    private static string HtmlText(string html) {
        string text = Regex.Replace(html, @"<(script|style|head)\b[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"</?(p|div|h[1-6]|br|li|section)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\n[\t \r]*\n(?:[\t \r]*\n)+", "\n\n");
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string path) {
        return archive.GetEntry(path) ?? throw new IOException("EPUB 缺少文件：" + path);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry) {
        using Stream stream = entry.Open();
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private void AddChapter(NovelBook book, string title, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }
        // 单行超长文本也切成小段章节；索引只记录本应用生成的安全文件名。
        int offset = 0;
        while (offset < text.Length) {
            int length = Math.Min(MaxChapterCharacters, text.Length - offset);
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) {
                length--;
            }
            string file = book.Chapters.Count.ToString("D5") + ".txt";
            File.WriteAllText(Path.Combine(BookDirectory(book), file), text.Substring(offset, length), new UTF8Encoding(false));
            book.Chapters.Add(new NovelChapter { Title = title, File = file });
            offset += length;
        }
    }

    /// <summary>读取单章正文，用于阅读、分页和搜索；文件名必须来自有效索引。</summary>
    public string ReadChapter(NovelBook book, int index) {
        ValidateBook(book);
        return File.ReadAllText(Path.Combine(BookDirectory(book), book.Chapters[index].File));
    }

    /// <summary>按章流式搜索，最多返回 200 条；位置为章节内的文字偏移。</summary>
    public List<NovelSearchHit> Search(NovelBook book, string query) {
        List<NovelSearchHit> hits = new();
        if (string.IsNullOrWhiteSpace(query)) {
            return hits;
        }
        for (int chapter = 0; chapter < book.Chapters.Count; chapter++) {
            string text = ReadChapter(book, chapter);
            int offset = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            while (offset >= 0 && hits.Count < 200) {
                int start = Math.Max(0, offset - 12);
                int length = Math.Min(text.Length - start, query.Length + 36);
                hits.Add(new NovelSearchHit { ChapterIndex = chapter, Offset = offset, Preview = book.Chapters[chapter].Title + "\n" + text.Substring(start, length).Replace('\n', ' ') });
                offset = text.IndexOf(query, offset + query.Length, StringComparison.OrdinalIgnoreCase);
            }
            if (hits.Count == 200) {
                break;
            }
        }
        return hits;
    }

    /// <summary>保存章节与文字偏移，改变字号或窗口大小后也能回到原文附近。</summary>
    public void SaveProgress(NovelBook book) {
        ValidateBook(book);
        string path = Path.Combine(BookDirectory(book), "book.json");
        string pending = path + ".pending";
        File.WriteAllText(pending, JsonSerializer.Serialize(book, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(pending, path, true);
    }

    private string BookDirectory(NovelBook book) {
        if (!Guid.TryParseExact(book.Id, "N", out _)) {
            throw new IOException("小说索引标识无效。");
        }
        return Path.Combine(directory, book.Id);
    }

    /// <summary>删除指定小说的导入副本和阅读进度，保留用户最初选择的 TXT 或 EPUB 文件。</summary>
    public void Delete(NovelBook book) {
        ValidateBook(book);
        string folder = Path.GetFullPath(BookDirectory(book));
        string library = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(library, StringComparison.OrdinalIgnoreCase)) { throw new IOException("小说目录不属于当前书库。"); }
        if (!Directory.Exists(folder)) { return; }
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { throw new IOException("小说目录是链接，已停止删除。"); }
        Directory.Delete(folder, true);
    }

    private static void ValidateBook(NovelBook book) {
        if (!Guid.TryParseExact(book.Id, "N", out _) || book.Chapters.Count == 0) {
            throw new IOException("小说索引不完整。");
        }
        foreach (NovelChapter chapter in book.Chapters) {
            if (!Regex.IsMatch(chapter.File, @"^\d{5,}\.txt$")) {
                throw new IOException("小说章节路径无效。");
            }
        }
        book.ChapterIndex = Math.Clamp(book.ChapterIndex, 0, book.Chapters.Count - 1);
        book.CharacterOffset = Math.Max(0, book.CharacterOffset);
    }
}
