using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OverlayTranslate.Controls;

/// <summary>
/// 选区绘制画布，负责在覆盖层上绘制选区矩形边框。
/// </summary>
public class SelectionCanvas : Canvas
{
    private Rectangle? _selectionRect;
    private readonly SolidColorBrush _strokeBrush = new(Colors.DodgerBlue);
    private readonly DoubleCollection _dashPattern = [4, 2];

    public SelectionCanvas()
    {
        IsHitTestVisible = false;
        _strokeBrush.Freeze();
    }

    /// <summary>
    /// 更新选区矩形的显示。
    /// </summary>
    public void UpdateSelection(Rect selection)
    {
        if (_selectionRect == null)
        {
            _selectionRect = new Rectangle
            {
                Stroke = _strokeBrush,
                StrokeThickness = 2,
                StrokeDashArray = _dashPattern,
                Fill = Brushes.Transparent
            };
            Children.Add(_selectionRect);
        }

        SetLeft(_selectionRect, selection.X);
        SetTop(_selectionRect, selection.Y);
        _selectionRect.Width = selection.Width;
        _selectionRect.Height = selection.Height;
    }

    /// <summary>
    /// 清除选区显示。
    /// </summary>
    public void ClearSelection()
    {
        if (_selectionRect != null)
        {
            Children.Remove(_selectionRect);
            _selectionRect = null;
        }
    }
}
