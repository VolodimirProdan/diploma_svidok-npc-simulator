using System.Text.Json;
using System.Text.Json.Serialization;

namespace Svidok.Core.Events;

/// <summary>Общий список всех событий мира в порядке появления. Это «правда» симуляции.</summary>
public sealed class EventLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<WorldEvent> _events = new(4096);
    private readonly Dictionary<string, List<WorldEvent>> _byType = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _countByActor = new(StringComparer.Ordinal);
    private byte[]? _cachedJson;
    private int _cachedCount = -1;

    public EventLog(EventSchema schema)
    {
        Schema = schema;
    }

    public EventSchema Schema { get; }

    public int Count => _events.Count;

    public IReadOnlyList<WorldEvent> Events => _events;

    /// <summary>Уведомление о каждом новом событии: нужно метрикам (аресты, награды) и наблюдателю.</summary>
    public event Action<WorldEvent>? EventAdded;

    public WorldEvent Add(
        int tick,
        string type,
        string? actorId,
        string? targetId,
        string? itemId,
        string? zoneId,
        int x,
        int y,
        double? loudness = null,
        double? importance = null,
        bool? isBackground = null,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        var def = Schema.Get(type);
        var worldEvent = new WorldEvent(
            _events.Count + 1,
            tick,
            type,
            actorId,
            targetId,
            itemId,
            zoneId,
            x,
            y,
            loudness ?? def.Loudness,
            importance ?? def.Importance,
            isBackground ?? def.Background,
            extra ?? new Dictionary<string, string>());

        _events.Add(worldEvent);
        if (!_byType.TryGetValue(type, out var list))
        {
            list = new List<WorldEvent>();
            _byType[type] = list;
        }
        list.Add(worldEvent);
        if (actorId is not null)
        {
            _countByActor[actorId] = _countByActor.GetValueOrDefault(actorId) + 1;
        }
        _cachedJson = null;
        EventAdded?.Invoke(worldEvent);
        return worldEvent;
    }

    public WorldEvent? ById(int id)
    {
        if (id <= 0 || id > _events.Count)
        {
            return null;
        }
        return _events[id - 1];
    }

    public IReadOnlyList<WorldEvent> OfType(string type) =>
        _byType.TryGetValue(type, out var list) ? list : Array.Empty<WorldEvent>();

    public int CountOfActor(string actorId) => _countByActor.GetValueOrDefault(actorId);

    public int CountOfType(string type) => _byType.TryGetValue(type, out var list) ? list.Count : 0;

    public bool HasQuestCompleted(string questId, string? actorId = "player")
    {
        foreach (var e in OfType("quest_completed"))
        {
            if (!string.Equals(e.Get("questId") ?? e.Extra.GetValueOrDefault("questId"), questId, StringComparison.Ordinal))
            {
                continue;
            }
            if (actorId is null || string.Equals(e.ActorId, actorId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public bool HasRewardGiven(string questId)
    {
        foreach (var e in OfType("reward_given"))
        {
            if (string.Equals(e.QuestIdOf(), questId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Сериализует журнал в JSON (UTF-8, без отступов). Размер используется в метрике H1.</summary>
    public byte[] ToJsonBytes()
    {
        if (_cachedJson is not null && _cachedCount == _events.Count)
        {
            return _cachedJson;
        }
        _cachedJson = JsonSerializer.SerializeToUtf8Bytes(_events, JsonOptions);
        _cachedCount = _events.Count;
        return _cachedJson;
    }

    public long ByteSize => ToJsonBytes().LongLength;

    public IReadOnlyList<WorldEvent> BackgroundEvents() => _events.Where(e => e.IsBackground).ToList();

    public IReadOnlyList<WorldEvent> ImportantEvents(int max = 40)
    {
        return _events.Where(e => !e.IsBackground)
            .OrderByDescending(e => e.Importance)
            .ThenBy(e => e.Id)
            .Take(max)
            .ToList();
    }
}

public static class WorldEventExtensions
{
    public static string? QuestIdOf(this WorldEvent e)
    {
        if (e.Extra.TryGetValue("questId", out var q) && !string.IsNullOrWhiteSpace(q))
        {
            return q;
        }
        return null;
    }

    public static string? ItemIdOf(this WorldEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.ItemId))
        {
            return e.ItemId;
        }
        return e.Extra.TryGetValue("item", out var item) ? item : null;
    }

    public static string? ZoneIdOf(this WorldEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.ZoneId))
        {
            return e.ZoneId;
        }
        return e.Extra.TryGetValue("zone", out var zone) ? zone : null;
    }
}
