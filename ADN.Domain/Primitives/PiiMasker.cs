namespace ADN_pay.Shared.Infrastructure;

public static class PiiMasker
{
    /// <summary>
    /// "0241234567" → "024***567"
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "***";
        return phone.Length < 6 ? "***" : $"{phone[..3]}***{phone[^3..]}";
    }

    /// <summary>
    /// "etudiant@gmail.com" → "e***@gmail.com"
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";
        var local = parts[0];
        return local.Length == 0 ? $"***@{parts[1]}" : $"{local[0]}***@{parts[1]}";
    }

    /// <summary>
    /// "AB123456" → "AB***456"
    /// </summary>
    public static string MaskId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "***";
        return id.Length < 5 ? "***" : $"{id[..2]}***{id[^3..]}";
    }
}
