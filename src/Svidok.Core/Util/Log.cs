using System.Globalization;

namespace Svidok.Core.Util;

public enum LogLevel
{
    Info,
    Warn,
    Error
}

/// <summary>
/// Простой файловый лог: logs/svidok_yyyyMMdd.log.
/// Запросы к модели целиком сюда не пишутся (только в кэш и llm_calls.csv).
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _filePath;

    public static string FilePath
    {
        get
        {
            EnsureInitialized();
            return _filePath!;
        }
    }

    private static void EnsureInitialized()
    {
        if (_filePath is not null)
        {
            return;
        }
        lock (Gate)
        {
            if (_filePath is not null)
            {
                return;
            }
            var dir = PathResolver.EnsureDirectory("logs");
            var name = "svidok_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
            _filePath = Path.Combine(dir, name);
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(LogLevel.Error, message + " :: " + ex.GetType().Name + ": " + ex.Message);

    public static void Write(LogLevel level, string message)
    {
        EnsureInitialized();
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                   + " [" + level.ToString().ToUpperInvariant() + "] " + message;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_filePath!, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // лог не должен ронять симуляцию
            }
            catch (UnauthorizedAccessException)
            {
                // лог не должен ронять симуляцию
            }
        }
        Console.WriteLine(line);
    }
}
