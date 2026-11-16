using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private NovelBook? activeBook;
    private string chapterText = "";
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Grid? readingArea;
    private TextBox? scrollText;
    private TextBlock? pageText;
    private TextBlock? pageLabel;
    private TextBlock? chapterLabel;
    private List<int> pageOffsets = new();
    private int pageIndex;
    private bool restoringScroll;
    private bool importing;
    private int searchVersion;

    private void ShowBooks() {
        if (!Navigate("books")) { return; }
        StackPanel body = Page("");
        Button import = PrimaryButton(importing ? "正在导入…" : "＋ 导入小说", ImportNovel);
        import.IsEnabled = !importing;
        import.Margin = new Thickness(0, 0, 0, 18);
        body.Children.Add(import);
        List<NovelBook> books = novelStorage.List();
        if (books.Count == 0) {
            body.Children.Add(Card(Text("留一点时间给故事。\n\n支持 TXT 与无加密的文字 EPUB，\n下次打开会继续上次阅读。", 14, true)));
        }
        foreach (NovelBook book in books) {
            string detail = $"{book.Chapters.Count} 章 · " + (settings.LastBookId == book.Id ? "继续上次阅读" : "开始阅读");
            NavigationRow(body, book.Title, detail, () => OpenBook(book), () => DeleteBook(book));
        }
        SetStatus("本地小说库 · TXT / EPUB · 不联网加载正文");
    }
    private void ImportNovel() {
        if (importing) { return; }
        ImportFileInline("导入小说", new[] { ".txt", ".epub" }, async path => {
            try {
                importing = true;
                SetStatus("正在识别章节…");
                NovelBook book = await Task.Run(() => novelStorage.Import(path));
                OpenBook(book);
            } catch (Exception error) { ShowNotice("无法导入小说", error.Message); }
            finally { importing = false; }
        });
    }
    private void DeleteBook(NovelBook book) {
        ConfirmInline("删除小说", "确定删除“" + book.Title + "”的导入副本及本机阅读进度？原始 TXT / EPUB 文件会保留。", () => {
            // 停止该书尚未落盘的进度与搜索回调，避免删除后再次写回缓存。
            if (activeBook?.Id == book.Id) {
                progressTimer.Stop();
                searchVersion++;
                activeBook = null;
                chapterText = "";
                scrollText = null;
                pageText = null;
            }
            novelStorage.Delete(book);
            if (settings.LastBookId == book.Id) { settings.LastBookId = ""; SaveSettings(); }
            ShowBooks();
        });
    }

    private bool HandleReaderNavigation(Key key) {
        int direction;
        if (key == Key.Down || key == Key.NumPad2) { direction = 1; }
        else if (key == Key.Up || key == Key.NumPad8) { direction = -1; }
        else { return false; }
        if (settings.ScrollReading && scrollText != null) {
            if (restoringScroll) { return true; }
            bool atStart = scrollText.VerticalOffset <= 1;
            bool atEnd = scrollText.VerticalOffset >= scrollText.ExtentHeight - scrollText.ViewportHeight - 1;
            if ((direction < 0 && atStart) || (direction > 0 && atEnd)) {
                TurnPage(direction);
            } else {
                double step = settings.ReaderFontSize * settings.ReaderLineSpacing * 3;
                scrollText.ScrollToVerticalOffset(scrollText.VerticalOffset + direction * step);
            }
        } else { TurnPage(direction); }
        return true;
    }

    private void OpenBook(NovelBook book) {
        if (!Navigate("reader")) { return; }
        activeBook = book;
        if (!settings.RememberReading) {
            book.ChapterIndex = 0;
            book.CharacterOffset = 0;
        }
        chapterText = novelStorage.ReadChapter(book, book.ChapterIndex);
        settings.LastBookId = book.Id;
        SaveSettings();
        BuildReader();
    }
    private Border? readerTopChrome;
    private Border? readerBottomChrome;
    private bool readerChromeShown;
    private void BuildReader() {
        if (activeBook == null) { return; }
        Grid layout = new();
        chapterLabel = Text(activeBook.Chapters[activeBook.ChapterIndex].Title, 13, true);
        chapterLabel.FontWeight = FontWeights.Medium;
        chapterLabel.Margin = new Thickness(8, 18, 8, 0);
        chapterLabel.VerticalAlignment = VerticalAlignment.Top;
        layout.Children.Add(chapterLabel);
        readingArea = new Grid { Margin = new Thickness(settings.ReaderMargin - 8, 56, settings.ReaderMargin - 8, 14), ClipToBounds = true };
        ApplyReaderColors(readingArea);
        readingArea.PreviewMouseWheel += ReaderWheel;
        layout.Children.Add(readingArea);
        DockPanel title = new() { LastChildFill = true };
        title.Children.Add(IconButton("\uE72B", ShowBooks, "返回书架"));
        Button preferences = Button("Aa", OpenReaderSettings);
        preferences.Style = (Style)Resources["QuietButton"];
        DockPanel.SetDock(preferences, Dock.Right);
        title.Children.Add(preferences);
        Button settingsButton = IconButton("\uE713", OpenSettings, "外观设置");
        DockPanel.SetDock(settingsButton, Dock.Right);
        title.Children.Add(settingsButton);
        TextBlock bookTitle = Text(activeBook.Title, 13);
        bookTitle.TextWrapping = TextWrapping.NoWrap;
        bookTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        bookTitle.VerticalAlignment = VerticalAlignment.Center;
        bookTitle.Margin = new Thickness(6, 0, 4, 0);
        title.Children.Add(bookTitle);
        readerTopChrome = Card(title, new Thickness(6));
        readerTopChrome.VerticalAlignment = VerticalAlignment.Top;
        readerTopChrome.Margin = new Thickness(0, 0, 0, 0);
        readerTopChrome.Visibility = Visibility.Hidden;
        readerTopChrome.Opacity = 0;
        readerTopChrome.IsHitTestVisible = false;
        readerTopChrome.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.12 };
        layout.Children.Add(readerTopChrome);
        Grid footer = new();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel left = new() { Orientation = Orientation.Horizontal };
        left.Children.Add(IconButton("\uE8A5", ShowChapters, "章节目录"));
        left.Children.Add(IconButton("\uE721", ShowNovelSearch, "搜索正文"));
        footer.Children.Add(left);
        pageLabel = Text("", 11, true);
        pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        pageLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pageLabel, 1);
        footer.Children.Add(pageLabel);
        StackPanel paging = new() { Orientation = Orientation.Horizontal };
        paging.Children.Add(IconButton("\uE76B", PreviousPage, "上一页"));
        paging.Children.Add(IconButton("\uE76C", NextPage, "下一页"));
        Grid.SetColumn(paging, 2);
        footer.Children.Add(paging);
        readerBottomChrome = Card(footer, new Thickness(6));
        readerBottomChrome.Margin = new Thickness(0);
        readerBottomChrome.VerticalAlignment = VerticalAlignment.Bottom;
        readerBottomChrome.Visibility = Visibility.Hidden;
        readerBottomChrome.Opacity = 0;
        readerBottomChrome.IsHitTestVisible = false;
        readerBottomChrome.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.12 };
        layout.Children.Add(readerBottomChrome);
        readerChromeShown = false;
        contentArea.Children.Add(layout);
        Dispatcher.BeginInvoke(new Action(Repaginate), DispatcherPriority.Loaded);
        progressTimer.Stop();
        progressTimer.Tick -= PersistProgress;
        progressTimer.Tick += PersistProgress;
        SetStatus("阅读中");
    }
    private void UpdateReaderChromeFromPointer(object sender, MouseEventArgs args) {
        if (page != "reader") { return; }
        double y = args.GetPosition(root).Y;
        SetReaderChrome(y < 92 || y > ActualHeight - 92);
    }
    private void SetReaderChrome(bool visible) {
        if (page != "reader" || readerTopChrome == null || readerBottomChrome == null || readerChromeShown == visible) { return; }
        readerChromeShown = visible;
        foreach (Border chrome in new[] { readerTopChrome, readerBottomChrome }) {
            chrome.Visibility = Visibility.Visible;
            chrome.IsHitTestVisible = visible;
            DoubleAnimation fade = new(visible ? 1 : 0, TimeSpan.FromMilliseconds(150));
            chrome.BeginAnimation(UIElement.OpacityProperty, fade);
            chrome.Focusable = visible;
        }
    }
    private void ApplyReaderColors(Grid area) {
        Brush background = Brushes.Transparent;
        if (settings.ReaderColors == "护眼") { background = ColorBrush("#F5F0DF"); }
        if (settings.ReaderColors == "自定义") { background = ColorBrush(settings.ReaderBackground); }
        area.Background = background;
    }
    private Brush ReaderInk() {
        if (settings.ReaderColors == "护眼") { return ColorBrush("#252525"); }
        return settings.ReaderColors == "自定义" ? ColorBrush(settings.ReaderText) : Ink;
    }
    private void Repaginate() {
        if (page != "reader" || readingArea == null || activeBook == null || readingArea.ActualHeight < 20) { return; }
        int wantedOffset = Math.Clamp(activeBook.CharacterOffset, 0, Math.Max(0, chapterText.Length - 1));
        readingArea.Children.Clear();
        scrollText = null;
        pageText = null;
        if (settings.ScrollReading) {
            scrollText = Input(chapterText, "小说正文");
            scrollText.IsReadOnly = true;
            scrollText.TextWrapping = TextWrapping.Wrap;
            scrollText.FontFamily = new FontFamily(settings.ReaderFontFamily);
            scrollText.FontSize = settings.ReaderFontSize;
            scrollText.SetValue(TextBlock.LineHeightProperty, settings.ReaderFontSize * settings.ReaderLineSpacing);
            scrollText.Background = Brushes.Transparent;
            scrollText.Foreground = ReaderInk();
            scrollText.BorderThickness = new Thickness(0);
            scrollText.Padding = new Thickness(0);
            scrollText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scrollText.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ReaderScrolled));
            readingArea.Children.Add(scrollText);
            restoringScroll = true;
            Dispatcher.BeginInvoke(new Action(() => RestoreScroll(wantedOffset)), DispatcherPriority.ContextIdle);
        } else {
            pageOffsets = ReaderPagination.Split(chapterText, settings.ReaderFontFamily, settings.ReaderFontSize, settings.ReaderLineSpacing, readingArea.ActualWidth, readingArea.ActualHeight);
            pageIndex = 0;
            for (int index = 0; index < pageOffsets.Count; index++) {
                if (pageOffsets[index] <= wantedOffset) { pageIndex = index; }
            }
            pageText = new TextBlock { FontFamily = new FontFamily(settings.ReaderFontFamily), FontSize = settings.ReaderFontSize, Foreground = ReaderInk(), TextWrapping = TextWrapping.Wrap, LineHeight = settings.ReaderFontSize * settings.ReaderLineSpacing, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Background = Brushes.Transparent };
            pageText.MouseLeftButtonUp += ReaderClicked;
            readingArea.Children.Add(pageText);
            UpdatePage(false);
        }
        UpdateReaderLabels();
    }
    private void RestoreScroll(int offset) {
        if (scrollText != null) {
            scrollText.UpdateLayout();
            // 返回上一章时直接使用实际滚动终点，避免自定义行高下按行定位只到章节中段。
            if (offset >= chapterText.Length - 1) {
                scrollText.ScrollToEnd();
            } else {
                int line = scrollText.GetLineIndexFromCharacterIndex(Math.Clamp(offset, 0, chapterText.Length));
                scrollText.ScrollToLine(Math.Max(0, line));
            }
        }
        restoringScroll = false;
    }
    private void ReaderScrolled(object sender, ScrollChangedEventArgs args) {
        if (restoringScroll || scrollText == null || activeBook == null || page != "reader") { return; }
        int index = scrollText.GetCharacterIndexFromPoint(new Point(2, 2), true);
        if (index >= 0) { activeBook.CharacterOffset = index; }
        progressTimer.Stop();
        progressTimer.Start();
    }
    private void ReaderClicked(object sender, MouseButtonEventArgs args) {
        if (!settings.ClickPaging || pageText == null) { return; }
        TurnPage(args.GetPosition(pageText).X < pageText.ActualWidth / 2 ? -1 : 1);
    }
    private void ReaderWheel(object sender, MouseWheelEventArgs args) {
        if (args.Delta == 0) { return; }
        int direction = args.Delta < 0 ? 1 : -1;
        if (settings.ScrollReading) {
            // 新章节的定位尚未完成时消费本次滚轮，避免滚动影响正在恢复的阅读位置。
            if (restoringScroll) { args.Handled = true; return; }
            if (scrollText == null) { return; }
            bool atEnd = scrollText.VerticalOffset >= scrollText.ExtentHeight - scrollText.ViewportHeight - 1;
            bool atStart = scrollText.VerticalOffset <= 1;
            bool crossingChapter = direction > 0 ? atEnd : atStart;
            if (!crossingChapter) { return; }
            // 章内滚动交给正文控件；只在章首或章末继续滚动时调用现有翻章与进度保存逻辑。
            TurnPage(direction);
            args.Handled = true;
            return;
        }
        if (!settings.WheelPaging) { return; }
        TurnPage(direction);
        args.Handled = true;
    }
    private void PreviousPage() { TurnPage(-1); }
    private void NextPage() { TurnPage(1); }
    private void TurnPage(int delta) {
        if (activeBook == null) { return; }
        if (settings.ScrollReading && scrollText != null) {
            bool atEnd = scrollText.VerticalOffset >= scrollText.ExtentHeight - scrollText.ViewportHeight - 1;
            bool atStart = scrollText.VerticalOffset <= 1;
            int nextChapter = activeBook.ChapterIndex + delta;
            if (((delta > 0 && atEnd) || (delta < 0 && atStart)) && nextChapter >= 0 && nextChapter < activeBook.Chapters.Count) {
                LoadChapter(nextChapter, delta > 0 ? 0 : int.MaxValue);
                return;
            }
            if (delta > 0) { scrollText.PageDown(); } else { scrollText.PageUp(); }
            return;
        }
        int next = pageIndex + delta;
        if (next >= 0 && next < pageOffsets.Count) {
            pageIndex = next;
            UpdatePage(true);
        } else {
            int chapter = activeBook.ChapterIndex + delta;
            if (chapter >= 0 && chapter < activeBook.Chapters.Count) {
                LoadChapter(chapter, delta < 0 ? int.MaxValue : 0);
            }
        }
    }
    private void UpdatePage(bool animate) {
        if (pageText == null || activeBook == null || pageOffsets.Count == 0) { return; }
        int start = pageOffsets[pageIndex];
        int end = pageIndex + 1 < pageOffsets.Count ? pageOffsets[pageIndex + 1] : chapterText.Length;
        pageText.Text = chapterText.Substring(start, end - start);
        activeBook.CharacterOffset = start;
        if (animate && settings.SmoothPaging) {
            TranslateTransform translation = new();
            pageText.RenderTransform = translation;
            translation.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(140)));
        }
        UpdateReaderLabels();
        progressTimer.Stop();
        progressTimer.Start();
    }
    private void UpdateReaderLabels() {
        if (activeBook == null || pageLabel == null) { return; }
        pageLabel.Text = settings.ScrollReading ? "滚动阅读" : $"{pageIndex + 1} / {pageOffsets.Count} 页";
        if (chapterLabel != null) { chapterLabel.Text = activeBook.Chapters[activeBook.ChapterIndex].Title; }
    }
    private void LoadChapter(int index, int offset) {
        if (activeBook == null) { return; }
        SaveReadingPosition();
        chapterText = novelStorage.ReadChapter(activeBook, index);
        activeBook.ChapterIndex = index;
        activeBook.CharacterOffset = Math.Clamp(offset, 0, Math.Max(0, chapterText.Length - 1));
        Repaginate();
    }
    private void PersistProgress(object? sender, EventArgs args) {
        progressTimer.Stop();
        Run(SaveReadingPosition);
    }
    private void SaveReadingPosition() {
        if (activeBook != null && settings.RememberReading) { novelStorage.SaveProgress(activeBook); }
    }
    private void ShowChapters() {
        if (activeBook == null || !Navigate("chapters")) { return; }
        StackPanel body = Page("章节目录", ResumeReader);
        for (int index = 0; index < activeBook.Chapters.Count; index++) {
            int chapter = index;
            Button item = Button(activeBook.Chapters[index].Title, () => JumpToChapter(chapter, 0));
            if (chapter == activeBook.ChapterIndex) { item.Background = Accent; item.Foreground = (Brush)Resources["OnAccent"]; }
            item.Margin = new Thickness(0, 0, 0, 6);
            body.Children.Add(item);
        }
    }
    private void ResumeReader() {
        if (!Navigate("reader")) { return; }
        BuildReader();
    }
    private void JumpToChapter(int chapter, int offset) {
        if (activeBook == null || !Navigate("reader")) { return; }
        activeBook.ChapterIndex = chapter;
        activeBook.CharacterOffset = offset;
        chapterText = novelStorage.ReadChapter(activeBook, chapter);
        BuildReader();
    }
    private void ShowNovelSearch() {
        if (activeBook == null || !Navigate("search")) { return; }
        StackPanel body = Page("搜索正文", ResumeReader);
        TextBox input = Input("", "搜索关键词");
        body.Children.Add(input);
        StackPanel results = new();
        Button search = Button("搜索", () => SearchBook(input.Text, results));
        search.Margin = new Thickness(0, 8, 0, 14);
        body.Children.Add(search);
        body.Children.Add(results);
        input.KeyDown += (_, args) => {
            if (args.Key == Key.Enter) { Run(() => SearchBook(input.Text, results)); }
        };
        input.Focus();
    }
    private async void SearchBook(string query, StackPanel results) {
        if (activeBook == null || string.IsNullOrWhiteSpace(query)) { return; }
        NovelBook book = activeBook;
        int version = ++searchVersion;
        results.Children.Clear();
        results.Children.Add(Text("正在搜索…", 12, true));
        try {
            List<NovelSearchHit> hits = await Task.Run(() => novelStorage.Search(book, query.Trim()));
            if (page != "search" || activeBook != book || version != searchVersion) { return; }
            results.Children.Clear();
            results.Children.Add(Text($"找到 {hits.Count} 处（最多显示 200 处）", 12, true));
            foreach (NovelSearchHit hit in hits) {
                Button result = Button(hit.Preview, () => JumpToChapter(hit.ChapterIndex, hit.Offset));
                result.Content = Text(hit.Preview, 13);
                result.Margin = new Thickness(0, 8, 0, 0);
                results.Children.Add(result);
            }
        } catch (Exception exception) { SetStatus("搜索失败：" + exception.Message); }
    }
}

