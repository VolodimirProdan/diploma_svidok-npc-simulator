using System.Globalization;
using System.Text;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Llm;

/// <summary>Сборка промпта V1–V4 из шаблонов data/prompts (раздел 9.5).</summary>
public sealed class PromptBuilder
{
    private readonly Variant _variant;
    private readonly ActionCatalog _catalog;
    private readonly EventSchema _schema;
    private readonly SimulationConfig _config;
    private readonly string _systemTemplate;
    private readonly string _schemaText;
    private readonly string _npcCardTemplate;
    private readonly string _knowledgeHeader;
    private readonly string _journalHeader;
    private readonly string _situationTemplate;

    private PromptBuilder(Variant variant, ActionCatalog catalog, EventSchema schema, SimulationConfig config, string promptsDir)
    {
        _variant = variant;
        _catalog = catalog;
        _schema = schema;
        _config = config;
        _systemTemplate = File.ReadAllText(Path.Combine(promptsDir, "system.txt"));
        _schemaText = File.ReadAllText(Path.Combine(promptsDir, "schema.txt")).Trim();
        _npcCardTemplate = File.ReadAllText(Path.Combine(promptsDir, "npc_card.txt"));
        _knowledgeHeader = File.ReadAllText(Path.Combine(promptsDir, "knowledge.txt"));
        _journalHeader = File.ReadAllText(Path.Combine(promptsDir, "journal.txt"));
        _situationTemplate = File.ReadAllText(Path.Combine(promptsDir, "situation.txt"));
    }

    public static PromptBuilder Load(Variant variant, ActionCatalog catalog, EventSchema schema, SimulationConfig config)
    {
        var dir = PathResolver.ResolveExisting("data/prompts");
        return new PromptBuilder(variant, catalog, schema, config, dir);
    }

    /// <summary>
    /// Игровой сценарий (S6–S8): в промпт добавляются игровые действия, события и поле amount.
    /// В экспериментах S1–S5 выключено, и промпт остаётся прежним.
    /// </summary>
    public bool GameMode { get; set; }

    /// <summary>Счётчик токенов (ставится извне, чтобы Core не зависел от Llm).</summary>
    public Func<string, int>? Counter { get; set; }

    /// <summary>Ограничение промпта V1 в токенах (из конфига llm.maxPromptTokensV1).</summary>
    public int MaxPromptTokensV1 { get; set; } = 900000;

    /// <summary>Число токенов полного журнала до обрезки (V1).</summary>
    public int LastWouldBeTokens { get; private set; }

    public PreparedPrompt Build(DecisionContext ctx)
    {
        return Build(ctx, Math.Max(1, _config.MaxKnowledgeInPrompt), MaxPromptTokensV1);
    }

    public PreparedPrompt Build(DecisionContext ctx, int maxKnowledge, int maxPromptTokensV1)
    {
        var system = BuildSystem(ctx.World);
        var user = BuildUser(ctx, maxKnowledge, maxPromptTokensV1);
        var local = Count(system + "\n" + user);
        return new PreparedPrompt(system, user, local);
    }

    /// <summary>Промпт V4 для одного NPC без отправки (метрики S4).</summary>
    public string BuildForNpc(World world, Npc npc)
    {
        var ctx = new DecisionContext(world, npc, Trigger.Manual, null, Array.Empty<DialogLine>())
        {
            Situation = "Звичайний день у селищі."
        };
        var prepared = Build(ctx);
        return prepared.System + "\n" + prepared.User;
    }

    private string BuildSystem(World world)
    {
        var text = _systemTemplate
            .Replace("{{ACTION_LIST}}", _catalog.ActionListForPrompt(GameMode), StringComparison.Ordinal)
            .Replace("{{ZONE_LIST}}", BuildZoneList(world), StringComparison.Ordinal)
            .Replace("{{NPC_LIST}}", BuildNpcList(world), StringComparison.Ordinal)
            .Replace("{{ITEM_LIST}}", BuildItemList(world), StringComparison.Ordinal)
            .Replace("{{THRESHOLDS}}", _catalog.ThresholdsForPrompt(GameMode), StringComparison.Ordinal)
            .Replace("{{SCHEMA}}", _schemaText, StringComparison.Ordinal);

        text = ApplyConditional(text, "V3V4", Variants.UsesCredibility(_variant));
        text = ApplyConditional(text, "V4", _variant == Variant.V4);
        text = ApplyConditional(text, "V1", _variant == Variant.V1);
        if (GameMode)
        {
            text = text.Trim() + "\n" + GameRulesText;
        }
        return text.Trim();
    }

