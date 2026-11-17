using Svidok.Core.Knowledge;

namespace Svidok.Core.Entities;

/// <summary>Неигровой персонаж (раздел 5.4 ТЗ).</summary>
public sealed class Npc
{
    public Npc(string id, string name, Role role)
    {
        Id = id;
        Name = name;
        Role = role;
    }

    public string Id { get; }

    public string Name { get; }

    public Role Role { get; }

    public string Bio { get; set; } = string.Empty;

    public string HomeZone { get; set; } = string.Empty;

    public string WorkZone { get; set; } = string.Empty;

    public List<ScheduleEntry> Schedule { get; } = new();

    /// <summary>Вероятность поделиться слухом в разговоре, 0..1.</summary>
    public double Talkativeness { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Интерполированная позиция для отрисовки (в тайлах).</summary>
    public double RenderX { get; set; }

    public double RenderY { get; set; }

    /// <summary>Текущий маршрут A* (тайлы без стартового).</summary>
    public Queue<(int X, int Y)> Path { get; } = new();

    public Activity Activity { get; set; } = Activity.Work;

    public ActionInstance? CurrentAction { get; set; }

    public KnowledgeBase Knowledge { get; } = new();

    public List<string> Inventory { get; } = new();

    public int Coins { get; set; }

    public bool PendingDecision { get; set; }

    public int LastDecisionTick { get; set; } = -10000;

    public int ConversationCooldownUntil { get; set; }

    public bool IsInConversation { get; set; }

    public string? ConversationPartnerId { get; set; }

    public int ConversationEndTick { get; set; }

    /// <summary>Оглушение после ареста: NPC стоит до этого тика.</summary>
    public int StunnedUntilTick { get; set; }

    /// <summary>Здоровье (игровые сценарии с драками).</summary>
    public int Hp { get; set; } = 10;

    public int MaxHp { get; set; } = 10;

    /// <summary>Без сознания до этого тика (0 HP в драке).</summary>
    public int KnockedOutUntilTick { get; set; } = -1;

    /// <summary>Не хочет говорить с игроком до этого тика (оборвал разговор).</summary>
    public int RefuseTalkUntilTick { get; set; } = -1;

    /// <summary>Пообещал молчать о событии: id события и сила обещания 0..1.</summary>
    public Dictionary<int, double> Secrets { get; } = new();

    public bool IsKnockedOut(int tick) => KnockedOutUntilTick > tick;

    /// <summary>Кого NPC видел и когда (для искажения поля actor).</summary>
    public Dictionary<string, int> SeenNpcAtTick { get; } = new(StringComparer.Ordinal);

    /// <summary>Ручной триггер из панели наблюдателя.</summary>
    public bool ManualRequested { get; set; }

    /// <summary>Момент появления в мире (для мандрівника Захара).</summary>
    public int SpawnTick { get; set; }

    /// <summary>Последняя реплика NPC (для отрисовки над головой и окна диалога).</summary>
    public string? LastSay { get; set; }

    public int LastSayTick { get; set; } = -1000;

    /// <summary>Тик начала текущего действия — для метрик реакции (S5).</summary>
    public int LastActionStartTick { get; set; } = -1000;

    /// <summary>Маршрут патруля (id зон по кругу) и текущий индекс.</summary>
    public List<string> PatrolRoute { get; } = new();

    public int PatrolIndex { get; set; }

    public int PatrolWaitUntil { get; set; }

    /// <summary>Последние действия NPC (тик, действие, ссылка на событие) — для метрик S5.</summary>
    public List<(int Tick, string ActionId, int? EventRef)> ActionHistory { get; } = new();

    public void RememberAction(int tick, string actionId, int? eventRef)
    {
        ActionHistory.Add((tick, actionId, eventRef));
        if (ActionHistory.Count > 40)
        {
            ActionHistory.RemoveAt(0);
        }
    }

    public bool IsAsleep => Activity == Activity.Sleep;

    /// <summary>Женский персонаж: в готовых фразах глаголы в женском роде.</summary>
    public bool IsFemale { get; set; }

    /// <summary>Родня: id → кем приходится («дружина», «брат»).</summary>
    public Dictionary<string, string> Family { get; } = new(StringComparer.Ordinal);

    public (int X, int Y) Position => (X, Y);

    public double EffectiveTalkativeness(double multiplier) => Math.Clamp(Talkativeness * multiplier, 0, 1);

    public bool KnowsEvent(int eventId) => Knowledge.ByEventId(eventId) is not null;

    public ScheduleEntry? CurrentScheduleEntry(int minuteOfDay)
    {
        foreach (var entry in Schedule)
        {
            if (entry.Contains(minuteOfDay))
            {
                return entry;
            }
        }
        return Schedule.Count > 0 ? Schedule[0] : null;
    }

    public string CurrentZoneId(Svidok.Core.WorldModel.GameMap map)
    {
        return map.ZoneAt(X, Y)?.Id ?? "road";
    }

    public void ClearPath() => Path.Clear();
}
