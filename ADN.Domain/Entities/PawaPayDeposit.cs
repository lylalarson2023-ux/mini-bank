using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public enum PawaPayDepositStatus
    {
        Pending,
        Completed,
        Failed
    }

    public class PawaPayDeposit
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(64)]
        public string DepositId { get; set; } = "";

        public int UserId { get; set; }

        public decimal Amount { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "";

        [MaxLength(32)]
        public string Correspondent { get; set; } = "";

        [MaxLength(20)]
        public string PhoneNumber { get; set; } = "";

        public PawaPayDepositStatus Status { get; set; } = PawaPayDepositStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public string? RawCallbackJson { get; set; }

        [ForeignKey(nameof(UserId))]
        public UserProfile? User { get; set; }
    }
}
