using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public class CreditRequest
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }

        // ADR-001 : centimes (long)
        public long Montant { get; set; }

        public int DureeMois { get; set; }
        public string Categorie { get; set; } = string.Empty;
        public string Statut { get; set; } = "EN_ATTENTE";
        public DateTime DateDemande { get; set; } = DateTime.UtcNow;

        // Taux = pourcentage → decimal (exception ADR-001)
        public decimal TauxAnnuel { get; set; }

        public string? MotifRejet { get; set; }

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
    }
}
