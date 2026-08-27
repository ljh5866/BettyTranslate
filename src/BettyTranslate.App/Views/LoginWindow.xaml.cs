using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using BettyTranslate.App.ViewModels;

namespace BettyTranslate.App.Views;

/// <summary>
/// 登录窗口：绑定登录/注册命令，处理窗口拖拽、关闭、密码框同步与模式切换清理
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel(App.AuthService);
        DataContext = _viewModel;
        // PasswordBox 不支持绑定，两个密码框都在代码后同步到 ViewModel
        PasswordBox.PasswordChanged += (_, _) => _viewModel.Password = PasswordBox.Password;
        RegPasswordBox.PasswordChanged += (_, _) => _viewModel.Password = RegPasswordBox.Password;
        // 切换登录/注册模式时清空两个密码框，避免跨模式残留
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.IsRegisterMode))
            {
                PasswordBox.Clear();
                RegPasswordBox.Clear();
            }
        };
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    /// <summary>按住标题栏拖动窗口</summary>
    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnLoginSucceeded()
    {
        try
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("进入主界面失败：" + ex.Message, "Betty Translate",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
