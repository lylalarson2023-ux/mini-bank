using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ADN_pay.Data;
using ADN_pay.Models;

namespace ADN_pay.Services
{
    public class PawaPayOptions
    {
        public string ApiToken { get; set; } = "";
        public string BaseUrl { get; set; } = "https://api.sandbox.pawapay.io";
        public string CallbackUrl { get; set; } = "";
    }

    public interface IPawaPayService
    {
        bool EstConfigured { get; }
        Task<PawaPayInitiateResponse?> InitiateDepositAsync(decimal amount, string currency, string correspondent, string phoneNumber, int userId);
        Task<PawaPayStatusResponse?> CheckDepositStatusAsync(string depositId);
        Task HandleCallbackAsync(PawaPayCallbackDto dto);
    }

    public class PawaPayService : IPawaPayService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PawaPayOptions _options;
        private readonly IDbContextFactory<BankDbContext> _dbFactory;
        private readonly ILogger<PawaPayService> _logger;

        public PawaPayService(
            IHttpClientFactory httpClientFactory,
            IOptions<PawaPayOptions> options,
            IDbContextFactory<BankDbContext> dbFactory,
            ILogger<PawaPayService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _dbFactory = dbFactory;
            _logger = logger;
        }

        public bool EstConfigured => !string.IsNullOrEmpty(_options.ApiToken);

        public async Task<PawaPayInitiateResponse?> InitiateDepositAsync(
            decimal amount, string currency, string correspondent, string phoneNumber, int userId)
        {
            var depositId = Guid.NewGuid().ToString();
            var client = CreateClient();

            var body = new PawaPayInitiateRequest
            {
                DepositId = depositId,
                Amount = amount.ToString("F2"),
                Currency = currency,
                Correspondent = correspondent,
                Payer = new PawaPayPayer
                {
                    Address = new PawaPayAddress { Value = phoneNumber }
                },
                CustomerTimestamp = DateTime.UtcNow.ToString("O"),
                StatementDescription = GenerateStatementDescription(),
                Metadata = new List<PawaPayMetadata>
                {
                    new() { FieldName = "userId", FieldValue = userId.ToString() }
                }
            };

            var json = JsonSerializer.Serialize(body, JsonOpts);
            var maskedPhone = phoneNumber.Length > 4
                ? phoneNumber[..^4] + "XXXX"
                : "XXXX";
            _logger.LogInformation(
                "PawaPay InitiateDeposit — depositId={DepositId}, amount={Amount} {Currency}, " +
                "correspondent={Correspondent}, phone={Phone}",
                depositId, amount, currency, correspondent, maskedPhone);

            var response = await client.PostAsync(
                "/deposits", new StringContent(json, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "PawaPay InitiateDeposit failed — status={Status}, body={Body}",
                    response.StatusCode, errorBody);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PawaPayInitiateResponse>(responseJson, JsonOpts);

            await using var db = await _dbFactory.CreateDbContextAsync();
            db.PawaPayDeposits.Add(new PawaPayDeposit
            {
                DepositId = depositId,
                UserId = userId,
                Amount = amount,
                Currency = currency,
                Correspondent = correspondent,
                PhoneNumber = phoneNumber,
                Status = result?.Status == "ACCEPTED"
                    ? PawaPayDepositStatus.Pending
                    : PawaPayDepositStatus.Failed
            });
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "PawaPay InitiateDeposit result — depositId={DepositId}, status={Status}",
                depositId, result?.Status);
            return result;
        }

        public async Task<PawaPayStatusResponse?> CheckDepositStatusAsync(string depositId)
        {
            var client = CreateClient();
            _logger.LogInformation("PawaPay CheckDepositStatus — depositId={DepositId}", depositId);

            var response = await client.GetAsync($"/deposits/{depositId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "PawaPay CheckDepositStatus failed — depositId={DepositId}, status={Status}",
                    depositId, response.StatusCode);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PawaPayStatusResponse>(responseJson, JsonOpts);

            if (result != null)
                await UpdateLocalDepositStatus(depositId, result.Status);

            return result;
        }

        public async Task HandleCallbackAsync(PawaPayCallbackDto dto)
        {
            _logger.LogInformation(
                "PawaPay callback received — depositId={DepositId}, status={Status}, type={Type}",
                dto.DepositId, dto.Status, dto.Type);

            var rawJson = JsonSerializer.Serialize(dto, JsonOpts);
            await UpdateLocalDepositStatus(dto.DepositId, dto.Status, rawJson);

            if (dto.Status == "COMPLETED")
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var deposit = await db.PawaPayDeposits.FirstOrDefaultAsync(d => d.DepositId == dto.DepositId);
                if (deposit != null && deposit.Status != PawaPayDepositStatus.Completed)
                {
                    deposit.Status = PawaPayDepositStatus.Completed;
                    deposit.UpdatedAt = DateTime.UtcNow;

                    var user = await db.UserProfiles.FindAsync(deposit.UserId);
                    if (user != null)
                    {
                        user.Solde += (long)(deposit.Amount * 100);
                    }

                    await db.SaveChangesAsync();
                    _logger.LogInformation(
                        "PawaPay deposit completed — depositId={DepositId}, userId={UserId}, amount={Amount}",
                        dto.DepositId, deposit.UserId, deposit.Amount);
                }
            }
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("PawaPay");
            client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiToken);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private async Task UpdateLocalDepositStatus(string depositId, string apiStatus, string? rawJson = null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var deposit = await db.PawaPayDeposits.FirstOrDefaultAsync(d => d.DepositId == depositId);
            if (deposit == null)
            {
                _logger.LogWarning("PawaPay deposit not found in DB — depositId={DepositId}", depositId);
                return;
            }

            deposit.Status = apiStatus switch
            {
                "COMPLETED" => PawaPayDepositStatus.Completed,
                "FAILED" or "REJECTED" => PawaPayDepositStatus.Failed,
                _ => PawaPayDepositStatus.Pending
            };
            deposit.UpdatedAt = DateTime.UtcNow;

            if (rawJson != null)
                deposit.RawCallbackJson = rawJson;

            await db.SaveChangesAsync();
        }

        private static string GenerateStatementDescription()
        {
            var prefix = "ADN";
            var suffix = DateTime.UtcNow.ToString("HHmmss");
            return $"{prefix}{suffix}";
        }
    }
}
