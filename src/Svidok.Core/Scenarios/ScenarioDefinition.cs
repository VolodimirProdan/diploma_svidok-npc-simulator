using System.Text.Json;

namespace Svidok.Core.Scenarios;

public sealed class ScriptedEvent
{
    /// <summary>Тик относительно старта сценария.</summary>
    public int Tick { get; set; }

    /// <summary>Тип: обычное событие мира или служебный (ask, attack_dialog, fast_forward, set_param).</summary>
    public string Type { get; set; } = string.Empty;

    public string? Actor { get; set; }

    public string? Target { get; set; }

    public string? Item { get; set; }

    public string? Zone { get; set; }

    public Dictionary<string, string>? Extra { get; set; }

    /// <summary>Свидетели, которым запись достаётся принудительно (фильтр восприятия для события не вызывается).</summary>
    public List<string>? ForcedWitnesses { get; set; }

    /// <summary>attack_dialog: файл с набором атак.</summary>
    public string? AttackSet { get; set; }

    public bool ResetBetween { get; set; }

    /// <summary>ask: кому задавать вопросы и какие.</summary>
    public List<string>? Npcs { get; set; }

    public List<string>? Questions { get; set; }

    /// <summary>fast_forward: сколько тиков прокрутить без модели.</summary>
    public int? Ticks { get; set; }

    /// <summary>set_param: имя параметра и значение.</summary>
    public string? Param { get; set; }

    public string? Value { get; set; }
}

public sealed class StartSpec
{
    public int Day { get; set; } = 1;

    public string Time { get; set; } = "06:00";

    public int MinuteOfDay
    {
        get
        {
            var parts = (Time ?? "06:00").Split(':');
            if (parts.Length != 2)
            {
                return 360;
            }
            return (int.TryParse(parts[0], out var h) ? h : 6) * 60 + (int.TryParse(parts[1], out var m) ? m : 0);
        }
    }
}

public sealed class PlayerSpec
{
    public int X { get; set; }

    public int Y { get; set; }

    public List<string> Inventory { get; set; } = new();

    public int Coins { get; set; }

    /// <summary>Свой дом игрока (зона карты), если есть.</summary>
    public string Home { get; set; } = string.Empty;
}

/// <summary>Игровые механики сценария. По умолчанию всё выключено (эксперименты S1–S5).</summary>
public sealed class FeatureSpec
{
    public bool Social { get; set; }

    public bool Combat { get; set; }

    public bool Life { get; set; }
}

/// <summary>Зона своей карты: прямоугольник в тайлах.</summary>
public sealed class ZoneSpec
{
    public string Id { get; set; } = string.Empty;

    public string NameUk { get; set; } = string.Empty;

    public int X0 { get; set; }

    public int Y0 { get; set; }

    public int X1 { get; set; }

    public int Y1 { get; set; }

    public List<string> Neighbours { get; set; } = new();
}

/// <summary>Подработка в S8: где работать и кто платит.</summary>
public sealed class JobSpec
{
    public string Zone { get; set; } = string.Empty;

    public string Employer { get; set; } = string.Empty;

    public string TitleUk { get; set; } = string.Empty;

    public int Pay { get; set; } = 2;
}

public sealed class QuestSpec
{
    public string Id { get; set; } = string.Empty;

    public string Giver { get; set; } = string.Empty;

    public string TitleUk { get; set; } = string.Empty;

    public string RequiredItem { get; set; } = string.Empty;

    public int Reward { get; set; }

    public string InitialState { get; set; } = "NotTaken";
}

public sealed class ItemPlacement
{
    public string Id { get; set; } = string.Empty;

    /// <summary>id NPC, id зоны или "ground".</summary>
    public string? Owner { get; set; }

    public string? Zone { get; set; }
}

public sealed class ExperimentSpec
{
    public List<int> Seeds { get; set; } = new();

    public int HonestRuns { get; set; }

    public List<string> Metrics { get; set; } = new();
}

/// <summary>Описание сценария из data/scenarios/*.json (раздел 17).</summary>
public sealed class ScenarioDefinition
{
    public string Id { get; set; } = string.Empty;

    public string NameUk { get; set; } = string.Empty;

    public string DescriptionUk { get; set; } = string.Empty;

    public string WhatChecksUk { get; set; } = string.Empty;

    public List<string> Modes { get; set; } = new();

    public List<string> Variants { get; set; } = new();

    public string MapId { get; set; } = "default";

    public List<string>? MapRows { get; set; }

    public string Population { get; set; } = "default";

    public List<string> ExtraNpcs { get; set; } = new();

    public StartSpec Start { get; set; } = new();

    public int DurationTicks { get; set; } = 1440;

    public PlayerSpec Player { get; set; } = new();

    public List<QuestSpec> Quests { get; set; } = new();

    public JsonElement Overrides { get; set; }

    public List<ScriptedEvent> Script { get; set; } = new();

    public ExperimentSpec Experiment { get; set; } = new();

    public List<ItemPlacement> Items { get; set; } = new();

    public List<string> RecommendedVariants { get; set; } = new();

    public FeatureSpec Features { get; set; } = new();

    /// <summary>Зоны своей карты (вместе с mapRows); пусто — зоны карты по умолчанию.</summary>
    public List<ZoneSpec> Zones { get; set; } = new();

    public List<JobSpec> Jobs { get; set; } = new();

    /// <summary>Сюжеты из data/stories (id файлов).</summary>
    public List<string> Stories { get; set; } = new();

    /// <summary>S2-big: сначала 2 игровых суток без модели.</summary>
    public bool Big { get; set; }

    /// <summary>S4: раз в день случайное важное событие.</summary>
    public bool DailyRandomEvent { get; set; }

    public bool SupportsMode(string mode) =>
        Modes.Count == 0 || Modes.Contains(mode, StringComparer.OrdinalIgnoreCase);

    public bool SupportsVariant(string variant) =>
        Variants.Count == 0 || Variants.Contains(variant, StringComparer.OrdinalIgnoreCase);

    public int StartAbsoluteMinute => Start.MinuteOfDay;
}
