using System.Windows;
using BettyTranslate.App.ViewModels;

namespace BettyTranslate.App.Views;

/// <summary>
/// 登录窗口：绑定登录/注册命令，密码框在代码后同步到 ViewModel
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel(App.AuthService);
        DataContext = _viewModel;
        // PasswordBox 不支持绑定，同步到 ViewModel
        PasswordBox.PasswordChanged += (_, _) => _viewModel.Password = PasswordBox.Password;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded()
    {
        var main = new MainWindow();
        main.Show();
        Close();
    }
}
