using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BettyTranslate.Core.Auth;

namespace BettyTranslate.App.ViewModels;

/// <summary>
/// 登录页 ViewModel：登录 / 注册 / 错误提示
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _auth;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>登录成功事件（由窗口订阅以跳转主界面）</summary>
    public event Action? LoginSucceeded;

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand RegisterCommand { get; }

    public LoginViewModel(IAuthService auth)
    {
        _auth = auth;
        LoginCommand = new AsyncRelayCommand(LoginAsync, () => CanSubmit);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, () => CanSubmit);
    }

    partial void OnIsBusyChanged(bool value)
    {
        LoginCommand.NotifyCanExecuteChanged();
        RegisterCommand.NotifyCanExecuteChanged();
    }

    partial void OnEmailChanged(string value) => NotifyCanSubmit();
    partial void OnPasswordChanged(string value) => NotifyCanSubmit();

    private bool CanSubmit => !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    private void NotifyCanSubmit()
    {
        LoginCommand.NotifyCanExecuteChanged();
        RegisterCommand.NotifyCanExecuteChanged();
    }

    private async Task LoginAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
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

    private async Task RegisterAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            if (await _auth.SignUpAsync(Email, Password))
                ErrorMessage = "注册成功，请前往邮箱确认后登录";
            else
                ErrorMessage = "注册失败，请重试";
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
