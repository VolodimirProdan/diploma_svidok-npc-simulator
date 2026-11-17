using System.Globalization;
using System.Text.Json;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Scenarios;

// ---------------------------------------------------------------- описание сюжета (data/stories/*.json)

/// <summary>Сюжет: условия запуска и этапы. Условия проверяет игра, модель только озвучивает происходящее.</summary>
public sealed class StoryDefinition
{
    public string Id { get; set; } = string.Empty;

    public string TitleUk { get; set; } = string.Empty;

    /// <summary>Когда сюжет начинается (все условия сразу).</summary>
    public List<StoryCondition> Start { get; set; } = new();

    public List<StoryStage> Stages { get; set; } = new();
}

public sealed class StoryStage
{
    public string Id { get; set; } = string.Empty;

    public string TitleUk { get; set; } = string.Empty;

    /// <summary>Что видит игрок на панели «Сюжет».</summary>
    public string DescriptionUk { get; set; } = string.Empty;

    /// <summary>Что знают жители (идёт в промпт); {player}, {headman} подставляются.</summary>
    public string PromptUk { get; set; } = string.Empty;

    public List<StoryEffect> Enter { get; set; } = new();

    public List<StoryExit> Exits { get; set; } = new();

    public bool Final { get; set; }
}

/// <summary>Выход из этапа: срабатывает первым, чьи условия выполнены.</summary>
public sealed class StoryExit
{
    public string? To { get; set; }

    public string LabelUk { get; set; } = string.Empty;

    public List<StoryCondition> When { get; set; } = new();

    public List<StoryEffect> Effects { get; set; } = new();
}

public sealed class StoryCondition
{
    /// <summary>
    /// day_at_least, hour_between, stage_days, support, avg_trust_below, avg_trust_above, role_prefers,
    /// errands_done, known_bad, flag, player_at, player_free.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public string? Target { get; set; }

    public string? Other { get; set; }

    public string? Role { get; set; }

    public string? Zone { get; set; }

    public string? Flag { get; set; }

    public double Value { get; set; }

    public double Min { get; set; }

    public double Max { get; set; }

    public int Count { get; set; }

    public List<string> Exclude { get; set; } = new();

    /// <summary>Подпись для панели; если пусто — строится сама.</summary>
    public string? LabelUk { get; set; }

    /// <summary>Флаг ставит игрок кнопкой на панели «Сюжет» (текст кнопки).</summary>
    public string? ButtonUk { get; set; }
}

public sealed class StoryEffect
{
    /// <summary>
    /// announce, notice, approach, campaign_start, campaign_stop, election, coup, set_headman, jail_player,
    /// set_flag, clear_flag, trust_from_friends, end.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public string? Npc { get; set; }

    public string? Zone { get; set; }

    public string? Text { get; set; }

    public string? Opening { get; set; }

    public string? Reason { get; set; }

    public string? Target { get; set; }

    public string? Who { get; set; }

    public string? Flag { get; set; }

    public double Delta { get; set; }

    public List<string> Candidates { get; set; } = new();

    public string? WinTo { get; set; }

    public string? LoseTo { get; set; }
}

/// <summary>Состояние сюжета в мире.</summary>
public sealed class StoryState
{
    public StoryState(StoryDefinition definition)
    {
        Definition = definition;
    }

    public StoryDefinition Definition { get; }

    public bool Started { get; set; }

    public string? StageId { get; set; }

    public int StageStartTick { get; set; }

    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

    /// <summary>Что уже случилось: для панели «Сюжет».</summary>
    public List<string> Journal { get; } = new();

    public string? Result { get; set; }

    public StoryStage? Stage => Definition.Stages.FirstOrDefault(s => string.Equals(s.Id, StageId, StringComparison.Ordinal));
}

/// <summary>Одна строка прогресса условия для панели.</summary>
public sealed record StoryProgress(string Text, bool Done, string? Button, string? Flag);

// ---------------------------------------------------------------- движок

