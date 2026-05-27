using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;
using Microsoft.AspNetCore.Http;

namespace MBANK_ETUDIANT.Services
{
    public class AuthService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AuthService> _logger;
        private readonly IHttpContextAccessor _http;
        private readonly NotificationHistoryService _notifHist;

        public AuthService(BankDbContext context, UserContext user, ILogger<AuthService> logger, IHttpContextAccessor http,
            NotificationHistoryService notifHist)
        {
            _context = context;
            _user = user;
            _logger = logger;
            _http = http;
            _notifHist = notifHist;
        }

        private async Task LogLoginAsync(int userId, string email, bool success, string? reason = null)
        {
            var log = new UserLogin
            {
                UserId = userId,
                Email = email,
                Date = DateTime.UtcNow,
                Success = success,
                FailureReason = reason,
                IpAddress = _http.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "unknown",
                UserAgent = _http.HttpContext?.Request?.Headers["User-Agent"].ToString() ?? "unknown"
            };
            _context.UserLogins.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task InitializeAsync(int userId)
        {
            var user = await _context.UserProfiles.FindAsync(userId);
            if (user != null)
            {
                _user.Profil = user;
                _user.EstConnecte = true;
                _logger.LogInformation("Session initialisée pour {Email} (Id={UserId})", user.Email, userId);
            }
        }

        public async Task<bool> SeConnecter(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Tentative de connexion avec email/mot de passe vide");
                return false;
            }
            var emailLower = email.Trim().ToLower();
            var user = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == emailLower);
            if (user == null || string.IsNullOrEmpty(user.MotDePasseHash))
            {
                await LogLoginAsync(0, email, false, "Compte introuvable");
                _logger.LogWarning("Échec connexion : {Email} introuvable", email);
                return false;
            }
            if (!BCrypt.Net.BCrypt.Verify(password, user.MotDePasseHash))
            {
                await LogLoginAsync(user.Id, email, false, "Mot de passe incorrect");
                _logger.LogWarning("Échec connexion : mot de passe incorrect pour {Email}", email);
                return false;
            }
            await LogLoginAsync(user.Id, email, true);
            _user.Profil = user;
            _user.EstConnecte = true;
            await _notifHist.AddNotificationAsync("Connexion réussie", "SUCCESS", "CONNEXION");
            _logger.LogInformation("Connexion réussie : {Email}", email);
            return true;
        }

        public async Task<bool> VerifierSessionExistante()
        {
            if (_user.EstConnecte) return true;

            var httpUser = _http.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated != true) return false;

            var userIdClaim = httpUser.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return false;

            var user = await _context.UserProfiles.FindAsync(userId);
            if (user == null) return false;

            var cookieStamp = httpUser.FindFirst("SecurityStamp")?.Value;
            if (!string.IsNullOrEmpty(cookieStamp) && cookieStamp != user.SecurityStamp)
            {
                _logger.LogWarning("SecurityStamp mismatch pour UserId={UserId} — session révoquée", userId);
                return false;
            }

            _user.Profil = user;
            _user.EstConnecte = true;
            _logger.LogInformation("Session restaurée via cookie pour UserId={UserId}", userId);
            return true;
        }

        public void Deconnexion()
        {
            _logger.LogInformation("Déconnexion : {Email}", _user.Profil.Email);
            _user.Profil = new();
            _user.EstConnecte = false;
        }

        public async Task<(bool Success, string Message)> CreerNouveauCompte(UserProfile u, string motDePasse)
        {
            if (string.IsNullOrWhiteSpace(motDePasse) || motDePasse.Length < 8)
                return (false, "Le mot de passe doit contenir au moins 8 caractères");
            if (!motDePasse.Any(char.IsUpper))
                return (false, "Le mot de passe doit contenir au moins une majuscule");
            if (!motDePasse.Any(char.IsLower))
                return (false, "Le mot de passe doit contenir au moins une minuscule");
            if (!motDePasse.Any(char.IsDigit))
                return (false, "Le mot de passe doit contenir au moins un chiffre");
            if (!motDePasse.Any(c => !char.IsLetterOrDigit(c)))
                return (false, "Le mot de passe doit contenir au moins un caractère spécial (!@#$%^&*)");

            u.Email = u.Email.Trim().ToLower();
            if (await _context.UserProfiles.AnyAsync(x => x.Email == u.Email))
            {
                _logger.LogWarning("Création compte échouée : {Email} déjà existant", u.Email);
                return (false, "Cet email est déjà utilisé");
            }
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(motDePasse);
            u.MotDePasse = "";
            _context.UserProfiles.Add(u);
            try
            {
                var result = await _context.SaveChangesAsync() > 0;
                if (result)
                {
                    if (_user.EstConnecte && _user.Profil.Id > 0)
                        await _notifHist.AddNotificationAsync("Bienvenue sur MBANK — votre compte a été créé avec succès", "SUCCESS", "COMPTE");
                    _logger.LogInformation("Nouveau compte créé : {Email}", u.Email);
                }
                return (result, result ? "Compte créé avec succès" : "Erreur lors de la création du compte");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec création compte {Email}", u.Email);
                return (false, "Une erreur technique est survenue. Veuillez réessayer.");
            }
        }
        public async Task MigreMotsDePasseEnClair()
        {
            var users = await _context.UserProfiles.ToListAsync();
            foreach (var u in users)
            {
                if (u.MotDePasseHash.StartsWith("$2"))
                    continue;

                var plainPassword = !string.IsNullOrEmpty(u.MotDePasse) ? u.MotDePasse : u.MotDePasseHash;
                if (!string.IsNullOrEmpty(plainPassword))
                {
                    u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);
                    u.MotDePasse = "";
                }
            }
            await _context.SaveChangesAsync();
            _logger.LogInformation("Migration des mots de passe vers BCrypt terminée.");
        }
    }
}
