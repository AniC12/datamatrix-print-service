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
            JobStatus.Ready => new SolidColorBrush(Color.FromRgb(23, 162, 184)),
            JobStatus.Preparing => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
            JobStatus.Completed => new SolidColorBrush(Color.FromRgb(40, 167, 69)),
            JobStatus.Cancelled => new SolidColorBrush(Color.FromRgb(108, 117, 125)),
            JobStatus.Error => new SolidColorBrush(Color.FromRgb(220, 53, 69)),
            _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SeverityToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            AlertSeverity.Error => new SolidColorBrush(Color.FromRgb(248, 215, 218)),   // light red
            AlertSeverity.Warning => new SolidColorBrush(Color.FromRgb(255, 243, 205)), // light yellow
            AlertSeverity.Info => new SolidColorBrush(Color.FromRgb(212, 237, 218)),    // light green
            _ => new SolidColorBrush(Color.FromRgb(255, 243, 205))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SeverityToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            AlertSeverity.Error => new SolidColorBrush(Color.FromRgb(114, 28, 36)),
            AlertSeverity.Warning => new SolidColorBrush(Color.FromRgb(133, 100, 4)),
            AlertSeverity.Info => new SolidColorBrush(Color.FromRgb(21, 87, 36)),
            _ => new SolidColorBrush(Colors.Black)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SeverityToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            AlertSeverity.Error => "X",
            AlertSeverity.Warning => "!",
            AlertSeverity.Info => "i",
            _ => "?"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class JobStatusToActionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not JobStatus status || parameter is not string action)
            return System.Windows.Visibility.Collapsed;

        var visible = action switch
        {
            "Pause" => status == JobStatus.Printing,
            "Cancel" => status is JobStatus.Printing or JobStatus.Ready or JobStatus.Preparing,
            "StartPrint" => status == JobStatus.Ready,
            "Resume" => false, // Paused not in Phase 1 enum but reserved
            _ => false
        };

        return visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
