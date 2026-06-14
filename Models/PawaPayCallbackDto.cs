using System.Text.Json.Serialization;

namespace ADN_pay.Models
{
    /// <summary>
    /// Represents the inbound callback payload sent by PawaPay when a deposit status changes.
    /// See https://docs.pawapay.io for the full schema.
    /// </summary>
    public class PawaPayCallbackDto
    {
        [JsonPropertyName("depositId")]
        public string DepositId { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("amount")]
        public string Amount { get; set; } = "";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "";

        [JsonPropertyName("correspondent")]
        public string Correspondent { get; set; } = "";

        [JsonPropertyName("statementDescription")]
        public string? StatementDescription { get; set; }

        [JsonPropertyName("customerTimestamp")]
        public DateTime? CustomerTimestamp { get; set; }

        [JsonPropertyName("created")]
        public DateTime? Created { get; set; }
    }
}
