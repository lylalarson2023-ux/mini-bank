using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    // Demande de dépôt manuel : l'utilisateur annonce un montant, reçoit une
    // référence unique à mettre dans le motif de son envoi (virement bancaire ou
    // Mobile Money vers le numéro du fondateur), et l'admin valide (crédit
    // idempotent) ou rejette à réception des fonds réels.
    public class BankTransferRequest
    {
        public const string EnAttente = "EN_ATTENTE";
        public const string Valide = "VALIDE";
        public const string Rejete = "REJETE";
        public const string Annule = "ANNULE";

        public const string CanalVirement = "VIREMENT";
        public const string CanalMobileMoney = "MOBILE_MONEY";

        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        // ADR-001 : centimes (long)
        public long MontantCentimes { get; set; }

        // Référence à inscrire dans le motif du virement (ex. « ADN-7K2M4P »).
        [Required, MaxLength(24)]
        public string Reference { get; set; } = "";

        [MaxLength(16)]
        public string Statut { get; set; } = EnAttente;

        [MaxLength(16)]
        public string Canal { get; set; } = CanalVirement;

        // Montant communiqué au client dans la devise d'envoi (ex. FCFA pour le
        // Mobile Money), figé à la création — l'admin le compare au SMS reçu.
        public long? MontantConverti { get; set; }

        [MaxLength(8)]
        public string? DeviseConvertie { get; set; }

        // Frais de change (centimes DH) figés à la création : la marge prélevée par
        // ADN_pay sur ce dépôt Mobile Money. 0 pour un virement bancaire (sans change).
        // Reporté dans Transaction.Frais à la validation (transparence dans l'historique).
        public long FraisCentimes { get; set; }

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
