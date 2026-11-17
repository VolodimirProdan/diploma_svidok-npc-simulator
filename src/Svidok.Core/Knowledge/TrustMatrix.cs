using Svidok.Core.Config;
using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Knowledge;

/// <summary>Матрица доверия: r(A→B) несимметрично, 0.05..0.95 (раздел 8.5).</summary>
public sealed class TrustMatrix
{
    private const double MinTrust = 0.05;
    private const double MaxTrust = 0.95;

    private readonly Dictionary<string, Dictionary<string, double>> _map = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Dictionary<string, double>> Raw => _map;

    public double Get(string from, string to)
    {
        if (_map.TryGetValue(from, out var row) && row.TryGetValue(to, out var value))
        {
            return value;
        }
        return 0.45;
    }

    public void Set(string from, string to, double value)
    {
        if (!_map.TryGetValue(from, out var row))
        {
            row = new Dictionary<string, double>(StringComparer.Ordinal);
            _map[from] = row;
        }
        row[to] = Math.Clamp(value, MinTrust, MaxTrust);
    }

    public void Adjust(string from, string to, double delta)
    {
        Set(from, to, Get(from, to) + delta);
    }

    public IReadOnlyList<(string Id, double R)> TopTrusted(string from, int count, bool includePlayer = false)
    {
        return Sorted(from, includePlayer).Take(count).ToList();
    }

    public IReadOnlyList<(string Id, double R)> LeastTrusted(string from, int count, bool includePlayer = false)
    {
        return Sorted(from, includePlayer).Reverse().Take(count).ToList();
    }

    private IReadOnlyList<(string Id, double R)> Sorted(string from, bool includePlayer)
    {
        if (!_map.TryGetValue(from, out var row))
        {
            return Array.Empty<(string, double)>();
        }
        return row
            .Where(pair => includePlayer || !string.Equals(pair.Key, Player.PlayerId, StringComparison.Ordinal))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    /// <summary>Начальные значения: семья, друзья, коллеги, соседи, чужинец, староста, игрок.</summary>
    public void Build(
        IReadOnlyList<Npc> npcs,
        SimulationConfig config,
        IReadOnlyCollection<(string A, string B)> friends,
        double trustMultiplier,
        bool includePlayer)
    {
        var defaults = config.TrustDefaults;
        var friendSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (a, b) in friends)
        {
            friendSet.Add(Key(a, b));
            friendSet.Add(Key(b, a));
        }

        foreach (var a in npcs)
        {
            foreach (var b in npcs)
            {
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                double value;
                if (a.Role == Role.Traveler || b.Role == Role.Traveler)
                {
                    value = defaults.Stranger;
                }
                else if (friendSet.Contains(Key(a.Id, b.Id)))
                {
                    value = defaults.Friend;
                }
                else if (!string.IsNullOrEmpty(a.HomeZone) && string.Equals(a.HomeZone, b.HomeZone, StringComparison.Ordinal))
                {
                    value = defaults.Family;
                }
                else if (!string.IsNullOrEmpty(a.WorkZone) && string.Equals(a.WorkZone, b.WorkZone, StringComparison.Ordinal))
                {
                    value = defaults.Colleague;
                }
                else if (AreNeighbourZones(a.HomeZone, b.HomeZone))
                {
                    value = defaults.Neighbour;
                }
                else
                {
                    value = defaults.Default;
                }

                if (b.Role == Role.Headman)
                {
                    value += defaults.HeadmanBonus;
                }

                Set(a.Id, b.Id, value * trustMultiplier);
            }

            if (includePlayer)
            {
                Set(a.Id, Player.PlayerId, defaults.Player * trustMultiplier);
            }
        }

        if (includePlayer)
        {
            foreach (var npc in npcs)
            {
                Set(Player.PlayerId, npc.Id, defaults.Player * trustMultiplier);
            }
            Set(Player.PlayerId, Player.PlayerId, MinTrust);
        }
    }

    private static bool AreNeighbourZones(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }
        var zoneA = DefaultZones.ById(a);
        if (zoneA is not null && zoneA.Neighbours.Contains(b, StringComparer.Ordinal))
        {
            return true;
        }
        var zoneB = DefaultZones.ById(b);
        return zoneB is not null && zoneB.Neighbours.Contains(a, StringComparer.Ordinal);
    }

    private static string Key(string a, string b) => a + "->" + b;
}