    private string GameRulesText =>
        "Ігрові дії: " + _catalog.GameActionsForPrompt() + ".\n" +
        "Ігрові події: give_coins=Дав монети; threat=Погроза; item_found=Знайшли предмет (target — господар хати, де знайшли); " +
        "banish=Вигнання; fine=Штраф; curfew=Порушення комендантської години; trade=Обмін; knockout=Знепритомнів; " +
        "search=Обшук (result=found — знайшли вкрадене, empty — нічого); recovered=Знайшли пропажу на місці.\n" +
        "Підозрюєш когось у крадіжці — обери search з target (обшукати) або investigate з eventRef (перевірити місце): " +
        "після перевірки ти справді щось знайдеш або ні, і за знахідкою прийде варта.\n" +
        "Якщо в say обіцяєш щось зробити (перевірити, обшукати, покликати варту, вигнати, вдарити), обери цю дію в action, а не say.\n" +
        "Поле \"amount\" — число монет для demand_payment і fine, інакше null. Ціль-гравець має id player.\n" +
        "Кожна дія — справжній вчинок у світі. Описуй його у полі say від першої особи.";

    private static string BuildZoneList(World world) =>
        string.Join(", ", world.Map.Zones.Select(z => z.Id + " (" + z.NameUk + ")"));

    private static string BuildNpcList(World world) =>
        string.Join(", ", world.Npcs.Select(n => n.Id + " (" + n.Name + ")"));

    private static string BuildItemList(World world) =>
        string.Join(", ", world.Items.Values.Select(i => i.Id + " (" + i.NameUk + ")"));

