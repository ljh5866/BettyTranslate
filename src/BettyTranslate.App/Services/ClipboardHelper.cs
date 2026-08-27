using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace BettyTranslate.App.Services;

/// <summary>
/// 划词翻译文本捕获工具。
/// 优先用 UI Automation 直接读取焦点控件中的选中文本（完全不碰剪贴板）；
/// 失败时回退到「模拟 Ctrl+C 复制 → 读取 → 恢复剪贴板原样」，保证用户剪贴板不被选区覆盖。
/// </summary>
public static class ClipboardHelper
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_C = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    /// <summary>
    /// 捕获前台焦点控件中选中的文本。
    /// 优先用 UI Automation 直接读取（不碰剪贴板）；失败时模拟 Ctrl+C 复制后读取，
    /// 并在读取完成后恢复剪贴板为原来的内容。
    /// 捕获失败或无选中文本时返回 null。注意：含剪贴板分支，必须在 STA 线程（UI 线程）上调用。
    /// </summary>
    public static string? CopySelectedText()
    {
        // 优先用 UI Automation 直接读选中文本，完全不碰剪贴板
        var uiaText = ReadSelectedTextViaUia();
        if (!string.IsNullOrWhiteSpace(uiaText))
            return uiaText.Trim();

        // 回退：模拟 Ctrl+C 复制后读取，最后恢复剪贴板原样
        var backup = SaveClipboardContent();
        try
        {
            var beforeSeq = GetClipboardSequenceNumber();
            // 注入可能被个别程序忽略，最多尝试 3 次，直到剪贴板出现新内容
            for (var attempt = 0; attempt < 3; attempt++)
            {
                SendCopyShortcut();
                var text = WaitForNewClipboardText(beforeSeq);
                if (text != null)
                    return text;
            }
            return null;
        }
        finally
        {
            // 无论如何都要恢复用户原本的剪贴板内容
            RestoreClipboardContent(backup);
        }
    }

    /// <summary>用 UI Automation 从焦点元素取选中文本。取不到返回 null。不过剪贴板。</summary>
    private static string? ReadSelectedTextViaUia()
    {
        try
        {
            var root = AutomationElement.FocusedElement;
            if (root == null)
                return null;

            // 在焦点元素及其祖先链上寻找支持「选中文本范围」的控件
            var walker = TreeWalker.ControlViewWalker;
            var element = root;
            for (var depth = 0; depth < 6 && element != null; depth++, element = walker.GetParent(element))
            {
                // TextPattern：标准文本控件（浏览器、Word、记事本等）通过选中范围暴露内容
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var tpObj) && tpObj is TextPattern tp)
                {
                    var sb = new StringBuilder();
                    foreach (var range in tp.GetSelection())
                    {
                        var text = range.GetText(int.MaxValue);
                        if (!string.IsNullOrEmpty(text))
                            sb.Append(text);
                    }
                    if (sb.Length > 0)
                        return sb.ToString();
                }

                // ValuePattern：部分输入框直接暴露当前值
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) && vpObj is ValuePattern vp)
                {
                    var value = vp.Current.Value;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }
        }
        catch
        {
            // UIA 不可用，交由调用方回退到剪贴板方案
        }
        return null;
    }

    /// <summary>备份当前剪贴板内容；备份不可用时返回 null（表示原本剪贴板为空）</summary>
    private static IDataObject? SaveClipboardContent()
    {
        try
        {
            return Clipboard.GetDataObject();
        }
        catch
        {
            // 剪贴板暂不可用，按空处理
            return null;
        }
    }

    /// <summary>恢复剪贴板为备份的内容；备份为空则清空剪贴板</summary>
    private static void RestoreClipboardContent(IDataObject? backup)
    {
        try
        {
            if (backup == null)
                Clipboard.Clear();
            else
                Clipboard.SetDataObject(backup, true);
        }
        catch
        {
            // 恢复失败时忽略，避免覆盖掉正在使用的剪贴板
        }
    }

    /// <summary>等待剪贴板序号变化（Ctrl+C 已写入新选区）并读取文本；超时未变化则返回 null</summary>
    private static string? WaitForNewClipboardText(uint beforeSeq)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (GetClipboardSequenceNumber() != beforeSeq && Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }
            catch
            {
                // 剪贴板暂不可用，稍后重试
            }
            Thread.Sleep(60);
        }
        return null;
    }

    /// <summary>按下 Ctrl+C 触发一次「复制」，用 SendInput 并保留按键间隔，提高在不同程序中的成功率</summary>
    private static void SendCopyShortcut()
    {
        SendKey(VK_CONTROL, 0);
        Thread.Sleep(40);
        SendKey(VK_C, 0);
        Thread.Sleep(40);
        SendKey(VK_C, KEYEVENTF_KEYUP);
        Thread.Sleep(40);
        SendKey(VK_CONTROL, KEYEVENTF_KEYUP);
    }

    private static void SendKey(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = flags },
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
