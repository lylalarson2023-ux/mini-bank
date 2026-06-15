using System.Net.Http.Json;

namespace ADN_pay.Services
{
    /// Implémentation de production : envoie via l'API Resend (https://resend.com).
    /// Clé lue depuis la config (Resend:ApiKey — user-secrets en dev, variable d'env en prod).
    public class ResendEmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly ILogger<ResendEmailSender> _logger;
        private readonly string _from;

        public ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> logger)
        {
            _http = http;
            _logger = logger;
            _from = config["Resend:FromEmail"] ?? "ADN_pay <onboarding@resend.dev>";

            var key = config["Resend:ApiKey"] ?? "";
            _http.BaseAddress = new Uri("https://api.resend.com/");
            if (!string.IsNullOrEmpty(key))
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }

        public async Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null)
        {
            try
            {
                var payload = new { from = _from, to = new[] { to }, subject, html = htmlBody };
                var resp = await _http.PostAsJsonAsync("emails", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("Resend a échoué ({Status}) : {Body}", (int)resp.StatusCode, body);
                    return false;
                }
                _logger.LogInformation("E-mail envoyé via Resend à {To} ({Subject})", to, subject);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception lors de l'envoi Resend");
                return false;
            }
        }
    }
}
