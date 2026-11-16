using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace DesktopNotes;

/// <summary>独立 EXE 入口：单实例、托盘以及 Explorer 重建后的卡片恢复。</summary>
public static class Program {
    /// <summary>启动桌面卡片或执行隔离验证；返回进程退出码，0 表示成功。</summary>
    [STAThread]
    public static int Main(string[] args) {
        if (args.Contains("--tray-icon-test")) { return FeatureVerification.VerifyTrayIcon(); }
        if (args.Contains("--screen-color-target")) { return FeatureVerification.RunScreenColorTarget(); }
        string projectDirectory = Directory.Exists(@"D:\ChatGPT")
            ? @"D:\ChatGPT\Luma"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopNotes");
        if (args.Any(value => value.EndsWith("-test", StringComparison.Ordinal))) {
            return FeatureVerification.CheckAll(projectDirectory, args.First(value => value.EndsWith("-test", StringComparison.Ordinal)));
        }
        string configDirectory = Argument(args, "--config-dir") ?? Path.Combine(projectDirectory, "config");
        string notesDirectory = Argument(args, "--data-dir") ?? Path.Combine(projectDirectory, "notes");
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(configDirectory)))).Substring(0, 20);
        using Mutex mutex = new(true, @"Local\DesktopNotes_" + identity, out bool firstInstance);
        if (!firstInstance) {
            try {
                using EventWaitHandle signal = EventWaitHandle.OpenExisting(@"Local\DesktopNotes_Show_" + identity);
                signal.Set();
            } catch (WaitHandleCannotBeOpenedException) {
                Console.Error.WriteLine("Luma 正在启动，请稍等。");
            }
            return 0;
        }
        using EventWaitHandle showSignal = new(false, EventResetMode.AutoReset, @"Local\DesktopNotes_Show_" + identity);
        try {
            AppSettings settings = AppSettings.Load(configDirectory, notesDirectory);
            Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MainWindow? window = null;
            bool exiting = false;

            // 应用保持单实例；退出前必须保存成功，冲突时回到卡片由用户选择版本。
            void Exit() {
                if (window != null && !window.SaveCurrent()) {
                    window.Show();
                    window.ShowNotice("保存暂停", "当前笔记尚未保存，请先处理保存错误或文件冲突，再退出。");
                    return;
                }
                exiting = true;
                if (window != null) {
                    window.AllowExit = true;
                    window.Close();
                }
                application.Shutdown();
            }

            void ShowCard() {
                if (window == null || window.WasClosed) {
                    window = new MainWindow(settings, configDirectory, Exit);
                }
                window.RevealDrawer(true);
                window.EnsureAttached();
            }

            // 托盘直接读取带 Alpha 的多尺寸资源，按系统小图标尺寸选择，避免关联图标二次缩放产生白边。
            using Stream iconStream = typeof(Program).Assembly.GetManifestResourceStream("Luma.TrayIcon")
                ?? throw new IOException("缺少 Luma 托盘图标资源。");
            using System.Drawing.Icon appIcon = new(iconStream, System.Windows.Forms.SystemInformation.SmallIconSize);
            using System.Windows.Forms.NotifyIcon tray = new() {
                Icon = appIcon,
                Text = "Luma · 待办与小说",
                Visible = true,
            };
            System.Windows.Forms.ContextMenuStrip menu = new();
            menu.ShowImageMargin = false;
            menu.Font = new System.Drawing.Font("Microsoft YaHei UI", 10);
            menu.Padding = new System.Windows.Forms.Padding(6);
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayColors());
            menu.Items.Add("显示 Luma", null, (_, _) => ShowCard());
            menu.Items.Add("设置", null, (_, _) => { ShowCard(); window?.ShowSettings(); });
            menu.Items.Add("退出", null, (_, _) => Exit());
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items) {
                item.Padding = new System.Windows.Forms.Padding(14, 9, 14, 9);
            }
            menu.Opened += (_, _) => {
                using System.Drawing.Drawing2D.GraphicsPath round = new();
                int radius = 16;
                int width = menu.Width - 1;
                int height = menu.Height - 1;
                round.AddArc(0, 0, radius, radius, 180, 90);
                round.AddArc(width - radius, 0, radius, radius, 270, 90);
                round.AddArc(width - radius, height - radius, radius, radius, 0, 90);
                round.AddArc(0, height - radius, radius, radius, 90, 90);
                round.CloseFigure();
                menu.Region?.Dispose();
                menu.Region = new System.Drawing.Region(round);
            };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (_, _) => ShowCard();

            // Explorer 销毁了原宿主时，独立托盘进程仍在；按实际窗口状态恢复一次。
            DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(3) };
            healthTimer.Tick += (_, _) => {
                if (exiting) {
                    return;
                }
                if (window?.WasClosed == true || showSignal.WaitOne(0)) {
                    ShowCard();
                } else {
                    window?.EnsureAttached();
                }
            };
            healthTimer.Start();
            application.Startup += (_, _) => ShowCard();
            string? capturePath = Argument(args, "--capture");
            if (capturePath != null) {
                DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromSeconds(2) };
                captureTimer.Tick += (_, _) => { captureTimer.Stop(); window?.CapturePreview(capturePath); };
                captureTimer.Start();
            }
            application.SessionEnding += (_, _) => window?.SaveCurrent();
            application.DispatcherUnhandledException += (_, eventArgs) => {
                WriteFailure(configDirectory, eventArgs.Exception);
                eventArgs.Handled = true;
                if (window != null && !window.WasClosed) {
                    window.ShowNotice("运行错误", "错误已记录到配置目录的 error.log。\n" + eventArgs.Exception.Message);
                } else { application.Shutdown(1); }
            };
            int result = application.Run();
            healthTimer.Stop();
            tray.Visible = false;
            menu.Dispose();
            return result;
        } catch (Exception exception) {
            WriteFailure(configDirectory, exception);
            Console.Error.WriteLine("无法启动 Luma：" + exception.Message);
            return 1;
        }
    }

    private static string? Argument(string[] args, string name) {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed class TrayColors : System.Windows.Forms.ProfessionalColorTable {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(247, 247, 250);
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(220, 222, 228);
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(226, 234, 247);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(226, 234, 247);
    }

    private static void WriteFailure(string configDirectory, Exception exception) {
        try {
            Directory.CreateDirectory(configDirectory);
            File.AppendAllText(Path.Combine(configDirectory, "error.log"), DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + " " + exception + Environment.NewLine);
        } catch (Exception logException) {
            System.Diagnostics.Debug.WriteLine("错误日志无法写入：" + logException.Message);
        }
    }

}
