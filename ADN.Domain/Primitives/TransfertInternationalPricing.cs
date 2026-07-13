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

        // ─────────────────────────── Frais de change (transparence) ───────────────────────────
        // La marge de change prélevée par ADN_pay, exprimée en frais explicites.
        // Consignée en DH dans Transaction.Frais ; montrée en FCFA au client (concret).

        // Frais de change (centimes DH) d'un DÉPÔT : la marge est déduite du crédit,
        // donc le proche paie l'équivalent de « montant + frais ». Pour un montant
        // crédité M et une marge m : frais = M × m / (1 − m). Arrondi au centime
        // supérieur (cohérent avec DepotFcfaAEnvoyer, qui protège la marge).
        public static long DepotFraisCentimes(long centimesDhCredites, decimal margePct)
        {
            if (margePct <= 0m || centimesDhCredites <= 0) return 0L;
            return (long)Math.Ceiling(centimesDhCredites * margePct / (1m - margePct));
        }

        // Frais de change (centimes DH) d'un RETRAIT : la marge est ajoutée au débit,
        // donc le bénéficiaire reçoit l'équivalent de « montant − frais ». Pour un
        // montant débité M et une marge m : frais = M × m / (1 + m). Arrondi au
        // centime supérieur.
        public static long RetraitFraisCentimes(long centimesDhDebites, decimal margePct)
        {
            if (margePct <= 0m || centimesDhDebites <= 0) return 0L;
            return (long)Math.Ceiling(centimesDhDebites * margePct / (1m + margePct));
        }

        // Part de frais (en FCFA) incluse dans un montant converti, pour l'affichage
        // client — la fraction « marge » du FCFA envoyé (dépôt) ou reçu (retrait).
        // Dérivée du montant déjà figé sur la demande → cohérente avec l'écran.
        public static long FraisFcfa(long montantConvertiFcfa, decimal margePct)
        {
            if (margePct <= 0m || montantConvertiFcfa <= 0) return 0L;
            return (long)Math.Round(montantConvertiFcfa * margePct, MidpointRounding.AwayFromZero);
        }
    }
}
