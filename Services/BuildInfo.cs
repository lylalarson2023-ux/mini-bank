using System.IO;
using System.Linq;
using System.Reflection;

namespace ADN_pay.Services;

/// <summary>
/// Expose la version réellement déployée de l'application :
/// la date de compilation (déduite de l'assembly) et le commit git
/// embarqué au moment du build. Sert à afficher dans l'UI quelle
/// version tourne, pour lever tout doute sur « mes modifs sont-elles en ligne ? ».
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;

    /// <summary>Date/heure de compilation locale, ex. "2026-06-13 17:21".</summary>
    public static string Timestamp { get; } = ResolveBuildTime();

    /// <summary>
    /// Commit git court ; suffixe "-dirty" s'il y avait des modifications
    /// non commitées au moment du build. "dev" si indisponible.
    /// </summary>
    public static string Commit { get; } = ReadMetadata("GitCommit") ?? "dev";

    /// <summary>Forme compacte pour l'UI, ex. "2026-06-13 17:21 · eab5a00-dirty".</summary>
    public static string Short => $"{Timestamp} · {Commit}";

    /// <summary>Jeton URL-safe changeant à chaque build (cache-buster des assets statiques).</summary>
    public static string CacheTag { get; } = ResolveCacheTag();

    private static string ResolveCacheTag()
    {
        try
        {
            var location = Assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                return File.GetLastWriteTime(location).Ticks.ToString("x");
        }
        catch { /* ignoré */ }
        return "1";
    }

    private static string ResolveBuildTime()
    {
        try
        {
            var location = Assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                return File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            // ignoré : on retombe sur la valeur par défaut
        }
        return "date inconnue";
    }

    private static string? ReadMetadata(string key)
    {
        var value = Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
