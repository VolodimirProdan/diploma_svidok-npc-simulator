namespace Svidok.Core.WorldModel;

/// <summary>Зона карты: прямоугольник в тайлах с украинским названием и соседями.</summary>
public sealed class Zone
{
    public Zone(string id, string nameUk, int x0, int y0, int x1, int y1, params string[] neighbours)
    {
        Id = id;
        NameUk = nameUk;
        X0 = Math.Min(x0, x1);
        Y0 = Math.Min(y0, y1);
        X1 = Math.Max(x0, x1);
        Y1 = Math.Max(y0, y1);
        Neighbours = neighbours;
    }

    public string Id { get; }

    public string NameUk { get; }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public IReadOnlyList<string> Neighbours { get; }

    public bool Contains(int x, int y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

    public int CenterX => (X0 + X1) / 2;

    public int CenterY => (Y0 + Y1) / 2;

    public (int X, int Y) Center => (CenterX, CenterY);

    public override string ToString() => Id + " (" + NameUk + ")";
}

/// <summary>Именованный набор зон карты по умолчанию (раздел 5.1 ТЗ).</summary>
public static class DefaultZones
{
    public static readonly Zone Tavern = new("tavern", "Корчма", 2, 2, 12, 9, "square", "headman_house");
    public static readonly Zone HeadmanHouse = new("headman_house", "Хата старости", 15, 2, 20, 6, "tavern", "square");
    public static readonly Zone Forge = new("forge", "Кузня", 30, 2, 38, 7, "market", "square");
    public static readonly Zone Market = new("market", "Ринок", 30, 10, 42, 13, "forge", "square");
    public static readonly Zone Square = new("square", "Площа", 19, 12, 28, 19, "tavern", "market", "warehouse", "house_2", "house_3");
    public static readonly Zone Warehouse = new("warehouse", "Склад", 2, 20, 10, 27, "square", "house_2");
    public static readonly Zone GuardTower = new("guard_tower", "Вартова вежа", 40, 19, 45, 24, "market", "house_4");
    public static readonly Zone House2 = new("house_2", "Хата Мельників", 14, 22, 19, 26, "warehouse", "square");
    public static readonly Zone House3 = new("house_3", "Хата Ковальчуків", 27, 22, 32, 26, "square", "pond");
    public static readonly Zone House4 = new("house_4", "Хата Рибаків", 40, 27, 45, 30, "pond", "guard_tower");
    public static readonly Zone Pond = new("pond", "Ставок", 33, 25, 38, 30, "house_3", "house_4");

    public static IReadOnlyList<Zone> All { get; } = new List<Zone>
    {
        Tavern, HeadmanHouse, Forge, Market, Square, Warehouse,
        GuardTower, House2, House3, House4, Pond
    };

    public static Zone? ById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        foreach (var zone in All)
        {
            if (string.Equals(zone.Id, id, StringComparison.Ordinal))
            {
                return zone;
            }
        }
        return null;
    }
}
