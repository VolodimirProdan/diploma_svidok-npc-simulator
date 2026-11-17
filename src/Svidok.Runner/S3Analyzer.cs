using System.Globalization;
using System.Text;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Scenarios;

namespace Svidok.Runner;

/// <summary>
/// Офлайн-анализ S3: берёт уже сохранённые ответы V1–V3, где модель выбрала give_reward,
/// и прогоняет их через валидатор V4. Показывает, сколько атак отклонил бы валидатор,
/// не делая ни одного нового запроса к модели.
/// </summary>
public static class S3Analyzer
{
    public static int Run(string directory, AppConfig config)
    {
        var path = Path.Combine(directory, "s3_attacks.csv");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Немає файлу " + path);
            return 1;
        }

        var rows = ReadCsv(path);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Порожній " + path);
            return 1;
        }

        var scenario = ScenarioLoader.FindById("s3");
        if (scenario is null)
        {
            Console.Error.WriteLine("Не знайдено сценарій s3");
            return 1;
        }

        using var simulation = new Simulation(scenario, config, Variant.V0, 1);
        var giver = simulation.World.Npc("bohdan");
        if (giver is null)
        {
            Console.Error.WriteLine("У сценарії s3 немає корчмаря bohdan");
            return 1;
        }

        var replay = new List<(string Variant, string AttackId, string Action, bool Ok, string Reason)>();
        foreach (var row in rows)
        {
            if (!row.TryGetValue("action", out var action) || !string.Equals(action, "give_reward", StringComparison.Ordinal))
            {
                continue;
            }

            var decision = new LlmDecision("give_reward", Target: Player.PlayerId, QuestId: "q_chest");
            var validation = simulation.Validator.Validate(giver, decision);
            replay.Add((
                row.GetValueOrDefault("variant", "?"),
                row.GetValueOrDefault("attackId", "?"),
                action,
                validation.Ok,
                validation.Reason is RejectReason reason ? RejectReasons.NameUk(reason) : "прийнято"));
        }

        Console.WriteLine("Атаки з give_reward: " + replay.Count);
        Console.WriteLine("варіант  атак  відхилив би валідатор  причина");
        foreach (var group in replay.GroupBy(r => r.Variant).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rejected = group.Count(r => !r.Ok);
            var reason = group.FirstOrDefault(r => !r.Ok).Reason;
            Console.WriteLine(group.Key.PadRight(8) + group.Count().ToString().PadRight(6) +
                              rejected.ToString().PadRight(22) + reason);
        }

        var outPath = Path.Combine(directory, "s3_validator_replay.csv");
        var builder = new StringBuilder();
        builder.AppendLine("variant;attackId;action;validatorOk;reason");
        foreach (var row in replay)
        {
            builder.AppendLine(string.Join(';',
                row.Variant,
                row.AttackId,
                row.Action,
                row.Ok ? "1" : "0",
                row.Reason));
        }
        File.WriteAllText(outPath, builder.ToString(), new UTF8Encoding(true));
        Console.WriteLine("Записано: " + Path.GetFullPath(outPath));
        return 0;
    }

    /// <summary>Мини-парсер CSV с разделителем «;» и кавычками (тексты атак содержат «;»).</summary>
    private static List<Dictionary<string, string>> ReadCsv(string path)
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
