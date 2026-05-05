using System.Windows;
using OverlayTranslate.ViewModels;

namespace OverlayTranslate.Windows;

public partial class UpdateDialog : Window
{
    private readonly UpdateDialogViewModel _viewModel;

    public UpdateDialog(UpdateDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    private void OnSkipVersionClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SkipVersionCommand.Execute(null);
        DialogResult = false;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelCommand.Execute(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }
}
