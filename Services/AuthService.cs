using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using Microsoft.AspNetCore.Http;

namespace ADN_pay.Services
{
    public class AuthService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ILogger<AuthService> _logger;
        private readonly IHttpContextAccessor _http;
        private readonly NotificationHistoryService _notifHist;

        public AuthService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<AuthService> logger, IHttpContextAccessor http,
            NotificationHistoryService notifHist)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
            _http = http;
            _notifHist = notifHist;
        }

        private async Task LogLoginAsync(int? userId, string email, bool success, string? reason = null)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
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
            ctx.UserLogins.Add(log);
            await ctx.SaveChangesAsync();
        }

        public async Task InitializeAsync(int userId)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var user = await ctx.UserProfiles.FindAsync(userId);
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
            await using var ctx = await _factory.CreateDbContextAsync();
            var emailLower = email.Trim().ToLower();
            var user = await ctx.UserProfiles.FirstOrDefaultAsync(u => u.Email == emailLower);
            if (user == null || string.IsNullOrEmpty(user.MotDePasseHash))
            {
                await LogLoginAsync(user?.Id, email, false, "Compte introuvable");
                _logger.LogWarning("Échec connexion : {Email} introuvable", email);
                return false;
            }
            if (!BCrypt.Net.BCrypt.Verify(password, user.MotDePasseHash))
            {
                await LogLoginAsync(user.Id, email, false, "Mot de passe incorrect");
                _logger.LogWarning("Échec connexion : mot de passe incorrect pour {Email}", email);
                return false;
            }
            if (user.CompteCloture)
            {
                await LogLoginAsync(user.Id, email, false, "Compte clôturé");
                _logger.LogWarning("Connexion refusée : compte clôturé (Id={UserId})", user.Id);
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

            await using var ctx = await _factory.CreateDbContextAsync();
            var user = await ctx.UserProfiles.FindAsync(userId);
            if (user == null) return false;
            if (user.CompteCloture) return false;

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

            await using var ctx = await _factory.CreateDbContextAsync();
            u.Email = u.Email.Trim().ToLower();
            if (await ctx.UserProfiles.AnyAsync(x => x.Email == u.Email))
            {
                _logger.LogWarning("Création compte échouée : {Email} déjà existant", u.Email);
                return (false, "Cet email est déjà utilisé");
            }
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(motDePasse);
            u.MotDePasse = "";
            ctx.UserProfiles.Add(u);
            try
            {
                var result = await ctx.SaveChangesAsync() > 0;
                if (result)
                {
                    if (_user.EstConnecte && _user.Profil.Id > 0)
                        await _notifHist.AddNotificationAsync("Bienvenue sur ADN_pay — votre compte a été créé avec succès", "SUCCESS", "COMPTE");
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
            await using var ctx = await _factory.CreateDbContextAsync();
            var users = await ctx.UserProfiles.ToListAsync();
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
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Migration des mots de passe vers BCrypt terminée.");
        }
    }
}
