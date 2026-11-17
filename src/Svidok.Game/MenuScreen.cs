using System.Numerics;
using ImGuiNET;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Scenarios;
using Svidok.Llm;

namespace Svidok.Game;

public sealed record MenuResult(ScenarioDefinition Scenario, Variant Variant, int Seed, bool PlayerMode,
    PlayerProfile? Profile = null);

/// <summary>Главное меню: карточки сценариев, режим, вариант, seed, режим модели (раздел 11.1).</summary>
public sealed class MenuScreen
{
    private static readonly string[] ModelModes = ["Live", "LiveWithCache", "ReplayOnly", "Mock"];

    private readonly List<ScenarioDefinition> _scenarios = new();
    private bool _loaded;
    private int _selected;
    private int _modeIndex;
    private int _variantIndex;
    private string _seedText = "1";
    private int _modelModeIndex = 1;
    private string _apiKeyInput = string.Empty;

    private bool _rememberKey = true;

    private enum ProfileAction
    {
        None,
        New,
        Rename,
        Delete,
        Reset
    }

    private List<PlayerProfile> _profiles = new();
    private int _profileIndex;
    private ProfileAction _profileAction;
    private string _profileName = string.Empty;

    public MenuResult? Draw(GameApp app)
    {
        if (!_loaded)
        {
            _scenarios.AddRange(ScenarioLoader.LoadAll());
            _profiles = PlayerProfiles.LoadAll();
            if (_profiles.Count > 0)
            {
                _seedText = _profiles[0].LastSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            _profileAction = _profiles.Count == 0 ? ProfileAction.New : ProfileAction.None;
            _loaded = true;
            var configured = Array.FindIndex(ModelModes,
                m => string.Equals(m, app.Config.Llm.Mode, StringComparison.OrdinalIgnoreCase));
            _modelModeIndex = configured >= 0 ? configured : 1;
            var sandbox = _scenarios.FindIndex(sc => string.Equals(sc.Id, "s6", StringComparison.OrdinalIgnoreCase));
            SelectScenario(sandbox >= 0 ? sandbox : 0);
        }

        MenuResult? result = null;
        var width = Raylib_cs.Raylib.GetScreenWidth();
        var height = Raylib_cs.Raylib.GetScreenHeight();

        ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24, 20));
        if (ImGui.Begin("##menu", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                    ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                    ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.TextUnformatted("СВІДОК");
            ImGui.SameLine();
            ImGui.TextDisabled(UiText.AppSubtitle);
            ImGui.Separator();

            if (_scenarios.Count == 0)
            {
                ImGui.TextUnformatted("Не знайдено жодного сценарію в data/scenarios");
                ImGui.End();
                ImGui.PopStyleVar();
                return null;
            }

            _selected = Math.Clamp(_selected, 0, _scenarios.Count - 1);
            var leftWidth = Math.Min(420, width * 0.42f);
            var listHeight = height - 110;

            if (ImGui.BeginChild("##list", new Vector2(leftWidth, listHeight), ImGuiChildFlags.Borders))
            {
                ImGui.TextUnformatted(UiText.ChooseScenario);
                ImGui.Separator();
                for (var i = 0; i < _scenarios.Count; i++)
                {
                    var scenario = _scenarios[i];
                    var label = scenario.Id.ToUpperInvariant() + " · " + scenario.NameUk;
                    if (ImGui.Selectable(label + "##sc" + i, i == _selected, ImGuiSelectableFlags.None,
                            new Vector2(0, 34)))
                    {
                        SelectScenario(i);
                    }
                }
                ImGui.Spacing();
                ImGui.PushTextWrapPos(0);
                ImGui.TextDisabled("Щоб самому поговорити з персонажами і перевірити метод, оберіть S6 · Пісочниця, " +
                                   "режим «Гравець» і варіант V4 з увімкненим ШІ.");
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();

            ImGui.SameLine();

            if (ImGui.BeginChild("##details", new Vector2(0, listHeight), ImGuiChildFlags.Borders))
            {
                ImGui.PushTextWrapPos(0);
                var scenario = _scenarios[_selected];
                ImGui.TextUnformatted(scenario.NameUk);
                ImGui.TextWrapped(UiText.ScenarioPurpose(scenario.Id, scenario.DescriptionUk));
                ImGui.TextDisabled(scenario.WhatChecksUk);
                ImGui.Separator();

                ImGui.TextUnformatted(UiText.Mode);
                var supportsPlayer = scenario.SupportsMode("Player");
                if (!supportsPlayer)
                {
                    _modeIndex = 0;
                }
                ImGui.BeginDisabled(!supportsPlayer);
                if (ImGui.RadioButton(UiText.PlayerMode, _modeIndex == 1))
                {
                    _modeIndex = 1;
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.RadioButton(UiText.ObserverMode, _modeIndex == 0))
                {
                    _modeIndex = 0;
                }
                ImGui.TextDisabled(_modeIndex == 1
                    ? "Ви ходите селом, говорите з людьми, берете завдання, крадете."
                    : supportsPlayer
                        ? "Ви дивитесь збоку: що бачить, чує і знає кожен персонаж."
                        : "Ви дивитесь збоку: що бачить, чує і знає кожен персонаж. Режим гравця тут не передбачено.");

                if (_modeIndex == 1)
                {
                    ImGui.Spacing();
                    DrawProfiles(scenario);
                }

                ImGui.Spacing();
                var variants = VariantsOf(scenario);
                var labels = variants.Select(UiText.VariantLabel).ToArray();
                _variantIndex = Math.Clamp(_variantIndex, 0, variants.Length - 1);
                ImGui.SetNextItemWidth(330);
                ImGui.Combo(UiText.Variant, ref _variantIndex, labels, labels.Length);
                if (scenario.RecommendedVariants.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(UiText.Recommended + ": " + string.Join(", ", scenario.RecommendedVariants));
                }
                ImGui.TextDisabled(UiText.VariantHint(variants[_variantIndex]));

                ImGui.Spacing();
                ImGui.Separator();
                DrawAiBlock(app);
                ImGui.Separator();
                ImGui.Spacing();

                var variant = Variants.Parse(variants[_variantIndex]);
                var mode = ModelModes[_modelModeIndex];
                var needsNetwork = mode is "Live" or "LiveWithCache";
                var blocked = variant != Variant.V0 && needsNetwork && !LlmClientFactory.HasApiKey(app.Config.Llm);

                ImGui.BeginDisabled(blocked);
                if (ImGui.Button(UiText.Start, new Vector2(200, 40)))
                {
                    var seed = int.TryParse(_seedText, out var parsed) ? parsed : 1;
                    app.Config.Llm.Mode = mode;
                    PlayerProfile? profile = null;
                    if (_modeIndex == 1)
                    {
                        profile = CurrentProfile() ?? CreateProfile(string.Empty);
                        // продолжение сохранения — в том же мире, что и было
                        if (profile.Saves.TryGetValue(scenario.Id, out var continued))
                        {
                            seed = continued.Seed;
                        }
                        profile.LastSeed = seed;
                        _seedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        PlayerProfiles.Save(profile);
                    }
                    result = new MenuResult(scenario, variant, seed, _modeIndex == 1, profile);
                }
                ImGui.EndDisabled();
                if (blocked)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.40f, 1f), UiText.AiNeedKey);
                }

                ImGui.Spacing();
                if (ImGui.CollapsingHeader(UiText.ForExperiments))
                {
                    ImGui.SetNextItemWidth(200);
                    ImGui.InputText(UiText.Seed, ref _seedText, 16);
                    var modeLabels = ModelModes.Select(UiText.ModelModeLabel).ToArray();
                    ImGui.SetNextItemWidth(420);
                    ImGui.Combo(UiText.ModelMode, ref _modelModeIndex, modeLabels, modeLabels.Length);
                }
                ImGui.PopTextWrapPos();
            }
            ImGui.EndChild();
        }
        ImGui.End();
        ImGui.PopStyleVar();

        return result;
    }

    private PlayerProfile? CurrentProfile() =>
        _profiles.Count == 0 ? null : _profiles[Math.Clamp(_profileIndex, 0, _profiles.Count - 1)];

    private PlayerProfile CreateProfile(string name)
    {
        var profile = PlayerProfiles.Create(name);
        _profiles.Insert(0, profile);
        _profileIndex = 0;
        return profile;
    }

    /// <summary>Профиль игрока: имя, под которым его знают персонажи, и их память о нём.</summary>
    private void DrawProfiles(ScenarioDefinition scenario)
    {
        ImGui.TextUnformatted("Профіль гравця");
        var current = CurrentProfile();

        if (current is not null && _profileAction is ProfileAction.None or ProfileAction.Delete or ProfileAction.Reset)
        {
            var names = _profiles.Select(p => p.Name).ToArray();
            _profileIndex = Math.Clamp(_profileIndex, 0, names.Length - 1);
            ImGui.SetNextItemWidth(220);
            if (ImGui.Combo("##profile", ref _profileIndex, names, names.Length) && CurrentProfile() is { } picked)
            {
                _seedText = picked.LastSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            current = CurrentProfile()!;
            ImGui.SameLine();
            if (ImGui.Button("Новий"))
            {
                _profileAction = ProfileAction.New;
                _profileName = string.Empty;
            }
            ImGui.SameLine();
            if (ImGui.Button("Перейменувати"))
            {
                _profileAction = ProfileAction.Rename;
                _profileName = current.Name;
            }
            ImGui.SameLine();
            if (ImGui.Button("Забути все"))
            {
                _profileAction = ProfileAction.Reset;
            }
            ImGui.SameLine();
            if (ImGui.Button("Видалити"))
            {
                _profileAction = ProfileAction.Delete;
            }

            var key = PlayerProfile.MemoryKey(scenario);
            var known = current.Scenarios.TryGetValue(key, out var memory) ||
                        current.Scenarios.TryGetValue(scenario.Id, out memory)
                ? memory.Npcs.Count(n => n.Value.Talks > 0)
                : 0;
            ImGui.TextDisabled("Персонажі знають вас як «" + current.Name + "». Знайомих тут: " + known + ".");
            if (current.Saves.TryGetValue(scenario.Id, out var save))
            {
                ImGui.TextUnformatted("Збереження від " + save.Saved.ToString("dd.MM HH:mm") + ": " + save.Coins +
                                      " мон., речей " + save.Inventory.Count + ", сід " + save.Seed + ". Гра продовжиться звідти.");
                ImGui.SameLine();
                if (ImGui.SmallButton("Почати з нуля"))
                {
                    current.Saves.Remove(scenario.Id);
                    PlayerProfiles.Save(current);
                }
                ImGui.SetItemTooltip("Місце й речі скинуться. Те, як до вас ставляться жителі, залишиться.");
            }
        }

        switch (_profileAction)
        {
            case ProfileAction.New:
            case ProfileAction.Rename:
            {
                var creating = _profileAction == ProfileAction.New;
                ImGui.SetNextItemWidth(220);
                var submit = ImGui.InputTextWithHint("##profilename", "Ваше ім'я в селі", ref _profileName,
                    PlayerProfiles.MaxNameLength, ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                submit |= ImGui.Button(creating ? "Створити" : "Зберегти");
                if (submit)
                {
                    if (creating)
                    {
                        CreateProfile(_profileName);
                    }
                    else if (current is not null)
                    {
                        PlayerProfiles.Rename(current, _profileName);
                    }
                    _profileAction = ProfileAction.None;
                }
                if (_profiles.Count > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Скасувати"))
                    {
                        _profileAction = ProfileAction.None;
                    }
                }
                break;
            }
            case ProfileAction.Delete when current is not null:
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.40f, 1f), "Видалити профіль «" + current.Name + "» разом з усією пам'яттю?");
                if (ImGui.Button("Так, видалити"))
                {
                    PlayerProfiles.Delete(current);
                    _profiles.Remove(current);
                    _profileIndex = 0;
                    _profileAction = _profiles.Count == 0 ? ProfileAction.New : ProfileAction.None;
                }
                ImGui.SameLine();
                if (ImGui.Button("Ні##delete"))
                {
                    _profileAction = ProfileAction.None;
                }
                break;
            case ProfileAction.Reset when current is not null:
                ImGui.TextColored(new Vector4(0.95f, 0.80f, 0.35f, 1f), "Усі персонажі в усіх сценаріях забудуть «" + current.Name + "». Продовжити?");
                if (ImGui.Button("Так, забути"))
                {
                    PlayerProfiles.ResetMemory(current);
                    _profileAction = ProfileAction.None;
                }
                ImGui.SameLine();
                if (ImGui.Button("Ні##reset"))
                {
                    _profileAction = ProfileAction.None;
                }
                break;
        }
    }

    private void DrawAiBlock(GameApp app)
    {
        var env = app.Config.Llm.ApiKeyEnv;
        var hasKey = LlmClientFactory.HasApiKey(app.Config.Llm);
        ImGui.TextUnformatted(UiText.AiTitle);
        ImGui.TextColored(hasKey ? new Vector4(0.35f, 0.85f, 0.45f, 1f) : new Vector4(0.95f, 0.45f, 0.40f, 1f),
            hasKey ? UiText.AiOn : UiText.AiOff);

        if (hasKey)
        {
            if (ImGui.Button(UiText.AiDisable, new Vector2(160, 26)))
            {
                Environment.SetEnvironmentVariable(env, null);
                ApiKeyStore.Delete();
            }
            ImGui.SetItemTooltip("Вимкне ШІ і зітре збережений ключ з цього комп'ютера.");
            ImGui.SameLine();
            ImGui.TextDisabled(ApiKeyStore.HasSaved ? "Ключ збережено на цьому комп'ютері." : "Ключ діє до закриття програми.");
            return;
        }

        ImGui.SetNextItemWidth(330);
        var submit = ImGui.InputTextWithHint("##apikey", UiText.AiKeyHint, ref _apiKeyInput, 200,
            ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        submit |= ImGui.Button(UiText.AiEnable, new Vector2(150, 0));
        if (submit && !string.IsNullOrWhiteSpace(_apiKeyInput))
        {
            Environment.SetEnvironmentVariable(env, _apiKeyInput.Trim());
            if (_rememberKey)
            {
                ApiKeyStore.Save(_apiKeyInput);
            }
            _apiKeyInput = string.Empty;
        }
        ImGui.Checkbox("Запам'ятати на цьому комп'ютері", ref _rememberKey);
        ImGui.SetItemTooltip("Ключ зберігається зашифрованим у вашому профілі Windows, не в папці гри.");
    }

    private void SelectScenario(int index)
    {
        _selected = Math.Clamp(index, 0, Math.Max(0, _scenarios.Count - 1));
        if (_scenarios.Count == 0)
        {
            return;
        }

        var scenario = _scenarios[_selected];
        _modeIndex = scenario.SupportsMode("Player") ? 1 : 0;

        var variants = VariantsOf(scenario);
        var recommended = scenario.RecommendedVariants.Count > 0 ? scenario.RecommendedVariants[^1] : "V4";
        var index4 = Array.FindIndex(variants, v => string.Equals(v, recommended, StringComparison.OrdinalIgnoreCase));
        _variantIndex = index4 >= 0 ? index4 : 0;
    }

    private static string[] VariantsOf(ScenarioDefinition scenario) =>
        scenario.Variants.Count > 0
            ? scenario.Variants.ToArray()
            : new[] { "V0", "V1", "V2", "V3", "V4" };
}
