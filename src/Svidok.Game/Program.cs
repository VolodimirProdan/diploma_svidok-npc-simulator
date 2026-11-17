using Svidok.Core.Decisions;

namespace Svidok.Game;

public static class Program
{
    public static void Main(string[] args)
    {
        var options = GameOptions.Parse(args);
        using var app = new GameApp(options);
        app.Run();
    }
}
