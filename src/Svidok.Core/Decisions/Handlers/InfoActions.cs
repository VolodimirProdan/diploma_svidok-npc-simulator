using Svidok.Core.Entities;

namespace Svidok.Core.Decisions.Handlers;

/// <summary>ignore — ничего не делает.</summary>
public sealed class IgnoreActionHandler : IActionHandler
{
    public string ActionId => "ignore";

    public void Start(ActionContext ctx)
    {
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>say — реплика в диалоге или над головой.</summary>
public sealed class SayActionHandler : IActionHandler
{
    private const int MaxSayLength = 200;

    public string ActionId => "say";

    public void Start(ActionContext ctx)
    {
        var text = ctx.Action.Say ?? string.Empty;
        if (text.Length > MaxSayLength)
        {
            text = text[..MaxSayLength];
        }
        ctx.Npc.LastSay = text;
        ctx.Npc.LastSayTick = ctx.Tick;
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>refuse — вежливый отказ.</summary>
public sealed class RefuseActionHandler : IActionHandler
{
    public string ActionId => "refuse";

    public void Start(ActionContext ctx)
    {
        var text = string.IsNullOrWhiteSpace(ctx.Action.Say) ? "Ні, не можу." : ctx.Action.Say!;
        if (text.Length > 200)
        {
            text = text[..200];
        }
        ctx.Npc.LastSay = text;
        ctx.Npc.LastSayTick = ctx.Tick;
    }

    public bool Tick(ActionContext ctx) => true;
}
