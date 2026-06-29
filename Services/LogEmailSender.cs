using System.Text.RegularExpressions;

namespace ADN_pay.Services
{
    /// Implémentation de développement : n'envoie aucun e-mail réel.
    /// Écrit le contenu (dont les codes/liens) dans les logs Serilog → testable sans boîte mail.
    public class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;

        public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;

        public Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null)
        {
            var corps = textBody ?? StripHtml(htmlBody);
            _logger.LogInformation(
                "\n📧 ─── E-MAIL (DEV, non envoyé) ───\n   À      : {To}\n   Sujet  : {Subject}\n   Contenu: {Corps}\n──────────────────────────────",
                to, subject, corps);
            return Task.FromResult(true);
        }

        public Task<bool> SendTemplateAsync(string to, int templateId, object? parameters = null)
        {
            _logger.LogInformation(
                "\n📧 ─── E-MAIL TEMPLATE (DEV, non envoyé) ───\n   À          : {To}\n   TemplateId : {Id}\n   Params     : {Params}\n──────────────────────────────",
                to, templateId, System.Text.Json.JsonSerializer.Serialize(parameters));
            return Task.FromResult(true);
        }

        private static string StripHtml(string html) =>
            Regex.Replace(html ?? "", "<.*?>", " ").Replace("&nbsp;", " ").Trim();
    }
}
