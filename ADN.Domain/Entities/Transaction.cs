using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public class Transaction
    {
        [Key]
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;

        // DÉPÔT, RETRAIT, ÉPARGNE, CRÉDIT
        public string Type { get; set; } = "";

        // ADR-001 : centimes (long)
        public long Frais { get; set; }
        public string Motif { get; set; } = "";
        public long SoldeApres { get; set; }
        public long Montant { get; set; }
        public string Libelle { get; set; } = "";

        // Référence unique du dépôt externe (ex. « stripe:cs_… », « virement:ADN-… ») :
        // l'index unique garantit qu'un même paiement ne peut créditer le compte
        // qu'une seule fois (idempotence anti-rejeu).
        [MaxLength(96)]
        public string? ReferenceExterne { get; set; }

        // --- RELATIONS ---
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }

        [NotMapped]
        public long MontantBrut => Montant + Frais;

        [NotMapped]
        public bool IsEntree => Type is "DÉPÔT" or "RÉCEPTION" or "CRÉDIT" or "DEPOT" or "RECEPTION" or "RETOUR_ÉPARGNE";

        [NotMapped]
        public bool IsSortie => Type is "RETRAIT" or "VIREMENT" or "ÉPARGNE";
    }
}
