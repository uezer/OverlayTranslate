using System.Windows;
using OverlayTranslate.ViewModels;

namespace OverlayTranslate.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = _vm;
        InitializeComponent();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        MessageBox.Show(
            "设置已保存。\n\n" +
            "以下设置立即生效：语言、OCR/翻译引擎选择、API Key。\n" +
            "以下设置需要重启应用：热键、日志级别、日志文件路径、Python 路径、OCR 模型路径。",
            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
