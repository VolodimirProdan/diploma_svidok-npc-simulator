namespace Svidok.Core.Entities;

/// <summary>Предмет из data/items.json (раздел 5.6).</summary>
public sealed class ItemDef
{
    public string Id { get; set; } = string.Empty;

    public string NameUk { get; set; } = string.Empty;

    /// <summary>Категория для искажений: valuable (ценное) или goods (товар).</summary>
    public string Category { get; set; } = "goods";

    /// <summary>Где предмет лежит по умолчанию: id зоны, id NPC или item.</summary>
    public string Location { get; set; } = string.Empty;

    public string PurposeUk { get; set; } = string.Empty;

    /// <summary>Владелец предмета, если он лежит у NPC (для краж).</summary>
    public string? OwnerId { get; set; }

    /// <summary>Предмет только для игровых сценариев: в экспериментах S1–S5 не загружается.</summary>
    public bool Game { get; set; }

    /// <summary>Насколько снижает голод, если съесть (0 — несъедобно).</summary>
    public int Food { get; set; }

    /// <summary>Обычная цена в монетах (подсказка модели при торге).</summary>
    public int Price { get; set; }
}

public static class ItemCategories
{
    public const string Valuable = "valuable";
    public const string Goods = "goods";
}
