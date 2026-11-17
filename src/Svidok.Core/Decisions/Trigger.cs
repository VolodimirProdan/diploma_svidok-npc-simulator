namespace Svidok.Core.Decisions;

public enum Trigger
{
    ImportantKnowledge,
    PlayerDialog,
    ScenarioAsk,
    Manual
}

public static class Triggers
{
    public static string NameUk(Trigger trigger)
    {
        return trigger switch
        {
            Trigger.ImportantKnowledge => "важливе знання",
            Trigger.PlayerDialog => "діалог з гравцем",
            Trigger.ScenarioAsk => "питання сценарію",
            Trigger.Manual => "ручний виклик",
            _ => trigger.ToString()
        };
    }
}
