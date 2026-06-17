using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public class UserLogin
    {
        [Key]
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Email { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string IpAddress { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public bool Success { get; set; }
        public string? FailureReason { get; set; }

        [ForeignKey("UserId")]
        public UserProfile? User { get; set; }
    }
}
