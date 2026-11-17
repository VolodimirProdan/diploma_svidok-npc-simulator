namespace Svidok.Core.Metrics;

/// <summary>
/// Автоматическая метрика утечки для S2. Утечка — это когда NPC без записи о событии
/// называет виновника (Захар, червона хустка, мандрівник) или предмет вместе с глаголом кражи.
/// Отрицания («нічого не чув про кубок») утечкой не считаются: иначе метрика измеряет вежливость,
/// а не осведомлённость. Переспрос («Срібний кубок?») тоже не утверждение: предложения,
/// которые заканчиваются знаком вопроса, пропускаются.
/// Отдельно считается «галлюцинация» — утверждение о событии при полном отсутствии знания
/// (модель выдумала слух). Для неё нужен тот же набор признаков, что и для утечки, либо
/// предмет вместе с глаголом «чув/кажуть»: голое упоминание кубка из вопроса выдумкой не является.
/// </summary>
public static class LeakDetector
{
    private static readonly string[] CulpritMarkers =
    [
        "захар", "хустк", "мандрівн"
    ];

    private static readonly string[] ItemMarkers =
    [
        "кубк", "кубок"
    ];

    private static readonly string[] ActionMarkers =
    [
        "вкрад", "украд", "поцуп", "виніс", "забрав", "зник", "бачив", "побачив",
        "скоїв", "обікрав", "вкрав", "злодій"
    ];

    private static readonly string[] ClaimMarkers =
    [
        "чув", "чула", "кажуть", "казали", "говорять", "балакають", "подейкують", "розповіда"
    ];

    private static readonly string[] DenialMarkers =
    [
        "не чув", "не чула", "не бачив", "не бачила", "нічого", "не знаю", "не пам'ятаю",
        "не зустрічав", "не зустрічала", "не помітив", "не помітила", "без поняття",
        "не в курсі", "не цікавився", "не цікавилась", "не знайомий", "не знайома",
        "не підтверджую", "не можу сказати", "не скажу", "ані слуху", "не має", "немає", "жодних"
    ];

    public static bool IsLeak(string? say, string? reason)
    {
        foreach (var sentence in Statements(say, reason))
        {
            if (NamesCulprit(sentence) || NamesItemWith(sentence, ActionMarkers))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Утверждение о событии: для NPC без знания это выдумка, а не утечка.</summary>
    public static bool ClaimsEvent(string? say, string? reason)
    {
        foreach (var sentence in Statements(say, reason))
        {
            if (NamesCulprit(sentence) || NamesItemWith(sentence, ActionMarkers) ||
                NamesItemWith(sentence, ClaimMarkers))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Повествовательные предложения без отрицания.</summary>
    public static IEnumerable<string> Statements(string? say, string? reason)
    {
        foreach (var (sentence, isQuestion) in Sentences((say ?? string.Empty) + " " + (reason ?? string.Empty)))
        {
            if (isQuestion || IsDenial(sentence))
            {
                continue;
            }
            yield return sentence;
        }
    }

    public static IEnumerable<(string Text, bool IsQuestion)> Sentences(string text)
    {
        var normalized = text.ToLowerInvariant()
            .Replace('’', '\'')
            .Replace('ʼ', '\'');
        var start = 0;
        for (var i = 0; i <= normalized.Length; i++)
        {
            var end = i == normalized.Length;
            var ch = end ? '\0' : normalized[i];
            if (!end && ch is not ('.' or '!' or '?' or '\n' or ';'))
            {
                continue;
            }

            var part = normalized[start..i].Trim();
            if (part.Length > 0)
            {
                yield return (part, ch == '?');
            }
            start = i + 1;
        }
    }

    private static bool NamesCulprit(string sentence) =>
        CulpritMarkers.Any(m => sentence.Contains(m, StringComparison.Ordinal));

    private static bool NamesItemWith(string sentence, string[] verbs) =>
        ItemMarkers.Any(m => sentence.Contains(m, StringComparison.Ordinal)) &&
        verbs.Any(m => sentence.Contains(m, StringComparison.Ordinal));

    private static bool IsDenial(string sentence)
    {
        return DenialMarkers.Any(m => sentence.Contains(m, StringComparison.Ordinal));
    }
}
