using System.Linq;

namespace ADN_pay.Services
{
    // Règles de robustesse du mot de passe, partagées par les flux qui en définissent
    // un (inscription, réinitialisation) : 8+ caractères, 1 majuscule, 1 minuscule,
    // 1 chiffre, 1 caractère spécial. Retourne (true, null) si valide, sinon le message.
    public static class PasswordPolicy
    {
        public static (bool Ok, string? Error) Validate(string? password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return (false, "Le mot de passe doit contenir au moins 8 caractères");
            if (!password.Any(char.IsUpper))
                return (false, "Le mot de passe doit contenir au moins une majuscule");
            if (!password.Any(char.IsLower))
                return (false, "Le mot de passe doit contenir au moins une minuscule");
            if (!password.Any(char.IsDigit))
                return (false, "Le mot de passe doit contenir au moins un chiffre");
            if (!password.Any(c => !char.IsLetterOrDigit(c)))
                return (false, "Le mot de passe doit contenir au moins un caractère spécial (!@#$%^&*)");
            return (true, null);
        }
    }
}
