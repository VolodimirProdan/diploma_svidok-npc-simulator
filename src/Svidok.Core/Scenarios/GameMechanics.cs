using System.Globalization;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Scenarios;

/// <summary>
/// Игровые механики сценариев S6–S8: находки подброшенных вещей, обещания молчать, здоровье и драки,
/// голод, подработка, комендантский час, обезьянник и изгнание. В S1–S5 не вызывается.
/// </summary>
public static class GameMechanics
{
    private const string OutskirtsZone = "outskirts";

    /// <summary>Один тик игровых механик (после движения, до восприятия).</summary>
    public static void Tick(Simulation sim)
    {
        var world = sim.World;
        var game = world.Game;

        ExpireOffers(world);
        UpdateKnockouts(world);
        UpdatePlayerSleep(world);

        if (game.Social)
        {
            DiscoverPlantedItems(world);
            UpdatePlayerSanctions(world);
            if (world.Tick % 10 == 0)
            {
                UpdateApproaches(world);
            }
            StoryEngine.Tick(world);
        }

        if (world.Clock.IsNewHour)
        {
            DecaySecrets(world);
            Regenerate(world);
            if (game.Life)
            {
                UpdateHunger(world);
            }
        }

        if (game.Life || game.Social)
        {
            UpdateWork(sim);
        }
        if (game.Social && world.Clock.IsNewHour)
        {
            UpdateErrands(sim);
        }
        if (game.Life && world.Tick % 5 == 0)
        {
            UpdateCurfew(world);
        }
    }

    // ---------------------------------------------------------------- поступки игрока

