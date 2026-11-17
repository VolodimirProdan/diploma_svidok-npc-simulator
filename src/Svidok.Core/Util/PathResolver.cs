namespace Svidok.Core.Util;

/// <summary>
/// Разрешает пути к данным и ресурсам независимо от того, откуда запущена программа:
/// из каталога решения, из bin/Debug/net10.0 или из опубликованной папки.
/// </summary>
public static class PathResolver
{
    private static readonly Lazy<string> _root = new(FindRoot, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Root => _root.Value;

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        var probe = dir;
        while (probe is not null)
        {
            if (File.Exists(Path.Combine(probe.FullName, "Svidok.sln")))
            {
                return probe.FullName;
            }
            probe = probe.Parent;
        }

        probe = dir;
        while (probe is not null)
        {
            if (File.Exists(Path.Combine(probe.FullName, "appsettings.json")) &&
                Directory.Exists(Path.Combine(probe.FullName, "data")))
            {
                return probe.FullName;
            }
            probe = probe.Parent;
        }

        return AppContext.BaseDirectory;
    }

    public static string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Root;
        }
        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }
        return Path.GetFullPath(Path.Combine(Root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
    }

    /// <summary>Ищет файл в корне решения, затем в каталоге сборки.</summary>
    public static string ResolveExisting(string relativePath)
    {
        var primary = Resolve(relativePath);
        if (File.Exists(primary))
        {
            return primary;
        }
        var local = Path.Combine(AppContext.BaseDirectory, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        if (File.Exists(local))
        {
            return local;
        }
        return primary;
    }

    public static string EnsureDirectory(string relativePath)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }
}
