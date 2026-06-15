namespace ADN_pay.Services
{
    /// Abstraction d'envoi d'e-mail. Deux implémentations selon l'environnement :
    /// LogEmailSender (dev, écrit dans les logs) et ResendEmailSender (prod, API Resend).
    public interface IEmailSender
    {
        Task<bool> SendAsync(string to, string subject, string htmlBody, string? textBody = null);
    }
}
