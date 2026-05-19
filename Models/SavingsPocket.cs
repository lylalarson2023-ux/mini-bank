using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBANK_ETUDIANT.Models
{
    public class SavingsPocket
    {
        [Key]
        public int Id { get; set; }

        // L'ID de l'étudiant propriétaire (Clé étrangère)
        public int UserId { get; set; } 

        public string Objectif { get; set; } = string.Empty;
        
        // Par défaut, un objectif est actif
        public string StatutGoal { get; set; } = "Actif"; 

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontantActuel { get; set; }

        // Date limite pour atteindre l'objectif
        public DateTime Cible { get; set; }
        
        public DateTime DateCreation { get; set; } = DateTime.Now;

        // --- RELATIONS ---

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
        
        // --- PROPRIÉTÉS DE COMPATIBILITÉ (Getters) ---
        // Ces propriétés permettent de garder la compatibilité avec ton code existant 
        // sans doubler les colonnes en base de données.
        
        [NotMapped]
        public decimal CapitalActuel => MontantActuel; 
        
        [NotMapped]
        public DateTime DateCible => Cible;            
    }
}