public static class StoryEngine
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Плохие поступки и насколько они бьют по доверию к тому, кто их совершил.</summary>
    private static readonly Dictionary<string, double> BadDeeds = new(StringComparer.Ordinal)
    {
        ["theft"] = 0.25,
        ["attack"] = 0.2,
        ["knockout"] = 0.25,
        ["threat"] = 0.1,
        ["bribe"] = 0.15
    };

    /// <summary>Хорошие поступки: помог, вернул вещь.</summary>
    private static readonly Dictionary<string, double> GoodDeeds = new(StringComparer.Ordinal)
    {
        ["recovered"] = 0.08,
        ["quest_completed"] = 0.06,
        ["help_fire"] = 0.08
    };

    public static StoryDefinition? Load(string id)
    {
        var path = PathResolver.ResolveExisting("data/stories/" + id + ".json");
        if (!File.Exists(path))
        {
            Log.Warn("Сюжет не знайдено: data/stories/" + id + ".json");
            return null;
        }
        return JsonSerializer.Deserialize<StoryDefinition>(File.ReadAllText(path), Options);
    }

    public static void Tick(World world)
    {
        var game = world.Game;
        if (world.Tick % 10 == 0)
        {
            UpdateReputation(world);
        }
        if (world.Clock.IsNewHour)
        {
            UpdateCampaigns(world);
            UpdateWordOfMouth(world);
        }
        if (world.Tick % 10 != 5)
        {
            return;
        }
        foreach (var story in game.Stories)
        {
            Advance(world, story);
        }
        game.StoryContext = string.Join(" ", game.Stories
            .Where(s => s.Started && s.Stage is { } stage && !string.IsNullOrWhiteSpace(stage.PromptUk))
            .Select(s => Fill(world, s.Stage!.PromptUk)));
    }

    private static void Advance(World world, StoryState story)
    {
        if (!story.Started)
        {
            if (story.Definition.Start.Count == 0 || story.Definition.Start.All(c => Check(world, story, c)))
            {
                story.Started = true;
                if (story.Definition.Stages.Count > 0)
                {
                    EnterStage(world, story, story.Definition.Stages[0].Id);
                }
            }
            return;
        }

        var stage = story.Stage;
        if (stage is null || stage.Final)
        {
            return;
        }
        foreach (var exit in stage.Exits)
        {
            if (!exit.When.All(c => Check(world, story, c)))
            {
                continue;
            }
            var before = story.StageId;
            foreach (var effect in exit.Effects)
            {
                Apply(world, story, effect);
            }
            if (string.Equals(story.StageId, before, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(exit.To))
            {
                EnterStage(world, story, exit.To!);
            }
            return;
        }
    }

    private static void EnterStage(World world, StoryState story, string stageId)
    {
        var stage = story.Definition.Stages.FirstOrDefault(s => string.Equals(s.Id, stageId, StringComparison.Ordinal));
        if (stage is null)
        {
            Log.Warn("Етап сюжету не знайдено: " + stageId);
            return;
        }
        story.StageId = stage.Id;
        story.StageStartTick = world.Tick;
        story.Journal.Add(world.Clock.TimeString + " — " + stage.TitleUk);
        foreach (var effect in stage.Enter)
        {
            Apply(world, story, effect);
            if (!string.Equals(story.StageId, stage.Id, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    // ---------------------------------------------------------------- условия

    private static IEnumerable<Npc> Residents(World world, StoryCondition? c, params string?[] skip) =>
        world.ActiveNpcs().Where(n =>
            !skip.Contains(n.Id) && (c is null || !c.Exclude.Contains(n.Id)));

    public static bool Check(World world, StoryState story, StoryCondition c) => Evaluate(world, story, c).Done;

    public static StoryProgress Evaluate(World world, StoryState story, StoryCondition c)
    {
        var tick = world.Tick;
        var player = world.Player;
        var target = Resolve(world, c.Target);
        var other = Resolve(world, c.Other);
        switch (c.Type)
        {
            case "day_at_least":
                return new StoryProgress(c.LabelUk ?? "День " + (int)c.Value, world.Clock.Day >= (int)c.Value, null, null);
            case "hour_between":
            {
                var hour = world.Clock.Hour;
                return new StoryProgress(c.LabelUk ?? "з " + (int)c.Min + ":00 до " + (int)c.Max + ":00",
                    hour >= (int)c.Min && hour < (int)c.Max, null, null);
            }
            case "stage_days":
            {
                var left = (int)(c.Value * 1440) - (tick - story.StageStartTick);
                var text = c.LabelUk ?? "Минуло днів: " + (int)c.Value;
                if (left > 0)
                {
                    text += " (лишилось " + (left / 60) + " год)";
                }
                return new StoryProgress(text, left <= 0, null, null);
            }
            case "support":
            {
                var count = Residents(world, c, target, other).Count(n => world.Trust.Get(n.Id, target!) >= c.Min);
                return new StoryProgress((c.LabelUk ?? "Довіряють " + Name(world, target) + " (від " + Percent(c.Min) + ")") +
                                         ": " + count + "/" + c.Count, count >= c.Count, null, null);
            }
            case "avg_trust_below":
            case "avg_trust_above":
            {
                var avg = AverageTrust(world, target!);
                var below = c.Type == "avg_trust_below";
                var done = below ? avg < c.Value : avg >= c.Value;
                return new StoryProgress((c.LabelUk ?? "Село довіряє " + Name(world, target)) + ": " + Percent(avg) +
                                         (below ? " (треба нижче " : " (треба від ") + Percent(c.Value) + ")", done, null, null);
            }
            case "role_prefers":
            {
                var role = Enum.TryParse<Role>(c.Role, true, out var parsed) ? parsed : Role.Guard;
                var group = world.ActiveNpcs().Where(n => n.Role == role).ToList();
                var on = group.Count(n => world.Trust.Get(n.Id, target!) > world.Trust.Get(n.Id, other!));
                return new StoryProgress((c.LabelUk ?? Roles.NameUk(role) + " на боці " + Name(world, target)) +
                                         ": " + on + "/" + group.Count, group.Count > 0 && on == group.Count, null, null);
            }
            case "errands_done":
            {
                var done = world.Quests.All.Values.Count(q => q.AutoReward && q.State == Quests.QuestState.Rewarded);
                return new StoryProgress((c.LabelUk ?? "Виконано доручень") + ": " + done + "/" + c.Count, done >= c.Count, null, null);
            }
            case "known_bad":
            {
                var count = Residents(world, c, target).Count(n => KnowsBadOf(n, target!));
                return new StoryProgress((c.LabelUk ?? "Знають про гріхи " + Name(world, target)) + ": " + count + "/" + c.Count,
                    count >= c.Count, null, null);
            }
            case "flag":
                return new StoryProgress(c.LabelUk ?? c.Flag ?? string.Empty, c.Flag is not null && story.Flags.Contains(c.Flag),
                    c.ButtonUk, c.Flag);
            case "player_at":
                return new StoryProgress(c.LabelUk ?? "Бути тут: " + world.ZoneName(c.Zone),
                    string.Equals(world.ZoneOfTile(player.X, player.Y), c.Zone, StringComparison.Ordinal), null, null);
            case "player_free":
                return new StoryProgress(c.LabelUk ?? "Ви на волі", !player.IsJailed(tick) && !player.IsBanished(tick) && !player.IsKnockedOut(tick),
                    null, null);
            default:
                return new StoryProgress("невідома умова " + c.Type, false, null, null);
        }
    }

    public static double AverageTrust(World world, string targetId)
    {
        var values = world.ActiveNpcs()
            .Where(n => !string.Equals(n.Id, targetId, StringComparison.Ordinal))
            .Select(n => world.Trust.Get(n.Id, targetId))
            .ToList();
        return values.Count == 0 ? 0 : values.Average();
    }

    private static bool KnowsBadOf(Npc npc, string targetId) =>
        npc.Knowledge.Entries.Any(e => e.C >= 0.3 &&
                                       (BadDeeds.ContainsKey(e.Type) && string.Equals(e.ActorId, targetId, StringComparison.Ordinal) ||
                                        e.Type is "item_found" or "search" && string.Equals(e.Field("target"), targetId, StringComparison.Ordinal) &&
                                        (e.Type == "item_found" || e.Field("result") == "found")));

    // ---------------------------------------------------------------- последствия

    private static void Apply(World world, StoryState story, StoryEffect e)
    {
        var game = world.Game;
        switch (e.Type)
        {
            case "announce":
            {
                var speakerId = Resolve(world, e.Npc);
                var npc = world.Npc(speakerId);
                var byPlayer = string.Equals(speakerId, Player.PlayerId, StringComparison.Ordinal);
                var text = Fill(world, e.Text ?? string.Empty);
                var zone = e.Zone ?? npc?.WorkZone ?? "square";
                var tile = byPlayer ? world.Player.Position : npc?.Position ?? GameMechanics.FreeTile(world, zone, world.Player.Position);
                var ev = GameMechanics.Emit(world, "story", byPlayer ? Player.PlayerId : npc?.Id, null, null, tile.X, tile.Y,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = text });
                // кто рядом — слышит сам; остальные узнают от них
                foreach (var listener in world.ActiveNpcs())
                {
                    if (Pathfinding.Euclidean(listener.Position, tile) <= world.Config.HearingRadius * 1.5 || ReferenceEquals(listener, npc))
                    {
                        listener.Knowledge.AddFirstHand(ev, Source.Heard, 1.0, false, world.Tick, listener.Id);
                    }
                }
                if (npc is not null)
                {
                    npc.LastSay = text;
                    npc.LastSayTick = world.Tick;
                }
                game.StoryNotices.Enqueue((byPlayer ? "Ви: " : npc is null ? string.Empty : npc.Name + ": ") + text);
                story.Journal.Add(world.Clock.TimeString + " — " + text);
                break;
            }
            case "notice":
            {
                var text = Fill(world, e.Text ?? string.Empty);
                game.StoryNotices.Enqueue(text);
                story.Journal.Add(world.Clock.TimeString + " — " + text);
                break;
            }
            case "approach":
            {
                if (world.Npc(Resolve(world, e.Npc)) is { } npc)
                {
                    GameMechanics.SendToPlayer(world, npc, Fill(world, e.Opening ?? "Є розмова."), Fill(world, e.Reason ?? string.Empty));
                }
                break;
            }
            case "campaign_start":
                if (Resolve(world, e.Npc) is { } campaigner)
                {
                    game.Campaigners.Add(campaigner);
                }
                break;
            case "campaign_stop":
                if (Resolve(world, e.Npc) is { } stopped)
                {
                    game.Campaigners.Remove(stopped);
                }
                break;
            case "election":
                Election(world, story, e);
                break;
            case "coup":
                Coup(world, story, e);
                break;
            case "set_headman":
            {
                var who = Resolve(world, e.Who);
                game.HeadmanId = who;
                game.StoryNotices.Enqueue("Новий староста: " + Name(world, who) + ".");
                break;
            }
            case "jail_player":
            {
                var guard = world.ActiveNpcs().FirstOrDefault(n => n.Role == Role.Guard);
                GameMechanics.JailPlayer(world, guard?.Id);
                break;
            }
            case "set_flag":
                if (e.Flag is not null)
                {
                    story.Flags.Add(e.Flag);
                }
                break;
            case "clear_flag":
                if (e.Flag is not null)
                {
                    story.Flags.Remove(e.Flag);
                }
                break;
            case "trust_from_friends":
            {
                // друзья и семья пострадавшего меняют отношение к тому, кто это сделал
                var victim = Resolve(world, e.Npc);
                var target = Resolve(world, e.Target) ?? Player.PlayerId;
                if (victim is null)
                {
                    break;
                }
                foreach (var npc in world.ActiveNpcs())
                {
                    if (!string.Equals(npc.Id, victim, StringComparison.Ordinal) && world.Trust.Get(npc.Id, victim) >= 0.7)
                    {
                        world.Trust.Adjust(npc.Id, target, e.Delta);
                    }
                }
                break;
            }
            case "end":
                story.Result = Fill(world, e.Text ?? string.Empty);
                game.StoryNotices.Enqueue(story.Result);
                break;
        }
    }

    /// <summary>Каждый житель голосует за того кандидата, которому доверяет больше. Ничья — не голосует.</summary>
    private static void Election(World world, StoryState story, StoryEffect e)
    {
        var candidates = e.Candidates.Select(c => Resolve(world, c)).Where(c => c is not null).Select(c => c!).ToList();
        if (candidates.Count < 2)
        {
            return;
        }
        var votes = candidates.ToDictionary(c => c, _ => 0, StringComparer.Ordinal);
        foreach (var voter in world.ActiveNpcs())
        {
            if (candidates.Contains(voter.Id))
            {
                continue;
            }
            var ranked = candidates.Select(c => (Id: c, Trust: world.Trust.Get(voter.Id, c))).OrderByDescending(r => r.Trust).ToList();
            if (ranked[0].Trust - ranked[1].Trust < 0.02)
            {
                continue;
            }
            votes[ranked[0].Id]++;
        }
        var summary = string.Join(", ", votes.Select(v =>
            (string.Equals(v.Key, Player.PlayerId, StringComparison.Ordinal) ? "ви" : world.NpcName(v.Key)) + " — " + v.Value));
        game(world).StoryNotices.Enqueue("Підсумки голосування: " + summary + ".");
        story.Journal.Add(world.Clock.TimeString + " — голосування: " + summary);
        var winner = votes.OrderByDescending(v => v.Value).First();
        var tie = votes.Count(v => v.Value == winner.Value) > 1;
        var playerWon = !tie && string.Equals(winner.Key, Player.PlayerId, StringComparison.Ordinal);
        if (!playerWon)
        {
            world.Game.HeadmanId = tie ? world.Game.HeadmanId : winner.Key;
        }
        var to = playerWon ? e.WinTo : e.LoseTo;
        if (!string.IsNullOrWhiteSpace(to))
        {
            EnterStage(world, story, to!);
        }
    }

    /// <summary>Сход: если за игроком больше людей, чем за старостой, и варта на его стороне — переворот удался.</summary>
    private static void Coup(World world, StoryState story, StoryEffect e)
    {
        var target = Resolve(world, e.Target) ?? world.Game.HeadmanId ?? "petro";
        var forPlayer = 0;
        var forTarget = 0;
        foreach (var npc in world.ActiveNpcs())
        {
            if (string.Equals(npc.Id, target, StringComparison.Ordinal))
            {
                continue;
            }
            var toPlayer = world.Trust.Get(npc.Id, Player.PlayerId);
            var toTarget = world.Trust.Get(npc.Id, target);
            if (toPlayer > toTarget + 0.02)
            {
                forPlayer++;
            }
            else if (toTarget > toPlayer + 0.02)
            {
                forTarget++;
            }
        }
        var guards = world.ActiveNpcs().Where(n => n.Role == Role.Guard).ToList();
        var guardsWithPlayer = guards.Count(g => world.Trust.Get(g.Id, Player.PlayerId) > world.Trust.Get(g.Id, target));
        var success = forPlayer > forTarget && guards.Count > 0 && guardsWithPlayer == guards.Count;
        var summary = "підтримали вас: " + forPlayer + ", підтримали старосту (" + world.NpcName(target) + "): " + forTarget +
                      ", вартових на вашому боці: " + guardsWithPlayer + "/" + guards.Count;
        world.Game.StoryNotices.Enqueue("Сход на площі: " + summary + ".");
        story.Journal.Add(world.Clock.TimeString + " — сход: " + summary);
        var to = success ? e.WinTo : e.LoseTo;
        if (!string.IsNullOrWhiteSpace(to))
        {
            EnterStage(world, story, to!);
        }
    }

    private static GameState game(World world) => world.Game;

    // ---------------------------------------------------------------- доверие и агитация

    /// <summary>
    /// Узнал о плохом поступке — меньше доверяет тому, кто его совершил; о хорошем — больше.
    /// Сила зависит от тяжести поступка и от того, насколько житель в это верит (видел сам или слышал слух).
    /// Каждая запись учитывается один раз.
    /// </summary>
    private static void UpdateReputation(World world)
    {
        var processed = world.Game.ReputationSeen;
        foreach (var npc in world.ActiveNpcs())
        {
            foreach (var entry in npc.Knowledge.Entries)
            {
                var key = npc.Id + "|" + entry.Key.ToString(CultureInfo.InvariantCulture);
                if (processed.Contains(key))
                {
                    continue;
                }
                processed.Add(key);

                string? doer = null;
                var weight = 0.0;
                if (BadDeeds.TryGetValue(entry.Type, out var bad))
                {
                    doer = entry.ActorId;
                    weight = -bad;
                }
                else if (GoodDeeds.TryGetValue(entry.Type, out var good))
                {
                    doer = entry.ActorId;
                    weight = good;
                }
                else if (entry.Type == "item_found" || entry.Type == "search" && entry.Field("result") == "found")
                {
                    doer = entry.Field("target");
                    weight = -0.2;
                }
                if (string.IsNullOrWhiteSpace(doer) || string.Equals(doer, npc.Id, StringComparison.Ordinal))
                {
                    continue;
                }
                var certainty = Sources.IsFirstHand(entry.Source) ? 1.0 : 0.6;
                world.Trust.Adjust(npc.Id, doer, weight * entry.C * certainty);
            }
        }
    }

    /// <summary>
    /// Слово друзей и родни: если кто-то вам доверяет (от 60%), его близкий понемногу тоже начинает
    /// доверять больше («за тебе поручилися»); если кто-то вас не терпит (до 20%) — близкий охладевает.
    /// </summary>
    private static void UpdateWordOfMouth(World world)
    {
        foreach (var (a, b) in world.Game.Friends)
        {
            Influence(world, a, b);
            Influence(world, b, a);
        }
    }

    private static void Influence(World world, string speaker, string listener)
    {
        if (world.Npc(speaker) is null || world.Npc(listener) is null)
        {
            return;
        }
        var said = world.Trust.Get(speaker, Player.PlayerId);
        var heard = world.Trust.Get(listener, Player.PlayerId);
        if (said >= 0.6 && heard < said - 0.15)
        {
            world.Trust.Adjust(listener, Player.PlayerId, 0.02);
        }
        else if (said <= 0.2 && heard > said + 0.15)
        {
            world.Trust.Adjust(listener, Player.PlayerId, -0.02);
        }
    }

    /// <summary>Кандидат каждый час уговаривает кого-то из бодрствующих: доверие к нему понемногу растёт.</summary>
    private static void UpdateCampaigns(World world)
    {
        var hour = world.Clock.Hour;
        if (hour < 8 || hour >= 20)
        {
            return;
        }
        foreach (var campaignerId in world.Game.Campaigners)
        {
            if (world.Npc(campaignerId) is not { } campaigner || campaigner.IsAsleep || campaigner.IsKnockedOut(world.Tick))
            {
                continue;
            }
            var listeners = world.ActiveNpcs()
                .Where(n => !ReferenceEquals(n, campaigner) && !n.IsAsleep && world.Trust.Get(n.Id, campaignerId) < 0.85)
                .ToList();
            if (listeners.Count == 0)
            {
                continue;
            }
            var listener = listeners[world.Rng.NextInt(0, listeners.Count, world.Rng.Sim)];
            world.Trust.Adjust(listener.Id, campaignerId, 0.04);
        }
    }

    // ---------------------------------------------------------------- наклеп игрока

    private static readonly (string Type, string[] Words)[] Accusations =
    [
        ("theft", ["вкрав", "вкрала", "краде", "злодій", "злодійка", "поцупив", "поцупила", "стягнув"]),
        ("attack", ["побив", "побила", "вдарив", "вдарила", "напав", "напала", "б'є"]),
        ("bribe", ["хабар", "хабарі", "підкупив", "підкупила", "продажний", "продажна"])
    ];

    /// <summary>
    /// Игрок говорит жителю, что кто-то украл, побил или берёт хабари. Житель верит настолько,
    /// насколько доверяет игроку. Это запись «слова гравця»: она снижает доверие к оговорённому и может пойти дальше слухом.
    /// </summary>
    public static void ProcessSlander(World world, Npc listener, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        var lower = text.ToLowerInvariant();
        var accusation = Accusations.FirstOrDefault(a => a.Words.Any(w => lower.Contains(w, StringComparison.Ordinal)));
        if (accusation.Type is null)
        {
            return;
        }
        var accused = world.Npcs.FirstOrDefault(n => !ReferenceEquals(n, listener) && Mentions(lower, n.Name));
        if (accused is null)
        {
            return;
        }
        var trust = world.Trust.Get(listener.Id, Player.PlayerId);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["actor"] = accused.Id,
            ["text"] = text.Length > 160 ? text[..160] : text
        };
        listener.Knowledge.AddClaim(accusation.Type, fields, Math.Clamp(trust * 0.7, 0.05, 0.8), world.Tick, Player.PlayerId, 0.6);
    }

    /// <summary>Имя в тексте с учётом падежей: «Петро», «Петра», «Петрові», «Петром».</summary>
    private static bool Mentions(string lowerText, string name)
    {
        var lowerName = name.ToLowerInvariant();
        if (lowerText.Contains(lowerName, StringComparison.Ordinal))
        {
            return true;
        }
        if (lowerName.Length < 4)
        {
            return false;
        }
        var stem = lowerName[..^1];
        var index = lowerText.IndexOf(stem, StringComparison.Ordinal);
        return index >= 0 && (index == 0 || !char.IsLetter(lowerText[index - 1]));
    }

    // ---------------------------------------------------------------- помощники

    private static string? Resolve(World world, string? id) => id switch
    {
        null => null,
        "player" => Player.PlayerId,
        "headman" => world.Game.HeadmanId,
        _ => id
    };

    private static string Name(World world, string? id) =>
        string.Equals(id, Player.PlayerId, StringComparison.Ordinal) ? "вам" : world.NpcName(id);

    private static string Percent(double value) => ((int)Math.Round(value * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    private static string Fill(World world, string text) => text
        .Replace("{player}", world.Player.Name, StringComparison.Ordinal)
        .Replace("{headman}", world.NpcName(world.Game.HeadmanId), StringComparison.Ordinal);
}
