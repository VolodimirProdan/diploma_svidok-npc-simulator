using Svidok.Core.Decisions.Handlers;
using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

/// <summary>Исполнение действий: каждое действие — отдельный IActionHandler.</summary>
public sealed class ActionExecutor
{
    private const int MaxSayLength = 200;
    private const int MaxReasonLength = 150;

    private readonly World _world;
    private readonly ActionCatalog _catalog;
    private readonly Dictionary<string, IActionHandler> _handlers = new(StringComparer.Ordinal);

    public ActionExecutor(World world, ActionCatalog catalog)
    {
        _world = world;
        _catalog = catalog;
        Register(new IgnoreActionHandler());
        Register(new SayActionHandler());
        Register(new RefuseActionHandler());
        Register(new GoToActionHandler());
        Register(new InvestigateActionHandler());
        Register(new HideValuablesActionHandler());
        Register(new FleeActionHandler());
        Register(new HelpFireActionHandler());
        Register(new CloseShopActionHandler());
        Register(new WarnActionHandler());
        Register(new ReportToGuardActionHandler());
        Register(new ShoutAlarmActionHandler());
        Register(new ArrestActionHandler());
        Register(new GiveItemActionHandler());
        Register(new GiveRewardActionHandler());
        Register(new EndTalkActionHandler());
        Register(new DemandPaymentActionHandler());
        Register(new KeepSecretActionHandler());
        Register(new TreatActionHandler());
        Register(new GiveQuestActionHandler());
        Register(new AcceptTradeActionHandler());
        Register(new BanishActionHandler());
        Register(new FineActionHandler());
        Register(new AttackActionHandler());
        Register(new SearchActionHandler());
        Register(new ApproachPlayerActionHandler());
    }

    public ActionInstance Execute(Npc npc, LlmDecision decision)
    {
        var def = _catalog.Get(decision.Action);
        var action = new ActionInstance
        {
            ActionId = def?.Action ?? "ignore",
            NameUk = def?.NameUk ?? decision.Action,
            TargetId = decision.Target,
            ZoneId = decision.Zone,
            ItemId = decision.Item,
            QuestId = decision.QuestId,
            Say = Truncate(decision.Say, MaxSayLength),
            EventRef = decision.EventRef,
            Amount = decision.Amount,
            Reason = Truncate(decision.Reason, MaxReasonLength),
            StartTick = _world.Tick
        };

        npc.CurrentAction = action;
        npc.LastActionStartTick = _world.Tick;
        npc.ClearPath();
        npc.RememberAction(_world.Tick, action.ActionId, action.EventRef);

        if (action.ActionId is not ("ignore" or "say" or "refuse" or "end_talk" or "demand_payment" or "keep_secret" or
            "treat" or "give_quest" or "accept_trade" or "fine"))
        {
            npc.Activity = Activity.React;
        }

        if (_handlers.TryGetValue(action.ActionId, out var handler))
        {
            action.Handler = handler;
            handler.Start(new ActionContext(_world, npc, action));
        }
        else
        {
            action.Done = true;
        }

        if (action.Done && ReferenceEquals(npc.CurrentAction, action))
        {
            npc.CurrentAction = null;
        }
        return action;
    }

    public void Tick(Npc npc)
    {
        var action = npc.CurrentAction;
        if (action is null)
        {
            return;
        }
        if (action.Handler is null)
        {
            npc.CurrentAction = null;
            return;
        }

        var done = action.Handler.Tick(new ActionContext(_world, npc, action));
        // обработчик может сам назначить следующее действие (проверка -> обыск), его не трогаем
        if ((done || action.Done) && ReferenceEquals(npc.CurrentAction, action))
        {
            npc.CurrentAction = null;
        }
    }

    public void TickAll()
    {
        foreach (var npc in _world.Npcs)
        {
            if (npc.CurrentAction is not null)
            {
                Tick(npc);
            }
        }
    }

    private void Register(IActionHandler handler) => _handlers[handler.ActionId] = handler;

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return text.Length <= max ? text : text[..max];
    }
}
