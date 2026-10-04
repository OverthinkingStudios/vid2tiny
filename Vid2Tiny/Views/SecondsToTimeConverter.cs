using System.Globalization;
using System.Windows.Data;

namespace Vid2Tiny.Views;

public sealed class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var time = TimeSpan.FromSeconds(value is double seconds && double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);
        return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.f" : @"m\:ss\.f", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
