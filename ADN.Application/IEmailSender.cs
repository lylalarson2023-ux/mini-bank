namespace ADN_pay.Services
{
    /// Abstraction d'envoi d'e-mail. Deux implémentations selon l'environnement :
    /// LogEmailSender (dev, écrit dans les logs) et BrevoEmailSender (prod, API Brevo).
    public interface IEmailSender
    {
        Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null);

        // Envoi via un template Brevo (ID numérique) + paramètres ({{ params.X }} dans le template).
        Task<bool> SendTemplateAsync(string to, int templateId, object? parameters = null);
    }
}
