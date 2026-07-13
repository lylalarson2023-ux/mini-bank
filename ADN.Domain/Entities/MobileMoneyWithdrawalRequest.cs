using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    // Demande de retrait par Mobile Money : le client indique le montant et le
    // numéro Mobile Money du bénéficiaire (souvent lui-même ou un proche au
    // Gabon), reçoit une référence, et l'admin (canal Alex) valide une fois
    // l'envoi réel effectué. C'est CETTE validation qui débite le solde — jamais
    // avant : le client ne doit pas être débité tant qu'il n'a pas reçu l'argent.
    // Symétrique de BankTransferRequest (dépôt), sens inversé.
    public class MobileMoneyWithdrawalRequest
    {
        public const string EnAttente = "EN_ATTENTE";
        public const string Valide = "VALIDE";
        public const string Rejete = "REJETE";
        public const string Annule = "ANNULE";

        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        // ADR-001 : centimes (long) — montant débité du solde ADN_pay (DH).
        public long MontantCentimes { get; set; }

        // Référence courte affichée au client et communiquée à Alex pour le
        // rapprochement (ex. « ADN-7K2M4P »).
        [Required, MaxLength(24)]
        public string Reference { get; set; } = "";

        [MaxLength(16)]
        public string Statut { get; set; } = EnAttente;

        // Montant réellement envoyé par Alex au bénéficiaire (FCFA), figé à la
        // création (même taux que le dépôt Mobile Money).
        public long? MontantAEnvoyer { get; set; }

        [MaxLength(8)]
        public string? DeviseEnvoi { get; set; }

        // Frais de change (centimes DH) figés à la création : la marge prélevée par
        // ADN_pay sur ce retrait Mobile Money. Reporté dans Transaction.Frais à la
        // validation (transparence dans l'historique).
        public long FraisCentimes { get; set; }

        // Destinataire de l'envoi Mobile Money (le client lui-même ou un proche).
        [Required, MaxLength(32)]
        public string NumeroBeneficiaire { get; set; } = "";

        [MaxLength(120)]
        public string NomBeneficiaire { get; set; } = "";

        public string? MotifRejet { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.UtcNow;
        public DateTime? DateTraitement { get; set; }

        // E-mail de l'admin qui a validé/rejeté.
        [MaxLength(120)]
        public string? TraitePar { get; set; }

        [ForeignKey(nameof(UserId))]
        public UserProfile? User { get; set; }
    }
}
