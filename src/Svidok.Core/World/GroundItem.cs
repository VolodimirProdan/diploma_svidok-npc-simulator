using Svidok.Core.Entities;

namespace Svidok.Core.WorldModel;

/// <summary>Предмет на земле: можно подобрать (F) или украсть (Q).</summary>
public sealed class GroundItem
{
    public GroundItem(string itemId, int x, int y, string zoneId)
    {
        ItemId = itemId;
        X = x;
        Y = y;
        ZoneId = zoneId;
    }

    public string ItemId { get; }

    public int X { get; }

    public int Y { get; }

    public string ZoneId { get; }

    /// <summary>Кто подбросил предмет (игрок или сценарий); null — предмет лежал сам.</summary>
    public string? PlantedBy { get; set; }

    public int PlacedTick { get; set; }

    /// <summary>Потерянная вещь из доручення «знайти»: кто её ищет, жители её не трогают.</summary>
    public string? ErrandId { get; set; }
}
