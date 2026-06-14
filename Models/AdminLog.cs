using System.ComponentModel.DataAnnotations;

namespace ADN_pay.Models
{
    public class AdminLog
    {
        [Key]
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Action { get; set; } = ""; // ex: "VALIDATION_PREMIUM"
        public string Cible { get; set; } = "";  // Nom de l'utilisateur concerné
        public string Details { get; set; } = ""; // ex: "Frais de 100 DH prélevés"
        public string StatutResultat { get; set; } = "SUCCESS"; 
    }
}