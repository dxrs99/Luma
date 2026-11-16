using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopNotes;

public sealed partial class MainWindow {
    private readonly DispatcherTimer edgeHideTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private DateTime? edgeOutsideSince;
    private DispatcherTimer? edgeSlideTimer;
    private EdgeTabWindow? edgeTab;
    private string dockEdge = "";
    private bool edgeHidden;
    private bool edgeAnimating;
    private DesktopHost.NativeRect edgeBounds;

    private void InitializeEdgeHide() {
        // 持续检查而非只等一次 MouseLeave；捕获释放、弹层关闭、外部移动后都会重新开始等待。
        edgeHideTimer.Tick += (_, _) => CheckEdgeHide();
        UpdateDockEdge();
        edgeHideTimer.Start();
    }

    private void CheckEdgeHide() {
        if (!IsVisible || edgeHidden || edgeAnimating || transitioning || modalOpen || windowManipulating || Mouse.Captured != null) {
            edgeOutsideSince = null;
            return;
        }
        UpdateDockEdge();
        if (dockEdge.Length == 0 || desktopHost == null) { edgeOutsideSince = null; return; }
        DesktopHost.NativeRect bounds = desktopHost.Bounds;
        DesktopHost.NativePoint pointer = DesktopHost.CursorPosition();
        bool pointerInside = pointer.X >= bounds.Left && pointer.X < bounds.Right && pointer.Y >= bounds.Top && pointer.Y < bounds.Bottom;
        if (pointerInside) { edgeOutsideSince = null; return; }
        edgeOutsideSince ??= DateTime.UtcNow;
        if ((DateTime.UtcNow - edgeOutsideSince.Value).TotalMilliseconds >= 300) {
            edgeOutsideSince = null;
            HideAtEdge();
        }
    }

    private void UpdateDockEdge() {
        if (desktopHost == null || edgeHidden || edgeAnimating) { return; }
        string previous = dockEdge;
        dockEdge = "";
        if (!settings.EdgeAutoHideEnabled) { edgeOutsideSince = null; return; }
        DesktopHost.NativeRect bounds = desktopHost.Bounds;
        System.Drawing.Rectangle work = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height)).WorkingArea;
        int threshold = (int)Math.Ceiling(32 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        if (bounds.Left <= work.Left + threshold) { dockEdge = "left"; }
        else if (bounds.Right >= work.Right - threshold) { dockEdge = "right"; }
        else if (bounds.Top <= work.Top + threshold) { dockEdge = "top"; }
        else if (bounds.Bottom >= work.Bottom - threshold) { dockEdge = "bottom"; }
        if (dockEdge != previous) { edgeOutsideSince = null; }
    }

    private void HideAtEdge() {
        if (dockEdge.Length == 0 || edgeHidden || !IsVisible || modalOpen || !SaveCurrent()) { return; }
        edgeBounds = desktopHost!.Bounds;
        System.Drawing.Rectangle work = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(edgeBounds.Left, edgeBounds.Top, edgeBounds.Width, edgeBounds.Height)).WorkingArea;
        edgeTab ??= new EdgeTabWindow(() => RestoreFromEdge(true), ((SolidColorBrush)Accent).Color);
        bool vertical = dockEdge == "left" || dockEdge == "right";
        int tabWidth = vertical ? 12 : 72;
        int tabHeight = vertical ? 72 : 12;
        int tabLeft = Math.Clamp(edgeBounds.Left + edgeBounds.Width / 2 - tabWidth / 2, work.Left, work.Right - tabWidth);
        int tabTop = Math.Clamp(edgeBounds.Top + edgeBounds.Height / 2 - tabHeight / 2, work.Top, work.Bottom - tabHeight);
        if (dockEdge == "left") { tabLeft = work.Left; }
        if (dockEdge == "right") { tabLeft = work.Right - tabWidth; }
        if (dockEdge == "top") { tabTop = work.Top; }
        if (dockEdge == "bottom") { tabTop = work.Bottom - tabHeight; }
        int targetLeft = edgeBounds.Left;
        int targetTop = edgeBounds.Top;
        if (dockEdge == "left") { targetLeft = work.Left - edgeBounds.Width; }
        if (dockEdge == "right") { targetLeft = work.Right; }
        if (dockEdge == "top") { targetTop = work.Top - edgeBounds.Height; }
        if (dockEdge == "bottom") { targetTop = work.Bottom; }
        edgeHidden = true;
        SlideEdgeWindow(targetLeft, targetTop, () => {
            Hide();
            edgeTab.Place(tabLeft, tabTop, tabWidth, tabHeight);
        });
    }

    private void RestoreFromEdge(bool explicitRequest) {
        if (!edgeHidden || edgeAnimating) { return; }
        edgeOutsideSince = null;
        // 必须在显示主窗口之前检查，且不能用“显示桌面”收起用户正在使用的应用。
        bool previousMode = settings.AlwaysOnTop;
        if (explicitRequest && !previousMode && DesktopHost.HasVisibleApplication()) { settings.AlwaysOnTop = true; }
        edgeTab?.Hide();
        Show();
        edgeHidden = false;
        if (!EnsureAttached(revealTopmost: true)) {
            settings.AlwaysOnTop = previousMode;
            EnsureAttached();
        }
        UpdateModeButtons();
        UpdateRoundedClip();
        SlideEdgeWindow(edgeBounds.Left, edgeBounds.Top, () => {
            Activate();
            SaveBounds();
        });
    }

    private void SlideEdgeWindow(int left, int top, Action completed) {
        edgeSlideTimer?.Stop();
        edgeAnimating = true;
        ApplyWindowMaterial();
        DesktopHost.NativeRect start = desktopHost!.Bounds;
        DateTime started = DateTime.UtcNow;
        edgeSlideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        edgeSlideTimer.Tick += (_, _) => {
            double progress = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 180, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 3);
            desktopHost.Move((int)Math.Round(start.Left + (left - start.Left) * eased), (int)Math.Round(start.Top + (top - start.Top) * eased), start.Width, start.Height);
            if (progress < 1) { return; }
            edgeSlideTimer.Stop();
            edgeAnimating = false;
            ApplyWindowMaterial();
            completed();
        };
        edgeSlideTimer.Start();
    }

    private void DisposeEdgeHide() {
        edgeHideTimer.Stop();
        edgeSlideTimer?.Stop();
        edgeTab?.Close();
    }
}
