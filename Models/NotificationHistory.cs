using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MBANK_ETUDIANT.Models
{
    public class NotificationHistory
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; } = "";
        public string Type { get; set; } = "INFO";
        public string Categorie { get; set; } = "GENERAL";
        public bool Lu { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
    }
}
