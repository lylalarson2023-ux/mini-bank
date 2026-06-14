using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ADN_pay.Shared.Infrastructure;

public interface IAlertingService
{
    Task SendCriticalAsync(string message);
    Task SendWarningAsync(string message);
}

public class AlertingService : IAlertingService
{
    private readonly HttpClient _http;
    private readonly ILogger<AlertingService> _logger;
    private readonly string? _slackWebhookUrl;

    public AlertingService(HttpClient http, IConfiguration config, ILogger<AlertingService> logger)
    {
        _http = http;
        _logger = logger;
        _slackWebhookUrl = config["Alerting:SlackWebhookUrl"];
    }

    public async Task SendCriticalAsync(string message)
    {
        _logger.LogCritical("ALERTE CRITIQUE : {Message}", message);
        await PostToSlackAsync($":rotating_light: *CRITIQUE* — {message}");
    }

    public async Task SendWarningAsync(string message)
    {
        _logger.LogWarning("ALERTE WARNING : {Message}", message);
        await PostToSlackAsync($":warning: *WARNING* — {message}");
    }

    private async Task PostToSlackAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_slackWebhookUrl))
            return;

        try
        {
            var payload = JsonSerializer.Serialize(new { text });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync(_slackWebhookUrl, content);
        }
        catch (Exception ex)
        {
            // Ne jamais propager — l'alerting ne doit pas casser le flux applicatif
            _logger.LogError(ex, "Échec envoi alerte Slack");
        }
    }
}
