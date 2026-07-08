using System;

namespace ADN_pay.Models
{
    // Tarification du corridor de transfert international Gabon<->Maroc (canal
    // Mobile Money manuel, Alex — sans intégration API, taux configurés côté app).
    // taux = DH par 1 FCFA (ex. 0,0156). marge = fraction (0,10 = 10%).
    //
    // Dépôt (Gabon->Maroc, le proche envoie du FCFA à Alex, le solde DH de
    // l'étudiant est crédité) : la marge est DÉDUITE du montant crédité.
    // Retrait (Maroc->Gabon, l'étudiant débite son solde, Alex paie le
    // bénéficiaire en FCFA) : la marge est AJOUTÉE au coût débité.
    public static class TransfertInternationalPricing
    {
        // Centimes DH que l'étudiant veut voir crédités -> FCFA que le proche doit
        // envoyer. Arrondi au FCFA supérieur : ne jamais demander moins que
        // l'équivalent exact (protège la marge côté demande).
        public static long DepotFcfaAEnvoyer(long centimesDhVoulus, decimal tauxDhParFcfa, decimal margePct)
        {
            var tauxEffectif = tauxDhParFcfa * (1m - margePct);
            var fcfa = (centimesDhVoulus / 100m) / tauxEffectif;
            return (long)Math.Ceiling(fcfa);
        }

        // Centimes DH débités du solde de l'étudiant -> FCFA reçus par le
        // bénéficiaire. Arrondi au FCFA inférieur : ne jamais payer plus que ce
        // que couvre le débit (protège la marge côté paiement).
        public static long RetraitFcfaARecevoir(long centimesDhDebites, decimal tauxDhParFcfa, decimal margePct)
        {
            var tauxEffectif = tauxDhParFcfa * (1m + margePct);
            var fcfa = (centimesDhDebites / 100m) / tauxEffectif;
            return (long)Math.Floor(fcfa);
        }
    }
}
