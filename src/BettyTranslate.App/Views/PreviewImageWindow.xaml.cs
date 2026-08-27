using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BettyTranslate.App.Views;

/// <summary>
/// 图片翻译结果预览窗：显示合成后的新图片，支持保存到本地或复制到剪贴板。
/// </summary>
public partial class PreviewImageWindow : Window
{
    private readonly Bitmap _image;

    public PreviewImageWindow(Bitmap image)
    {
        InitializeComponent();
        _image = image;

        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存翻译图片",
            Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg|位图 (*.bmp)|*.bmp",
            FileName = $"翻译_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            _ => ImageFormat.Png,
        };
        try
        {
            _image.Save(dialog.FileName, format);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "Betty Translate",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(this, $"已保存到：{dialog.FileName}", "Betty Translate",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(ToBitmapSource(_image));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败：" + ex.Message, "Betty Translate",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var src = new BitmapImage();
        src.BeginInit();
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.StreamSource = ms;
        src.EndInit();
        src.Freeze();
        return src;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}
