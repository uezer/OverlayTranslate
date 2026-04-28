using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Rectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using Canvas = System.Windows.Controls.Canvas;

namespace OverlayTranslate;

public partial class OverlayWindow : Window
{
    public event EventHandler<Rectangle>? SelectionCommitted;
    public event EventHandler? ExitRequested;
    public event EventHandler? ReselectRequested;
    public event EventHandler? RetryRequested;

    private Point? _dragStart;
    private Rectangle? _currentSelection;
    private bool _selectionEnabled = true;

    public OverlayWindow()
    {
        InitializeComponent();
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        UpdateMasks(null);
    }

    public void ShowScreenshot(Bitmap bitmap)
    {
        ScreenshotImage.Source = ConvertToBitmapSource(bitmap);
        EnableSelectionMode();
        Focus();
        Keyboard.Focus(this);
    }

    public void ShowProcessing(Rectangle selection, string message)
    {
        _selectionEnabled = false;
        _currentSelection = selection;
        UpdateSelectionVisual(selection);
        StatusText.Text = message;
        StatusBadge.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Visible;
        RetryButton.IsEnabled = false;
        PositionControls(selection);
        Focus();
        Keyboard.Focus(this);
    }

    public void ShowRenderedResult(Bitmap bitmap, Rectangle? selection, string message)
    {
        ScreenshotImage.Source = ConvertToBitmapSource(bitmap);
        StatusText.Text = message;
        StatusBadge.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        if (selection is null)
        {
            _currentSelection = null;
            SelectionBorder.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Collapsed;
            UpdateMasks(null);
            return;
        }

        _currentSelection = selection;
        UpdateSelectionVisual(selection.Value);
        Toolbar.Visibility = Visibility.Visible;
        RetryButton.IsEnabled = true;
        PositionControls(selection.Value);
    }

    public void ShowError(string message)
    {
        StatusText.Text = message;
        StatusBadge.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Visible;
        RetryButton.IsEnabled = _currentSelection is not null;
        if (_currentSelection is { } selection)
        {
            PositionControls(selection);
        }
    }

    public void EnableSelectionMode()
    {
        _selectionEnabled = true;
        _dragStart = null;
        StatusBadge.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        RetryButton.IsEnabled = false;
        UpdateMasks(_currentSelection);
        Focus();
        Keyboard.Focus(this);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_selectionEnabled)
        {
            return;
        }

        _dragStart = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_selectionEnabled || _dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(this);
        Rectangle selection = CreateRectangle(_dragStart.Value, current);
        _currentSelection = selection;
        UpdateSelectionVisual(selection);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        ReleaseMouseCapture();
        if (_currentSelection is { } selection)
        {
            _selectionEnabled = false;
            SelectionCommitted?.Invoke(this, selection);
        }

        _dragStart = null;
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnReselectClick(object sender, RoutedEventArgs e)
    {
        ReselectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectionVisual(Rectangle selection)
    {
        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, selection.Left);
        Canvas.SetTop(SelectionBorder, selection.Top);
        SelectionBorder.Width = selection.Width;
        SelectionBorder.Height = selection.Height;
        UpdateMasks(selection);
        PositionControls(selection);
    }

    private void PositionControls(Rectangle selection)
    {
        double toolbarLeft = Math.Max(12, Math.Min(ActualWidth - Toolbar.ActualWidth - 12, selection.Left + (selection.Width / 2.0) - 110));
        double toolbarTop = selection.Bottom + 12;
        if (toolbarTop + 60 > ActualHeight)
        {
            toolbarTop = Math.Max(12, selection.Top - 64);
        }

        Canvas.SetLeft(Toolbar, toolbarLeft);
        Canvas.SetTop(Toolbar, toolbarTop);

        Canvas.SetLeft(StatusBadge, toolbarLeft);
        Canvas.SetTop(StatusBadge, Math.Max(12, toolbarTop - 46));
    }

    private void UpdateMasks(Rectangle? selection)
    {
        double width = ActualWidth <= 0 ? Width : ActualWidth;
        double height = ActualHeight <= 0 ? Height : ActualHeight;

        if (selection is null)
        {
            SetRect(MaskTop, 0, 0, width, height);
            SetRect(MaskLeft, 0, 0, 0, 0);
            SetRect(MaskRight, 0, 0, 0, 0);
            SetRect(MaskBottom, 0, 0, 0, 0);
            return;
        }

        Rectangle value = selection.Value;
        SetRect(MaskTop, 0, 0, width, value.Top);
        SetRect(MaskLeft, 0, value.Top, value.Left, value.Height);
        SetRect(MaskRight, value.Right, value.Top, Math.Max(0, width - value.Right), value.Height);
        SetRect(MaskBottom, 0, value.Bottom, width, Math.Max(0, height - value.Bottom));
    }

    private static Rectangle CreateRectangle(Point start, Point end)
    {
        int left = (int)Math.Round(Math.Min(start.X, end.X));
        int top = (int)Math.Round(Math.Min(start.Y, end.Y));
        int width = (int)Math.Round(Math.Abs(end.X - start.X));
        int height = (int)Math.Round(Math.Abs(end.Y - start.Y));
        return new Rectangle(left, top, width, height);
    }

    private static void SetRect(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = width;
        element.Height = height;
    }

    private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        nint hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);
}
