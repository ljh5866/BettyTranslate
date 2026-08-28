using System;
using System.IO;

namespace BettyTranslate.App.Services;

/// <summary>
/// 桌面快捷方式：在桌面创建/删除指向本程序的 .lnk 快捷方式。
/// </summary>
public static class DesktopShortcutHelper
{
    /// <summary>快捷方式名称（不含扩展名）</summary>
    private const string ShortcutName = "BettyTranslate";
    private const string Extension = ".lnk";

    /// <summary>桌面快捷方式的完整路径</summary>
    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ShortcutName + Extension);

    /// <summary>桌面是否已存在本程序的快捷方式</summary>
    public static bool IsEnabled()
    {
        try
        {
            return File.Exists(ShortcutPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>创建/删除桌面快捷方式；成功返回 true</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                CreateShortcut();
                return File.Exists(ShortcutPath);
            }
            else
            {
                if (File.Exists(ShortcutPath))
                    File.Delete(ShortcutPath);
                return !File.Exists(ShortcutPath);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>通过 WScript.Shell COM 创建指向本程序的快捷方式</summary>
    private static void CreateShortcut()
    {
        var exePath = Environment.ProcessPath ?? string.Empty;
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法获取 WScript.Shell 组件类型");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 WScript.Shell 对象");

        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
        shortcut.Description = "贝蒂翻译";
        shortcut.IconLocation = $"{exePath},0";
        shortcut.Save();
    }
}
