using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace DesktopNotes;

/// <summary>附件仅保存在当前笔记的 images 目录，不从网络加载图片。</summary>
internal static class ImageStore {
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    private const long MaxBytes = 20 * 1024 * 1024;

    /// <summary>验证本地图片后复制为独立附件，返回 Markdown 使用的相对路径。</summary>
    public static string Import(string notesDirectory, string source) {
        string extension = Path.GetExtension(source).ToLowerInvariant();
        if (!Extensions.Contains(extension) || !File.Exists(source) || new FileInfo(source).Length > MaxBytes) {
            throw new IOException("请选择 20 MB 以内的 PNG、JPG、BMP 或 GIF 图片。");
        }
        Read(source);
        string folder = Path.Combine(notesDirectory, "images");
        Directory.CreateDirectory(folder);
        string name = Guid.NewGuid().ToString("N") + extension;
        File.Copy(source, Path.Combine(folder, name), false);
        return "images/" + name;
    }

    /// <summary>将剪贴板位图编码为 PNG 附件，限制像素总数以免编辑器加载超大图片。</summary>
    public static string SaveClipboard(string notesDirectory, BitmapSource bitmap) {
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 24000000) { throw new IOException("图片超过 2400 万像素。"); }
        string folder = Path.Combine(notesDirectory, "images");
        Directory.CreateDirectory(folder);
        string name = Guid.NewGuid().ToString("N") + ".png";
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = new(Path.Combine(folder, name), FileMode.CreateNew);
        encoder.Save(file);
        return "images/" + name;
    }

    /// <summary>仅解析 images 目录内的本地附件，拒绝外部路径、目录穿越及远程地址。</summary>
    public static string Resolve(string notesDirectory, string relative) {
        string folder = Path.GetFullPath(Path.Combine(notesDirectory, "images")) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(notesDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) {
            throw new IOException("图片路径不属于当前笔记附件目录。");
        }
        return path;
    }

    /// <summary>缩略图按解码宽度读取，关闭文件后仍可显示。</summary>
    public static BitmapImage Read(string path) {
        if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) { throw new IOException("图片不存在或超过 20 MB。"); }
        BitmapImage bitmap = new();
        using FileStream stream = File.OpenRead(path);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 1600;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
