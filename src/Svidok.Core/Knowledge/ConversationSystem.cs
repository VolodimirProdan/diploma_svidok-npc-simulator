using Svidok.Core.Config;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Perception;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Knowledge;

/// <summary>Разговоры и обмен слухами (раздел 8.6).</summary>
public sealed class ConversationSystem
{
    private readonly SimulationConfig _config;
    private readonly DistortionEngine _distortion;

    public ConversationSystem(SimulationConfig config, DistortionEngine distortion)
    {
        _config = config;
        _distortion = distortion;
    }

    public void Tick(World world)
    {
        EndFinishedConversations(world);

        foreach (var npc in world.ActiveNpcs())
        {
            if (npc.IsInConversation || npc.IsAsleep || npc.StunnedUntilTick > world.Tick)
            {
                continue;
            }

            foreach (var other in world.ActiveNpcs())
            {
                if (ReferenceEquals(npc, other) || other.IsInConversation || other.IsAsleep)
                {
                    continue;
                }
                if (string.CompareOrdinal(npc.Id, other.Id) >= 0)
                {
                    continue;
                }
                if (npc.ConversationCooldownUntil > world.Tick || other.ConversationCooldownUntil > world.Tick)
                {
                    continue;
                }
                if (Pathfinding.Euclidean(npc.Position, other.Position) > _config.TalkDistance)
                {
                    continue;
                }
                if (!world.Rng.Chance(_config.PTalkPerTick, world.Rng.Rumor))
                {
                    continue;
                }

                StartConversation(world, npc, other);
                break;
            }
        }
    }

    private void EndFinishedConversations(World world)
    {
        foreach (var npc in world.Npcs)
        {
            if (!npc.IsInConversation)
            {
                continue;
            }
            if (world.Tick < npc.ConversationEndTick)
            {
                npc.Activity = Activity.Talk;
                continue;
            }
            npc.IsInConversation = false;
            npc.ConversationPartnerId = null;
            npc.ConversationCooldownUntil = world.Tick + _config.TalkCooldownTicks;
        }
    }

    private void StartConversation(World world, Npc a, Npc b)
    {
        a.IsInConversation = true;
        a.ConversationPartnerId = b.Id;
        a.ConversationEndTick = world.Tick + _config.TalkDurationTicks;
        a.ConversationCooldownUntil = a.ConversationEndTick + _config.TalkCooldownTicks;
        a.Activity = Activity.Talk;
        a.ClearPath();

        b.IsInConversation = true;
        b.ConversationPartnerId = a.Id;
        b.ConversationEndTick = a.ConversationEndTick;
        b.ConversationCooldownUntil = a.ConversationEndTick + _config.TalkCooldownTicks;
        b.Activity = Activity.Talk;
        b.ClearPath();

        world.Log.Add(
            world.Tick,
            "talk",
            a.Id,
            b.Id,
            null,
            world.ZoneOfTile(a.X, a.Y),
            a.X,
            a.Y,
            world.Schema.Get("talk").Loudness,
            world.Schema.Get("talk").Importance,
            true);

        if (!world.RumorsEnabled)
        {
            return;
        }

        Share(world, a, b);
        Share(world, b, a);
    }

    private void Share(World world, Npc sender, Npc receiver)
    {
        var candidates = sender.Knowledge.Entries
            .Where(e => e.Importance >= 0.3)
            .Where(e => !e.SharedWith.Contains(receiver.Id))
            .Select(e => (Entry: e, Score: Score(e, world.Tick)))
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Entry.Key)
            .Take(_config.MaxSharedPerTalk)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var talkativeness = sender.EffectiveTalkativeness(_config.TalkativenessMultiplier);
        var overhearers = FindOverhearers(world, sender, receiver);

        foreach (var (entry, _) in candidates)
        {
            if (!world.Rng.Chance(talkativeness, world.Rng.Rumor))
            {
                continue;
            }

            // игровые сценарии: обещал молчать — не рассказывает, но слабое обещание иногда не держит
            if (entry.EventId is int secretId && sender.Secrets.TryGetValue(secretId, out var strength))
            {
                if (!world.Rng.Chance((1.0 - strength) * 0.5, world.Rng.Rumor))
                {
                    world.Game.SecretBlocked++;
                    continue;
                }
                world.Game.SecretLeaked++;
            }

            var retelling = _distortion.BuildRetelling(entry, sender, world, world.Rng.Rumor);
            Deliver(world, sender, receiver, retelling, Source.Rumor, 1.0);
            entry.SharedWith.Add(receiver.Id);

            foreach (var listener in overhearers)
            {
                Deliver(world, sender, listener, entry, Source.Overheard, _config.OverheardFactor);
            }
        }
    }

    /// <summary>Передача одной записи от NPC к NPC по правилу 8.2 (c_A = c_B × r(A→B)).</summary>
    public void Deliver(World world, Npc sender, Npc receiver, KnowledgeEntry entry, Source source, double factor)
    {
        var trust = world.Trust.Get(receiver.Id, sender.Id);
        var credibility = Credibility.Transfer(entry.C, trust) * factor;
        if (credibility < world.Config.CForget)
        {
            return;
        }

        var copy = entry.Clone();
        copy.C = Credibility.Clamp(credibility);
        copy.Source = source;
        copy.Chain = new List<string>(entry.Chain) { receiver.Id };
        copy.LearnedTick = world.Tick;
        copy.UpdatedTick = world.Tick;
        copy.SharedWith = new HashSet<string>(StringComparer.Ordinal);
        receiver.Knowledge.MergeIncoming(copy, world.Tick);
    }

    private List<Npc> FindOverhearers(World world, Npc speaker, Npc receiver)
    {
        var result = new List<Npc>();
        foreach (var listener in world.ActiveNpcs())
        {
            if (ReferenceEquals(listener, speaker) || ReferenceEquals(listener, receiver) || listener.IsAsleep)
            {
                continue;
            }
            var blockers = LineOfSight.CountBlockers(world.Map, speaker.Position, listener.Position);
            var radius = _config.HearingRadius * 0.6 * Math.Pow(0.5, blockers);
            if (Pathfinding.Euclidean(speaker.Position, listener.Position) <= radius)
            {
                result.Add(listener);
            }
        }
        return result;
    }

    private static double Score(KnowledgeEntry entry, int tick)
    {
        var age = Math.Max(0, tick - entry.UpdatedTick);
        return entry.Importance * entry.C * Math.Exp(-age / 720.0);
    }
}
