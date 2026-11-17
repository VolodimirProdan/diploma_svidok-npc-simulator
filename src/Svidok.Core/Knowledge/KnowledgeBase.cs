using Svidok.Core.Events;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Knowledge;

/// <summary>Память NPC: не больше memoryCapacity записей, одно событие — одна запись.</summary>
public sealed class KnowledgeBase
{
    private readonly Dictionary<int, KnowledgeEntry> _byEvent = new();
    private readonly Dictionary<string, KnowledgeEntry> _byFakeKey = new(StringComparer.Ordinal);
    private int _nextKey = 1;

    public List<KnowledgeEntry> Entries { get; } = new();

    public int Capacity { get; set; } = 200;

    public double CForget { get; set; } = 0.05;

    public int Count => Entries.Count;

    public KnowledgeEntry? ByEventId(int eventId) =>
        _byEvent.TryGetValue(eventId, out var entry) ? entry : null;

    public bool ContainsEvent(int eventId) => _byEvent.ContainsKey(eventId);

    public IEnumerable<KnowledgeEntry> FirstHandEntries =>
        Entries.Where(e => Sources.IsFirstHand(e.Source));

    /// <summary>Первое восприятие события самим NPC: источник Participant/Witnessed/Heard.</summary>
    public KnowledgeEntry AddFirstHand(WorldEvent worldEvent, Source source, double c, bool maskActor, int tick, string ownerId)
    {
        var existing = ByEventId(worldEvent.Id);
        if (existing is not null)
        {
            existing.Fields = BuildFields(worldEvent, maskActor);
            existing.Source = source;
            existing.C = Credibility.Clamp(Math.Max(existing.C, c));
            existing.OriginId = ownerId;
            existing.Chain = new List<string> { ownerId };
            existing.UpdatedTick = tick;
            existing.TimesHeard++;
            existing.IsDistorted = false;
            existing.IsFalse = false;
            existing.Text = existing.Fields.TryGetValue("text", out var known) ? known : null;
            return existing;
        }
        var fields = BuildFields(worldEvent, maskActor);
        var entry = new KnowledgeEntry
        {
            Key = _nextKey++,
            EventId = worldEvent.Id,
            Type = worldEvent.Type,
            Fields = fields,
            Source = source,
            C = Credibility.Clamp(c),
            OriginId = string.Empty,
            Chain = new List<string>(),
            LearnedTick = tick,
            UpdatedTick = tick,
            TimesHeard = 1,
            Importance = worldEvent.Importance,
            IsFalse = false,
            IsDistorted = false,
            Text = fields.TryGetValue("text", out var text) ? text : null
        };
        entry.OriginId = ownerId;
        entry.Chain.Add(ownerId);

        Entries.Add(entry);
        _byEvent[worldEvent.Id] = entry;
        EnforceCapacity();
        return entry;
    }

    public KnowledgeEntry? Find(KnowledgeEntry incoming)
    {
        if (incoming.EventId is int eventId && _byEvent.TryGetValue(eventId, out var byEvent))
        {
            return byEvent;
        }
        var key = FakeKey(incoming);
        return _byFakeKey.TryGetValue(key, out var byKey) ? byKey : null;
    }

    /// <summary>Вливает знание, полученное от другого персонажа, по правилам 8.2–8.3.</summary>
    public KnowledgeEntry MergeIncoming(KnowledgeEntry incoming, int tick)
    {
        var existing = Find(incoming);
        if (existing is null)
        {
            incoming.Key = _nextKey++;
            Entries.Add(incoming);
            if (incoming.EventId is int id)
            {
                _byEvent[id] = incoming;
            }
            else
            {
                _byFakeKey[FakeKey(incoming)] = incoming;
            }
            EnforceCapacity();
            return incoming;
        }

        var sameOrigin = string.Equals(existing.OriginId, incoming.OriginId, StringComparison.Ordinal);
        var useNewFields = incoming.C > existing.C;

        if (sameOrigin)
        {
            existing.C = Credibility.Clamp(Math.Max(existing.C, incoming.C));
            if (incoming.Chain.Count > 0 && incoming.Chain.Count < existing.Chain.Count)
            {
                existing.Chain = new List<string>(incoming.Chain);
                existing.Source = incoming.Source;
                existing.LearnedTick = incoming.LearnedTick;
            }
        }
        else
        {
            existing.C = Credibility.NoisyOr(existing.C, incoming.C);
            if (useNewFields)
            {
                existing.OriginId = incoming.OriginId;
                existing.Chain = new List<string>(incoming.Chain);
                existing.Source = incoming.Source;
            }
        }

        if (useNewFields)
        {
            existing.Fields = new Dictionary<string, string>(incoming.Fields, StringComparer.Ordinal);
            existing.IsDistorted = incoming.IsDistorted;
            existing.Text = incoming.Text;
        }

        existing.TimesHeard++;
        existing.UpdatedTick = tick;
        return existing;
    }

