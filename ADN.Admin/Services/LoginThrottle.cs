using System.Collections.Concurrent;

namespace ADN_pay.Admin.Services;

// Anti-brute-force du login admin. Le formulaire de connexion Blazor passe par le
// circuit SignalR : le middleware de rate limiting HTTP ne le voit pas, d'où ce
// compteur applicatif. Échecs comptés par clé (IP et e-mail séparément) ; au-delà
// du seuil, la clé est verrouillée pour la durée de blocage.
public class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private sealed record Entry(int Count, DateTimeOffset FirstFailure, DateTimeOffset? LockedUntil);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    // Durée restante de blocage si l'une des clés est verrouillée, sinon null.
    public TimeSpan? RetryAfter(params string?[] keys)
    {
        var now = DateTimeOffset.UtcNow;
        TimeSpan? worst = null;
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key) || !_entries.TryGetValue(key, out var e)) continue;

            if (e.LockedUntil is { } until && until > now)
            {
                var remaining = until - now;
                if (worst is null || remaining > worst) worst = remaining;
            }
            else if (e.LockedUntil is not null || now - e.FirstFailure > FailureWindow)
            {
                _entries.TryRemove(key, out _); // blocage ou fenêtre expirés
            }
        }
        return worst;
    }

    public void RecordFailure(params string?[] keys)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            _entries.AddOrUpdate(key,
                _ => new Entry(1, now, null),
                (_, e) =>
                {
                    if (now - e.FirstFailure > FailureWindow && e.LockedUntil is null)
                        return new Entry(1, now, null); // nouvelle fenêtre
                    var count = e.Count + 1;
                    return new Entry(count, e.FirstFailure,
                        count >= MaxFailures ? now + Lockout : e.LockedUntil);
                });
        }
    }

    public void RecordSuccess(params string?[] keys)
    {
        foreach (var key in keys)
            if (!string.IsNullOrEmpty(key)) _entries.TryRemove(key, out _);
    }
}
