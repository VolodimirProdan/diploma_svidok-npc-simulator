namespace Svidok.Core.Knowledge;

public enum Source
{
    Participant,
    Witnessed,
    Heard,
    Rumor,
    Overheard,
    PlayerClaim
}

public static class Sources
{
    public static string NameUk(Source source)
    {
        return source switch
        {
            Source.Participant => "учасник",
            Source.Witnessed => "бачив сам",
            Source.Heard => "чув сам",
            Source.Rumor => "чутка",
            Source.Overheard => "підслухав",
            Source.PlayerClaim => "слова гравця",
            _ => source.ToString()
        };
    }

    public static bool IsFirstHand(Source source) =>
        source is Source.Participant or Source.Witnessed or Source.Heard;
}
