using System.Globalization;
using Svidok.Core.Metrics;

namespace Svidok.Runner;

/// <summary>
/// Офлайн-пересчёт метрик S2 по сохранённым ответам: новые правила утечки (отрицания
/// не считаются) и отдельная метрика «галлюцинация». Сеть не нужна.
/// </summary>
public static class S2Analyzer
{
    public static int Run(string directory)
    {
        var path = Path.Combine(directory, "s2_answers.csv");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Немає файлу " + path);
            return 1;
        }

        var rows = SimpleCsv.Read(path);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Порожній " + path);
            return 1;
        }

        var recomputed = new List<IReadOnlyList<string>>();
        var byVariant = new Dictionary<string, (int Unknown, int Leaks, int Hallucinations, int Known, int Recalls)>();

        foreach (var row in rows)
        {
            var variant = row.GetValueOrDefault("variant", "?");
            var knows = row.GetValueOrDefault("knows", "0") == "1";
            var oldLeak = row.GetValueOrDefault("leak", "0") == "1";
            var say = row.GetValueOrDefault("say", string.Empty);
            var reason = row.GetValueOrDefault("reason", string.Empty);

            var leak = !knows && LeakDetector.IsLeak(say, reason);
            // в V1 весь журнал в промпте: упоминание события там — утечка, а не выдумка
            var hallucination = !knows && variant != "V1" && LeakDetector.ClaimsEvent(say, reason);
            var recall = knows && LeakDetector.IsLeak(say, reason);

            recomputed.Add(new[]
            {
                variant,
                row.GetValueOrDefault("seed", "0"),
                row.GetValueOrDefault("npcId", string.Empty),
                knows ? "1" : "0",
                row.GetValueOrDefault("question", string.Empty),
                say,
                leak ? "1" : "0",
                hallucination ? "1" : "0",
                recall ? "1" : "0",
                oldLeak ? "1" : "0"
            });

            var stat = byVariant.GetValueOrDefault(variant);
            if (!knows)
            {
                stat.Unknown++;
                if (leak)
                {
                    stat.Leaks++;
                }
                if (hallucination)
                {
                    stat.Hallucinations++;
                }
            }
            else
            {
                stat.Known++;
                if (recall)
                {
                    stat.Recalls++;
                }
            }
            byVariant[variant] = stat;
        }

        SimpleCsv.Write(Path.Combine(directory, "s2_answers_recomputed.csv"),
            ["variant", "seed", "npcId", "knows", "question", "say", "leakNew", "hallucination", "recall", "leakOld"],
            recomputed);

        Console.WriteLine("варіант  незнаючих  MKR(текст)  галюцинації  знаючих  Recall");
        foreach (var pair in byVariant.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var s = pair.Value;
            var mkr = s.Unknown == 0 ? 0 : (double)s.Leaks / s.Unknown;
            var halluc = s.Unknown == 0 ? 0 : (double)s.Hallucinations / s.Unknown;
            var recall = s.Known == 0 ? 0 : (double)s.Recalls / s.Known;
            Console.WriteLine(pair.Key.PadRight(8) +
                              s.Unknown.ToString().PadRight(11) +
                              mkr.ToString("0.000", CultureInfo.InvariantCulture).PadRight(12) +
                              halluc.ToString("0.000", CultureInfo.InvariantCulture).PadRight(13) +
                              s.Known.ToString().PadRight(8) +
                              recall.ToString("0.000", CultureInfo.InvariantCulture));
        }

        Console.WriteLine("Записано: " + Path.GetFullPath(Path.Combine(directory, "s2_answers_recomputed.csv")));
        return 0;
    }
}