    /// <summary>Игрок даёт NPC монеты: настоящее событие, которое NPC знает как участник.</summary>
    public static bool PlayerGiveCoins(World world, Npc npc, int coins)
    {
        var player = world.Player;
        coins = Math.Clamp(coins, 1, Math.Max(1, player.Coins));
        if (player.Coins < coins || coins <= 0)
        {
            return false;
        }

        player.Coins -= coins;
        npc.Coins += coins;
        world.Game.Payments.Add(new PlayerPayment(world.Tick, npc.Id, coins));
        var ev = Emit(world, "give_coins", Player.PlayerId, npc.Id, null, player.X, player.Y,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["coins"] = coins.ToString(CultureInfo.InvariantCulture) });
        npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, npc.Id);
        return true;
    }

    /// <summary>Игрок платит то, что потребовал NPC; NPC отдаёт обещанный предмет, если он у него есть.</summary>
    public static bool PlayerPayDemand(World world, Npc npc)
    {
        if (!world.Game.Demands.TryGetValue(npc.Id, out var demand))
        {
            return false;
        }
        if (!PlayerGiveCoins(world, npc, demand.Coins))
        {
            return false;
        }

        world.Game.Demands.Remove(npc.Id);
        if (!string.IsNullOrWhiteSpace(demand.Item) && npc.Inventory.Remove(demand.Item))
        {
            world.Player.AddItem(demand.Item);
            var give = Emit(world, "give_item", npc.Id, Player.PlayerId, demand.Item, npc.X, npc.Y);
            npc.Knowledge.AddFirstHand(give, Source.Participant, 1.0, false, world.Tick, npc.Id);
        }
        return true;
    }

    public static void PlayerRefuseDemand(World world, Npc npc) => world.Game.Demands.Remove(npc.Id);

    /// <summary>Игрок предлагает обмен; решение принимает NPC (действие accept_trade).</summary>
    public static bool PlayerOfferTrade(World world, Npc npc, string playerItem, string? npcItem)
    {
        if (!world.Player.HasItem(playerItem))
        {
            return false;
        }
        if (npcItem is not null && !npc.Inventory.Contains(npcItem, StringComparer.Ordinal))
        {
            npcItem = null;
        }
        world.Game.Trades[npc.Id] = new TradeOffer(playerItem, npcItem, world.Tick);
        return true;
    }

    /// <summary>Игрок угрожает: событие слышно и видно окружающим.</summary>
    public static void PlayerThreaten(World world, Npc npc, string? text)
    {
        var player = world.Player;
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(text))
        {
            extra["text"] = text.Length > 120 ? text[..120] : text;
        }
        var ev = Emit(world, "threat", Player.PlayerId, npc.Id, null, player.X, player.Y, extra);
        npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, npc.Id);
    }

    /// <summary>Игрок бьёт NPC.</summary>
    public static void PlayerHit(World world, Npc npc)
    {
        var ev = Hit(world, Player.PlayerId, npc.Id);
        if (ev is not null)
        {
            npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, npc.Id);
        }
    }

    /// <summary>
    /// Подбросить выбранный предмет на текущий тайл: его найдёт кто-то из жильцов или прохожих,
    /// и находка разойдётся как слух.
    /// </summary>
    public static bool PlayerPlant(World world, string itemId)
    {
        var player = world.Player;
        if (!player.RemoveItem(itemId))
        {
            return false;
        }
        world.GroundItems.Add(new GroundItem(itemId, player.X, player.Y, world.ZoneOfTile(player.X, player.Y))
        {
            PlantedBy = Player.PlayerId,
            PlacedTick = world.Tick
        });
        return true;
    }

    /// <summary>Просто выбросить предмет (без подставы).</summary>
    public static bool PlayerDrop(World world, string itemId)
    {
        var player = world.Player;
        if (!player.RemoveItem(itemId))
        {
            return false;
        }
        world.GroundItems.Add(new GroundItem(itemId, player.X, player.Y, world.ZoneOfTile(player.X, player.Y))
        {
            PlacedTick = world.Tick
        });
        return true;
    }

    /// <summary>Съесть предмет. Возвращает, насколько снизился голод (0 — несъедобно).</summary>
    public static int PlayerEat(World world, string itemId)
    {
        if (!world.Items.TryGetValue(itemId, out var item) || item.Food <= 0)
        {
            return 0;
        }
        if (!world.Player.RemoveItem(itemId))
        {
            return 0;
        }
        var player = world.Player;
        player.Hunger = Math.Max(0, player.Hunger - item.Food);
        player.Hp = Math.Min(player.MaxHp, player.Hp + 1);
        Emit(world, "eat", Player.PlayerId, null, itemId, player.X, player.Y);
        return item.Food;
    }

    /// <summary>Начать подработку в текущей зоне. Возвращает подпись работы или null, если здесь работы нет.</summary>
    public static JobSpecView? PlayerStartWork(Simulation sim)
    {
        var world = sim.World;
        var player = world.Player;
        if (!world.Game.Life && !world.Game.Social || player.IsBusy(world.Tick))
        {
            return null;
        }
        var zone = world.ZoneOfTile(player.X, player.Y);
        var job = sim.Scenario.Jobs.FirstOrDefault(j => string.Equals(j.Zone, zone, StringComparison.Ordinal));
        if (job is null)
        {
            return null;
        }
        var hour = world.Clock.Hour;
        if (hour < 7 || hour >= 19)
        {
            return new JobSpecView(job.TitleUk, job.Pay, false, "Зараз ніхто не наймає: робота з 7:00 до 19:00.");
        }
        player.WorkUntilTick = world.Tick + GameRules.WorkTicks;
        player.WorkZone = zone;
        return new JobSpecView(job.TitleUk, job.Pay, true, null);
    }

    // ---------------------------------------------------------------- драки

    /// <summary>Удар: от 2 до 4 HP. При 0 HP — без сознания на час, потом 1 HP.</summary>
    public static WorldEvent? Hit(World world, string attackerId, string targetId)
    {
        var damage = world.Rng.NextInt(GameRules.MinHit, GameRules.MaxHit + 1, world.Rng.Sim);
        (int X, int Y) position;
        var knockedOut = false;

        if (string.Equals(targetId, Player.PlayerId, StringComparison.Ordinal))
        {
            var player = world.Player;
            if (player.IsKnockedOut(world.Tick))
            {
                return null;
            }
            player.Hp = Math.Max(0, player.Hp - damage);
            player.SleepUntilTick = -1;
            position = player.Position;
            if (player.Hp == 0)
            {
                player.KnockedOutUntilTick = world.Tick + GameRules.KnockoutTicks;
                player.WorkUntilTick = -1;
                knockedOut = true;
            }
        }
        else if (world.Npc(targetId) is Npc target)
        {
            if (target.IsKnockedOut(world.Tick))
            {
                return null;
            }
            target.Hp = Math.Max(0, target.Hp - damage);
            position = target.Position;
            if (target.Hp == 0)
            {
                target.KnockedOutUntilTick = world.Tick + GameRules.KnockoutTicks;
                target.StunnedUntilTick = target.KnockedOutUntilTick;
                target.CurrentAction = null;
                target.ClearPath();
                knockedOut = true;
            }
        }
        else
        {
            return null;
        }

        var attack = Emit(world, "attack", attackerId, targetId, null, position.X, position.Y,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["damage"] = damage.ToString(CultureInfo.InvariantCulture) });
        if (knockedOut)
        {
            Emit(world, "knockout", attackerId, targetId, null, position.X, position.Y);
        }
        return attack;
    }

    // ---------------------------------------------------------------- санкции против игрока

    public static void JailPlayer(World world, string? guardId)
    {
        var player = world.Player;
        player.JailedUntilTick = world.Tick + GameRules.JailTicks;
        player.WorkUntilTick = -1;
        var cell = FreeTile(world, "guard_tower", player.Position);
        player.Teleport(cell.X, cell.Y);
        world.Game.Arrests.Add(Player.PlayerId);
    }

    public static void BanishPlayer(World world)
    {
        var player = world.Player;
        player.BanishedUntilTick = world.Tick + GameRules.BanishTicks;
        player.WorkUntilTick = -1;
        var spot = world.Zone(OutskirtsZone) is not null
            ? FreeTile(world, OutskirtsZone, player.Position)
            : EdgeTile(world);
        player.Teleport(spot.X, spot.Y);
    }

    // ---------------------------------------------------------------- сценарные команды S7/S8

    /// <summary>Служебные команды сценария: plant, keep_secret, give_coins, set_culprit. true — команда обработана.</summary>
    public static bool ExecuteScripted(Simulation sim, ScriptedEvent scripted)
    {
        var world = sim.World;
        var type = scripted.Type ?? string.Empty;

        if (type.Equals("set_culprit", StringComparison.OrdinalIgnoreCase))
        {
            world.Game.TrueCulpritId = scripted.Actor;
            world.Game.FramedNpcId = scripted.Target;
            return true;
        }

        if (type.Equals("plant", StringComparison.OrdinalIgnoreCase))
        {
            // предмет подбрасывают в дом цели (target) или в указанную зону
            if (string.IsNullOrWhiteSpace(scripted.Item))
            {
                return true;
            }
            var zone = scripted.Zone ?? world.Npc(scripted.Target)?.HomeZone ?? "square";
            foreach (var npc in world.Npcs)
            {
                npc.Inventory.Remove(scripted.Item);
            }
            world.Player.RemoveItem(scripted.Item);
            world.GroundItems.RemoveAll(g => string.Equals(g.ItemId, scripted.Item, StringComparison.Ordinal));
            var tile = FreeTile(world, zone, world.Zone(zone)?.Center ?? (1, 1));
            world.GroundItems.Add(new GroundItem(scripted.Item, tile.X, tile.Y, zone)
            {
                PlantedBy = scripted.Actor ?? "unknown",
                PlacedTick = world.Tick
            });
            if (!string.IsNullOrWhiteSpace(scripted.Target))
            {
                world.Game.FramedNpcId = scripted.Target;
            }
            return true;
        }

        if (type.Equals("keep_secret", StringComparison.OrdinalIgnoreCase))
        {
            // actor обещает молчать о последнем известном ему событии типа extra.eventType
            var npc = world.Npc(scripted.Actor);
            if (npc is null)
            {
                return true;
            }
            var eventType = scripted.Extra is not null && scripted.Extra.TryGetValue("eventType", out var t) ? t : "theft";
            var strength = scripted.Extra is not null && scripted.Extra.TryGetValue("strength", out var raw) &&
                           double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0.9;
            var entry = npc.Knowledge.Entries
                .Where(e => string.Equals(e.Type, eventType, StringComparison.Ordinal) && e.EventId is not null)
                .OrderByDescending(e => e.LearnedTick)
                .FirstOrDefault();
            if (entry?.EventId is int eventId)
            {
                npc.Secrets[eventId] = Math.Clamp(strength, 0, 1);
            }
            return true;
        }

        if (type.Equals("give_coins", StringComparison.OrdinalIgnoreCase))
        {
            var npc = world.Npc(scripted.Target);
            var coins = scripted.Extra is not null && scripted.Extra.TryGetValue("coins", out var c) &&
                        int.TryParse(c, out var n) ? n : 3;
            if (npc is not null)
            {
                world.Player.Coins += coins;
                PlayerGiveCoins(world, npc, coins);
            }
            return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- вспомогательное

    public static WorldEvent Emit(World world, string type, string? actor, string? target, string? item, int x, int y,
        Dictionary<string, string>? extra = null)
    {
        var def = world.Schema.Get(type);
        return world.Log.Add(world.Tick, type, actor, target, item, world.ZoneOfTile(x, y), x, y,
            def.Loudness, def.Importance, def.Background, extra);
    }

    public static (int X, int Y) FreeTile(World world, string zoneId, (int X, int Y) near)
    {
        var tiles = world.Map.WalkableTilesIn(zoneId);
        if (tiles.Count == 0)
        {
            return near;
        }
        return tiles
            .OrderBy(t => Math.Abs(t.X - near.X) + Math.Abs(t.Y - near.Y))
            .First();
    }

    private static (int X, int Y) EdgeTile(World world)
    {
        var map = world.Map;
        var y = map.Height / 2;
        for (var x = 1; x < map.Width - 1; x++)
        {
            if (map.IsWalkable(x, y))
            {
                return (x, y);
            }
        }
        return world.Player.Position;
    }

    // ---------------------------------------------------------------- проверка, обыск, варта

    /// <summary>
    /// Чем заканчивается «перевірити» на месте события: вещь лежит там — нашёл и вернул владельцу;
    /// известен подозреваемый — идёт его обыскивать; иначе честно говорит, что ничего не нашёл.
    /// </summary>
    public static void ResolveInvestigation(Decisions.ActionContext ctx)
    {
        var world = ctx.World;
        var npc = ctx.Npc;
        var entry = ctx.Action.EventRef is int eventRef ? npc.Knowledge.ByEventId(eventRef) : null;
        var truth = ctx.Action.EventRef is int id ? world.Log.ById(id) : null;
        var itemId = ctx.Action.ItemId ?? truth?.ItemId ?? entry?.Field("item");
        var zone = world.ZoneOfTile(npc.X, npc.Y);

        if (itemId is not null)
        {
            var ground = world.GroundItems.FirstOrDefault(g => g.ErrandId is null &&
                string.Equals(g.ItemId, itemId, StringComparison.Ordinal) &&
                string.Equals(world.ZoneOfTile(g.X, g.Y), zone, StringComparison.Ordinal));
            if (ground is not null)
            {
                world.GroundItems.Remove(ground);
                var owner = RightfulOwner(world, itemId, truth);
                (owner ?? npc).Inventory.Add(itemId);
                var recovered = Emit(world, "recovered", npc.Id, owner?.Id, itemId, ground.X, ground.Y);
                npc.Knowledge.AddFirstHand(recovered, Source.Participant, 1.0, false, ctx.Tick, npc.Id);
                owner?.Knowledge.AddFirstHand(recovered, Source.Participant, 1.0, false, ctx.Tick, owner.Id);
                Say(npc, ctx.Tick, "Ось де він, " + world.ItemName(itemId) + "! " +
                                   (owner is null || ReferenceEquals(owner, npc) ? "Заберу." : "Віднесу " + owner.Name + "."));
                return;
            }
        }

        var suspect = entry?.ActorId;
        if (!string.IsNullOrWhiteSpace(suspect) && !string.Equals(suspect, npc.Id, StringComparison.Ordinal) &&
            (string.Equals(suspect, Player.PlayerId, StringComparison.Ordinal) || world.Npc(suspect) is not null))
        {
            if (AlreadySearched(world, ctx.Action.EventRef, suspect))
            {
                Say(npc, ctx.Tick, "Тут нічого, а " + world.NpcName(suspect) + " вже обшукували.");
                return;
            }
            Say(npc, ctx.Tick, "Тут нічого. Піду поговорю з " + world.NpcName(suspect) + ".");
            StartAction(world, npc, "search", suspect, ctx.Action.EventRef, itemId, "Обшукати");
            return;
        }

        if (entry is not null)
        {
            entry.C *= 0.5;
        }
        Say(npc, ctx.Tick, "Нічого не знайшов. Може, люди набрехали.");
    }

    /// <summary>
    /// Обыск: событие «обшук» видят все поблизости. Нашёл — вещь возвращается владельцу,
    /// доверие к пойманному падает, зовут варту. Не нашёл — подозрение ослабевает, обысканный обижен.
    /// </summary>
    /// <summary>По этому событию этого человека уже обыскали: нашли — дело закрыто, не нашли — не раньше чем через 4 часа.</summary>
    public static bool AlreadySearched(World world, int? eventRef, string targetId)
    {
        if (eventRef is not int id || !world.Game.Searches.TryGetValue((id, targetId), out var last))
        {
            return false;
        }
        return last.Found || world.Tick - last.Tick < 240;
    }

    public static void Search(World world, Npc searcher, string targetId, int? eventRef, string? itemId)
    {
        var tick = world.Tick;
        if (AlreadySearched(world, eventRef, targetId))
        {
            Say(searcher, tick, "Його вже обшукували.");
            return;
        }
        var truth = eventRef is int id ? world.Log.ById(id) : null;
        var isPlayer = string.Equals(targetId, Player.PlayerId, StringComparison.Ordinal);
        var targetNpc = isPlayer ? null : world.Npc(targetId);
        var inventory = isPlayer ? world.Player.Inventory : targetNpc?.Inventory;
        if (inventory is null)
        {
            return;
        }

        itemId ??= truth?.ItemId;
        string? found = null;
        if (itemId is not null && inventory.Contains(itemId, StringComparer.Ordinal))
        {
            found = itemId;
        }
        else if (itemId is null)
        {
            // что искать, не сказано: подозрительна чужая вещь
            found = inventory.FirstOrDefault(i =>
                world.Items.TryGetValue(i, out var def) && def.OwnerId is { } owner &&
                !string.Equals(owner, targetId, StringComparison.Ordinal));
        }

        var position = isPlayer ? world.Player.Position : targetNpc!.Position;
        var ev = Emit(world, "search", searcher.Id, targetId, found ?? itemId, position.X, position.Y,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["result"] = found is null ? "empty" : "found",
                ["reason"] = eventRef?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            });
        searcher.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, tick, searcher.Id);
        targetNpc?.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, tick, targetNpc.Id);
        if (eventRef is int searchedRef)
        {
            world.Game.Searches[(searchedRef, targetId)] = (tick, found is not null);
        }

        if (found is null)
        {
            if (eventRef is int refId && searcher.Knowledge.ByEventId(refId) is { } entry)
            {
                entry.C *= 0.4;
            }
            if (targetNpc is not null)
            {
                world.Trust.Adjust(targetNpc.Id, searcher.Id, -0.1);
            }
            else
            {
                world.Trust.Adjust(searcher.Id, Player.PlayerId, 0.05);
            }
            Say(searcher, tick, "Нічого немає. Вибач, помилився.");
            return;
        }

        if (isPlayer)
        {
            world.Player.RemoveItem(found);
        }
        else
        {
            targetNpc!.Inventory.Remove(found);
        }
        var owner = RightfulOwner(world, found, truth);
        if (owner is not null && !string.Equals(owner.Id, targetId, StringComparison.Ordinal))
        {
            owner.Inventory.Add(found);
            owner.Knowledge.AddFirstHand(ev, Source.Heard, 0.9, false, tick, owner.Id);
            world.Trust.Adjust(owner.Id, targetId, -0.3);
        }
        else
        {
            searcher.Inventory.Add(found);
        }
        world.Trust.Adjust(searcher.Id, targetId, -0.3);
        Say(searcher, tick, "Ось воно, «" + world.ItemName(found) + "»! Так і знав. Кличу варту.");
        CallGuard(world, searcher, targetId, ev);
    }

    /// <summary>
    /// Вартовому донесли: он не просто запоминает, а идёт обыскивать названного виновника,
    /// а если виновник неизвестен — осматривать место события.
    /// </summary>
    public static void GuardFollowUp(World world, Npc guard, int? eventRef)
    {
        if (eventRef is not int id || guard.CurrentAction is not null || guard.IsAsleep || guard.IsKnockedOut(world.Tick))
        {
            return;
        }
        var entry = guard.Knowledge.ByEventId(id);
        if (entry is null)
        {
            return;
        }
        var suspect = entry.ActorId;
        var item = entry.Field("item");
        if (!string.IsNullOrWhiteSpace(suspect) && !string.Equals(suspect, guard.Id, StringComparison.Ordinal) &&
            (string.Equals(suspect, Player.PlayerId, StringComparison.Ordinal) || world.Npc(suspect) is not null))
        {
            if (AlreadySearched(world, id, suspect))
            {
                return;
            }
            Say(guard, world.Tick, "Добре. Піду обшукаю " + world.NpcName(suspect) + ".");
            StartAction(world, guard, "search", suspect, id, item, "Обшукати");
            return;
        }
        var zone = entry.Field("zone");
        if (!string.IsNullOrWhiteSpace(zone))
        {
            Say(guard, world.Tick, "Добре. Піду подивлюся на місці.");
            StartAction(world, guard, "investigate", null, id, item, "Перевірити", zone);
        }
    }

    /// <summary>Пойманного забирает варта: сам обыскивавший, если он вартовий, иначе ближайший вартовий.</summary>
    private static void CallGuard(World world, Npc caller, string culpritId, WorldEvent evidence)
    {
        var guard = caller.Role == Role.Guard
            ? caller
            : world.ActiveNpcs()
                .Where(n => n.Role == Role.Guard && !n.IsKnockedOut(world.Tick) && !string.Equals(n.Id, culpritId, StringComparison.Ordinal))
                .OrderBy(n => Math.Abs(n.X - caller.X) + Math.Abs(n.Y - caller.Y))
                .FirstOrDefault();
        if (guard is null)
        {
            return;
        }
        if (!ReferenceEquals(guard, caller))
        {
            guard.Knowledge.AddFirstHand(evidence, Source.Heard, 0.9, false, world.Tick, guard.Id);
            Say(guard, world.Tick, "Іду. Розберемося з " + world.NpcName(culpritId) + ".");
        }
        StartAction(world, guard, "arrest", culpritId, evidence.Id, null, "Затримати");
    }

    /// <summary>Чья вещь: потерпевший в краже, иначе владелец из описания предмета.</summary>
    private static Npc? RightfulOwner(World world, string itemId, WorldEvent? theft)
    {
        if (theft is { Type: "theft", TargetId: { } victim } && world.Npc(victim) is Npc robbed)
        {
            return robbed;
        }
        return world.Items.TryGetValue(itemId, out var def) && def.OwnerId is { } owner ? world.Npc(owner) : null;
    }

    /// <summary>Назначить NPC действие без модели (следствие уже случившегося события).</summary>
    private static ActionInstance StartAction(World world, Npc npc, string actionId, string? targetId, int? eventRef, string? itemId,
        string nameUk, string? zoneId = null)
    {
        Decisions.IActionHandler handler = actionId switch
        {
            "search" => new Decisions.Handlers.SearchActionHandler(),
            "arrest" => new Decisions.Handlers.ArrestActionHandler(),
            "investigate" => new Decisions.Handlers.InvestigateActionHandler(),
            "approach_player" => new Decisions.Handlers.ApproachPlayerActionHandler(),
            _ => new Decisions.Handlers.IgnoreActionHandler()
        };
        var action = new ActionInstance
        {
            ActionId = actionId,
            NameUk = nameUk,
            TargetId = targetId,
            EventRef = eventRef,
            ItemId = itemId,
            ZoneId = zoneId,
            StartTick = world.Tick,
            Handler = handler
        };
        npc.CurrentAction = action;
        npc.LastActionStartTick = world.Tick;
        npc.ClearPath();
        npc.RememberAction(world.Tick, actionId, eventRef);
        npc.Activity = Activity.React;
        handler.Start(new Decisions.ActionContext(world, npc, action));
        if (action.Done)
        {
            npc.CurrentAction = null;
        }
        return action;
    }

    private static void Say(Npc npc, int tick, string text)
    {
        npc.LastSay = UkGrammar.For(npc, text);
        npc.LastSayTick = tick;
    }

    // ---------------------------------------------------------------- жители сами идут к игроку

    private const int ApproachGapTicks = 60;
    private const int ApproachCooldownTicks = 6 * 60;
    private const double ApproachMaxDistance = 35;

    /// <summary>
    /// Раз в 10 минут: есть ли у кого-то повод самому подойти к игроку. Поводы по важности:
    /// думает, что игрок у него украл; хочет дать работу; услышал о игроке плітку; давно не виделись с другом.
    /// Не чаще одного прихода в час и одного прихода одного жителя за 6 часов.
    /// </summary>
    private static void UpdateApproaches(World world)
    {
        var game = world.Game;
        var tick = world.Tick;
        var player = world.Player;
        if (!game.PlayerActive || game.IncomingTalks.Count > 0 || tick - game.LastAnyApproachTick < ApproachGapTicks ||
            player.IsBusy(tick) || player.IsBanished(tick) || IsCurfew(world))
        {
            return;
        }

        (Npc Npc, string Key, string Reason, string Opening)? best = null;
        var bestPriority = int.MaxValue;
        foreach (var npc in world.ActiveNpcs())
        {
            if (npc.CurrentAction is not null || npc.IsAsleep || npc.IsKnockedOut(tick) || npc.IsInConversation ||
                npc.RefuseTalkUntilTick > tick ||
                Pathfinding.Euclidean(npc.Position, player.Position) > ApproachMaxDistance ||
                game.LastApproach.TryGetValue(npc.Id, out var last) && tick - last < ApproachCooldownTicks)
            {
                continue;
            }
            var motive = MotiveOf(world, npc);
            if (motive is null || game.ApproachReasonsUsed.Contains(motive.Value.Key) || motive.Value.Priority >= bestPriority)
            {
                continue;
            }
            bestPriority = motive.Value.Priority;
            best = (npc, motive.Value.Key, motive.Value.Reason, motive.Value.Opening);
        }

        if (best is not { } chosen)
        {
            return;
        }
        game.ApproachReasonsUsed.Add(chosen.Key);
        if (chosen.Key.EndsWith(":meet", StringComparison.Ordinal))
        {
            game.LastMeetTick = tick;
        }
        var action = StartAction(world, chosen.Npc, "approach_player", Player.PlayerId, null, null, "Підійти до гравця");
        action.Reason = chosen.Reason;
        action.Say = UkGrammar.For(chosen.Npc, chosen.Opening);
    }

    /// <summary>Сюжет посылает жителя к игроку с конкретной фразой (без ограничений на частоту).</summary>
    public static void SendToPlayer(World world, Npc npc, string opening, string reason)
    {
        if (!world.Game.PlayerActive)
        {
            return;
        }
        var action = StartAction(world, npc, "approach_player", Player.PlayerId, null, null, "Підійти до гравця");
        action.Reason = reason;
        action.Say = UkGrammar.For(npc, opening);
    }

    private static (int Priority, string Key, string Reason, string Opening)? MotiveOf(World world, Npc npc)
    {
        var tick = world.Tick;
        var name = world.Player.Name;
        var trust = world.Trust.Get(npc.Id, Player.PlayerId);

        // думает, что игрок украл именно у него
        foreach (var entry in npc.Knowledge.Entries)
        {
            if (entry.Type == "theft" && entry.C >= 0.5 &&
                string.Equals(entry.ActorId, Player.PlayerId, StringComparison.Ordinal) &&
                string.Equals(entry.Field("target"), npc.Id, StringComparison.Ordinal))
            {
                var item = entry.Field("item");
                if (item is not null && npc.Inventory.Contains(item, StringComparer.Ordinal))
                {
                    continue;
                }
                var itemName = item is null ? "моє" : "«" + world.ItemName(item) + "»";
                return (1, npc.Id + ":theft:" + entry.PromptRef,
                    "ти думаєш, що " + name + " вкрав у тебе " + itemName + "; хочеш повернути своє",
                    "Стій, " + name + "! Де " + itemName + "?");
            }
        }

        // хочет дать работу
        var quest = world.Quests.QuestOfGiver(npc.Id);
        if (quest is not null && quest.State == Quests.QuestState.NotTaken && trust >= 0.25)
        {
            return (2, npc.Id + ":quest:" + quest.Id,
                "хочеш запропонувати гравцю роботу: «" + quest.TitleUk + "»",
                name + ", маєш хвилинку? Є для тебе робота.");
        }

        // услышал о игроке свежую плітку
        foreach (var entry in npc.Knowledge.Entries)
        {
            if (Sources.IsFirstHand(entry.Source) || tick - entry.LearnedTick > 180 ||
                !(string.Equals(entry.ActorId, Player.PlayerId, StringComparison.Ordinal) ||
                  string.Equals(entry.Field("target"), Player.PlayerId, StringComparison.Ordinal)))
            {
                continue;
            }
            var what = world.Schema.Get(entry.Type).NameUk.ToLowerInvariant();
            return (3, npc.Id + ":rumor:" + entry.PromptRef,
                "ти почув плітку про гравця (" + what + ") і хочеш розпитати, чи це правда",
                "Чув я тут про тебе дещо, " + name + "...");
        }

        // давний знакомый, хорошо относится
        if (trust >= 0.75)
        {
            var day = world.Clock.Day;
            return (4, npc.Id + ":hello:" + day,
                "ти добре ставишся до гравця і просто хочеш привітатися й поговорити",
                "О, " + name + "! Давно тебе не бачив.");
        }

        // балакучий житель хочет познакомиться с новым человеком
        if (npc.Talkativeness >= 0.6 && trust >= 0.25 && !world.Game.Acquainted.Contains(npc.Id) &&
            tick - world.Game.LastMeetTick >= 180)
        {
            return (5, npc.Id + ":meet",
                "ти балакучий і хочеш познайомитися з новою людиною в селі, розпитати, хто такий і що тут робить",
                "Доброго дня! Щось я тебе раніше не бачив. Ти хто будеш?");
        }
        return null;
    }

    // ---------------------------------------------------------------- сон и кровати

    /// <summary>
    /// Раздаёт кровати: в каждом доме по одной на жителя, у стен и подальше от дверей и проходов,
    /// чтобы спящие не перекрывали дорогу. Игроку — кровать в его доме.
    /// </summary>
    public static void AssignBeds(World world)
    {
        var game = world.Game;
        game.Beds.Clear();
        game.PlayerBed = null;

        var residents = world.Npcs
            .Where(n => !string.IsNullOrWhiteSpace(n.HomeZone))
            .GroupBy(n => n.HomeZone, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var playerHome = world.Player.HomeZone;
        if (!string.IsNullOrWhiteSpace(playerHome) && !residents.ContainsKey(playerHome))
        {
            residents[playerHome] = new List<Npc>();
        }

        foreach (var (zoneId, people) in residents)
        {
            var needed = people.Count + (string.Equals(zoneId, playerHome, StringComparison.Ordinal) ? 1 : 0);
            var beds = PickBedTiles(world, zoneId, needed);
            var index = 0;
            if (string.Equals(zoneId, playerHome, StringComparison.Ordinal) && beds.Count > 0)
            {
                game.PlayerBed = beds[index++];
            }
            foreach (var npc in people)
            {
                if (index >= beds.Count)
                {
                    break;
                }
                game.Beds[npc.Id] = beds[index++];
            }
        }
    }

    private static List<(int X, int Y)> PickBedTiles(World world, string zoneId, int count)
    {
        var map = world.Map;
        var tiles = map.WalkableTilesIn(zoneId);
        var scored = new List<((int X, int Y) Tile, int Score)>();
        foreach (var tile in tiles)
        {
            var nearExit = false;
            var walls = 0;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }
                    var x = tile.X + dx;
                    var y = tile.Y + dy;
                    var kind = map.TileAt(x, y);
                    if (kind is TileKind.DoorOpen or TileKind.DoorClosed)
                    {
                        nearExit = true;
                    }
                    else if (map.IsWalkable(x, y) && !string.Equals(world.ZoneOfTile(x, y), zoneId, StringComparison.Ordinal))
                    {
                        nearExit = true;
                    }
                    else if (!map.IsWalkable(x, y))
                    {
                        walls++;
                    }
                }
            }
            if (map.TileAt(tile.X, tile.Y) is TileKind.DoorOpen or TileKind.DoorClosed)
            {
                continue;
            }
            scored.Add((tile, (nearExit ? -100 : 0) + walls * 10));
        }

        // кровати не вплотную друг к другу, чтобы между ними можно было пройти
        var result = new List<(int X, int Y)>();
        foreach (var candidate in scored.OrderByDescending(s => s.Score).ThenBy(s => s.Tile.Y).ThenBy(s => s.Tile.X))
        {
            if (result.Count >= count)
            {
                break;
            }
            if (result.Any(b => Math.Abs(b.X - candidate.Tile.X) <= 1 && Math.Abs(b.Y - candidate.Tile.Y) <= 1))
            {
                continue;
            }
            result.Add(candidate.Tile);
        }
        // в тесном доме кладём кровати и рядом
        foreach (var candidate in scored.OrderByDescending(s => s.Score))
        {
            if (result.Count >= count)
            {
                break;
            }
            if (!result.Contains(candidate.Tile))
            {
                result.Add(candidate.Tile);
            }
        }
        return result;
    }

    public sealed record SleepResult(bool Started, string Message);

    /// <summary>
    /// Игрок ложится спать: ночью — до 6:00, днём — на два часа. Если есть свой дом, спать можно только там
    /// (или в обезьяннике). Пока спит, время идёт быстрее; удар будит.
    /// </summary>
    public static SleepResult PlayerSleep(World world)
    {
        var player = world.Player;
        var tick = world.Tick;
        if (player.IsAsleep(tick))
        {
            return new SleepResult(false, "Ви вже спите.");
        }
        if (player.IsKnockedOut(tick))
        {
            return new SleepResult(false, "Ви без тями.");
        }
        var jailed = player.IsJailed(tick);
        if (!jailed && !string.IsNullOrWhiteSpace(player.HomeZone) && !IsAtHome(world))
        {
            return new SleepResult(false, "Спати можна вдома: " + world.ZoneName(player.HomeZone) + ".");
        }

        var minute = world.Clock.MinuteOfDay;
        int duration;
        if (minute >= 20 * 60)
        {
            duration = 24 * 60 - minute + 6 * 60;
        }
        else if (minute < 6 * 60)
        {
            duration = 6 * 60 - minute;
        }
        else
        {
            duration = 120;
        }

        if (!jailed && world.Game.PlayerBed is { } bed && IsAtHome(world))
        {
            player.Teleport(bed.X, bed.Y);
        }
        player.WorkUntilTick = -1;
        player.SleepUntilTick = tick + duration;
        return new SleepResult(true, duration > 120 ? "Ви лягли спати до ранку." : "Ви прилягли на дві години.");
    }

    /// <summary>Сон восстанавливает здоровье быстрее, чем бодрствование.</summary>
    private static void UpdatePlayerSleep(World world)
    {
        var player = world.Player;
        if (!player.IsAsleep(world.Tick))
        {
            return;
        }
        if (world.Tick % 60 == 30 && player.Hp < player.MaxHp)
        {
            player.Hp++;
        }
    }

    public static bool IsAtHome(World world)
    {
        var player = world.Player;
        return !string.IsNullOrWhiteSpace(player.HomeZone) &&
               string.Equals(world.ZoneOfTile(player.X, player.Y), player.HomeZone, StringComparison.Ordinal);
    }

    public static bool IsCurfew(World world)
    {
        var minute = world.Clock.MinuteOfDay;
        return minute >= GameRules.CurfewFromMinute || minute < GameRules.CurfewToMinute;
    }

    private static void ExpireOffers(World world)
    {
        var game = world.Game;
        foreach (var key in game.Demands.Where(p => world.Tick - p.Value.Tick > GameRules.DemandTtlTicks).Select(p => p.Key).ToList())
        {
            game.Demands.Remove(key);
        }
        foreach (var key in game.Trades.Where(p => world.Tick - p.Value.Tick > GameRules.TradeTtlTicks).Select(p => p.Key).ToList())
        {
            game.Trades.Remove(key);
        }
        if (game.Payments.Count > 200)
        {
            game.Payments.RemoveRange(0, game.Payments.Count - 200);
        }
    }

    private static void UpdateKnockouts(World world)
    {
        foreach (var npc in world.Npcs)
        {
            if (npc.KnockedOutUntilTick == world.Tick && npc.Hp <= 0)
            {
                npc.Hp = 1;
            }
        }
        var player = world.Player;
        if (player.KnockedOutUntilTick == world.Tick && player.Hp <= 0)
        {
            player.Hp = 1;
        }
    }

    private static void DiscoverPlantedItems(World world)
    {
        if (world.GroundItems.Count == 0)
        {
            return;
        }

        foreach (var ground in world.GroundItems.ToList())
        {
            if (ground.PlantedBy is null || world.Tick - ground.PlacedTick < 5)
            {
                continue;
            }

            Npc? finder = null;
            foreach (var npc in world.ActiveNpcs())
            {
                if (npc.IsAsleep || npc.IsKnockedOut(world.Tick) ||
                    string.Equals(npc.Id, ground.PlantedBy, StringComparison.Ordinal))
                {
                    continue;
                }
                if (Math.Abs(npc.X - ground.X) <= 2 && Math.Abs(npc.Y - ground.Y) <= 2)
                {
                    finder = npc;
                    break;
                }
            }
            if (finder is null)
            {
                continue;
            }

            // в чьём доме нашли: подставленный, если это его дом, иначе любой другой жилец
            var residents = world.Npcs.Where(n => string.Equals(n.HomeZone, ground.ZoneId, StringComparison.Ordinal)).ToList();
            var owner = residents.FirstOrDefault(n => string.Equals(n.Id, world.Game.FramedNpcId, StringComparison.Ordinal))
                        ?? residents.FirstOrDefault(n => !ReferenceEquals(n, finder))
                        ?? residents.FirstOrDefault();

            world.GroundItems.Remove(ground);
            finder.Inventory.Add(ground.ItemId);
            var found = Emit(world, "item_found", finder.Id, owner?.Id, ground.ItemId, ground.X, ground.Y,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["place"] = world.ZoneName(ground.ZoneId) });
            finder.Knowledge.AddFirstHand(found, Source.Witnessed, 1.0, false, world.Tick, finder.Id);
            world.Game.ItemsFound++;
        }
    }

    private static void UpdatePlayerSanctions(World world)
    {
        var player = world.Player;
        if (!player.IsBanished(world.Tick) || player.IsJailed(world.Tick))
        {
            return;
        }
        var zone = world.ZoneOfTile(player.X, player.Y);
        if (zone is "road" or OutskirtsZone)
        {
            return;
        }

        // выгнанного, который вернулся в село, вартовий отводит в обезьянник
        foreach (var guard in world.ActiveNpcs())
        {
            if (guard.Role != Role.Guard || guard.IsAsleep || guard.IsKnockedOut(world.Tick))
            {
                continue;
            }
            if (Pathfinding.Euclidean(guard.Position, player.Position) > 5)
            {
                continue;
            }
            JailPlayer(world, guard.Id);
            var ev = Emit(world, "arrest", guard.Id, Player.PlayerId, null, player.X, player.Y,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = "повернувся після вигнання" });
            guard.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, guard.Id);
            return;
        }
    }

    private static void DecaySecrets(World world)
    {
        foreach (var npc in world.Npcs)
        {
            if (npc.Secrets.Count == 0)
            {
                continue;
            }
            foreach (var key in npc.Secrets.Keys.ToList())
            {
                npc.Secrets[key] = Math.Max(0, npc.Secrets[key] - GameRules.SecretDecayPerHour);
            }
        }
    }

    private static void Regenerate(World world)
    {
        foreach (var npc in world.Npcs)
        {
            if (npc.Hp > 0 && npc.Hp < npc.MaxHp && !npc.IsKnockedOut(world.Tick))
            {
                npc.Hp++;
            }
        }
        var player = world.Player;
        var starving = world.Game.Life && player.Hunger >= GameRules.StarvingHunger;
        if (!starving && player.Hp > 0 && player.Hp < player.MaxHp && !player.IsKnockedOut(world.Tick))
        {
            player.Hp++;
        }
    }

    private static void UpdateHunger(World world)
    {
        var player = world.Player;
        player.Hunger = Math.Min(100, player.Hunger + GameRules.HungerPerHour);
        if (player.Hunger >= GameRules.StarvingHunger && player.Hp > 0)
        {
            player.Hp--;
            if (player.Hp == 0)
            {
                player.KnockedOutUntilTick = world.Tick + GameRules.KnockoutTicks;
                Emit(world, "knockout", null, Player.PlayerId, null, player.X, player.Y,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = "голод" });
            }
        }
    }

    private static void UpdateWork(Simulation sim)
    {
        var world = sim.World;
        var player = world.Player;
        if (player.WorkUntilTick != world.Tick || player.WorkZone is null)
        {
            return;
        }

        var job = sim.Scenario.Jobs.FirstOrDefault(j => string.Equals(j.Zone, player.WorkZone, StringComparison.Ordinal));
        var pay = job?.Pay ?? GameRules.WorkPay;
        player.Coins += pay;
        if (world.Game.Life)
        {
            player.Hunger = Math.Min(100, player.Hunger + GameRules.WorkHunger);
        }
        var workedZone = player.WorkZone;
        player.WorkZone = null;
        var employer = world.Npc(job?.Employer);
        var ev = Emit(world, "work", Player.PlayerId, employer?.Id, null, player.X, player.Y,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["coins"] = pay.ToString(CultureInfo.InvariantCulture) });
        employer?.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, employer.Id);
        if (employer is not null)
        {
            world.Trust.Adjust(employer.Id, Player.PlayerId, 0.03);
        }

        // доручення «допомогти з роботою» засчитывается этой сменой
        var help = world.Quests.All.Values.FirstOrDefault(q =>
            q.Kind == Quests.QuestKind.Help && q.State == Quests.QuestState.Taken &&
            string.Equals(q.ZoneId, workedZone, StringComparison.Ordinal));
        if (help is not null && world.Quests.TryComplete(help.Id))
        {
            var giver = world.Npc(help.GiverId);
            var done = Emit(world, "quest_completed", Player.PlayerId, help.GiverId, null, player.X, player.Y,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["questId"] = help.Id });
            giver?.Knowledge.AddFirstHand(done, Source.Participant, 1.0, false, world.Tick, giver.Id);
            PayErrand(world, help);
        }
    }

    // ---------------------------------------------------------------- доручення села

    private const int MaxOpenErrands = 4;
    private const int ErrandLifetimeTicks = 24 * 60;

    private static readonly string[] FindItems = ["ring", "letter", "tools", "apple", "fish", "wine"];
    private static readonly string[] DeliverItems = ["letter", "pie", "bread"];

    /// <summary>
    /// Раз в игровой час (с 7 до 19) кто-то из жителей может попросить о помощи: найти потерянное,
    /// отнести посылку, помочь с работой. Задания видно на «Дошці оголошень»; можно взять там или у самого жителя.
    /// Не взятое за сутки — снимается.
    /// </summary>
    private static void UpdateErrands(Simulation sim)
    {
        var world = sim.World;
        var game = world.Game;
        var tick = world.Tick;
        if (!game.PlayerActive)
        {
            return;
        }

        foreach (var stale in world.Quests.All.Values
                     .Where(q => q.AutoReward && q.State == Quests.QuestState.NotTaken && tick - q.CreatedTick > ErrandLifetimeTicks)
                     .ToList())
        {
            world.Quests.Remove(stale.Id);
            if (stale.Kind == Quests.QuestKind.Find)
            {
                world.GroundItems.RemoveAll(g => g.ErrandId == stale.Id);
            }
        }

        var hour = world.Clock.Hour;
        var open = world.Quests.All.Values.Count(q => q.AutoReward && q.State is Quests.QuestState.NotTaken or Quests.QuestState.Taken);
        if (hour < 7 || hour >= 19 || open >= MaxOpenErrands || !world.Rng.Chance(0.45, world.Rng.Sim))
        {
            return;
        }

        var givers = world.ActiveNpcs()
            .Where(n => n.Role != Role.Guard && !n.IsAsleep && !n.IsKnockedOut(tick) &&
                        !world.Quests.All.Values.Any(q => q.GiverId == n.Id && q.State is Quests.QuestState.NotTaken or Quests.QuestState.Taken))
            .ToList();
        if (givers.Count == 0)
        {
            return;
        }
        var giver = givers[world.Rng.NextInt(0, givers.Count, world.Rng.Sim)];
        var kinds = new List<Quests.QuestKind> { Quests.QuestKind.Find, Quests.QuestKind.Deliver };
        if (sim.Scenario.Jobs.Any(j => string.Equals(j.Employer, giver.Id, StringComparison.Ordinal)))
        {
            kinds.Add(Quests.QuestKind.Help);
            kinds.Add(Quests.QuestKind.Help);
        }
        var kind = kinds[world.Rng.NextInt(0, kinds.Count, world.Rng.Sim)];
        var id = "errand_" + tick.ToString(CultureInfo.InvariantCulture);
        var quest = kind switch
        {
            Quests.QuestKind.Find => MakeFindErrand(world, giver, id),
            Quests.QuestKind.Deliver => MakeDeliverErrand(world, giver, id),
            _ => MakeHelpErrand(sim, giver, id)
        };
        if (quest is null)
        {
            return;
        }
        quest.AutoReward = true;
        quest.CreatedTick = tick;
        world.Quests.Add(quest);
    }

    private static bool ItemBusy(World world, string itemId) =>
        world.Quests.All.Values.Any(q => q.State is Quests.QuestState.NotTaken or Quests.QuestState.Taken &&
                                         string.Equals(q.RequiredItem, itemId, StringComparison.Ordinal));

    private static Quests.Quest? MakeFindErrand(World world, Npc giver, string id)
    {
        var items = FindItems.Where(i => world.Items.ContainsKey(i) && !ItemBusy(world, i)).ToList();
        var zones = world.Map.Zones
            .Where(z => !z.Id.StartsWith("house", StringComparison.Ordinal) && z.Id is not ("guard_tower" or "player_house" or "headman_house") &&
                        world.Map.ZoneHasWalkableTile(z.Id))
            .ToList();
        if (items.Count == 0 || zones.Count == 0)
        {
            return null;
        }
        var item = items[world.Rng.NextInt(0, items.Count, world.Rng.Sim)];
        var zone = zones[world.Rng.NextInt(0, zones.Count, world.Rng.Sim)];
        var tile = world.Map.RandomTileIn(zone.Id, world.Rng.Sim);
        world.GroundItems.Add(new GroundItem(item, tile.X, tile.Y, zone.Id) { ErrandId = id, PlacedTick = world.Tick });
        var reward = world.Rng.NextInt(3, 7, world.Rng.Sim);
        return new Quests.Quest(id, giver.Id, "Знайти " + world.ItemName(item).ToLowerInvariant(), item, reward, Quests.QuestState.NotTaken)
        {
            Kind = Quests.QuestKind.Find,
            ZoneId = zone.Id,
            HintUk = UkGrammar.G(giver, "Загубив", "Загубила") + ": " + giver.Name + ". Шукайте тут: " + zone.NameUk + ". Знайшли — F, потім віддайте власнику."
        };
    }

    private static Quests.Quest? MakeDeliverErrand(World world, Npc giver, string id)
    {
        var items = DeliverItems.Where(i => world.Items.ContainsKey(i) && !ItemBusy(world, i)).ToList();
        var recipients = world.ActiveNpcs().Where(n => !ReferenceEquals(n, giver) && n.Role != Role.Guard).ToList();
        if (items.Count == 0 || recipients.Count == 0)
        {
            return null;
        }
        var item = items[world.Rng.NextInt(0, items.Count, world.Rng.Sim)];
        var recipient = recipients[world.Rng.NextInt(0, recipients.Count, world.Rng.Sim)];
        var reward = world.Rng.NextInt(2, 5, world.Rng.Sim);
        return new Quests.Quest(id, giver.Id, "Віднести " + world.ItemName(item).ToLowerInvariant() + " для " + recipient.Name,
            item, reward, Quests.QuestState.NotTaken)
        {
            Kind = Quests.QuestKind.Deliver,
            RecipientId = recipient.Id,
            HintUk = "Посилка «" + world.ItemName(item) + "» від: " + giver.Name + ". Кому: " + recipient.Name +
                     " (зазвичай буває: " + world.ZoneName(recipient.WorkZone) + "). Посилку дадуть, щойно візьметеся."
        };
    }

    private static Quests.Quest? MakeHelpErrand(Simulation sim, Npc giver, string id)
    {
        var job = sim.Scenario.Jobs.FirstOrDefault(j => string.Equals(j.Employer, giver.Id, StringComparison.Ordinal));
        if (job is null)
        {
            return null;
        }
        var reward = sim.World.Rng.NextInt(2, 5, sim.World.Rng.Sim);
        return new Quests.Quest(id, giver.Id, job.TitleUk, string.Empty, reward, Quests.QuestState.NotTaken)
        {
            Kind = Quests.QuestKind.Help,
            ZoneId = job.Zone,
            HintUk = "Прийдіть туди: " + sim.World.ZoneName(job.Zone) + " і натисніть J (7:00–19:00). Крім плати за роботу — ще " + reward + " мон. зверху."
        };
    }

    /// <summary>Игрок берёт задание (с дошки или от жителя): событие, а для посылки — сама посылка в руки.</summary>
    public static void OnQuestTaken(World world, Quests.Quest quest, Npc? giver = null)
    {
        giver ??= world.Npc(quest.GiverId);
        var position = giver?.Position ?? world.Player.Position;
        var ev = Emit(world, "quest_given", quest.GiverId, Player.PlayerId, quest.Kind == Quests.QuestKind.Deliver ? quest.RequiredItem : null,
            position.X, position.Y, new Dictionary<string, string>(StringComparer.Ordinal) { ["questId"] = quest.Id });
        giver?.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, giver.Id);
        if (quest.Kind == Quests.QuestKind.Deliver)
        {
            world.Player.AddItem(quest.RequiredItem);
        }
    }

    /// <summary>Взять задание с дошки оголошень.</summary>
    public static bool PlayerTakeQuest(World world, string questId)
    {
        var quest = world.Quests.Get(questId);
        if (quest is null || !world.Quests.TryTake(questId))
        {
            return false;
        }
        OnQuestTaken(world, quest);
        return true;
    }

    /// <summary>Доручення выполнено: плата сразу, жители начинают относиться лучше.</summary>
    public static void PayErrand(World world, Quests.Quest quest)
    {
        if (!world.Quests.TryReward(quest.Id))
        {
            return;
        }
        var player = world.Player;
        player.Coins += quest.RewardCoins;
        var giver = world.Npc(quest.GiverId);
        var ev = Emit(world, "reward_given", quest.GiverId, Player.PlayerId, null, player.X, player.Y,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["questId"] = quest.Id,
                ["coins"] = quest.RewardCoins.ToString(CultureInfo.InvariantCulture)
            });
        giver?.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, world.Tick, giver.Id);
        world.Trust.Adjust(quest.GiverId, Player.PlayerId, 0.12);
        if (quest.RecipientId is { } recipient)
        {
            world.Trust.Adjust(recipient, Player.PlayerId, 0.05);
        }
    }

    private static void UpdateCurfew(World world)
    {
        var player = world.Player;
        if (string.IsNullOrWhiteSpace(player.HomeZone) || !IsCurfew(world) || IsAtHome(world) ||
            player.IsJailed(world.Tick) || player.IsKnockedOut(world.Tick))
        {
            return;
        }
        var zone = world.ZoneOfTile(player.X, player.Y);
        if (zone == "tavern")
        {
            return;
        }

        // одна «ночь» — от 22:00 до 05:00; штраф не чаще раза за ночь
        var night = (world.Clock.AbsoluteMinute + (24 * 60 - GameRules.CurfewFromMinute)) / GameClock.TicksPerDay;
        if (night == world.Game.CurfewFinedNight)
        {
            return;
        }

        foreach (var guard in world.ActiveNpcs())
        {
            if (guard.Role != Role.Guard || guard.IsAsleep || guard.IsKnockedOut(world.Tick))
            {
                continue;
            }
            if (Pathfinding.Euclidean(guard.Position, player.Position) > world.Config.VisionRadius * world.Config.NightVisionFactor + 1)
            {
                continue;
            }

            world.Game.CurfewFinedNight = night;
            var fine = Math.Min(player.Coins, GameRules.CurfewFine);
            player.Coins -= fine;
            guard.Coins += fine;
            var ev = Emit(world, "curfew", Player.PlayerId, guard.Id, null, player.X, player.Y,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["fine"] = fine.ToString(CultureInfo.InvariantCulture) });
            guard.Knowledge.AddFirstHand(ev, Source.Witnessed, 1.0, false, world.Tick, guard.Id);
            return;
        }
    }
}

/// <summary>Описание подработки для интерфейса.</summary>
public sealed record JobSpecView(string TitleUk, int Pay, bool Started, string? Message);
