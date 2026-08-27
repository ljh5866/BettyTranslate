using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BettyTranslate.Core.Auth;

namespace BettyTranslate.App.ViewModels;

/// <summary>
/// 登录页 ViewModel：登录（邮箱+密码）/ 注册（邮箱+密码+6位验证码）/ 提示信息
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _verificationCode = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>绿色提示信息（如"验证码已发送"）</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>当前是否处于注册模式（false 为登录模式）</summary>
    [ObservableProperty]
    private bool _isRegisterMode;

    /// <summary>当前是否处于登录模式（与 IsRegisterMode 相反）</summary>
    public bool IsLoginMode => !IsRegisterMode;

    /// <summary>登录成功事件（由窗口订阅以跳转主界面）</summary>
    public event Action? LoginSucceeded;

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand SendCodeCommand { get; }
    public IAsyncRelayCommand RegisterCommand { get; }
    public IRelayCommand SwitchToLoginCommand { get; }
    public IRelayCommand SwitchToRegisterCommand { get; }

    public LoginViewModel(IAuthService auth)
    {
        _auth = auth;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => CanSubmit);
        SendCodeCommand = new AsyncRelayCommand(SendCodeAsync, () => CanSendCode);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, () => CanRegister);
        SwitchToLoginCommand = new RelayCommand(SwitchToLogin);
        SwitchToRegisterCommand = new RelayCommand(SwitchToRegister);
    }

    partial void OnIsBusyChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        SendCodeCommand.NotifyCanExecuteChanged();
        RegisterCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRegisterModeChanged(bool value) => OnPropertyChanged(nameof(IsLoginMode));

    partial void OnEmailChanged(string value) => NotifyAll();
    partial void OnPasswordChanged(string value) => NotifyAll();
    partial void OnVerificationCodeChanged(string value) => NotifyAll();

    private bool CanSubmit => !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>发送验证码：邮箱格式合法且非忙碌</summary>
    private bool CanSendCode => !IsBusy && EmailRegex.IsMatch(Email);

    /// <summary>注册：邮箱、密码合法且验证码为 6 位数字</summary>
    private bool CanRegister => !IsBusy && EmailRegex.IsMatch(Email)
        && Password.Length >= 6 && VerificationCodeRegex.IsMatch(VerificationCode);

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex VerificationCodeRegex = new(@"^\d{6}$", RegexOptions.Compiled);

    private void NotifyAll()
    {
        LoginCommand.NotifyCanExecuteChanged();
        SendCodeCommand.NotifyCanExecuteChanged();
        RegisterCommand.NotifyCanExecuteChanged();
    }

    /// <summary>切换到登录模式并清空表单</summary>
    private void SwitchToLogin()
    {
        IsRegisterMode = false;
        ClearInputs();
    }

    /// <summary>切换到注册模式并清空表单</summary>
    private void SwitchToRegister()
    {
        IsRegisterMode = true;
        ClearInputs();
    }

    private void ClearInputs()
    {
        Password = string.Empty;
        VerificationCode = string.Empty;
        ErrorMessage = string.Empty;
        Notice = string.Empty;
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        Notice = string.Empty;
        try
        {
            if (await _auth.SignInAsync(Email, Password))
                LoginSucceeded?.Invoke();
            else
                ErrorMessage = "登录失败，请检查邮箱与密码";
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendCodeAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        Notice = string.Empty;
        try
        {
            await _auth.SendVerificationCodeAsync(Email);
            Notice = "验证码已发送，请查收邮箱";
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RegisterAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        Notice = string.Empty;
        try
        {
            // 验证码校验通过后即完成注册并登录（VerifyOTP + SignUp）
            if (await _auth.RegisterWithCodeAsync(Email, Password, VerificationCode))
            {
                Notice = "注册成功";
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = "注册成功，请前往邮箱确认后返回登录";
            }
        }
        catch (AuthException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
