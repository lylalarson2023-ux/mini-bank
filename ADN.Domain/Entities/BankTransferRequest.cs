using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    // Demande de dépôt par virement bancaire : l'utilisateur annonce un montant,
    // reçoit une référence unique à mettre dans le motif du virement, et l'admin
    // valide (crédit idempotent) ou rejette à réception du virement réel.
    public class BankTransferRequest
    {
        public const string EnAttente = "EN_ATTENTE";
        public const string Valide = "VALIDE";
        public const string Rejete = "REJETE";
        public const string Annule = "ANNULE";

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
