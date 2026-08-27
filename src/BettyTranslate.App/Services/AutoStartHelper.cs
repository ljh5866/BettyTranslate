using System.Diagnostics;
using Microsoft.Win32;

namespace BettyTranslate.App.Services;

/// <summary>
/// 开机自启动：通过 Windows 任务计划程序（Task Scheduler）在登录时以最高权限启动本程序。
/// 之所以不用注册表 Run 项：requireAdministrator 程序放在 Run 项里，登录阶段不会弹 UAC 会被直接跳过。
/// </summary>
public static class AutoStartHelper
{
    private const string TaskName = "BettyTranslateAutoStart";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BettyTranslate";

    /// <summary>当前是否已设置开机自启动（任务是否存在）</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var p = RunSchTasks($"/Query /TN \"{TaskName}\"");
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>设置/取消开机自启动；成功返回 true</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? string.Empty;
                // ONLOGON：登录时触发；RL HIGHEST：以最高权限静默提权运行，避免登录阶段无法弹 UAC 而启动失败。
                // --autostart：让程序识别为开机自启，启动后隐藏到系统托盘并保持运行状态。
                using var p = RunSchTasks(
                    $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --autostart\" /SC ONLOGON /RL HIGHEST /F");
                p.WaitForExit(10000);
                if (p.ExitCode != 0)
                    return false;

                // 清理旧版残留的注册表 Run 启动项，避免留下会失败的启动入口
                RemoveLegacyRunEntry();
                return true;
            }
            else
            {
                using var p = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
                p.WaitForExit(10000);
                // 任务不存在(ExitCode=1)也视为已关闭，避免重复删除报错
                return p.ExitCode == 0 || p.ExitCode == 1;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>清理旧版（注册表 Run 项）残留的自启动入口</summary>
    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 忽略：清理失败不影响主流程
        }
    }

    private static Process RunSchTasks(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        return Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 schtasks 进程");
    }
}
