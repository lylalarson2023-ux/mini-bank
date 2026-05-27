using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBANK_ETUDIANT.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        
        // DÉPÔT, RETRAIT, ÉPARGNE, CRÉDIT
        public string Type { get; set; } = ""; 
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Frais { get; set; } 
        
        public string Motif { get; set; } = "";
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal SoldeApres { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Montant { get; set; }
        
        public string Libelle { get; set; } = "";

        // --- RELATIONS ---
        
        // Clé étrangère vers UserProfile
        public int UserId { get; set; }
        
        // Propriété de navigation pour l'accès facile aux données du compte
        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }

        // Propriété calculée pour le montant total débité/crédité
        public decimal MontantBrut => Montant + Frais;
    }
}