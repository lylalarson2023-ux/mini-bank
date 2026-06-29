using System.Text.RegularExpressions;
using ADN_pay.Services;

namespace ADN_pay.Api.Services;

public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger;

    public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

    public Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        var corps = textBody ?? Regex.Replace(htmlBody ?? "", "<.*?>", " ").Replace("&nbsp;", " ").Trim();
        _logger.LogInformation(
            "\n[E-MAIL non envoyé]\n  À     : {To}\n  Sujet : {Subject}\n  Corps : {Corps}",
            to, subject, corps);
        return Task.FromResult(true);
    }

    public Task<bool> SendTemplateAsync(string to, int templateId, object? parameters = null)
    {
        _logger.LogInformation(
            "\n[E-MAIL TEMPLATE non envoyé]\n  À          : {To}\n  TemplateId : {Id}\n  Params     : {Params}",
            to, templateId, System.Text.Json.JsonSerializer.Serialize(parameters));
        return Task.FromResult(true);
    }
}
