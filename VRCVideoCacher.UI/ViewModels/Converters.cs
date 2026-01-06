using System.Globalization;
using Avalonia.Data.Converters;

namespace VRCVideoCacher.UI.ViewModels;

public class BoolToStatusConverter : IValueConverter
{
    public static readonly BoolToStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Running" : "Stopped";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToValidConverter : IValueConverter
{
    public static readonly BoolToValidConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Valid" : "Not Set";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    private static readonly string[] SizeSuffixes = ["B", "KB", "MB", "GB", "TB"];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return "0 B";

        if (bytes == 0)
            return "0 B";

        var mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, SizeSuffixes.Length - 1);
        var adjustedSize = bytes / Math.Pow(1024, mag);

        return $"{adjustedSize:N2} {SizeSuffixes[mag]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class GreaterThanZeroConverter : IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int i => i > 0,
            float f => f > 0,
            double d => d > 0,
            long l => l > 0,
            _ => false
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
