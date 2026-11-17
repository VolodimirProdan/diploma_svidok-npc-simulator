using System.Text.Json;
using Svidok.Core.Util;

namespace Svidok.Core.Scenarios;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ScenarioDefinition Load(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        var root = doc.RootElement;
        var scenario = new ScenarioDefinition
        {
            Id = GetString(root, "id") ?? Path.GetFileNameWithoutExtension(path),
            NameUk = GetString(root, "nameUk") ?? string.Empty,
            DescriptionUk = GetString(root, "descriptionUk") ?? string.Empty,
            WhatChecksUk = GetString(root, "whatChecksUk") ?? string.Empty,
            Population = GetString(root, "population") ?? "default",
            DurationTicks = GetInt(root, "durationTicks") ?? 1440,
            Big = GetBool(root, "big") ?? false,
            DailyRandomEvent = GetBool(root, "dailyRandomEvent") ?? false
        };

        if (TryGet(root, "modes", out var modes))
        {
            scenario.Modes = modes.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        }
        if (TryGet(root, "variants", out var variants))
        {
            scenario.Variants = variants.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        }
        if (TryGet(root, "recommendedVariants", out var recommended))
        {
            scenario.RecommendedVariants = recommended.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        }
        if (TryGet(root, "extraNpcs", out var extra))
        {
            scenario.ExtraNpcs = extra.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        }

        if (TryGet(root, "map", out var map))
        {
            if (map.ValueKind == JsonValueKind.String)
            {
                scenario.MapId = map.GetString() ?? "default";
            }
            else if (map.ValueKind == JsonValueKind.Array)
            {
                scenario.MapRows = map.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
            }
        }

        if (TryGet(root, "start", out var start))
        {
            scenario.Start = start.Deserialize<StartSpec>(Options) ?? new StartSpec();
        }
        if (TryGet(root, "player", out var player))
        {
            scenario.Player = player.Deserialize<PlayerSpec>(Options) ?? new PlayerSpec();
        }
        if (TryGet(root, "quests", out var quests))
        {
            scenario.Quests = quests.Deserialize<List<QuestSpec>>(Options) ?? new List<QuestSpec>();
        }
        if (TryGet(root, "items", out var items))
        {
            scenario.Items = items.Deserialize<List<ItemPlacement>>(Options) ?? new List<ItemPlacement>();
        }
        if (TryGet(root, "overrides", out var overrides))
        {
            scenario.Overrides = overrides.Clone();
        }
        if (TryGet(root, "script", out var script))
        {
            scenario.Script = script.Deserialize<List<ScriptedEvent>>(Options) ?? new List<ScriptedEvent>();
        }
        if (TryGet(root, "experiment", out var experiment))
        {
            scenario.Experiment = experiment.Deserialize<ExperimentSpec>(Options) ?? new ExperimentSpec();
        }
        if (TryGet(root, "features", out var features))
        {
            scenario.Features = features.Deserialize<FeatureSpec>(Options) ?? new FeatureSpec();
        }
        if (TryGet(root, "zones", out var zones))
        {
            scenario.Zones = zones.Deserialize<List<ZoneSpec>>(Options) ?? new List<ZoneSpec>();
        }
        if (TryGet(root, "stories", out var stories))
        {
            scenario.Stories = stories.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
        }
        if (TryGet(root, "jobs", out var jobs))
        {
            scenario.Jobs = jobs.Deserialize<List<JobSpec>>(Options) ?? new List<JobSpec>();
        }

        return scenario;
    }

    public static List<ScenarioDefinition> LoadAll()
    {
        var dir = PathResolver.ResolveExisting("data/scenarios");
        var result = new List<ScenarioDefinition>();
        if (!Directory.Exists(dir))
        {
            return result;
        }
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            result.Add(Load(file));
        }
        return result;
    }

    public static ScenarioDefinition? FindById(string id)
    {
        foreach (var scenario in LoadAll())
        {
            if (string.Equals(scenario.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scenario.Id, "s" + id.TrimStart('s', 'S'), StringComparison.OrdinalIgnoreCase))
            {
                return scenario;
            }
        }
        return null;
    }

    /// <summary>Карта сценария: "default" — data/maps/default.txt, иначе data/maps/&lt;mapId&gt;.txt.</summary>
    public static string[] LoadMap(string? mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId) || string.Equals(mapId, "default", StringComparison.OrdinalIgnoreCase))
        {
            return LoadDefaultMap();
        }
        var path = PathResolver.ResolveExisting("data/maps/" + mapId.Trim() + ".txt");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Не знайдено карту data/maps/" + mapId + ".txt", path);
        }
        return File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
    }

    public static string[] LoadDefaultMap()
    {
        var path = PathResolver.ResolveExisting("data/maps/default.txt");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Не знайдено карту data/maps/default.txt", path);
        }
        return File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool? GetBool(JsonElement root, string name) => TryGet(root, name, out var value)
        ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        }
        : null;
}
