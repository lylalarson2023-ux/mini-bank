using System;

namespace MBANK_ETUDIANT.Models
{
    public class CreditRequest
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public decimal Montant { get; set; }
        public int DureeMois { get; set; }
        public string Categorie { get; set; } = string.Empty;
        public string Statut { get; set; } = "EN_ATTENTE";
        public DateTime DateDemande { get; set; } = DateTime.UtcNow;
        public UserProfile? User { get; set; }
    }
}
