using System.Text.Json;
using Svidok.Core.Entities;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

public sealed class ActionDef
{
    public string Action { get; set; } = string.Empty;

    public string NameUk { get; set; } = string.Empty;

    public List<string> Params { get; set; } = new();

    /// <summary>Порог достоверности c_min для V3/V4.</summary>
    public double CMin { get; set; }

    /// <summary>all | guards | non_guards | traders | quest_giver | item_owner</summary>
    public string Who { get; set; } = "all";

    public string EffectUk { get; set; } = string.Empty;

    /// <summary>Длительность после прибытия в тиках.</summary>
    public int DurationTicks { get; set; }

    public bool RequiresEventRef { get; set; }

    public bool RequiresTarget { get; set; }

    public bool RequiresZone { get; set; }

    public bool RequiresItem { get; set; }

    public bool RequiresQuest { get; set; }

    public double? MaxTargetDistance { get; set; }

    /// <summary>Действие только для игровых сценариев (S6–S8): в промпты S1–S5 не попадает.</summary>
    public bool Game { get; set; }

    public bool DwellAtDestination => DurationTicks > 0;
}

/// <summary>Каталог действий из data/actions.json (раздел 9.3).</summary>
public sealed class ActionCatalog
{
    private readonly Dictionary<string, ActionDef> _byId = new(StringComparer.Ordinal);
    private readonly List<ActionDef> _ordered = new();

    public IReadOnlyList<ActionDef> All => _ordered;

    public static ActionCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        JsonElement array;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            array = doc.RootElement;
        }
        else if (doc.RootElement.TryGetProperty("actions", out var actions))
        {
            array = actions;
        }
        else
        {
            throw new InvalidDataException("actions.json має містити масив дій");
        }

        var catalog = new ActionCatalog();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var element in array.EnumerateArray())
        {
            var def = element.Deserialize<ActionDef>(options);
            if (def is null || string.IsNullOrWhiteSpace(def.Action))
            {
                continue;
            }
            catalog._byId[def.Action] = def;
            catalog._ordered.Add(def);
        }
        return catalog;
    }

    public static ActionCatalog LoadDefault() => Load(PathResolver.ResolveExisting("data/actions.json"));

    public ActionDef? Get(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return null;
        }
        return _byId.TryGetValue(action.Trim(), out var def) ? def : null;
    }

    public bool Contains(string? action) => Get(action) is not null;

    public string NameUk(string? action) => Get(action)?.NameUk ?? action ?? string.Empty;

    public double CMin(string? action) => Get(action)?.CMin ?? 0;

    public string ActionListForPrompt(bool includeGame = false)
    {
        return string.Join(", ", _ordered.Where(a => includeGame || !a.Game).Select(a => a.Action));
    }

    /// <summary>Пояснения к игровым действиям для промпта игровых сценариев.</summary>
    public string GameActionsForPrompt()
    {
        return string.Join("; ", _ordered.Where(a => a.Game).Select(a => a.Action + " — " + a.EffectUk));
    }

    public string ThresholdsForPrompt(bool includeGame = false)
    {
        var parts = _ordered
            .Where(a => includeGame || !a.Game)
            .Where(a => a.CMin > 0)
            .Select(a => a.Action + ":" + a.CMin.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture));
        return string.Join(", ", parts);
    }

    /// <summary>Правило 1 валидатора: действие разрешено роли NPC.</summary>
    public bool WhoAllows(ActionDef def, Npc npc, World world)
    {
        var quest = world.Quests.QuestOfGiver(npc.Id);
        return def.Who switch
        {
            "all" => true,
            "guards" => npc.Role == Role.Guard,
            "guards_headman" => npc.Role is Role.Guard or Role.Headman,
            "non_guards" => npc.Role != Role.Guard,
            "traders" => npc.Role == Role.Trader,
            "quest_giver" => quest is not null,
            "item_owner" => npc.Inventory.Count > 0,
            _ => true
        };
    }
}
