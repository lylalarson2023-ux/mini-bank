namespace ADN_pay.Shared.Infrastructure;

public static class IdempotencyHelper
{
    /// <summary>
    /// Génère une clé d'idempotence UUID v4. À appeler côté client (Blazor),
    /// pas côté serveur — la clé doit voyager avec la requête.
    /// </summary>
    public static string NewKey() => Guid.NewGuid().ToString();

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Guid.TryParse(key, out _);
}
