namespace MBANK_ETUDIANT.Models
{
    public class AdminLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; } = ""; // ex: "VALIDATION_PREMIUM"
        public string Cible { get; set; } = "";  // Nom de l'utilisateur concerné
        public string Details { get; set; } = ""; // ex: "Frais de 100 DH prélevés"
        public string StatutResultat { get; set; } = "SUCCESS"; 
    }
}