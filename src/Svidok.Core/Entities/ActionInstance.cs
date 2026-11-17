using Svidok.Core.Decisions;

namespace Svidok.Core.Entities;

/// <summary>Активное действие NPC, назначенное решением (раздел 9.9).</summary>
public sealed class ActionInstance
{
    public string ActionId { get; init; } = "ignore";

    public string? TargetId { get; set; }

    public string? ZoneId { get; set; }

    public string? ItemId { get; set; }

    public string? QuestId { get; set; }

    public string? Say { get; set; }

    public int? EventRef { get; set; }

    /// <summary>Сумма в монетах (вимога плати, штраф).</summary>
    public int? Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    public int StartTick { get; set; }

    public int EndTick { get; set; }

    public bool Done { get; set; }

    public IActionHandler? Handler { get; set; }

    /// <summary>Внутреннее состояние обработчика (маршрут, фаза и т.п.).</summary>
    public object? State { get; set; }

    /// <summary>Название действия для интерфейса (из каталога).</summary>
    public string NameUk { get; set; } = string.Empty;

    public string DescribeUi()
    {
        var name = string.IsNullOrEmpty(NameUk) ? ActionId : NameUk;
        return TargetId is null ? name : name + " -> " + TargetId;
    }
}
