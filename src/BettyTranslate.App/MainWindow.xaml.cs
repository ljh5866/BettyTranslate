using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BettyTranslate.App.Services;
using BettyTranslate.App.Views;
using BettyTranslate.Core.Settings;
using BettyTranslate.Core.Translation;
using BettyTranslate.Core.Update;

namespace BettyTranslate.App;

/// <summary>
/// 主窗口：侧边导航（屏幕截屏翻译 / 设置）+ 内容区页面切换。
/// 点击「开始使用」后注册全局快捷键（可在设置页自定义），
/// 触发"框选区域 → 截图 → OCR → 翻译 → 中文覆盖显示"流程。
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyTranslateId = 1;
    private const int HotkeyImageTranslateId = 2;
    private const int HotkeyTranslateSelectionId = 3;

    private HotkeyService? _hotkeys;
    private TrayIconService? _trayIcon;
    private bool _reallyExit;
    private readonly bool _startInTray;
    private bool _isTranslating;
    private bool _isImageTranslating;
    private bool _isImageRunning;
    private bool _isSelectionRunning;
    private uint _modifier = HotkeyService.MOD_CONTROL;
    private uint _key = HotkeyService.VK_F10;
    private string _capturedKey = "F10";
    private uint _imageModifier = HotkeyService.MOD_CONTROL;
    private uint _imageKey = HotkeyService.VK_F11;
    private string _imageCapturedKey = "F11";
    private uint _selectionModifier = HotkeyService.MOD_CONTROL;
    private uint _selectionKey = HotkeyService.VK_F12;
    private string _selectionCapturedKey = "F12";
    private bool _loadingSettings;

    /// <param name="startInTray">true 时为开机自启：窗口创建后直接静默隐藏到系统托盘，不显示主界面</param>
    public MainWindow(bool startInTray = false)
    {
        InitializeComponent();
        _startInTray = startInTray;
        // 在标题显示版本号，便于确认运行的是最新构建
        var ver = typeof(MainWindow).Assembly.GetName().Version;
        Title = $"Betty Translate v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
        CurrentVersionText.Text = $"当前版本 v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
        LoadHotkeySettings();
        SubscribeModifierChanged();
    }

    /// <summary>窗口句柄创建后触发：恢复上次退出时仍在运行的功能（需句柄就绪才能注册全局热键）</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 创建系统托盘图标：左键双击恢复窗口，右键菜单可选择「打开主界面 / 退出程序」
        _trayIcon = new TrayIconService(new WindowInteropHelper(this).Handle);
        _trayIcon.OpenRequested += OpenFromTray;
        _trayIcon.ExitRequested += () => { _reallyExit = true; Close(); };

        RestoreRunningFeatures();

        // 启动时静默检查更新（无新版本时不打扰用户，仅记录结果）
        _ = CheckForUpdateAsync(silent: true);

        // 开机自启：静默隐藏到系统托盘，不弹出主界面（托盘图标此时已就绪，双击可恢复）
        if (_startInTray)
        {
            Hide();
            _trayIcon.ShowBalloon("Betty Translate", "已启动至电脑扩展栏，双击图标可打开主界面");
        }
    }

    /// <summary>从托盘唤回主窗口（双击图标 / 右键菜单「打开主界面」）</summary>
    private void OpenFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>启动时自动开启上次未关闭的功能（屏幕/图片/划词翻译），无需再次手动点击开始</summary>
    private void RestoreRunningFeatures()
    {
        var settings = AppSettings.Load(App.ConfigPath);
        var screenOn = settings.ScreenTranslateActive;
        var imageOn = settings.ImageTranslateActive;
        var selectionOn = settings.SelectionTranslateActive;

        if (screenOn) StartTranslate();
        if (imageOn) StartImageTranslate();
        if (selectionOn) StartSelectionTranslate();

        // 切换到仍在运行的功能页（优先级：屏幕翻译 > 图片翻译 > 划词翻译）
        if (screenOn) ShowTranslateView();
        else if (imageOn) ShowImageTranslateView();
        else if (selectionOn) ShowSelectionTranslateView();
    }

    /// <summary>按需更新并保存「功能运行状态」，用于下次启动自动恢复</summary>
    private static void UpdateFeatureState(Action<AppSettings> mutate)
    {
        try
        {
            var settings = AppSettings.Load(App.ConfigPath);
            mutate(settings);
            settings.Save(App.ConfigPath);
        }
        catch
        {
            // 状态保存失败忽略，不影响功能本身
        }
    }

    // ---------- 页面切换 ----------

    private void OnNavTranslateClick(object sender, RoutedEventArgs e) => ShowTranslateView();

    private void OnNavImageTranslateClick(object sender, RoutedEventArgs e) => ShowImageTranslateView();

    private void OnNavSelectionTranslateClick(object sender, RoutedEventArgs e) => ShowSelectionTranslateView();

    private void OnNavSettingsClick(object sender, RoutedEventArgs e) => ShowSettingsView();

    private void ShowTranslateView()
    {
        TranslateView.Visibility = Visibility.Visible;
        ImageTranslateView.Visibility = Visibility.Collapsed;
        SelectionTranslateView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        NavTranslateBtn.Style = (Style)FindResource("NavButtonSelected");
        NavImageBtn.Style = (Style)FindResource("NavButton");
        NavSelectionBtn.Style = (Style)FindResource("NavButton");
        NavSettingsBtn.Style = (Style)FindResource("NavButton");
    }

    private void ShowImageTranslateView()
    {
        TranslateView.Visibility = Visibility.Collapsed;
        ImageTranslateView.Visibility = Visibility.Visible;
        SelectionTranslateView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        NavTranslateBtn.Style = (Style)FindResource("NavButton");
        NavImageBtn.Style = (Style)FindResource("NavButtonSelected");
        NavSelectionBtn.Style = (Style)FindResource("NavButton");
        NavSettingsBtn.Style = (Style)FindResource("NavButton");
    }

    private void ShowSelectionTranslateView()
    {
        TranslateView.Visibility = Visibility.Collapsed;
        ImageTranslateView.Visibility = Visibility.Collapsed;
        SelectionTranslateView.Visibility = Visibility.Visible;
        SettingsView.Visibility = Visibility.Collapsed;
        NavTranslateBtn.Style = (Style)FindResource("NavButton");
        NavImageBtn.Style = (Style)FindResource("NavButton");
        NavSelectionBtn.Style = (Style)FindResource("NavButtonSelected");
        NavSettingsBtn.Style = (Style)FindResource("NavButton");
    }

    private void ShowSettingsView()
    {
        TranslateView.Visibility = Visibility.Collapsed;
        ImageTranslateView.Visibility = Visibility.Collapsed;
        SelectionTranslateView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        NavTranslateBtn.Style = (Style)FindResource("NavButton");
        NavImageBtn.Style = (Style)FindResource("NavButton");
        NavSelectionBtn.Style = (Style)FindResource("NavButton");
        NavSettingsBtn.Style = (Style)FindResource("NavButtonSelected");
        LoadSettingsIntoView();
    }

    // ---------- 快捷键设置 ----------

    /// <summary>从配置读取快捷键并更新界面提示</summary>
    private void LoadHotkeySettings()
    {
        var hk = AppSettings.Load(App.ConfigPath).Hotkey;
        _modifier = (uint)hk.ModifierVk;
        _key = (uint)hk.KeyVk;
        ModifierKeyText.Text = string.Join(" + ", hk.Modifiers);
        MainKeyText.Text = hk.Key;

        var imageHk = AppSettings.Load(App.ConfigPath).ImageHotkey;
        _imageModifier = (uint)imageHk.ModifierVk;
        _imageKey = (uint)imageHk.KeyVk;
        ImageModifierKeyText.Text = string.Join(" + ", imageHk.Modifiers);
        ImageMainKeyText.Text = imageHk.Key;

        var selHk = AppSettings.Load(App.ConfigPath).SelectionHotkey;
        _selectionModifier = (uint)selHk.ModifierVk;
        _selectionKey = (uint)selHk.KeyVk;
        SelectionModifierKeyText.Text = string.Join(" + ", selHk.Modifiers);
        SelectionMainKeyText.Text = selHk.Key;
    }

    /// <summary>进入设置页时载入当前配置</summary>
    private void LoadSettingsIntoView()
    {
        var settings = AppSettings.Load(App.ConfigPath);
        var hk = settings.Hotkey;
        CtrlCheck.IsChecked = hk.Modifiers.Contains("Control");
        AltCheck.IsChecked = hk.Modifiers.Contains("Alt");
        ShiftCheck.IsChecked = hk.Modifiers.Contains("Shift");
        WinCheck.IsChecked = hk.Modifiers.Contains("Win");
        _capturedKey = hk.Key;
        KeyBox.Text = hk.Key;
        UpdatePreview();

        var imageHk = settings.ImageHotkey;
        ImgCtrlCheck.IsChecked = imageHk.Modifiers.Contains("Control");
        ImgAltCheck.IsChecked = imageHk.Modifiers.Contains("Alt");
        ImgShiftCheck.IsChecked = imageHk.Modifiers.Contains("Shift");
        ImgWinCheck.IsChecked = imageHk.Modifiers.Contains("Win");
        _imageCapturedKey = imageHk.Key;
        ImgKeyBox.Text = imageHk.Key;
        UpdateImagePreview();

        var selHk = settings.SelectionHotkey;
        SelCtrlCheck.IsChecked = selHk.Modifiers.Contains("Control");
        SelAltCheck.IsChecked = selHk.Modifiers.Contains("Alt");
        SelShiftCheck.IsChecked = selHk.Modifiers.Contains("Shift");
        SelWinCheck.IsChecked = selHk.Modifiers.Contains("Win");
        _selectionCapturedKey = selHk.Key;
        SelKeyBox.Text = selHk.Key;
        UpdateSelectionPreview();

        ThemeLightRadio.IsChecked = settings.Theme is not ("dark" or "warm");
        ThemeDarkRadio.IsChecked = settings.Theme == "dark";
        ThemeWarmRadio.IsChecked = settings.Theme == "warm";

        // 开机自启动：以注册表启动项为准
        _loadingSettings = true;
        AutoStartCheck.IsChecked = AutoStartHelper.IsEnabled();
        DesktopShortcutCheck.IsChecked = DesktopShortcutHelper.IsEnabled();
        _loadingSettings = false;
        AutoStartStatusText.Text = AutoStartCheck.IsChecked == true
            ? "已开启开机自启动" : "已关闭开机自启动";
        DesktopShortcutStatusText.Text = DesktopShortcutCheck.IsChecked == true
            ? "已创建桌面快捷方式" : "未创建桌面快捷方式";

        UserApiKeyBox.Text = settings.UserDeepSeekKey;
        _ = UpdateFreeQuotaTextAsync();
    }

    /// <summary>刷新「剩余免费次数」提示（按当前账号在 Supabase 中记录的已用次数）</summary>
    private async Task UpdateFreeQuotaTextAsync()
    {
        try
        {
            if (await App.AuthService.IsImageTranslateUnlimitedAsync())
            {
                FreeQuotaText.Text = "剩余免费截图翻译次数：不限（已开通特权）";
                return;
            }
            var used = await App.AuthService.GetImageTranslateCountAsync();
            var left = Math.Max(0, App.FreeImageTranslateLimit - used);
            FreeQuotaText.Text = $"剩余免费截图翻译次数：{left} / {App.FreeImageTranslateLimit}" +
                                 (used >= App.FreeImageTranslateLimit ? "（已用尽，请填写自定义 Key）" : string.Empty);
        }
        catch
        {
            FreeQuotaText.Text = $"剩余免费截图翻译次数：{App.FreeImageTranslateLimit} / {App.FreeImageTranslateLimit}";
        }
    }

    /// <summary>当前选中的主题：dark / warm / light（默认浅色）</summary>
    private string GetSelectedTheme()
    {
        if (ThemeDarkRadio.IsChecked == true) return "dark";
        if (ThemeWarmRadio.IsChecked == true) return "warm";
        return "light";
    }

    private void SubscribeModifierChanged()
    {
        CtrlCheck.Checked += (_, _) => UpdatePreview();
        CtrlCheck.Unchecked += (_, _) => UpdatePreview();
        AltCheck.Checked += (_, _) => UpdatePreview();
        AltCheck.Unchecked += (_, _) => UpdatePreview();
        ShiftCheck.Checked += (_, _) => UpdatePreview();
        ShiftCheck.Unchecked += (_, _) => UpdatePreview();
        WinCheck.Checked += (_, _) => UpdatePreview();
        WinCheck.Unchecked += (_, _) => UpdatePreview();

        ImgCtrlCheck.Checked += (_, _) => UpdateImagePreview();
        ImgCtrlCheck.Unchecked += (_, _) => UpdateImagePreview();
        ImgAltCheck.Checked += (_, _) => UpdateImagePreview();
        ImgAltCheck.Unchecked += (_, _) => UpdateImagePreview();
        ImgShiftCheck.Checked += (_, _) => UpdateImagePreview();
        ImgShiftCheck.Unchecked += (_, _) => UpdateImagePreview();
        ImgWinCheck.Checked += (_, _) => UpdateImagePreview();
        ImgWinCheck.Unchecked += (_, _) => UpdateImagePreview();

        SelCtrlCheck.Checked += (_, _) => UpdateSelectionPreview();
        SelCtrlCheck.Unchecked += (_, _) => UpdateSelectionPreview();
        SelAltCheck.Checked += (_, _) => UpdateSelectionPreview();
        SelAltCheck.Unchecked += (_, _) => UpdateSelectionPreview();
        SelShiftCheck.Checked += (_, _) => UpdateSelectionPreview();
        SelShiftCheck.Unchecked += (_, _) => UpdateSelectionPreview();
        SelWinCheck.Checked += (_, _) => UpdateSelectionPreview();
        SelWinCheck.Unchecked += (_, _) => UpdateSelectionPreview();
    }

    private List<string> GetSelectedModifiers()
    {
        var list = new List<string>();
        if (CtrlCheck.IsChecked == true) list.Add("Control");
        if (AltCheck.IsChecked == true) list.Add("Alt");
        if (ShiftCheck.IsChecked == true) list.Add("Shift");
        if (WinCheck.IsChecked == true) list.Add("Win");
        return list;
    }

    private List<string> GetSelectedImageModifiers()
    {
        var list = new List<string>();
        if (ImgCtrlCheck.IsChecked == true) list.Add("Control");
        if (ImgAltCheck.IsChecked == true) list.Add("Alt");
        if (ImgShiftCheck.IsChecked == true) list.Add("Shift");
        if (ImgWinCheck.IsChecked == true) list.Add("Win");
        return list;
    }

    private List<string> GetSelectedSelectionModifiers()
    {
        var list = new List<string>();
        if (SelCtrlCheck.IsChecked == true) list.Add("Control");
        if (SelAltCheck.IsChecked == true) list.Add("Alt");
        if (SelShiftCheck.IsChecked == true) list.Add("Shift");
        if (SelWinCheck.IsChecked == true) list.Add("Win");
        return list;
    }

    /// <summary>判断两组快捷键（修饰键 + 主键）是否相同</summary>
    private static bool IsSameHotkey(IReadOnlyList<string> modsA, string keyA,
        IReadOnlyList<string> modsB, string keyB)
        => keyA == keyB && new HashSet<string>(modsA).SetEquals(modsB);

    private void UpdatePreview()
    {
        var mods = GetSelectedModifiers();
        PreviewText.Text = mods.Count == 0
            ? "(请至少选择一个修饰键)"
            : string.Join(" + ", mods) + " + " + _capturedKey;
    }

    private void UpdateImagePreview()
    {
        var mods = GetSelectedImageModifiers();
        ImgPreviewText.Text = mods.Count == 0
            ? "(请至少选择一个修饰键)"
            : string.Join(" + ", mods) + " + " + _imageCapturedKey;
    }

    private void UpdateSelectionPreview()
    {
        var mods = GetSelectedSelectionModifiers();
        SelPreviewText.Text = mods.Count == 0
            ? "(请至少选择一个修饰键)"
            : string.Join(" + ", mods) + " + " + _selectionCapturedKey;
    }

    private void OnKeyBoxFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        KeyBox.Text = "请按下按键…";
    }

    private void OnImgKeyBoxFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        ImgKeyBox.Text = "请按下按键…";
    }

    private void OnSelKeyBoxFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        SelKeyBox.Text = "请按下按键…";
    }

    private void OnKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        var name = CaptureKey(e);
        if (name != null)
        {
            _capturedKey = name;
            KeyBox.Text = name;
            UpdatePreview();
        }
        e.Handled = true;
    }

    private void OnImgKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        var name = CaptureKey(e);
        if (name != null)
        {
            _imageCapturedKey = name;
            ImgKeyBox.Text = name;
            UpdateImagePreview();
        }
        e.Handled = true;
    }

    private void OnSelKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        var name = CaptureKey(e);
        if (name != null)
        {
            _selectionCapturedKey = name;
            SelKeyBox.Text = name;
            UpdateSelectionPreview();
        }
        e.Handled = true;
    }

    /// <summary>把按键事件解析为字母 / 数字 / F1-F12 的按键名，不支持时返回 null</summary>
    private static string? CaptureKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key >= Key.A && key <= Key.Z)
            return key.ToString();
        if (key >= Key.F1 && key <= Key.F12)
            return key.ToString();
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return ((int)key - (int)Key.NumPad0).ToString();
        return null;
    }

    /// <summary>恢复默认快捷键 Ctrl + F10（预览恢复，需点击保存生效）</summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        CtrlCheck.IsChecked = true;
        AltCheck.IsChecked = false;
        ShiftCheck.IsChecked = false;
        WinCheck.IsChecked = false;
        _capturedKey = "F10";
        KeyBox.Text = "F10";
        UpdatePreview();
    }

    /// <summary>恢复图片翻译默认快捷键 Ctrl + F11（预览恢复，需点击保存生效）</summary>
    private void OnImgResetClick(object sender, RoutedEventArgs e)
    {
        ImgCtrlCheck.IsChecked = true;
        ImgAltCheck.IsChecked = false;
        ImgShiftCheck.IsChecked = false;
        ImgWinCheck.IsChecked = false;
        _imageCapturedKey = "F11";
        ImgKeyBox.Text = "F11";
        UpdateImagePreview();
    }

    /// <summary>恢复划词翻译默认快捷键 Ctrl + F12（预览恢复，需点击保存生效）</summary>
    private void OnSelResetClick(object sender, RoutedEventArgs e)
    {
        SelCtrlCheck.IsChecked = true;
        SelAltCheck.IsChecked = false;
        SelShiftCheck.IsChecked = false;
        SelWinCheck.IsChecked = false;
        _selectionCapturedKey = "F12";
        SelKeyBox.Text = "F12";
        UpdateSelectionPreview();
    }

    /// <summary>开机自启动开关：勾选即写入/删除注册表启动项并立即生效</summary>
    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;

        var enabled = AutoStartCheck.IsChecked == true;
        if (AutoStartHelper.SetEnabled(enabled))
        {
            AutoStartStatusText.Text = enabled ? "已开启开机自启动" : "已关闭开机自启动";
        }
        else
        {
            AutoStartCheck.IsChecked = !enabled;
            AutoStartStatusText.Text = "设置开机自启动失败"; 
        }
    }

    /// <summary>桌面快捷方式开关：勾选即在桌面创建/删除本程序的快捷方式并立即生效</summary>
    private void OnDesktopShortcutToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;

        var enabled = DesktopShortcutCheck.IsChecked == true;
        if (DesktopShortcutHelper.SetEnabled(enabled))
        {
            DesktopShortcutStatusText.Text = enabled ? "已创建桌面快捷方式" : "已删除桌面快捷方式";
        }
        else
        {
            DesktopShortcutCheck.IsChecked = !enabled;
            DesktopShortcutStatusText.Text = "设置桌面快捷方式失败";
        }
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        var mods = GetSelectedModifiers();
        if (mods.Count == 0)
        {
            MessageBox.Show(this, "请至少选择一个修饰键（Ctrl / Alt / Shift / Win）", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_capturedKey))
        {
            MessageBox.Show(this, "请先点击按键输入框并按下要使用的按键", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var imageMods = GetSelectedImageModifiers();
        if (imageMods.Count == 0)
        {
            MessageBox.Show(this, "图片翻译快捷键请至少选择一个修饰键（Ctrl / Alt / Shift / Win）", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_imageCapturedKey))
        {
            MessageBox.Show(this, "图片翻译请先点击按键输入框并按下要使用的按键", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (IsSameHotkey(mods, _capturedKey, imageMods, _imageCapturedKey))
        {
            MessageBox.Show(this, "图片翻译快捷键不能与屏幕翻译快捷键相同", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selMods = GetSelectedSelectionModifiers();
        if (selMods.Count == 0)
        {
            MessageBox.Show(this, "划词翻译快捷键请至少选择一个修饰键（Ctrl / Alt / Shift / Win）", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(_selectionCapturedKey))
        {
            MessageBox.Show(this, "划词翻译请先点击按键输入框并按下要使用的按键", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (IsSameHotkey(selMods, _selectionCapturedKey, mods, _capturedKey) ||
            IsSameHotkey(selMods, _selectionCapturedKey, imageMods, _imageCapturedKey))
        {
            MessageBox.Show(this, "划词翻译快捷键不能与屏幕翻译/图片翻译快捷键相同", "设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = AppSettings.Load(App.ConfigPath);
        settings.Hotkey.Modifiers = mods;
        settings.Hotkey.Key = _capturedKey;
        settings.ImageHotkey.Modifiers = imageMods;
        settings.ImageHotkey.Key = _imageCapturedKey;
        settings.SelectionHotkey.Modifiers = selMods;
        settings.SelectionHotkey.Key = _selectionCapturedKey;
        settings.Theme = GetSelectedTheme();
        settings.UserDeepSeekKey = UserApiKeyBox.Text.Trim();

        try
        {
            settings.Save(App.ConfigPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存配置失败：" + ex.Message, "设置",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 应用主题，实时换肤
        App.ApplyTheme(settings.Theme);

        // 刷新剩余免费次数提示
        _ = UpdateFreeQuotaTextAsync();

        // 更新提示；若原有热键已注册，则全部注销并按其运行态重新注册
        LoadHotkeySettings();
        if (_hotkeys != null)
        {
            _hotkeys.Dispose();
            _hotkeys = null;
        }

        var registered = true;
        if (EndButton.Visibility == Visibility.Visible)
            registered &= EnsureTranslateHotkey();
        if (ImageEndButton.Visibility == Visibility.Visible)
            registered &= EnsureImageHotkey();
        if (SelectionEndButton.Visibility == Visibility.Visible)
            registered &= EnsureSelectionHotkey();

        if (!registered)
        {
            ShowTranslateView();
            StatusText.Text = "快捷键注册失败，可能已被其他程序占用";
            return;
        }

        ShowTranslateView();
        StatusText.Text = $"设置已保存，屏幕翻译 {ModifierKeyText.Text} + {MainKeyText.Text}，图片翻译 {ImageModifierKeyText.Text} + {ImageMainKeyText.Text}，划词翻译 {SelectionModifierKeyText.Text} + {SelectionMainKeyText.Text}";
    }

    // ---------- 屏幕翻译 ----------

    /// <summary>注册屏幕翻译热键（id=1）；失败返回 false，可能被其他程序占用</summary>
    private bool EnsureTranslateHotkey()
    {
        if (_hotkeys == null)
        {
            _hotkeys = new HotkeyService(this);
            _hotkeys.HotKeyPressed += OnHotKeyPressed;
        }
        return _hotkeys.Register(HotkeyTranslateId,
            _modifier | HotkeyService.MOD_NOREPEAT, _key);
    }

    /// <summary>注册图片翻译热键（id=2）；失败返回 false，可能被其他程序占用</summary>
    private bool EnsureImageHotkey()
    {
        if (_hotkeys == null)
        {
            _hotkeys = new HotkeyService(this);
            _hotkeys.HotKeyPressed += OnHotKeyPressed;
        }
        return _hotkeys.Register(HotkeyImageTranslateId,
            _imageModifier | HotkeyService.MOD_NOREPEAT, _imageKey);
    }

    /// <summary>注册划词翻译热键（id=3）；失败返回 false，可能被其他程序占用</summary>
    private bool EnsureSelectionHotkey()
    {
        if (_hotkeys == null)
        {
            _hotkeys = new HotkeyService(this);
            _hotkeys.HotKeyPressed += OnHotKeyPressed;
        }
        return _hotkeys.Register(HotkeyTranslateSelectionId,
            _selectionModifier | HotkeyService.MOD_NOREPEAT, _selectionKey);
    }

    private void OnStartClick(object sender, RoutedEventArgs e) => StartTranslate();

    private void OnEndClick(object sender, RoutedEventArgs e) => EndTranslate();

    /// <summary>开始屏幕翻译：注册全局热键并进入运行态</summary>
    private void StartTranslate()
    {
        // 点击「开始使用」后才注册全局快捷键
        if (!EnsureTranslateHotkey())
        {
            StatusText.Text = $"快捷键（{ModifierKeyText.Text} + {MainKeyText.Text}）注册失败，可能已被其他程序占用";
            return;
        }

        StartButton.IsEnabled = false;
        StartButton.Content = "正在运行";
        EndButton.Visibility = Visibility.Visible;
        StatusText.Text = $"正在运行：请按 {ModifierKeyText.Text} + {MainKeyText.Text} 框选要翻译的屏幕区域";
        UpdateFeatureState(s => s.ScreenTranslateActive = true);
    }

    /// <summary>结束屏幕翻译：注销热键，恢复初始界面</summary>
    private void EndTranslate()
    {
        _hotkeys?.Unregister(HotkeyTranslateId);

        StartButton.IsEnabled = true;
        StartButton.Content = "开始使用";
        EndButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "已结束。点击「开始使用」可重新开启屏幕翻译";
        UpdateFeatureState(s => s.ScreenTranslateActive = false);
    }

    // ---------- 图片翻译 ----------

    private void OnImageStartClick(object sender, RoutedEventArgs e) => StartImageTranslate();

    private void OnImageEndClick(object sender, RoutedEventArgs e) => EndImageTranslate();

    /// <summary>开始图片翻译：注册全局热键并进入运行态</summary>
    private void StartImageTranslate()
    {
        if (!EnsureImageHotkey())
        {
            ImageStatusText.Text = $"快捷键（{ImageModifierKeyText.Text} + {ImageMainKeyText.Text}）注册失败，可能已被其他程序占用";
            return;
        }

        _isImageRunning = true;
        ImageStartButton.IsEnabled = false;
        ImageStartButton.Content = "正在运行";
        ImageEndButton.Visibility = Visibility.Visible;
        ImageStatusText.Text = $"正在运行：请按 {ImageModifierKeyText.Text} + {ImageMainKeyText.Text} 框选要翻译的屏幕区域";
        UpdateFeatureState(s => s.ImageTranslateActive = true);
    }

    /// <summary>结束图片翻译：注销热键，恢复初始界面</summary>
    private void EndImageTranslate()
    {
        _hotkeys?.Unregister(HotkeyImageTranslateId);

        _isImageRunning = false;
        ImageStartButton.IsEnabled = true;
        ImageStartButton.Content = "开始使用";
        ImageEndButton.Visibility = Visibility.Collapsed;
        ImageStatusText.Text = "已结束。点击「开始使用」可重新开启图片翻译";
        UpdateFeatureState(s => s.ImageTranslateActive = false);
    }

    // ---------- 划词翻译 ----------

    private void OnSelectionStartClick(object sender, RoutedEventArgs e) => StartSelectionTranslate();

    private void OnSelectionEndClick(object sender, RoutedEventArgs e) => EndSelectionTranslate();

    /// <summary>开始划词翻译：注册全局热键并进入运行态</summary>
    private void StartSelectionTranslate()
    {
        if (!EnsureSelectionHotkey())
        {
            SelectionStatusText.Text = $"快捷键（{SelectionModifierKeyText.Text} + {SelectionMainKeyText.Text}）注册失败，可能已被其他程序占用";
            return;
        }

        _isSelectionRunning = true;
        SelectionStartButton.IsEnabled = false;
        SelectionStartButton.Content = "正在运行";
        SelectionEndButton.Visibility = Visibility.Visible;
        SelectionStatusText.Text = $"正在运行：选中一段英文，按 {SelectionModifierKeyText.Text} + {SelectionMainKeyText.Text} 划词翻译";
        UpdateFeatureState(s => s.SelectionTranslateActive = true);
    }

    /// <summary>结束划词翻译：注销热键，恢复初始界面</summary>
    private void EndSelectionTranslate()
    {
        _hotkeys?.Unregister(HotkeyTranslateSelectionId);

        _isSelectionRunning = false;
        SelectionStartButton.IsEnabled = true;
        SelectionStartButton.Content = "开始使用";
        SelectionEndButton.Visibility = Visibility.Collapsed;
        SelectionStatusText.Text = "已结束。点击「开始使用」可重新开启划词翻译";
        UpdateFeatureState(s => s.SelectionTranslateActive = false);
    }

    /// <summary>抓取前台窗口选中的文本并翻译，在鼠标附近弹出结果悬浮窗</summary>
    private async Task StartSelectionTranslateAsync()
    {
        var source = ClipboardHelper.CopySelectedText();
        if (string.IsNullOrWhiteSpace(source))
        {
            MessageBox.Show(this, "未检测到选中的文本。请先选中一段文字，再按快捷键翻译。", "划词翻译",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ProgressWindow? progress = null;
        try
        {
            progress = new ProgressWindow("正在翻译选中文本…");
            progress.Show();

            var translation = await App.TranslateTextAsync(source);

            progress.Close();
            progress = null;

            SelectionTranslateWindow.ShowTranslation(source, translation);
        }
        catch (Exception ex)
        {
            progress?.Close();
            progress = null;
            MessageBox.Show(this, "划词翻译失败：" + ex.Message, "Betty Translate",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartImageTranslateAsync()
    {
        _isImageTranslating = true;
        try
        {
            // 读取最新配置，决定本次使用的翻译通道：
            //  - 用户自备 Key → 客户端直连 DeepSeek，不消耗免费额度
            //  - 未自备 Key  → 走服务端 Edge Function，开发者 Key 不在客户端，
            //                  由服务端校验免费额度并累加计数
            var settings = AppSettings.Load(App.ConfigPath);
            var userKey = settings.UserDeepSeekKey?.Trim() ?? string.Empty;
            IVisionTranslator? vision = null;
            var allowOcrFallback = true;

            if (string.IsNullOrEmpty(userKey))
            {
                // 免费体验路径：额度校验与计数都在服务端完成
                // 特权账号（user_usage.is_unlimited，由管理后台维护）免受限次限制，可直接使用
                var unlimited = await App.AuthService.IsImageTranslateUnlimitedAsync();
                if (!unlimited)
                {
                    var usedCount = await App.AuthService.GetImageTranslateCountAsync();
                    if (usedCount >= App.FreeImageTranslateLimit)
                    {
                        MessageBox.Show(this,
                            $"免费截图翻译体验已用完（共 {App.FreeImageTranslateLimit} 次）。\n请前往「设置」填写你自己的 DeepSeek API Key 后继续使用。",
                            "图片翻译", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }
                var token = await App.AuthService.GetAccessTokenAsync();
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show(this, "登录已失效，请重新登录", "图片翻译",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                vision = new ServerVisionTranslator(App.VisionFunctionUrl, token);
                // 免费路径不能回退到 OCR（OCR 走开发者百度 Key，会给所有用户免费翻译，绕过额度）
                allowOcrFallback = false;
            }
            else
            {
                // 用户自备 Key：直接使用，不消耗免费额度
                vision = new DeepSeekVisionTranslator(userKey);
            }

            Hide(); // 隐藏主窗口，避免被框选/截图
            ProgressWindow? progress = null;
            try
            {
                var selector = new RegionSelectorWindow();
                selector.ShowDialog();
                var region = selector.SelectedRegion;
                if (region == null || region.Value.Width < 8 || region.Value.Height < 8)
                    return; // 用户取消

                // 截图在此时完成（主窗口已隐藏，画面干净）
                using var bitmap = App.ImageTranslateService.CaptureRegion(region.Value);

                // 屏幕显示翻译进度，避免应用看似消失
                progress = new ProgressWindow("正在识别并翻译图中英文…");
                progress.Show();

                var result = await App.ImageTranslateService.TranslateBitmapAsync(bitmap, vision, allowOcrFallback);
                progress.Close();
                progress = null;

                if (result.Regions.Count == 0)
                {
                    MessageBox.Show(this, "所选区域未识别到可翻译的英文文本", "Betty Translate",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var preview = new PreviewImageWindow(result.Image);
                preview.ShowDialog();
            }
            catch (Exception ex)
            {
                progress?.Close();
                progress = null;
                MessageBox.Show(this, "图片翻译失败：" + ex.Message, "Betty Translate",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Show();
            }
        }
        finally
        {
            _isImageTranslating = false;
        }
    }

    private void OnHotKeyPressed(int id)
    {
        if (id == HotkeyImageTranslateId)
        {
            if (!_isImageTranslating && _isImageRunning)
                _ = StartImageTranslateAsync();
            return;
        }
        if (id == HotkeyTranslateSelectionId)
        {
            if (_isSelectionRunning)
                _ = StartSelectionTranslateAsync();
            return;
        }
        if (id == HotkeyTranslateId && !_isTranslating)
            _ = StartRegionTranslateAsync();
    }

    private async Task StartRegionTranslateAsync()
    {
        _isTranslating = true;
        Hide(); // 隐藏主窗口，避免被框选/截图
        try
        {
            var selector = new RegionSelectorWindow();
            selector.ShowDialog();
            var region = selector.SelectedRegion;
            if (region == null || region.Value.Width < 8 || region.Value.Height < 8)
                return; // 用户取消

            var lines = await App.TranslateService.TranslateRegionAsync(region.Value);
            if (lines.Count == 0)
            {
                MessageBox.Show(this, "所选区域未识别到可翻译的英文文本", "Betty Translate",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var overlay = new OverlayTranslateWindow(lines);
            overlay.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "翻译失败：" + ex.Message, "Betty Translate",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Show();
            _isTranslating = false;
        }
    }

    /// <summary>点击「官方网址」：用系统默认浏览器打开官网</summary>
    private void OnOfficialUrlClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("https://www.ttals.com") { UseShellExecute = true });

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        await App.AuthService.SignOutAsync();
        new LoginWindow().Show();
        _reallyExit = true; // 登出不需要弹窗询问，直接关闭主窗口
        Close();
    }

    // ---------- 检查更新 ----------

    /// <summary>点击「检查更新」按钮：立即联网检查并（如有）自动下载安装包</summary>
    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
        => await CheckForUpdateAsync(silent: false);

    /// <summary>点击「查看详情」跳转到 GitHub Release 页面</summary>
    private void OnUpdateLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    /// <summary>
    /// 点击主页面更新提示横幅的「前往更新」：切换到设置页并定位到更新区。
    /// </summary>
    private void OnUpdateBannerClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsView();
        UpdateSectionPanel.BringIntoView();
    }

    /// <summary>
    /// 检查更新。启动静默检查（silent=true）时仅在主页面提示有新版本，不自动下载；
    /// 设置页手动检查（silent=false）时下载并应用更新。
    /// </summary>
    private async Task CheckForUpdateAsync(bool silent)
    {
        if (!CheckUpdateButton.IsEnabled)
            return; // 防止重复触发

        CheckUpdateButton.IsEnabled = false;
        UpdateResultText.Visibility = Visibility.Collapsed;
        UpdateResultText.Text = string.Empty;
        try
        {
            var ver = typeof(MainWindow).Assembly.GetName().Version;
            var current = new Version(ver?.Major ?? 0, ver?.Minor ?? 0, ver?.Build ?? 0);
            var updateCfg = AppSettings.Load(App.ConfigPath).Update;
            if (string.IsNullOrWhiteSpace(updateCfg.RepoOwner) ||
                string.IsNullOrWhiteSpace(updateCfg.RepoName))
            {
                if (!silent)
                    UpdateStatusText.Text = "尚未配置更新仓库（请在 Config/appsettings.json 填写 update.RepoOwner / update.RepoName）。";
                return;
            }

            UpdateStatusText.Text = "正在检查更新…";
            var info = await App.UpdateService.CheckForUpdateAsync(current);
            if (info == null)
            {
                UpdateStatusText.Text = "已是最新版本。";
                return;
            }

            // 启动静默检查：仅在主页面提示有新版本，不自动下载；点击「前往更新」跳转到设置页更新区。
            if (silent)
            {
                ShowUpdateBanner(current, info.LatestVersion);
                return;
            }

            // 设置页手动检查：继续下载并应用更新
            await DownloadAndApplyUpdateAsync(info, current);
        }
        catch (Exception ex)
        {
            UpdateProgressBar.Visibility = Visibility.Collapsed;
            UpdateStatusText.Text = "检查更新失败：" + ex.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>在主页面顶部显示「发现新版本」提示横幅</summary>
    private void ShowUpdateBanner(Version current, Version latest)
    {
        UpdateBannerText.Text = $"发现新版本 v{latest}（当前 v{current}），可前往设置页更新。";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    /// <summary>下载新版本安装包并应用（设置页手动触发），zip 包在应用内自动替换并重启</summary>
    private async Task DownloadAndApplyUpdateAsync(UpdateInfo info, Version current)
    {
        UpdateStatusText.Text = $"发现新版本 v{info.LatestVersion}（当前 v{current}）。正在自动下载安装包…";
        UpdateLink.NavigateUri = new Uri(info.HtmlUrl);
        UpdateResultText.Visibility = Visibility.Visible;

        // 自动下载安装包到临时目录，并实时显示进度
        var dest = Path.Combine(Path.GetTempPath(), info.AssetName);
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<double>(p =>
        {
            UpdateProgressBar.Value = p;
            UpdateStatusText.Text = $"正在下载 v{info.LatestVersion}… {p:P0}";
        });
        await App.UpdateService.DownloadAsync(info.DownloadUrl, dest, progress);
        UpdateProgressBar.Visibility = Visibility.Collapsed;

        // zip 包：应用内自动替换并重启；其他安装包（exe/msi）直接启动
        if (info.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var updateDialog = new UpdateConfirmWindow(info.LatestVersion.ToString(), info.AssetName) { Owner = this };
            var confirmed = updateDialog.ShowDialog() == true;
            if (!confirmed)
            {
                // 用户暂不更新：删除临时安装包，避免占用磁盘空间
                try { File.Delete(dest); } catch { }
                UpdateStatusText.Text = $"已下载 v{info.LatestVersion}，暂未应用。随时可再次点击「检查更新」完成升级。";
                return;
            }

            UpdateStatusText.Text = "正在应用更新，软件即将自动重启…";
            try
            {
                AutoUpdater.PrepareAndApplyUpdate(
                    dest,
                    AppContext.BaseDirectory,
                    Environment.ProcessPath ??
                        Path.Combine(AppContext.BaseDirectory, "BettyTranslate.App.exe"),
                    info.LatestVersion);
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "应用更新失败：" + ex.Message;
                return;
            }

            // 关闭当前进程，由守护脚本完成文件替换并重启
            _reallyExit = true;
            Close();
            return;
        }

        UpdateStatusText.Text = $"已下载 {info.AssetName}（v{info.LatestVersion}）。正在启动安装程序…";
        Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
    }

    /// <summary>点击右上角关闭时，弹出美观的自定义弹窗，让用户选择「隐藏到电脑扩展栏 / 退出程序」</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_reallyExit)
        {
            base.OnClosing(e);
            return;
        }

        // 先取消本次关闭，避免弹窗打开期间窗口重入关闭流程
        e.Cancel = true;

        var dialog = new CloseConfirmWindow { Owner = this };
        dialog.ShowDialog();

        if (dialog.ExitChosen)
        {
            _reallyExit = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }
        else
        {
            Hide();
            _trayIcon?.ShowBalloon("Betty Translate", "已隐藏到电脑扩展栏，双击托盘图标可恢复主界面");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeys?.Dispose();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
