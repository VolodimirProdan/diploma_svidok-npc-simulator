using System.Text;

namespace Svidok.Runner;

/// <summary>Мини-парсер CSV с разделителем «;» и кавычками — для офлайн-анализа результатов.</summary>
public static class SimpleCsv
{
    public static List<Dictionary<string, string>> Read(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var result = new List<Dictionary<string, string>>();
        if (lines.Length < 2)
        {
            return result;
        }

        var headers = SplitLine(lines[0]);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }
            var values = SplitLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count && c < values.Count; c++)
            {
                row[headers[c]] = values[c];
            }
            result.Add(row);
        }
        return result;
    }

    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', headers));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(';', row));
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static List<string> SplitLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }
            if (ch == ';' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        values.Add(current.ToString());
        return values;
    }
}
