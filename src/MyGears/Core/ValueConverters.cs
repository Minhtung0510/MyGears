using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyGears.Core;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
            return v != Visibility.Visible;
        return false;
    }
}

public class DoubleToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return $"{(int)(Math.Clamp(d, 0.0, 1.0) * 100)}%";
        }
        return "0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return 0.0;
    }
}
