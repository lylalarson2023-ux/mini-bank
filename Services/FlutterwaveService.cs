using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ADN_pay.Data;
using ADN_pay.Models;

namespace ADN_pay.Services
{
    public class FlutterwaveOptions
    {
        public string SecretKey { get; set; } = "";
        // « Secret hash » configuré dans le dashboard Flutterwave (Settings →
        // Webhooks) : leurs webhooks l'envoient dans l'en-tête « verif-hash ».
        public string WebhookSecret { get; set; } = "";
        public string BaseUrl { get; set; } = "https://api.flutterwave.com/v3";
        public string Currency { get; set; } = "XAF";
        // Taux appliqué au montant saisi en DH pour facturer en XAF (arrondi au
        // XAF supérieur). À terme : taux vivant via une API de change.
        public decimal XafParDh { get; set; } = 60m;
    }

    public interface IFlutterwaveService
    {
        bool EstConfigured { get; }
        // Crée le dépôt local + la session de paiement hébergée ; retourne l'URL
        // de la page Flutterwave (ou null si échec).
        Task<string?> CreerPaiementAsync(long montantDhCentimes);
        // Vérifie le statut AUPRÈS DE L'API Flutterwave (jamais sur la foi du
        // navigateur ou du webhook) puis crédite — idempotent par TxRef.
        Task<bool> VerifierEtCrediterAsync(string txRef);
    }

    public class FlutterwaveService : IFlutterwaveService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly FlutterwaveOptions _options;
        private readonly UserContext _user;
        private readonly IDbContextFactory<BankDbContext> _dbFactory;
        private readonly ExternalDepositService _deposits;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<FlutterwaveService> _logger;

        public FlutterwaveService(
            IHttpClientFactory httpClientFactory,
            IOptions<FlutterwaveOptions> options,
            UserContext user,
            IDbContextFactory<BankDbContext> dbFactory,
            ExternalDepositService deposits,
            IHttpContextAccessor http,
            ILogger<FlutterwaveService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _user = user;
            _dbFactory = dbFactory;
            _deposits = deposits;
            _http = http;
            _logger = logger;
        }

        public bool EstConfigured => !string.IsNullOrEmpty(_options.SecretKey);

        // Le XAF n'a pas de décimales : arrondi au franc supérieur pour ne jamais
        // facturer moins que l'équivalent du montant crédité.
        public static long ConvertirDhCentimesEnXaf(long montantDhCentimes, decimal xafParDh)
            => (long)Math.Ceiling(montantDhCentimes / 100m * xafParDh);

        public async Task<string?> CreerPaiementAsync(long montantDhCentimes)
        {
            if (!EstConfigured || montantDhCentimes <= 0) return null;
            var profil = _user.Profil;
            if (profil is null) return null;

            var deposit = new FlutterwaveDeposit
            {
                TxRef = $"adnpay-{Guid.NewGuid():N}",
                UserId = profil.Id,
                MontantDhCentimes = montantDhCentimes,
                MontantXaf = ConvertirDhCentimesEnXaf(montantDhCentimes, _options.XafParDh),
                Currency = _options.Currency,
            };
            await using (var db = await _dbFactory.CreateDbContextAsync())
            {
                db.FlutterwaveDeposits.Add(deposit);
                await db.SaveChangesAsync();
            }

            var payload = new
            {
                tx_ref = deposit.TxRef,
                amount = deposit.MontantXaf,
                currency = deposit.Currency,
                redirect_url = $"{GetBaseUrl()}/api/flutterwave/callback",
                customer = new { email = profil.Email, name = $"{profil.Prenom} {profil.Nom}".Trim() },
                customizations = new
                {
                    title = "ADN_pay — Dépôt",
                    description = $"Dépôt de {montantDhCentimes / 100m:N2} DH ({deposit.MontantXaf} {deposit.Currency})"
                }
            };

            try
            {
                var client = CreateClient();
                var response = await client.PostAsync("/v3/payments",
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Flutterwave /payments a échoué — tx_ref={TxRef}, http={Status}, corps={Body}",
                        deposit.TxRef, response.StatusCode, json);
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                var link = doc.RootElement.GetProperty("data").GetProperty("link").GetString();
                _logger.LogInformation("Paiement Flutterwave créé — tx_ref={TxRef}, {Xaf} {Currency} pour {Dh} DH",
                    deposit.TxRef, deposit.MontantXaf, deposit.Currency, montantDhCentimes / 100m);
                return link;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Création du paiement Flutterwave impossible — tx_ref={TxRef}", deposit.TxRef);
                return null;
            }
        }

        public async Task<bool> VerifierEtCrediterAsync(string txRef)
        {
            if (!EstConfigured || string.IsNullOrWhiteSpace(txRef)) return false;

            await using var db = await _dbFactory.CreateDbContextAsync();
            var deposit = await db.FlutterwaveDeposits.FirstOrDefaultAsync(d => d.TxRef == txRef);
            if (deposit is null)
            {
                _logger.LogWarning("Flutterwave : tx_ref inconnu en base — {TxRef}", txRef);
                return false;
            }

            string json;
            try
            {
                var client = CreateClient();
                var response = await client.GetAsync(
                    $"/v3/transactions/verify_by_reference?tx_ref={Uri.EscapeDataString(txRef)}");
                json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Flutterwave verify a échoué — tx_ref={TxRef}, http={Status}", txRef, response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vérification Flutterwave impossible — tx_ref={TxRef}", txRef);
                return false;
            }

            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
            var statut = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("status", out var s)
                ? s.GetString() : null;
            var montant = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("amount", out var a)
                ? a.GetDecimal() : 0m;
            var devise = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("currency", out var c)
                ? c.GetString() : null;

            deposit.RawJson = json;
            deposit.UpdatedAt = DateTime.UtcNow;

            if (statut != "successful" || devise != deposit.Currency || montant < deposit.MontantXaf)
            {
                if (statut is "failed" or "cancelled")
                    deposit.Status = FlutterwaveDepositStatus.Failed;
                await db.SaveChangesAsync();
                _logger.LogWarning("Flutterwave non crédité — tx_ref={TxRef}, statut={Statut}, montant={Montant} {Devise} (attendu {Attendu} {Currency})",
                    txRef, statut, montant, devise, deposit.MontantXaf, deposit.Currency);
                return false;
            }

            deposit.Status = FlutterwaveDepositStatus.Completed;
            await db.SaveChangesAsync();

            return await _deposits.CrediterAsync(
                deposit.UserId,
                deposit.MontantDhCentimes,
                "flutterwave",
                txRef,
                $"Dépôt Mobile Money ({deposit.MontantXaf} {deposit.Currency} via Flutterwave)");
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("Flutterwave");
            client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/').Replace("/v3", ""));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private string GetBaseUrl()
        {
            var req = _http.HttpContext?.Request;
            if (req == null) return "http://localhost:5163";
            return $"{req.Scheme}://{req.Host}";
        }
    }
}
