using System.Text.RegularExpressions;
using Svidok.Core.Entities;

namespace Svidok.Core.Util;

/// <summary>
/// Род глаголов прошедшего времени в готовых фразах игры: «знайшов» → «знайшла», если говорит или действует женщина.
/// Меняются только слова из списка, остальной текст не трогается.
/// </summary>
public static class UkGrammar
{
    private static readonly Dictionary<string, string> Feminine = new(StringComparer.Ordinal)
    {
        ["обірвав"] = "обірвала", ["підійшов"] = "підійшла", ["прийшов"] = "прийшла", ["пішов"] = "пішла",
        ["кинув"] = "кинула", ["вигнав"] = "вигнала", ["оштрафував"] = "оштрафувала", ["вдарив"] = "вдарила",
        ["дав"] = "дала", ["доручив"] = "доручила", ["заплатив"] = "заплатила", ["знайшов"] = "знайшла",
        ["обшукав"] = "обшукала", ["видав"] = "видала", ["напав"] = "напала", ["наздогнав"] = "наздогнала",
        ["помилився"] = "помилилася", ["знав"] = "знала", ["бачив"] = "бачила", ["чув"] = "чула", ["Чув"] = "Чула",
        ["загубив"] = "загубила", ["забрав"] = "забрала", ["побив"] = "побила", ["затримав"] = "затримала",
        ["сказав"] = "сказала", ["приніс"] = "принесла", ["взяв"] = "взяла", ["повернув"] = "повернула",
        ["пообіцяв"] = "пообіцяла", ["відмовив"] = "відмовила", ["погодився"] = "погодилася", ["образився"] = "образилася",
        ["вкрав"] = "вкрала", ["пригостив"] = "пригостила", ["помітив"] = "помітила", ["радий"] = "рада"
    };

    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled);

    public static string For(Npc? npc, string text) => npc is { IsFemale: true } ? ToFeminine(text) : text;

    public static string ToFeminine(string text) =>
        Word.Replace(text, m => Feminine.TryGetValue(m.Value, out var fem) ? fem : m.Value);

    /// <summary>Выбор формы по полу: G(npc, "знайшов", "знайшла").</summary>
    public static string G(Npc? npc, string masculine, string feminine) => npc is { IsFemale: true } ? feminine : masculine;
}
