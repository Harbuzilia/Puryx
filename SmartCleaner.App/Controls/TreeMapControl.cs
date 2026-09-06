using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartCleaner.Core.DiskMap;

namespace SmartCleaner.App.Controls;

/// <summary>
/// Пользовательский контрол TreeMap для визуализации использования диска.
/// Рисует прямоугольники файлов/папок пропорционально их размеру.
/// Использует алгоритм Squarified TreeMap.
/// </summary>
public class TreeMapControl : Control
{
    // ─── Dependency Properties ─────────────────

    public static readonly DependencyProperty RootNodeProperty =
        DependencyProperty.Register(nameof(RootNode), typeof(DiskNode), typeof(TreeMapControl),
            new PropertyMetadata(null, OnRootNodeChanged));

    /// <summary>
    /// Корневой узел дерева для отображения.
    /// </summary>
    public DiskNode? RootNode
    {
        get => (DiskNode?)GetValue(RootNodeProperty);
        set => SetValue(RootNodeProperty, value);
    }

    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.Register(nameof(SelectedNode), typeof(DiskNode), typeof(TreeMapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// Выбранный узел (по клику).
    /// </summary>
    public DiskNode? SelectedNode
    {
        get => (DiskNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    // ─── State ─────────────────────────────────

    private readonly List<(Rect Rect, DiskNode Node)> _rects = [];
    private (Rect Rect, DiskNode Node)? _hoveredItem;

    // Цветовая палитра для папок по глубине
    private static readonly Brush[] DepthColors =
    [
        new SolidColorBrush(Color.FromRgb(66, 133, 244)),   // blue
        new SolidColorBrush(Color.FromRgb(52, 168, 83)),    // green
        new SolidColorBrush(Color.FromRgb(251, 188, 4)),    // yellow
        new SolidColorBrush(Color.FromRgb(234, 67, 53)),    // red
        new SolidColorBrush(Color.FromRgb(168, 102, 221)),  // purple
        new SolidColorBrush(Color.FromRgb(255, 112, 67)),   // deep orange
        new SolidColorBrush(Color.FromRgb(38, 198, 218)),   // cyan
        new SolidColorBrush(Color.FromRgb(102, 187, 106)),  // light green
    ];

    private static readonly Brush FileBrush = new SolidColorBrush(Color.FromRgb(78, 84, 92));
    private static readonly Brush TreeBorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0));
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private static readonly Brush TextBrush = Brushes.White;
    private static readonly Pen TreeBorderPen = new(TreeBorderBrush, 1);
    private static readonly Typeface TypefaceNormal = new("Segoe UI");

    static TreeMapControl()
    {
        foreach (var brush in DepthColors) brush.Freeze();
        FileBrush.Freeze();
        TreeBorderBrush.Freeze();
        HoverBrush.Freeze();
        TreeBorderPen.Freeze();
    }

    // ─── Render ────────────────────────────────

    private static void OnRootNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeMapControl ctrl)
            ctrl.InvalidateVisual();
    }

    /// <summary>
    /// Отрисовывает TreeMap.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _rects.Clear();

