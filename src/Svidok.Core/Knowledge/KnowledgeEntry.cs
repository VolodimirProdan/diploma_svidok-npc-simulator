using Svidok.Core.Events;

namespace Svidok.Core.Knowledge;

/// <summary>Запись в памяти конкретного NPC о событии (раздел 8.1).</summary>
public sealed class KnowledgeEntry
{
    public int Key { get; set; }

    /// <summary>Ссылка на реальное событие; null, если игрок выдумал.</summary>
    public int? EventId { get; set; }

    public string Type { get; set; } = string.Empty;

    /// <summary>Поля, КАК ИХ ЗНАЕТ NPC (могут быть искажены).</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public Source Source { get; set; }

    /// <summary>Достоверность 0..1.</summary>
    public double C { get; set; }

    /// <summary>Кто был первым свидетелем (или "player" для заявлений игрока).</summary>
    public string OriginId { get; set; } = string.Empty;

    /// <summary>Цепочка пересказчиков: [свидетель, ..., последний].</summary>
    public List<string> Chain { get; set; } = new();

    public int LearnedTick { get; set; }

    public int UpdatedTick { get; set; }

    public int TimesHeard { get; set; }

    public HashSet<string> SharedWith { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Служебное: отличается ли от правды (для метрик и UI наблюдателя).</summary>
    public bool IsDistorted { get; set; }

    /// <summary>Служебное: события вообще не было.</summary>
    public bool IsFalse { get; set; }

    public double Importance { get; set; }

    public string? Text { get; set; }

    public string ActorId => Fields.TryGetValue("actor", out var actor) ? actor : string.Empty;

    public string? Field(string name) => Fields.TryGetValue(name, out var value) ? value : null;

    /// <summary>Порядковый номер для промпта: EventId, для выдумок — отрицательный ключ.</summary>
    public int PromptRef => EventId ?? -Key;

    public KnowledgeEntry Clone()
    {
        return new KnowledgeEntry
        {
            Key = Key,
            EventId = EventId,
            Type = Type,
            Fields = new Dictionary<string, string>(Fields, StringComparer.Ordinal),
            Source = Source,
            C = C,
            OriginId = OriginId,
            Chain = new List<string>(Chain),
            LearnedTick = LearnedTick,
            UpdatedTick = UpdatedTick,
            TimesHeard = TimesHeard,
            SharedWith = new HashSet<string>(SharedWith, StringComparer.Ordinal),
            IsDistorted = IsDistorted,
            IsFalse = IsFalse,
            Importance = Importance,
            Text = Text
        };
    }

    /// <summary>Совпадают ли наблюдаемые поля с правдой журнала.</summary>
    public bool MatchesTruth(WorldEvent truth)
    {
        foreach (var pair in Fields)
        {
            var truthValue = pair.Key switch
            {
                "actor" => truth.ActorId,
                "target" => truth.TargetId,
                "item" => truth.ItemId,
                "zone" => truth.ZoneId,
                "text" => truth.Get("text"),
                "price" => truth.Get("price"),
                "coins" => truth.Get("coins"),
                "questId" => truth.Get("questId"),
                "detail" => truth.Get("detail"),
                _ => truth.Extra.TryGetValue(pair.Key, out var extra) ? extra : null
            };
            if (truthValue is null)
            {
                continue;
            }
            if (pair.Value == "невідомий" || pair.Value == "хтось")
            {
                continue;
            }
            if (!string.Equals(pair.Value, truthValue, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public override string ToString() =>
        "#" + PromptRef + " " + Type + " c=" + C.ToString("0.00") + " via " + Source;
}
