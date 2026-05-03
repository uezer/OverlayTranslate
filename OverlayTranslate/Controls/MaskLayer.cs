using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OverlayTranslate.Controls;

/// <summary>
/// 半透明遮罩层，选区部分被挖空以显示底层内容。
/// </summary>
public class MaskLayer : Canvas
{
    private readonly Rectangle _maskRect;
    private Path? _selectionPath;
    private readonly SolidColorBrush _maskBrush = new(Color.FromArgb(128, 0, 0, 0));

    public MaskLayer()
    {
        _maskBrush.Freeze();
        IsHitTestVisible = false;

        // 全屏半透明黑色遮罩
        _maskRect = new Rectangle
        {
            Fill = _maskBrush,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight
        };

        SetLeft(_maskRect, 0);
        SetTop(_maskRect, 0);
        Children.Add(_maskRect);

        // 响应窗口尺寸变化
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _maskRect.Width = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        _maskRect.Height = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
    }

    /// <summary>
    /// 设置选区，将选区部分从遮罩中挖空。
    /// </summary>
    public void SetSelection(Rect selection)
    {
        // 移除旧的选区路径
        if (_selectionPath != null)
        {
            Children.Remove(_selectionPath);
            _selectionPath = null;
        }

        // 全屏矩形几何
        var screenGeometry = new RectangleGeometry(new Rect(
            0, 0,
            _maskRect.Width,
            _maskRect.Height));

        // 选区矩形几何
        var selectionGeometry = new RectangleGeometry(selection);

        // 使用 CombinedGeometry 将选区从全屏遮罩中排除
        var combinedGeometry = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            screenGeometry,
            selectionGeometry);

        _selectionPath = new Path
        {
            Data = combinedGeometry,
            Fill = _maskBrush
        };

        // 移除全屏矩形，改用组合几何路径
        Children.Remove(_maskRect);
        Children.Add(_selectionPath);
    }

    /// <summary>
    /// 清除选区，恢复全屏遮罩。
    /// </summary>
    public void ClearSelection()
    {
        if (_selectionPath != null)
        {
            Children.Remove(_selectionPath);
            _selectionPath = null;
        }

        if (!Children.Contains(_maskRect))
        {
            Children.Add(_maskRect);
        }
    }
}
