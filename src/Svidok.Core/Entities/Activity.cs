namespace Svidok.Core.Entities;

public enum Activity
{
    Sleep,
    Work,
    Eat,
    Rest,
    Patrol,
    Trade,
    Walk,
    Talk,
    React
}

public static class Activities
{
    public static string NameUk(Activity activity)
    {
        return activity switch
        {
            Activity.Sleep => "Сон",
            Activity.Work => "Робота",
            Activity.Eat => "Їжа",
            Activity.Rest => "Відпочинок",
            Activity.Patrol => "Патруль",
            Activity.Trade => "Торгівля",
            Activity.Walk => "Прогулянка",
            Activity.Talk => "Розмова",
            Activity.React => "Реакція",
            _ => activity.ToString()
        };
    }

    public static Activity Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Activity.Work;
        }
        return Enum.TryParse<Activity>(value, true, out var activity) ? activity : Activity.Work;
    }
}
