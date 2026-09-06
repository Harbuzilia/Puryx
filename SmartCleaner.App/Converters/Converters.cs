using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartCleaner.App.Converters;

/// <summary>
/// Конвертер размера в читаемый формат
/// </summary>
public class SizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return SizeFormatter.Format(bytes);
        return "0 Б";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер RiskCategory в цвет фона
/// </summary>
public class RiskToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RiskCategory risk)
        {
            return risk switch
            {
                RiskCategory.SafeToDelete => new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)),
                RiskCategory.PerformanceCache => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                RiskCategory.UserData => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                RiskCategory.Locked => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                _ => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер RiskCategory в текст
/// </summary>
public class RiskToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RiskCategory risk)
        {
            return risk switch
            {
                RiskCategory.SafeToDelete => "Безопасно",
                RiskCategory.PerformanceCache => "Кэш",
                RiskCategory.UserData => "Защищено",
                RiskCategory.Locked => "Занято",
                _ => "?"
            };
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер RiskCategory в доступность чекбокса
/// </summary>
public class RiskToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RiskCategory risk)
        {
            return risk != RiskCategory.UserData && risk != RiskCategory.Locked;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер bool в Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер count=0 в Visibility.Visible (для empty state)
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Инверсия bool
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Конвертер bool в текст "Добавлено" / "+ Добавить"
/// </summary>
public class BoolToAddedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isAdded)
            return isAdded ? "✓ Добавлено" : "+ Добавить";
        return "+ Добавить";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Инверсия bool в Visibility (true → Collapsed, false → Visible)
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Показывает элемент если значение не null
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Показывает элемент если count > 0, скрывает если count == 0
/// (инверсия CountToVisibilityConverter)
/// </summary>
public class CountToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// int → Visibility
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            if (parameter is string paramStr && int.TryParse(paramStr, out int paramInt))
                return intValue == paramInt ? Visibility.Visible : Visibility.Collapsed;

            return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool → "Отключить" / "Включить" (для toggle кнопки в Автозагрузке)
/// </summary>
public class BoolToToggleTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
            return isEnabled ? "⛔ Откл." : "✅ Вкл.";
        return "✅ Вкл.";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// int count > 0 → Red / Danger brush, else Green / Safe brush
/// </summary>
public class ZeroToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 0)
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)); // Danger
        }
        return new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F)); // Safe
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool isDead → Red if dead, Green if active
/// </summary>
public class DeadStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDead && isDead)
        {
            return new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)); // Danger
        }
        return new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F)); // Safe
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// string is null or empty → Collapsed, else Visible
/// </summary>
public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