        if (RootNode == null || RootNode.Size == 0 || ActualWidth < 10 || ActualHeight < 10)
            return;

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        LayoutSquarified(dc, RootNode.Children, bounds, 0);
    }

    /// <summary>
    /// Squarified TreeMap layout.
    /// </summary>
    private void LayoutSquarified(DrawingContext dc, List<DiskNode> nodes, Rect bounds, int depth)
    {
        if (nodes.Count == 0 || bounds.Width < 2 || bounds.Height < 2) return;

        long totalSize = nodes.Sum(n => n.Size);
        if (totalSize == 0) return;

        double x = bounds.X, y = bounds.Y, w = bounds.Width, h = bounds.Height;
        bool horizontal = w >= h;

        foreach (var node in nodes)
        {
            double ratio = (double)node.Size / totalSize;
            double nodeW, nodeH;

            if (horizontal)
            {
                nodeW = w * ratio;
                nodeH = h;
            }
            else
            {
                nodeW = w;
                nodeH = h * ratio;
            }

            if (nodeW < 1 || nodeH < 1) continue;

            var rect = new Rect(x, y, nodeW, nodeH);

            // Рисуем прямоугольник
            var brush = node.IsFile ? GetFileBrush(node.FullPath) : GetColorForDepth(depth);
            dc.DrawRectangle(brush, TreeBorderPen, rect);

            // Hover overlay
            if (_hoveredItem.HasValue && _hoveredItem.Value.Node == node)
            {
                dc.DrawRectangle(HoverBrush, null, rect);
            }

            // Запоминаем для hit-теста
            _rects.Add((rect, node));

            // Текст (если достаточно места)
            if (nodeW > 30 && nodeH > 14)
            {
                var text = CreateText(node.Name, Math.Min(11, nodeW / node.Name.Length * 1.2));
                text.MaxTextWidth = nodeW - 4;
                text.MaxTextHeight = nodeH - 2;
                dc.DrawText(text, new Point(rect.X + 2, rect.Y + 1));
            }

            // Рекурсия для папок (если достаточно места)
            if (!node.IsFile && node.Children.Count > 0 && nodeW > 20 && nodeH > 20 && depth < 4)
            {
                var innerRect = new Rect(rect.X + 1, rect.Y + 14, rect.Width - 2, rect.Height - 15);
                if (innerRect.Width > 5 && innerRect.Height > 5)
                    LayoutSquarified(dc, node.Children, innerRect, depth + 1);
            }

            if (horizontal) x += nodeW;
            else y += nodeH;
        }
    }

    private static Brush GetColorForDepth(int depth) => DepthColors[depth % DepthColors.Length];

    private static readonly Dictionary<string, Brush> ExtensionBrushes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
        [".mkv"] = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
        [".avi"] = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
        [".zip"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        [".rar"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        [".7z"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        [".iso"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        [".vhdx"] = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        [".exe"] = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
        [".dll"] = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
        [".msi"] = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
        [".png"] = new SolidColorBrush(Color.FromRgb(236, 72, 153)),
        [".jpg"] = new SolidColorBrush(Color.FromRgb(236, 72, 153)),
        [".jpeg"] = new SolidColorBrush(Color.FromRgb(236, 72, 153)),
        [".cs"] = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
        [".js"] = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
        [".py"] = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
        [".pdf"] = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
        [".docx"] = new SolidColorBrush(Color.FromRgb(14, 165, 233))
    };

    private static Brush GetFileBrush(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && ExtensionBrushes.TryGetValue(ext, out var b))
            return b;
        return FileBrush;
    }

    private static FormattedText CreateText(string text, double size)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TypefaceNormal,
            Math.Max(8, size),
            TextBrush,
            GetSafeDpi());
    }

    /// <summary>
    /// Безопасное получение DPI — защита от null MainWindow (designer mode).
    /// </summary>
    private static double GetSafeDpi()
    {
        try
        {
            if (Application.Current?.MainWindow != null)
                return VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;
        }
        catch { /* designer mode */ }
        return 1.0;
    }

    // ─── Mouse interaction ─────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        var hit = HitTest(pos);

        if (hit != _hoveredItem)
        {
            _hoveredItem = hit;
            InvalidateVisual();

            if (hit.HasValue)
            {
                var n = hit.Value.Node;
                ToolTip = $"{n.Name}\n{SmartCleaner.Core.Helpers.SizeFormatter.Format(n.Size)}" +
                          $"\n{n.Percentage:F1}%\n{n.FullPath}";
            }
            else ToolTip = null;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var pos = e.GetPosition(this);
        var hit = HitTest(pos);
        if (hit.HasValue)
        {
            SelectedNode = hit.Value.Node;
        }
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var pos = e.GetPosition(this);
        var hit = HitTest(pos);
        if (hit.HasValue && !hit.Value.Node.IsFile && hit.Value.Node.Children.Count > 0)
        {
            // Двойной клик по папке → зумим в неё
            RootNode = hit.Value.Node;
        }
    }

    private (Rect Rect, DiskNode Node)? HitTest(Point pos)
    {
        // Обратный порядок — мелкие узлы сверху (нарисованы позже)
        for (int i = _rects.Count - 1; i >= 0; i--)
        {
            if (_rects[i].Rect.Contains(pos))
                return _rects[i];
        }
        return null;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }
}
