using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CodePrintManager.Domain.Enums;

namespace CodePrintManager.Desktop.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            PrinterStatus.Idle => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
            PrinterStatus.Printing => new SolidColorBrush(Color.FromRgb(0, 123, 255)),
            PrinterStatus.Error => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            PrinterStatus.Blocked => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
            PrinterStatus.Offline => new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            JobStatus.Printing => new SolidColorBrush(Color.FromRgb(0, 123, 255)),
            JobStatus.Completed => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
            JobStatus.Cancelled => new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            JobStatus.Error => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
