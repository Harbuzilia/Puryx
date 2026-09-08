using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartCleaner.App.Controls;

/// <summary>
/// Простой контрол столбчатой диаграммы для дашборда.
/// Рисует вертикальные бары с подписями через DrawingContext.
/// </summary>
public class BarChartControl : Control
{
    // ─── Dependency Properties ─────────────────

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(List<double>), typeof(BarChartControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty LabelsProperty =
        DependencyProperty.Register(nameof(Labels), typeof(List<string>), typeof(BarChartControl),
            new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Brush), typeof(BarChartControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(66, 133, 244)), OnDataChanged));

    /// <summary>Значения столбцов.</summary>
    public List<double>? Values
    {
        get => (List<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>Подписи столбцов (ось X).</summary>
    public List<string>? Labels
    {
        get => (List<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>Цвет столбцов.</summary>
    public Brush BarColor
    {
        get => (Brush)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BarChartControl ctrl) ctrl.InvalidateVisual();
    }

    // ─── Render ────────────────────────────────

    private static readonly Typeface TypefaceLabel = new("Segoe UI");
    private static readonly Brush GridLineBrush;
    private static readonly Pen GridLinePen;

    static BarChartControl()
    {
        GridLineBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        GridLineBrush.Freeze();
        GridLinePen = new Pen(GridLineBrush, 1);
        GridLinePen.Freeze();
    }

    /// <summary>
    /// Отрисовывает столбчатую диаграмму.
    /// </summary>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (Values == null || Values.Count == 0 || ActualWidth < 20 || ActualHeight < 40)
            return;

        double paddingBottom = 20; // для подписей
        double paddingTop = 10;
        double chartWidth = ActualWidth;
        double chartHeight = ActualHeight - paddingBottom - paddingTop;

        double maxValue = Values.Max();
        if (maxValue <= 0) maxValue = 1;

        int count = Values.Count;
        double barWidth = chartWidth / count * 0.7;
        double gap = chartWidth / count * 0.3;
        double step = chartWidth / count;

        var dpi = GetSafeDpi();

        // Горизонтальные линии сетки (4 шт)
        for (int i = 1; i <= 4; i++)
        {
            double y = paddingTop + chartHeight * (1.0 - i / 4.0);
            dc.DrawLine(GridLinePen, new Point(0, y), new Point(chartWidth, y));
        }

        // Столбцы
        for (int i = 0; i < count; i++)
        {
            double value = Values[i];
            double barHeight = (value / maxValue) * chartHeight;
            double x = i * step + gap / 2;
            double y = paddingTop + chartHeight - barHeight;

            // Градиент для каждого бара
            var rect = new Rect(x, y, barWidth, barHeight);
            dc.DrawRoundedRectangle(BarColor, null, rect, 3, 3);

            // Подпись снизу
            if (Labels != null && i < Labels.Count)
            {
                var labelText = new FormattedText(
                    Labels[i], CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, TypefaceLabel, 9, Foreground ?? Brushes.White, dpi);
                labelText.MaxTextWidth = step;
                labelText.TextAlignment = TextAlignment.Center;
                dc.DrawText(labelText, new Point(x - gap / 2, paddingTop + chartHeight + 3));
            }

            // Значение над столбцом (если есть место)
            if (barHeight > 15 && value > 0)
            {
                var valFormat = FormatValue(value);
                var valText = new FormattedText(
                    valFormat, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, TypefaceLabel, 9, Foreground ?? Brushes.White, dpi);
                valText.MaxTextWidth = barWidth + gap;
                valText.TextAlignment = TextAlignment.Center;
                dc.DrawText(valText, new Point(x - gap / 4, y - 14));
            }
        }
    }

    /// <summary>
    /// Форматирует значение (предполагаем байты → МБ/ГБ).
    /// </summary>
    private static string FormatValue(double bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024 * 1024 * 1024):F1}G";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024 * 1024):F0}M";
        if (bytes >= 1024) return $"{bytes / 1024:F0}K";
        return $"{bytes:F0}";
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
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
        catch (Exception ex) { /* designer mode */ System.Diagnostics.Debug.WriteLine($"[BarChartControl] GetSafeDpi error: {ex.Message}"); }
        return 1.0;
    }
}
