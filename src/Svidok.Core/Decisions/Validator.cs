using Svidok.Core.Entities;
using Svidok.Core.Quests;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

/// <summary>Валидатор решения модели (раздел 9.8). Работает в V0 и V4.</summary>
public sealed class Validator
{
    private readonly World _world;
    private readonly ActionCatalog _catalog;

    public Validator(World world, ActionCatalog catalog)
    {
        _world = world;
        _catalog = catalog;
    }

    public ValidationResult Validate(Npc npc, LlmDecision decision)
    {
        var def = _catalog.Get(decision.Action);
        if (def is null)
        {
            return ValidationResult.Fail(RejectReason.ActionNotAllowed, "дії немає в каталозі: " + decision.Action);
        }

        if (def.Game && !_world.Game.Social)
        {
            return ValidationResult.Fail(RejectReason.ActionNotAllowed, "дія лише для ігрових сценаріїв: " + def.Action);
        }

        if (!_catalog.WhoAllows(def, npc, _world))
        {
            return ValidationResult.Fail(RejectReason.ActionNotAllowed, "роль не може виконувати " + def.Action);
        }

        KnowledgeEntryLookup lookup = default;
        var hasEntry = false;
        if (decision.EventRef is int eventRef)
        {
            var entry = npc.Knowledge.ByEventId(eventRef);
            if (entry is not null)
            {
                lookup = new KnowledgeEntryLookup(entry.C, entry.Type);
                hasEntry = true;
            }
        }

        if (def.RequiresEventRef)
        {
            if (decision.EventRef is null)
            {
                return ValidationResult.Fail(RejectReason.InvalidParameter, "не вказано eventRef");
            }
            if (!hasEntry)
            {
                return ValidationResult.Fail(RejectReason.UnknownEvent, "eventRef=" + decision.EventRef + " немає у знаннях");
            }
            if (lookup.Credibility < def.CMin)
            {
                return ValidationResult.Fail(RejectReason.LowCredibility,
                    "c=" + lookup.Credibility.ToString("0.00") + " < c_min=" + def.CMin.ToString("0.0#"));
            }
        }

        if (def.RequiresQuest)
        {
            var quest = _world.Quests.Get(decision.QuestId);
            if (quest is null)
            {
                return ValidationResult.Fail(RejectReason.InvalidParameter, "невідоме завдання " + decision.QuestId);
            }

            if (def.Action == "give_reward")
            {
                var completedInLog = _world.Log.HasQuestCompleted(quest.Id, Player.PlayerId);
                var alreadyRewarded = _world.Log.HasRewardGiven(quest.Id) || quest.State == QuestState.Rewarded;
                if (!completedInLog || quest.State == QuestState.NotTaken || alreadyRewarded)
                {
                    return ValidationResult.Fail(RejectReason.NoQuestCompletion,
                        "немає quest_completed для " + quest.Id);
                }
            }
        }

        if (def.RequiresTarget && !string.IsNullOrWhiteSpace(decision.Target))
        {
            var isPlayer = string.Equals(decision.Target, Player.PlayerId, StringComparison.Ordinal);
            var targetNpc = _world.Npc(decision.Target);
            if (!isPlayer && targetNpc is null)
            {
                return ValidationResult.Fail(RejectReason.InvalidParameter, "невідома ціль " + decision.Target);
            }

            if (def.MaxTargetDistance is double maxDistance)
            {
                var targetPosition = isPlayer ? _world.Player.TileOf : targetNpc!.Position;
                var distance = Pathfinding.Euclidean(npc.Position, targetPosition);
                if (distance > maxDistance)
                {
                    return ValidationResult.Fail(RejectReason.InvalidParameter,
                        "ціль далі ніж " + maxDistance + " тайлів (" + distance.ToString("0.0") + ")");
                }
            }
        }

        if (def.RequiresZone && !string.IsNullOrWhiteSpace(decision.Zone) && _world.Zone(decision.Zone) is null)
        {
            return ValidationResult.Fail(RejectReason.InvalidParameter, "невідома зона " + decision.Zone);
        }

        if (def.RequiresItem)
        {
            if (string.IsNullOrWhiteSpace(decision.Item) || !_world.Items.ContainsKey(decision.Item))
            {
                return ValidationResult.Fail(RejectReason.InvalidParameter, "невідомий предмет " + decision.Item);
            }
            if (!npc.Inventory.Contains(decision.Item, StringComparer.Ordinal))
            {
                return ValidationResult.Fail(RejectReason.InvalidParameter, "предмета немає у NPC: " + decision.Item);
            }
        }

        if (_world.Game.Social)
        {
            var game = ValidateGame(npc, def, decision);
            if (!game.Ok)
            {
                return game;
            }
        }

        return ValidationResult.Success;
    }