    /// <summary>Запись, созданная словами игрока (8.8): EventId = null, IsFalse по сверке с журналом.</summary>
    public KnowledgeEntry AddClaim(string type, Dictionary<string, string> fields, double c, int tick, string originId, double importance)
    {
        var entry = new KnowledgeEntry
        {
            Key = _nextKey++,
            EventId = null,
            Type = type,
            Fields = fields,
            Source = Source.PlayerClaim,
            C = Credibility.Clamp(c),
            OriginId = originId,
            Chain = new List<string> { originId },
            LearnedTick = tick,
            UpdatedTick = tick,
            TimesHeard = 1,
            Importance = importance,
            IsFalse = true,
            IsDistorted = false,
            Text = fields.TryGetValue("text", out var text) ? text : null
        };
        Entries.Add(entry);
        _byFakeKey[FakeKey(entry)] = entry;
        EnforceCapacity();
        return entry;
    }

    public void RemoveBySource(Source source)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Source != source)
            {
                continue;
            }
            var entry = Entries[i];
            Entries.RemoveAt(i);
            if (entry.EventId is int id && _byEvent.TryGetValue(id, out var byEvent) && ReferenceEquals(byEvent, entry))
            {
                _byEvent.Remove(id);
            }
            _byFakeKey.Remove(FakeKey(entry));
        }
    }

    /// <summary>Раз в игровой час: C = C × exp(−λ), записи ниже порога удаляются.</summary>
    public void ForgetHourly(double lambdaRumor, double lambdaFirstHand)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            var lambda = Sources.IsFirstHand(entry.Source) ? lambdaFirstHand : lambdaRumor;
            entry.C = Credibility.Clamp(entry.C * Math.Exp(-lambda));
            if (entry.C >= CForget)
            {
                continue;
            }
            RemoveAt(i);
        }
    }

    public void RemoveAt(int index)
    {
        var entry = Entries[index];
        Entries.RemoveAt(index);
        if (entry.EventId is int id && _byEvent.TryGetValue(id, out var byEvent) && ReferenceEquals(byEvent, entry))
        {
            _byEvent.Remove(id);
        }
        var fakeKey = FakeKey(entry);
        if (_byFakeKey.TryGetValue(fakeKey, out var byKey) && ReferenceEquals(byKey, entry))
        {
            _byFakeKey.Remove(fakeKey);
        }
    }

    private void EnforceCapacity()
    {
        while (Entries.Count > Capacity)
        {
            var worstIndex = 0;
            var worstScore = double.MaxValue;
            for (var i = 0; i < Entries.Count; i++)
            {
                var score = Entries[i].Importance * Entries[i].C;
                if (score < worstScore)
                {
                    worstScore = score;
                    worstIndex = i;
                }
            }
            RemoveAt(worstIndex);
        }
    }

    public static string FakeKey(KnowledgeEntry entry)
    {
        var parts = entry.Fields.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value);
        return entry.Type + "|" + string.Join(";", parts);
    }

    public static Dictionary<string, string> BuildFields(WorldEvent worldEvent, bool maskActor)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(worldEvent.ActorId))
        {
            fields["actor"] = maskActor ? "невідомий" : worldEvent.ActorId;
        }
        if (!string.IsNullOrWhiteSpace(worldEvent.TargetId))
        {
            fields["target"] = worldEvent.TargetId;
        }
        if (!string.IsNullOrWhiteSpace(worldEvent.ItemId))
        {
            fields["item"] = worldEvent.ItemId;
        }
        if (!string.IsNullOrWhiteSpace(worldEvent.ZoneId))
        {
            fields["zone"] = worldEvent.ZoneId;
        }
        foreach (var pair in worldEvent.Extra)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                fields[pair.Key] = pair.Value;
            }
        }
        return fields;
    }

    /// <summary>Сверка с журналом: помечает запись искажённой.</summary>
    public void RefreshDistortion(World world)
    {
        foreach (var entry in Entries)
        {
            if (entry.EventId is not int id)
            {
                entry.IsFalse = true;
                continue;
            }
            var truth = world.Log.ById(id);
            if (truth is null)
            {
                entry.IsFalse = true;
                continue;
            }
            entry.IsFalse = false;
            entry.IsDistorted = !entry.MatchesTruth(truth);
        }
    }
}