    private static string ApplyConditional(string text, string tag, bool enabled)
    {
        var open = "{{#" + tag + "}}";
        var close = "{{/" + tag + "}}";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = text.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }
            var inner = text.Substring(start + open.Length, end - start - open.Length);
            text = text.Remove(start, end - start + close.Length).Insert(start, enabled ? inner : string.Empty);
            start = text.IndexOf(open, start, StringComparison.Ordinal);
        }
        return text;
    }

    private string BuildUser(DecisionContext ctx, int maxKnowledge, int maxPromptTokensV1)
    {
        var world = ctx.World;
        var npc = ctx.Npc;
        var sb = new StringBuilder();

        sb.AppendLine(BuildNpcCard(world, npc));
        sb.AppendLine();

        if (GameMode)
        {
            sb.AppendLine(npc.IsFemale
                ? "Ти жінка: про себе говори в жіночому роді (я бачила, я чула, я пішла)."
                : "Ти чоловік: про себе говори в чоловічому роді (я бачив, я чув, я пішов).");
            sb.AppendLine(npc.Family.Count == 0
                ? "Родичів у селі в тебе немає. Не вигадуй родини."
                : "Твоя родина в селі: " + string.Join(", ", npc.Family.Select(f => world.NpcName(f.Key) + " — " + f.Value)) +
                  ". Інших родичів у селі в тебе немає, не вигадуй їх.");
        }
        if (GameMode && world.Game.HeadmanId is { } headman)
        {
            sb.Append("Староста села зараз: ").Append(headman == Player.PlayerId ? world.Player.Name + " (гравець)" : world.NpcName(headman))
                .AppendLine(".");
        }
        if (GameMode && !string.IsNullOrWhiteSpace(world.Game.StoryContext))
        {
            sb.Append("Що зараз відбувається в селі: ").AppendLine(world.Game.StoryContext);
            sb.AppendLine();
        }

        if (_variant == Variant.V1)
        {
            sb.AppendLine(BuildJournal(world, maxPromptTokensV1));
        }
        else
        {
            sb.AppendLine(BuildKnowledge(world, npc, maxKnowledge));
        }
        sb.AppendLine();

        if (ctx.Interactive is { } interactive)
        {
            sb.AppendLine(BuildInteractive(ctx.Npc, interactive));
            sb.AppendLine();
        }

        sb.Append(BuildSituation(ctx));
        return sb.ToString();
    }

    /// <summary>
    /// Блок живого разговора в игре: имя игрока, память о нём, доверие, слухи о нём, манера речи
    /// и дополнительные поля ответа. В пакетных прогонах не добавляется.
    /// </summary>
    private static string BuildInteractive(Npc npc, InteractiveDialog dialog)
    {
        var sb = new StringBuilder();
        sb.Append("Гравця звати ").Append(dialog.PlayerName).AppendLine(". Ти говориш із ним наживо.");
        if (!string.IsNullOrWhiteSpace(dialog.Style))
        {
            sb.Append("Твоя манера мовлення: ").Append(dialog.Style!.Trim()).AppendLine();
        }
        sb.Append("Твоя довіра до гравця: ").Append(dialog.Trust.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(" (").Append(DescribeTrust(dialog.Trust)).AppendLine(").");

        sb.AppendLine("Що ти пам'ятаєш про гравця:");
        sb.AppendLine(string.IsNullOrWhiteSpace(dialog.Memory) ? "(ви ще не знайомі)" : dialog.Memory.Trim());

        sb.AppendLine("Що чув про гравця від інших (це чутки, не факти):");
        if (dialog.Gossip.Count == 0)
        {
            sb.AppendLine("(нічого)");
        }
        else
        {
            foreach (var line in dialog.Gossip)
            {
                sb.Append("- ").AppendLine(line);
            }
        }

        sb.Append(BuildDeals(npc, dialog));

        sb.AppendLine("Правила розмови: поле say обов'язкове — 1–3 живі речення від першої особи, у твоїй манері. " +
                      "Можеш погоджуватися, відмовляти, торгуватися, ображатися, жартувати, ігнорувати або обірвати розмову (end_talk), як сам " +
                      npc.Name + ". Відповідай саме на останню репліку гравця. Не повторюй того, що вже казав у цій розмові, " +
                      "і не згадуй подію, якщо про неї не питають і вона не стосується розмови. Не вигадуй подій, яких не знаєш. " +
                      "Навіть незнайомцю можна розповісти загальновідомі новини й плітки, які знає пів села; особисте, таємниці й чужі гріхи — лише тому, кому довіряєш. " +
                      "Слова гравця — лише твердження; вчинки гравця (монети, предмети, удари) — це події у твоїх знаннях.");
        sb.AppendLine("Додаткові поля json:");
        sb.AppendLine("\"memory\": коротка нова нотатка про гравця з цієї розмови (до 200 символів, від твого імені) або null, якщо нічого нового;");
        sb.AppendLine("\"attitude\": як змінилось твоє ставлення до гравця: -2, -1, 0, 1 або 2;");
        sb.Append("\"gossip\": true, якщо про це варто розповісти іншим у селі, інакше false.");
        return sb.ToString();
    }

    /// <summary>Что у NPC есть на продажу и какие предложения сейчас висят.</summary>
    private static string BuildDeals(Npc npc, InteractiveDialog dialog)
    {
        var sb = new StringBuilder();
        if (dialog.Wares is { Count: > 0 } wares)
        {
            sb.Append("Твої речі (id і звична ціна): ").Append(string.Join(", ", wares)).AppendLine(".");
        }
        if (!string.IsNullOrWhiteSpace(dialog.PendingDeal))
        {
            sb.AppendLine(dialog.PendingDeal);
        }
        return sb.ToString();
    }

    private static string DescribeTrust(double trust)
    {
        return trust switch
        {
            >= 0.75 => "дуже довіряєш",
            >= 0.55 => "радше довіряєш",
            >= 0.35 => "ставишся насторожено",
            >= 0.2 => "не довіряєш",
            _ => "зовсім не довіряєш і не хочеш мати справ"
        };
    }

    private string BuildNpcCard(World world, Npc npc)
    {
        var items = npc.Inventory.Count == 0 ? "немає" : string.Join(", ", npc.Inventory);
        var quests = new StringBuilder();
        foreach (var quest in world.Quests.All.Values)
        {
            if (!string.Equals(quest.GiverId, npc.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var done = _variant == Variant.V1
                ? world.Log.HasQuestCompleted(quest.Id, Player.PlayerId)
                : npc.Knowledge.Entries.Any(e =>
                    string.Equals(e.Type, "quest_completed", StringComparison.Ordinal) &&
                    string.Equals(e.Field("questId"), quest.Id, StringComparison.Ordinal));
            quests.Append("Завдання, які видав персонаж: ")
                .Append(quest.Id).Append(" \"").Append(quest.TitleUk).Append("\", нагорода ")
                .Append(quest.RewardCoins).Append(" монет, статус: ")
                .Append(done ? "виконано" : "не виконано").AppendLine(".");
        }

        return _npcCardTemplate
            .Replace("{{NAME}}", npc.Name, StringComparison.Ordinal)
            .Replace("{{ID}}", npc.Id, StringComparison.Ordinal)
            .Replace("{{ROLE}}", Roles.NameUk(npc.Role), StringComparison.Ordinal)
            .Replace("{{BIO}}", string.IsNullOrWhiteSpace(npc.Bio) ? "звичайний мешканець" : npc.Bio, StringComparison.Ordinal)
            .Replace("{{TIME}}", world.Clock.TimeString, StringComparison.Ordinal)
            .Replace("{{ZONE}}", world.ZoneName(world.ZoneOfTile(npc.X, npc.Y)), StringComparison.Ordinal)
            .Replace("{{ACTIVITY}}", Activities.NameUk(npc.Activity), StringComparison.Ordinal)
            .Replace("{{ITEMS}}", items, StringComparison.Ordinal)
            .Replace("{{COINS}}", npc.Coins.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{QUESTS}}", quests.ToString().TrimEnd(), StringComparison.Ordinal)
            .Trim();
    }

    private string BuildKnowledge(World world, Npc npc, int maxKnowledge)
    {
        var entries = npc.Knowledge.Entries
            .Where(e => !_schema.Get(e.Type).Background)
            .OrderByDescending(e => e.Importance * e.C)
            .ThenBy(e => e.PromptRef)
            .Take(Math.Max(1, maxKnowledge))
            .ToList();

        if (entries.Count == 0)
        {
            return "Що знає персонаж:\n(нічого важливого)";
        }

        var lines = new StringBuilder();
        foreach (var entry in entries)
        {
            lines.AppendLine(FormatKnowledgeLine(world, entry, npc));
        }
        return _knowledgeHeader.Replace("{{LINES}}", lines.ToString().TrimEnd(), StringComparison.Ordinal);
    }

    private string FormatKnowledgeLine(World world, KnowledgeEntry entry, Npc? owner = null)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(entry.PromptRef).Append(" | ").Append(_schema.Get(entry.Type).NameUk);
        AppendField(world, sb, "хто", entry.Field("actor"), true);
        AppendField(world, sb, "що", entry.Field("item"), false);
        AppendField(world, sb, "чиє", entry.Field("target"), true);
        AppendField(world, sb, "де", entry.Field("zone"), false);
        AppendField(world, sb, "ціна", entry.Field("price"), false);
        AppendField(world, sb, "монети", entry.Field("coins"), false);
        AppendField(world, sb, "текст", entry.Field("text"), false);
        sb.Append(" | коли=").Append(world.TimeOf(Math.Max(0, entry.EventId is int id && world.Log.ById(id) is { } truth ? truth.Tick : entry.LearnedTick)));

        if (Variants.UsesCredibility(_variant))
        {
            sb.Append(" | джерело=").Append(DescribeSource(world, entry));
            sb.Append(" | c=").Append(entry.C.ToString("0.00", CultureInfo.InvariantCulture));
        }
        if (GameMode && owner is not null && entry.EventId is int secretId && owner.Secrets.TryGetValue(secretId, out var promise))
        {
            sb.Append(" | ти обіцяв про це мовчати").Append(promise >= 0.3 ? string.Empty : " (обіцянка вже слабка)");
        }
        return sb.ToString();
    }

    private string BuildJournal(World world, int maxPromptTokensV1)
    {
        var events = world.Log.Events.ToList();
        var lines = events.Select(e => FormatJournalLine(world, e)).ToList();
        var full = _journalHeader.Replace("{{LINES}}", string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
        if (maxPromptTokensV1 >= int.MaxValue || _config.MaxKnowledgeInPrompt <= 0)
        {
            return full;
        }

        var tokens = Count(full);
        LastWouldBeTokens = tokens;
        if (tokens <= maxPromptTokensV1)
        {
            return full;
        }

        var drop = new HashSet<int>();
        var background = events.Where(e => e.IsBackground).Select(e => e.Id).ToList();
        var dropCount = Math.Max(1, background.Count / 20);
        var pointer = 0;
        while (tokens > maxPromptTokensV1 && pointer < background.Count)
        {
            for (var i = 0; i < dropCount && pointer < background.Count; i++, pointer++)
            {
                drop.Add(background[pointer]);
            }
            var kept = events.Where(e => !drop.Contains(e.Id)).Select(e => FormatJournalLine(world, e));
            full = _journalHeader.Replace("{{LINES}}", string.Join(Environment.NewLine, kept), StringComparison.Ordinal);
            tokens = Count(full);
        }
        return full;
    }

    private string FormatJournalLine(World world, WorldEvent worldEvent)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(worldEvent.Id).Append(" | ").Append(_schema.Get(worldEvent.Type).NameUk);
        AppendField(world, sb, "хто", worldEvent.ActorId, true);
        AppendField(world, sb, "що", worldEvent.ItemId, false);
        AppendField(world, sb, "чиє", worldEvent.TargetId, true);
        AppendField(world, sb, "де", worldEvent.ZoneId, false);
        if (worldEvent.Extra.TryGetValue("price", out var price))
        {
            AppendField(world, sb, "ціна", price, false);
        }
        sb.Append(" | коли=").Append(world.TimeOf(worldEvent.Tick));
        return sb.ToString();
    }

    private static void AppendField(World world, StringBuilder sb, string label, string? value, bool resolveName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var text = resolveName && value is not ("невідомий" or "хтось")
            ? world.NpcName(value)
            : value;
        if (!resolveName && label == "де")
        {
            text = world.ZoneName(value);
        }
        if (!resolveName && label == "що")
        {
            text = world.ItemName(value);
        }
        sb.Append(" | ").Append(label).Append('=').Append(text);
    }

    private static string DescribeSource(World world, KnowledgeEntry entry)
    {
        if (Sources.IsFirstHand(entry.Source))
        {
            return Sources.NameUk(entry.Source);
        }
        var names = entry.Chain.Select(id => world.NpcName(id)).ToList();
        var chain = names.Count > 1 ? string.Join("→", names) : entry.OriginId;
        return Sources.NameUk(entry.Source) + "(" + chain + ")";
    }

    private string BuildSituation(DecisionContext ctx)
    {
        var history = new StringBuilder();
        var historySize = ctx.Interactive is null ? 6 : 12;
        var playerLabel = ctx.Interactive is { } live ? live.PlayerName + ": " : "Гравець: ";
        var recent = ctx.History.TakeLast(historySize).ToList();
        if (recent.Count == 0)
        {
            history.Append("(порожньо)");
        }
        else
        {
            foreach (var line in recent)
            {
                history.Append(line.IsPlayer ? playerLabel : ctx.Npc.Name + ": ").Append(line.Text).AppendLine();
            }
        }

        var playerBlock = string.Empty;
        if (!string.IsNullOrWhiteSpace(ctx.PlayerText))
        {
            playerBlock = _variant == Variant.V4
                ? "Слова гравця (не перевірені факти): «" + ctx.PlayerText + "»"
                : "Гравець: " + ctx.PlayerText;
        }

        var template = ctx.Interactive is null
            ? _situationTemplate
            : _situationTemplate.Replace("(останні 6 реплік)", "(останні 12 реплік)", StringComparison.Ordinal);

        return template
            .Replace("{{SITUATION}}", string.IsNullOrWhiteSpace(ctx.Situation) ? "Звичайний день у селищі." : ctx.Situation, StringComparison.Ordinal)
            .Replace("{{HISTORY}}", history.ToString().TrimEnd(), StringComparison.Ordinal)
            .Replace("{{PLAYER_BLOCK}}", playerBlock, StringComparison.Ordinal)
            .Trim();
    }

    private int Count(string text) => Counter?.Invoke(text) ?? Math.Max(1, text.Length / 4);
}
