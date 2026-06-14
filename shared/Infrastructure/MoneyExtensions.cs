namespace ADN_pay.Shared.Infrastructure;

public static class MoneyExtensions
{
    /// <summary>Centimes → "150.00" (sans unité)</summary>
    public static string ToDisplay(this long cents) => (cents / 100m).ToString("N2");

    /// <summary>Centimes → "150.00 DH"</summary>
    public static string ToDh(this long cents) => $"{cents / 100m:N2} DH";

    /// <summary>DH (décimal utilisateur) → centimes long</summary>
    public static long ToCents(this decimal dh) => (long)Math.Round(dh * 100m, MidpointRounding.AwayFromZero);

    /// <summary>DH (double, ex : chart) → centimes long</summary>
    public static long ToCents(this double dh) => (long)Math.Round(dh * 100d, MidpointRounding.AwayFromZero);
}
