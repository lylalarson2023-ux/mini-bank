using System;

namespace ADN_pay.Models
{
    // Frais de virement interne (compte à compte, même établissement).
    // Gratuit pour PREMIUM et VIP. Pour les autres statuts (STANDARD/PENDING) :
    // 1% du montant envoyé, plafonné pour rester raisonnable sur les gros
    // montants (le plafond est atteint à partir de 2 000 DH envoyés).
    public static class FraisVirement
    {
        public const decimal Taux = 0.01m;           // 1%
        public const long PlafondCentimes = 2_000L;   // 20 DH

        public static long Calculer(long montantCentimes, UserStatus statut)
        {
            if (statut is UserStatus.PREMIUM or UserStatus.VIP) return 0L;
            if (montantCentimes <= 0) return 0L;
            var frais = (long)Math.Ceiling(montantCentimes * Taux);
            return Math.Min(frais, PlafondCentimes);
        }
    }
}