/// <summary>依据实际字体与视口重新分页；偏移只前进，保留每个字符且不拆开代理项。</summary>
public static class ReaderPagination {
    /// <summary>返回每页在原章节中的起始文字位置，字号及窗口改变后可重新计算。</summary>
    public static List<int> Split(string text, string font, double size, double spacing, double width, double height) {
        List<int> offsets = new();
        int start = 0;
        while (start < text.Length) {
            offsets.Add(start);
            int low = 1;
            int high = Math.Min(6000, text.Length - start);
            int fit = 1;
            while (low <= high) {
                int length = (low + high) / 2;
                // 与正文使用相同控件和固定行高测量，避免字体回退或行高差异把末行裁掉。
                TextBlock measured = new() { Text = text.Substring(start, length), FontFamily = new FontFamily(font), FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * spacing, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
                measured.Measure(new Size(Math.Max(20, width - 2), double.PositiveInfinity));
                if (measured.DesiredSize.Height <= Math.Max(size * spacing, height - 4)) { fit = length; low = length + 1; } else { high = length - 1; }
            }
            if (fit > 1 && start + fit < text.Length && char.IsHighSurrogate(text[start + fit - 1])) { fit--; }
            if (fit == 1 && start + 1 < text.Length && char.IsHighSurrogate(text[start])) { fit = 2; }
            start += fit;
        }
        if (offsets.Count == 0) { offsets.Add(0); }
        return offsets;
    }
}
