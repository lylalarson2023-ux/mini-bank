using System.Net.Http.Json;
using ADN_pay.Services;

namespace ADN_pay.Api.Services;

/// Implémentation de production : envoie via l'API Brevo (https://www.brevo.com).
/// Clé lue depuis la config (Brevo:ApiKey — user-secrets en dev, variable d'env BREVO_API_KEY en prod).
/// Expéditeur : Brevo:SenderEmail / Brevo:SenderName (domaine vérifié dans Brevo).
public class BrevoEmailSender : IEmailSender
{
    private const string ApiUrl = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _http;
    private readonly ILogger<BrevoEmailSender> _logger;
    private readonly string _senderEmail;
    private readonly string _senderName;

    public BrevoEmailSender(HttpClient http, IConfiguration config, ILogger<BrevoEmailSender> logger)
    {
        _http = http;
        _logger = logger;
        _senderEmail = config["Brevo:SenderEmail"] ?? "noreply@adnpay.net";
        _senderName  = config["Brevo:SenderName"]  ?? "ADN_pay";

        var key = config["Brevo:ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(key))
        {
            _http.DefaultRequestHeaders.Remove("api-key");
            _http.DefaultRequestHeaders.Add("api-key", key);
        }
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        try
        {
            var payload = new
            {
                sender = new { name = _senderName, email = _senderEmail },
                to = new[] { new { email = to } },
                subject,
                htmlContent = htmlBody,
                textContent = textBody
            };
            var resp = await _http.PostAsJsonAsync(ApiUrl, payload);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Brevo a échoué ({Status}) : {Body}", (int)resp.StatusCode, body);
                return false;
            }
            _logger.LogInformation("E-mail envoyé via Brevo à {To} ({Subject})", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception lors de l'envoi Brevo");
            return false;
        }
    }

    public async Task<bool> SendTemplateAsync(string to, int templateId, object? parameters = null)
    {
        try
        {
            // Avec un templateId, l'expéditeur et le sujet viennent du template Brevo.
            var payload = new
            {
                to = new[] { new { email = to } },
                templateId,
                @params = parameters
            };
            var resp = await _http.PostAsJsonAsync(ApiUrl, payload);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Brevo (template {Id}) a échoué ({Status}) : {Body}", templateId, (int)resp.StatusCode, body);
                return false;
            }
            _logger.LogInformation("E-mail (template {Id}) envoyé via Brevo à {To}", templateId, to);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception envoi template Brevo {Id}", templateId);
            return false;
        }
    }
}
