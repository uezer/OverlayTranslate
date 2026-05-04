using System.Windows;
using OverlayTranslate.Localization;
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
        MessageBox.Show(LocManager.Get("Msg_SettingsSaved_Body"),
            LocManager.Get("Msg_SettingsSaved_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
