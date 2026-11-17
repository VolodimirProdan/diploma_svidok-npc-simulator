using System.Globalization;
using System.Text;

namespace Svidok.Core.Metrics;

/// <summary>CSV по разделу 15: разделитель «;», десятичная точка, UTF-8 с BOM.</summary>
public static class CsvWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', headers.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', row.Select(Escape)));
        }
        File.WriteAllText(path, builder.ToString(), Utf8WithBom);
    }

    public static string Num(double value, int digits = 4) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "1" : "0";

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        var text = value.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Contains(';') || text.Contains('"'))
        {
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }
}
