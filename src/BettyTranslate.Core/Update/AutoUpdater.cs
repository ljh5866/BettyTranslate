using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace BettyTranslate.Core.Update;

/// <summary>
/// 应用内自动更新编排器。
/// 思路：Windows 无法直接替换正在运行的 exe，因此先把安装包解压到临时目录，并生成一个独立于应用的
/// 守护脚本（.cmd）；拉起脚本后由调用方关闭当前进程。脚本会等待主进程退出 → 镜像替换应用目录 →
/// 删除临时安装包与解压目录 → 重新启动应用。
/// 更新完成后会自动删除临时 zip 与解压目录，避免占用磁盘空间。
/// 仅使用内置 API，不引入新的 NuGet 依赖。
/// </summary>
public static class AutoUpdater
{
    /// <summary>
    /// 准备并触发应用内更新。执行成功后应关闭当前进程，由守护脚本完成后续替换与重启。
    /// </summary>
    /// <param name="zipPath">已下载的安装包（.zip）完整路径</param>
    /// <param name="appDir">应用所在目录（通常为 AppContext.BaseDirectory）</param>
    /// <param name="exePath">当前可执行文件完整路径（通常为 Environment.ProcessPath）</param>
    /// <param name="version">目标版本号，用于命名临时解压目录</param>
    public static void PrepareAndApplyUpdate(string zipPath, string appDir, string exePath, Version version)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("未找到已下载的安装包", zipPath);

        var updateRoot = Path.Combine(Path.GetTempPath(), "BettyTranslate-update");
        var staging = Path.Combine(updateRoot, version.ToString());
        var scriptPath = Path.Combine(updateRoot, $"apply-update-by-{version}.cmd");

        // 解压安装包到临时目录（zip 顶层直接是文件，解压后可直接覆盖应用目录）
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(zipPath, staging);

        // 生成守护脚本（纯 ASCII，避免中文系统下的代码页问题）
        var trimmedAppDir = appDir.TrimEnd('\\', '/');
        Directory.CreateDirectory(updateRoot);
        File.WriteAllText(scriptPath, BuildScript(trimmedAppDir, exePath, staging, zipPath, Environment.ProcessId),
            new System.Text.UTF8Encoding(false));

        // 以隐藏方式独立启动脚本，使其在本进程退出后仍能继续工作
        var psi = new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    /// <summary>生成守护批处理脚本。参数：%1=应用目录，%2=exe 路径，%3=临时解压目录，%4=安装包路径，%5=当前进程 ID。</summary>
    private static string BuildScript(string appDir, string exePath, string staging, string zipPath, int pid)
        => $"""
            @echo off
            setlocal EnableExtensions
            rem Wait for the main process to exit so the running exe can be replaced
            :wait
            tasklist /FI "PID eq %5" 2>nul | findstr /I /C:"%5" >nul
            if not errorlevel 1 (
              ping 127.0.0.1 -n 2 >nul
              goto wait
            )
            rem Wait a moment for file handles to be released
            ping 127.0.0.1 -n 4 >nul
            rem Mirror-replace the app directory: copy new files, remove stale ones, keep user config and logs
            robocopy "%3" "%1" /MIR /XD Config logs /R:2 /W:1 /NJH /NJS /NFP /NS /NC >nul
            rem Clean the extracted staging directory and the downloaded package
            rd /s /q "%3"
            del "%4" >nul 2>&1
            rem Restart the application
            start "" "%2"
            rem Remove this updater script itself (must be the last command)
            del "%~f0" >nul 2>&1
            """;
}
