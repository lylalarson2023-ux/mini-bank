using System.Text.Json.Serialization;

namespace ADN_pay.Models
{
    // ─── Initiate deposit request ───────────────────────────

    public class PawaPayInitiateRequest
    {
        [JsonPropertyName("depositId")]
        public string DepositId { get; set; } = "";

        [JsonPropertyName("amount")]
        public string Amount { get; set; } = "";

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "";

        [JsonPropertyName("correspondent")]
        public string Correspondent { get; set; } = "";

        [JsonPropertyName("payer")]
        public PawaPayPayer Payer { get; set; } = new();

        [JsonPropertyName("customerTimestamp")]
        public string CustomerTimestamp { get; set; } = "";

        [JsonPropertyName("statementDescription")]
        public string StatementDescription { get; set; } = "";

        [JsonPropertyName("metadata")]
        public List<PawaPayMetadata> Metadata { get; set; } = new();
    }

    public class PawaPayPayer
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "MSISDN";

        [JsonPropertyName("address")]
        public PawaPayAddress Address { get; set; } = new();
    }

    public class PawaPayAddress
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    public class PawaPayMetadata
    {
        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; } = "";

        [JsonPropertyName("fieldValue")]
        public string FieldValue { get; set; } = "";
    }

    // ─── API responses ─────────────────────────────────────

    public class PawaPayInitiateResponse
    {
        [JsonPropertyName("depositId")]
        public string DepositId { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("created")]
        public DateTime? Created { get; set; }
    }

    public class PawaPayStatusResponse
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
