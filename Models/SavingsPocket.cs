using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBANK_ETUDIANT.Models
{
    public class SavingsPocket
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; } 

        public string Objectif { get; set; } = string.Empty;
        
        public string StatutGoal { get; set; } = "Actif"; 

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantActuel { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantCible { get; set; }

        public DateTime Cible { get; set; }
        
        public DateTime DateCreation { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
        
        [NotMapped]
        public decimal CapitalActuel => MontantActuel; 
        
        [NotMapped]
        public DateTime DateCible => Cible;

        [NotMapped]
        public decimal Progression => MontantCible > 0 ? Math.Round(MontantActuel / MontantCible * 100, 1) : 0;
    }
}