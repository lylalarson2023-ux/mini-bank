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
