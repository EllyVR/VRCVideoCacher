using System.Globalization;
using Avalonia.Data.Converters;

namespace VRCVideoCacher.UI.Converters;

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
        var adjustedSize = bytes / Math.Pow(1024, mag);

        return $"{adjustedSize:N2} {SizeSuffixes[mag]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
