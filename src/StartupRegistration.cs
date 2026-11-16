using System;
using System.IO;
using Microsoft.Win32;

namespace DesktopNotes;

/// <summary>使用当前用户的 Windows 登录启动项，不需要管理员权限或常驻监控。</summary>
internal static class StartupRegistration {
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Luma.DesktopNotes";

    internal static bool IsEnabled(string executablePath, string registryPath = RunKey) {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryPath);
        string? command = key?.GetValue(ValueName) as string;
        return string.Equals(command, Command(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    internal static void SetEnabled(bool enabled, string executablePath, string registryPath = RunKey) {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath, true)
            ?? throw new IOException("无法打开当前用户的启动设置。");
        if (enabled) {
            if (!File.Exists(executablePath)) { throw new IOException("没有找到 Luma 程序，无法添加开机启动。"); }
            key.SetValue(ValueName, Command(executablePath), RegistryValueKind.String);
        } else {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string Command(string executablePath) {
        return "\"" + Path.GetFullPath(executablePath) + "\" --autostart";
    }
}
