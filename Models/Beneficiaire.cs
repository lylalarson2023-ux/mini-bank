using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBANK_ETUDIANT.Models
{
    public class Beneficiaire
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Nom { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Banque { get; set; }
        public string? RIB { get; set; }
        public DateTime DateAjout { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
    }
}
