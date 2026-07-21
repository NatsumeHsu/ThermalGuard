using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ThermalGuard.Converters;

public sealed class PercentageToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NormalBrush = CreateBrush("#69F0AE");
    private static readonly SolidColorBrush WarningBrush = CreateBrush("#FFD166");
    private static readonly SolidColorBrush CriticalBrush = CreateBrush("#FF6B6B");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percentage = value is double number ? number : 0;
        return percentage >= 85 ? CriticalBrush : percentage >= 60 ? WarningBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
