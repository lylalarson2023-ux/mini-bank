namespace MBANK_ETUDIANT.Models
{
    public enum NotificationType { SUCCESS, ERROR, INFO }
    
    public class Notification
    {
        public string Message { get; set; } = "";
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}