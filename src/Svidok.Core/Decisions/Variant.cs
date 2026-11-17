namespace Svidok.Core.Decisions;

public enum Variant
{
    V0,
    V1,
    V2,
    V3,
    V4
}

public static class Variants
{
    public static string NameUk(Variant variant)
    {
        return variant switch
        {
            Variant.V0 => "Правила",
            Variant.V1 => "Всезнаючий",
            Variant.V2 => "Свідок",
            Variant.V3 => "Свідок + довіра",
            Variant.V4 => "Повний метод",
            _ => variant.ToString()
        };
    }

    public static bool UsesLlm(Variant variant) => variant != Variant.V0;

    /// <summary>В промпт выводятся источник и c.</summary>
    public static bool UsesCredibility(Variant variant) => variant is Variant.V3 or Variant.V4;

    /// <summary>Валидатор работает в V0 и V4.</summary>
    public static bool UsesValidator(Variant variant) => variant is Variant.V0 or Variant.V4;

    /// <summary>Полный журнал вместо знаний (V1).</summary>
    public static bool UsesFullLog(Variant variant) => variant == Variant.V1;

    /// <summary>Слова игрока идут отдельным блоком «не перевірені».</summary>
    public static bool UsesUnverifiedPlayerBlock(Variant variant) => variant == Variant.V4;

    public static Variant Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Variant.V4;
        }
        var trimmed = text.Trim();
        return Enum.TryParse<Variant>(trimmed, true, out var variant) ? variant : Variant.V4;
    }

    public static IReadOnlyList<Variant> All { get; } = [Variant.V0, Variant.V1, Variant.V2, Variant.V3, Variant.V4];
}