    /// <summary>
    /// Правила игровых сценариев (S6–S8). В экспериментах S1–S5 не вызываются, поэтому их результаты не меняются.
    /// </summary>
    private ValidationResult ValidateGame(Npc npc, ActionDef def, LlmDecision decision)
    {
        var tick = _world.Tick;

        // обещание молчать: сознательно раскрыть секрет нельзя, пока обещание держится
        if (def.Action is "warn" or "report_to_guard" or "shout_alarm" &&
            decision.EventRef is int secretRef &&
            npc.Secrets.TryGetValue(secretRef, out var strength) && strength >= GameRules.SecretHoldThreshold)
        {
            return ValidationResult.Fail(RejectReason.PromisedSecret, "пообіцяв мовчати про подію #" + secretRef);
        }

        // арест: цель должна быть тем, кто совершил событие (поле actor или target находки)
        if (def.Action == "arrest" && decision.EventRef is int arrestRef && !string.IsNullOrWhiteSpace(decision.Target))
        {
            var entry = npc.Knowledge.ByEventId(arrestRef);
            var actor = entry?.Field("actor");
            var accused = entry?.Type is "item_found" or "search" ? entry.Field("target") : actor;
            if (!string.Equals(accused, decision.Target, StringComparison.Ordinal))
            {
                return ValidationResult.Fail(RejectReason.NoGrounds,
                    "подія #" + arrestRef + " не вказує на " + decision.Target);
            }
        }

        switch (def.Action)
        {
            case "keep_secret":
            {
                var paid = _world.Game.PaidRecently(npc.Id, tick, GameRules.PaymentWindowTicks);
                var trust = _world.Trust.Get(npc.Id, Player.PlayerId);
                if (paid <= 0 && trust < 0.7)
                {
                    return ValidationResult.Fail(RejectReason.NoGrounds, "мовчати за так не обіцяють: ні плати, ні довіри");
                }
                break;
            }
            case "give_quest":
            {
                var quest = _world.Quests.Get(decision.QuestId) ?? _world.Quests.QuestOfGiver(npc.Id);
                if (quest is null || !string.Equals(quest.GiverId, npc.Id, StringComparison.Ordinal) ||
                    quest.State != QuestState.NotTaken)
                {
                    return ValidationResult.Fail(RejectReason.InvalidParameter, "завдання не можна видати: " + decision.QuestId);
                }
                break;
            }
            case "accept_trade":
            {
                if (!_world.Game.Trades.ContainsKey(npc.Id))
                {
                    return ValidationResult.Fail(RejectReason.InvalidParameter, "гравець не пропонував обміну");
                }
                break;
            }
            case "banish":
            case "fine":
            {
                if (!string.Equals(decision.Target, Player.PlayerId, StringComparison.Ordinal))
                {
                    return ValidationResult.Fail(RejectReason.InvalidParameter, def.Action + " застосовується лише до гравця");
                }
                var minC = def.CMin;
                var knowsMisdeed = npc.Knowledge.Entries.Any(e =>
                    e.Type is "theft" or "attack" or "threat" or "curfew" or "knockout" &&
                    string.Equals(e.Field("actor"), Player.PlayerId, StringComparison.Ordinal) &&
                    e.C >= minC);
                var trust = _world.Trust.Get(npc.Id, Player.PlayerId);
                if (!knowsMisdeed && !(def.Action == "banish" && trust < 0.2))
                {
                    return ValidationResult.Fail(RejectReason.NoGrounds, "немає провини гравця з c ≥ " + minC.ToString("0.0#"));
                }
                break;
            }
            case "search":
            {
                // обыскивают того, на кого указывает известное событие, или (варта) любого при известной краже
                var target = decision.Target ?? string.Empty;
                var suspected = npc.Knowledge.Entries.Any(e => e.C >= 0.3 &&
                    (string.Equals(e.Field("actor"), target, StringComparison.Ordinal) ||
                     e.Type == "item_found" && string.Equals(e.Field("target"), target, StringComparison.Ordinal)));
                var guardDuty = npc.Role == Role.Guard && npc.Knowledge.Entries.Any(e => e.Type == "theft" && e.C >= 0.3);
                if (!suspected && !guardDuty)
                {
                    return ValidationResult.Fail(RejectReason.NoGrounds, "обшукувати без підозри не можна");
                }
                break;
            }
            case "attack":
            {
                // бить можно в ответ (на тебя напали за последние полчаса) или вартовому — того, о чьём нападении он знает
                var target = decision.Target ?? string.Empty;
                var selfDefence = npc.Knowledge.Entries.Any(e =>
                    e.Type == "attack" && e.C >= 0.9 &&
                    string.Equals(e.Field("actor"), target, StringComparison.Ordinal) &&
                    string.Equals(e.Field("target"), npc.Id, StringComparison.Ordinal) &&
                    tick - e.LearnedTick <= 30);
                var guardDuty = npc.Role == Role.Guard && npc.Knowledge.Entries.Any(e =>
                    e.Type == "attack" && e.C >= 0.9 &&
                    string.Equals(e.Field("actor"), target, StringComparison.Ordinal));
                if (!selfDefence && !guardDuty)
                {
                    return ValidationResult.Fail(RejectReason.NoGrounds, "бити без причини не можна");
                }
                break;
            }
        }

        return ValidationResult.Success;
    }

    private readonly record struct KnowledgeEntryLookup(double Credibility, string Type);
}
