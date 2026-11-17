using System.Globalization;
using Svidok.Core.Decisions;

namespace Svidok.Game;

/// <summary>Аргументы командной строки окна: автостарт сценария и снимок экрана для проверки отрисовки.</summary>
public sealed class GameOptions
{
    public string? ScenarioId { get; private set; }

    public Variant Variant { get; private set; } = Variant.V4;

    public int Seed { get; private set; } = 1;

    public bool PlayerMode { get; private set; }

    public int MaxFrames { get; private set; }

    public string? ScreenshotPath { get; private set; }

    public int Speed { get; private set; } = 1;

    public int TabIndex { get; private set; }

    public string? OpenDialogNpc { get; private set; }

    public string? SayText { get; private set; }

    public bool TechDetails { get; private set; }

    public bool ForcePanel { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool Heatmap { get; private set; }

    public bool AutoStart => ScenarioId is not null;

    public static GameOptions Parse(string[] args)
    {
        var options = new GameOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--scenario":
                    options.ScenarioId = Next(args, ref i);
                    break;
                case "--variant":
                    options.Variant = Variants.Parse(Next(args, ref i));
                    break;
                case "--seed":
                    options.Seed = int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var seed)
                        ? seed
                        : 1;
                    break;
                case "--mode":
                    options.PlayerMode = string.Equals(Next(args, ref i), "player", StringComparison.OrdinalIgnoreCase);
                    break;
                case "--frames":
                    options.MaxFrames = int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var frames)
                        ? frames
                        : 0;
                    break;
                case "--speed":
                    options.Speed = int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var speed)
                        ? Math.Clamp(speed, 0, 4)
                        : 1;
                    break;
                case "--tab":
                    options.TabIndex = int.TryParse(Next(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var tab)
                        ? Math.Clamp(tab, 0, 4)
                        : 0;
                    break;
                case "--open-dialog":
                    options.OpenDialogNpc = Next(args, ref i);
                    break;
                case "--say":
                    options.SayText = Next(args, ref i);
                    break;
                case "--heatmap":
                    options.Heatmap = true;
                    break;
                case "--show-help":
                    options.ShowHelp = true;
                    break;
                case "--panel":
                    options.ForcePanel = true;
                    break;
                case "--tech":
                    options.TechDetails = true;
                    break;
                case "--screenshot":
                    options.ScreenshotPath = Next(args, ref i);
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    break;
            }
        }
        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Svidok.Game — окно симулятора");
        Console.WriteLine("  --scenario s1        сразу запустить сценарий (без меню)");
        Console.WriteLine("  --variant V4         вариант решений");
        Console.WriteLine("  --seed 1             seed");
        Console.WriteLine("  --mode player        режим игрока вместо наблюдателя");
        Console.WriteLine("  --speed 4            скорость симуляции 0..4 (пауза, ×1, ×4, ×16, ×64)");
        Console.WriteLine("  --frames 300         закрыть окно после N кадров (для проверки)");
        Console.WriteLine("  --tab 0..4           вкладка панели (0 персонаж, 1 журнал, 2 слухи, 3 нейросеть, 4 статистика)");
        Console.WriteLine("  --open-dialog bohdan открыть диалог с NPC (режим игрока)");
        Console.WriteLine("  --say text           отправить реплику в открытый диалог");
        Console.WriteLine("  --tech               показать технические данные в диалоге");
        Console.WriteLine("  --panel              показать панель наблюдателя и в режиме игрока");
        Console.WriteLine("  --show-help          открыть окно справки (что это за программа)");
        Console.WriteLine("  --heatmap            включить тепловую карту слухов и выбрать событие S1");
        Console.WriteLine("  --screenshot path    сохранить снимок окна перед выходом");
    }

    private static string? Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }
        index++;
        return args[index];
    }
}
