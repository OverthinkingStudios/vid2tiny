using System.Globalization;
using System.Windows.Data;

namespace Vid2Tiny.Views;

public sealed class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var time = TimeSpan.FromSeconds(value is double seconds && double.IsFinite(seconds) ? Math.Round(Math.Max(0, seconds), 3) : 0);
        return time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss\.ff" : @"m\:ss\.ff", CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
