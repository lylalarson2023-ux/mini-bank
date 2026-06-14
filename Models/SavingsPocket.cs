using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public class SavingsPocket
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public string Objectif { get; set; } = string.Empty;

        public string StatutGoal { get; set; } = "Actif";

        public bool TuteurVisible { get; set; } = false;

        // ADR-001 : centimes (long)
        public long MontantActuel { get; set; }
        public long MontantCible { get; set; }

        public DateTime Cible { get; set; }

        public DateTime DateCreation { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }

        [NotMapped]
        public long CapitalActuel => MontantActuel;

        [NotMapped]
        public DateTime DateCible => Cible;

        // Progression reste decimal : c'est un pourcentage (exception ADR-001)
        [NotMapped]
        public decimal Progression => MontantCible > 0
            ? Math.Round((decimal)MontantActuel / MontantCible * 100, 1)
            : 0;
    }
}
