using System.Globalization;
using System.Windows.Data;

namespace Vid2Tiny.Views;

public sealed class RelativeDayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime day)
        {
            return value;
        }

        var today = DateTime.Today;
        return (today - day.Date).Days switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => day.ToString("dddd", CultureInfo.InvariantCulture),
            _ when day.Year == today.Year => day.ToString("dddd, MMMM d", CultureInfo.InvariantCulture),
            _ => day.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
