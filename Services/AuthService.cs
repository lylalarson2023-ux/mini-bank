using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ADN_pay.Services
{
    public class AuthService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ILogger<AuthService> _logger;
        private readonly IHttpContextAccessor _http;
        private readonly NotificationHistoryService _notifHist;
        private readonly IEmailSender _email;
        private readonly IConfiguration _config;

        public AuthService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<AuthService> logger, IHttpContextAccessor http,
            NotificationHistoryService notifHist, IEmailSender email, IConfiguration config)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
            _http = http;
            _notifHist = notifHist;
            _email = email;
            _config = config;
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
            if (user.Bloque)
            {
                await LogLoginAsync(user.Id, email, false, "Compte bloqué");
                _logger.LogWarning("Connexion refusée : compte bloqué (Id={UserId})", user.Id);
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

        // --- MOT DE PASSE OUBLIÉ (réinitialisation par code, pré-authentification) ---

        // Étape 1 : l'utilisateur donne son e-mail. Si un compte ACTIF existe, un code à
        // 6 chiffres lui est envoyé. Le message renvoyé est TOUJOURS générique
        // (anti-énumération : ne révèle pas si l'adresse a un compte).
        public async Task<string> RequestPasswordResetAsync(string email)
        {
            const string generic = "Si un compte est associé à cette adresse, un code de réinitialisation vient d'être envoyé.";
            var emailLower = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(emailLower) || !emailLower.Contains('@')) return generic;

            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FirstOrDefaultAsync(x => x.Email == emailLower);
            // Compte inexistant, clôturé ou bloqué : on ne divulgue rien, pas de code envoyé.
            if (u == null || u.CompteCloture || u.Bloque) return generic;

            var code = Random.Shared.Next(100000, 1000000).ToString();
            u.PasswordResetCodeHash = BCrypt.Net.BCrypt.HashPassword(code);
            u.PasswordResetCodeExpiry = DateTime.UtcNow.AddMinutes(15);
            await ctx.SaveChangesAsync();

            var html = EmailTemplate.Wrap(
                "Réinitialisation de votre mot de passe",
                EmailTemplate.Paragraphe("Bonjour,")
                + EmailTemplate.Paragraphe("Vous avez demandé à réinitialiser le mot de passe de votre compte <strong>ADN_pay</strong>. Utilisez le code ci-dessous :")
                + EmailTemplate.CodeBox(code)
                + EmailTemplate.Note("Ce code expire dans 15 minutes. Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail — votre mot de passe reste inchangé."),
                preheader: $"Votre code de réinitialisation ADN_pay : {code} (valable 15 minutes).");
            try
            {
                await _email.SendAsync(emailLower, "ADN_pay — Réinitialisation de votre mot de passe", html,
                    $"Votre code de réinitialisation ADN_pay : {code} (valable 15 minutes).");
            }
            catch (Exception ex)
            {
                // Un échec d'envoi ne doit pas révéler l'existence du compte : on log et on
                // renvoie quand même le message générique.
                _logger.LogWarning(ex, "Envoi du code de réinitialisation échoué.");
            }
            _logger.LogInformation("Code de réinitialisation généré pour un compte existant.");
            return generic;
        }

        // Étape 2 : vérifie le code + applique le nouveau mot de passe. Révoque les
        // sessions ouvertes (rotation du SecurityStamp) : après réinitialisation on
        // repart propre, un éventuel accès frauduleux en cours est coupé.
        public async Task<(bool Success, string Message)> ResetPasswordWithCodeAsync(string email, string code, string newPassword)
        {
            var (okPwd, errPwd) = PasswordPolicy.Validate(newPassword);
            if (!okPwd) return (false, errPwd!);

            var emailLower = (email ?? "").Trim().ToLowerInvariant();
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FirstOrDefaultAsync(x => x.Email == emailLower);
            if (u == null || string.IsNullOrEmpty(u.PasswordResetCodeHash) || u.PasswordResetCodeExpiry == null)
                return (false, "Aucune demande de réinitialisation en cours pour cette adresse.");
            if (u.PasswordResetCodeExpiry < DateTime.UtcNow)
            {
                u.PasswordResetCodeHash = null;
                u.PasswordResetCodeExpiry = null;
                await ctx.SaveChangesAsync();
                return (false, "Le code a expiré. Recommencez la demande.");
            }
            if (string.IsNullOrWhiteSpace(code) || !BCrypt.Net.BCrypt.Verify(code.Trim(), u.PasswordResetCodeHash))
                return (false, "Code incorrect.");

            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            u.MotDePasse = "";
            u.PasswordResetCodeHash = null;
            u.PasswordResetCodeExpiry = null;
            u.SecurityStamp = Guid.NewGuid().ToString("N"); // révoque les sessions ouvertes
            await ctx.SaveChangesAsync();

            _logger.LogInformation("Mot de passe réinitialisé via code (compte #{UserId}).", u.Id);
            return (true, "Votre mot de passe a été réinitialisé. Vous pouvez maintenant vous connecter.");
        }

        // --- VÉRIFICATION DE L'ADRESSE E-MAIL (code envoyé à l'inscription) ---

        // Valide le code de confirmation et marque l'adresse comme vérifiée.
        public async Task<(bool Success, string Message)> VerifyEmailCodeAsync(string email, string code)
        {
            var emailLower = (email ?? "").Trim().ToLowerInvariant();
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FirstOrDefaultAsync(x => x.Email == emailLower);
            if (u == null) return (false, "Compte introuvable.");
            if (u.EmailVerifie) return (true, "Votre adresse e-mail est déjà vérifiée.");
            if (string.IsNullOrEmpty(u.EmailVerifCodeHash) || u.EmailVerifCodeExpiry == null)
                return (false, "Aucun code de vérification en cours. Demandez un nouveau code.");
            if (u.EmailVerifCodeExpiry < DateTime.UtcNow)
                return (false, "Le code a expiré. Demandez un nouveau code.");
            if (string.IsNullOrWhiteSpace(code) || !BCrypt.Net.BCrypt.Verify(code.Trim(), u.EmailVerifCodeHash))
                return (false, "Code incorrect.");

            u.EmailVerifie = true;
            u.EmailVerifCodeHash = null;
            u.EmailVerifCodeExpiry = null;
            await ctx.SaveChangesAsync();
            // Reflète l'état sur la session en cours si c'est l'utilisateur connecté.
            if (_user.EstConnecte && _user.Profil.Id == u.Id) _user.Profil.EmailVerifie = true;
            _logger.LogInformation("Adresse e-mail vérifiée (compte #{UserId}).", u.Id);
            return (true, "Votre adresse e-mail a été vérifiée. Merci !");
        }

        // Renvoie un nouveau code de confirmation. Message générique (anti-énumération) :
        // ne révèle pas si l'adresse a un compte non vérifié.
        public async Task<string> ResendEmailVerificationAsync(string email)
        {
            const string generic = "Si un compte non vérifié est associé à cette adresse, un nouveau code vient d'être envoyé.";
            var emailLower = (email ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(emailLower) || !emailLower.Contains('@')) return generic;

            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FirstOrDefaultAsync(x => x.Email == emailLower);
            if (u == null || u.EmailVerifie || u.CompteCloture) return generic;

            var code = Random.Shared.Next(100000, 1000000).ToString();
            u.EmailVerifCodeHash = BCrypt.Net.BCrypt.HashPassword(code);
            u.EmailVerifCodeExpiry = DateTime.UtcNow.AddMinutes(30);
            await ctx.SaveChangesAsync();
            try
            {
                var html = EmailTemplate.Wrap(
                    "Confirmez votre adresse e-mail",
                    EmailTemplate.Paragraphe("Voici votre nouveau code de confirmation ADN_pay :")
                    + EmailTemplate.CodeBox(code)
                    + EmailTemplate.Note("Ce code expire dans 30 minutes."),
                    preheader: $"Votre code de confirmation ADN_pay : {code}");
                await _email.SendAsync(emailLower, "ADN_pay — Confirmez votre adresse e-mail", html,
                    $"Votre code de confirmation ADN_pay : {code} (valable 30 minutes).");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Renvoi du code de vérification échoué.");
            }
            return generic;
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
            // Vérification e-mail : le compte démarre NON vérifié, un code part à l'inscription.
            u.EmailVerifie = false;
            var codeVerif = Random.Shared.Next(100000, 1000000).ToString();
            u.EmailVerifCodeHash = BCrypt.Net.BCrypt.HashPassword(codeVerif);
            u.EmailVerifCodeExpiry = DateTime.UtcNow.AddMinutes(30);
            ctx.UserProfiles.Add(u);
            try
            {
                var result = await ctx.SaveChangesAsync() > 0;
                if (result)
                {
                    if (_user.EstConnecte && _user.Profil.Id > 0)
                        await _notifHist.AddNotificationAsync("Bienvenue sur ADN_pay — votre compte a été créé avec succès", "SUCCESS", "COMPTE");
                    _logger.LogInformation("Nouveau compte créé : {Email}", u.Email);

                    // E-mail de bienvenue + confirmation d'adresse (code) via le gabarit (non bloquant).
                    try
                    {
                        var prenom = EmailTemplate.Escape(u.Prenom);
                        var html = EmailTemplate.Wrap(
                            $"Bienvenue sur ADN_pay{(string.IsNullOrWhiteSpace(prenom) ? "" : ", " + prenom)} !",
                            EmailTemplate.Paragraphe($"Bonjour{(string.IsNullOrWhiteSpace(prenom) ? "" : " " + prenom)},")
                            + EmailTemplate.Paragraphe("Bienvenue sur <strong>ADN_pay</strong> ! Pour finaliser votre inscription, confirmez votre adresse e-mail avec le code ci-dessous :")
                            + EmailTemplate.CodeBox(codeVerif)
                            + EmailTemplate.Note("Ce code expire dans 30 minutes.")
                            + EmailTemplate.Paragraphe("Une fois votre adresse confirmée, vous profiterez pleinement de votre compte."),
                            preheader: $"Votre code de confirmation ADN_pay : {codeVerif}");
                        await _email.SendAsync(u.Email, "ADN_pay — Confirmez votre adresse e-mail", html,
                            $"Votre code de confirmation ADN_pay : {codeVerif} (valable 30 minutes).");
                    }
                    catch (Exception exMail)
                    {
                        _logger.LogWarning(exMail, "Envoi e-mail de confirmation d'inscription échoué (non bloquant).");
                    }
                }
                return (result, result ? "Compte créé avec succès" : "Erreur lors de la création du compte");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec création compte {Email}", u.Email);
                return (false, "Une erreur technique est survenue. Veuillez réessayer.");
            }
        }

        // Propose 3 adresses @adnpay.ma DISPONIBLES dérivées du prénom/nom.
        public async Task<List<string>> GenererAdnEmailsAsync(string prenom, string nom)
        {
            string p = NormaliserLocalPart(prenom);
            string n = NormaliserLocalPart(nom);
            if (string.IsNullOrEmpty(p)) p = "user";
            if (string.IsNullOrEmpty(n)) n = "adn";

            var bases = new List<string>
            {
                $"{p}.{n}",
                $"{p[0]}{n}",
                $"{n}.{p}"
            }.Distinct().ToList();

            await using var ctx = await _factory.CreateDbContextAsync();
            var taken = new HashSet<string>(
                await ctx.UserProfiles.Where(x => x.AdnEmail != "").Select(x => x.AdnEmail).ToListAsync());

            var result = new List<string>();
            foreach (var b in bases)
            {
                var addr = $"{b}@adnpay.ma";
                int i = 1;
                while (taken.Contains(addr) || result.Contains(addr))
                    addr = $"{b}{i++}@adnpay.ma";
                result.Add(addr);
            }
            return result.Take(3).ToList();
        }

        // Réserve l'adresse @adnpay.ma choisie pour le compte (identifié par son e-mail perso).
        public async Task<(bool Success, string Message)> ReserverAdnEmailAsync(string email, string adnEmail)
        {
            email = (email ?? "").Trim().ToLower();
            adnEmail = (adnEmail ?? "").Trim().ToLower();

            if (string.IsNullOrEmpty(adnEmail) || !adnEmail.EndsWith("@adnpay.ma"))
                return (false, "Adresse @adnpay.ma invalide.");
            var local = adnEmail[..adnEmail.IndexOf('@')];
            if (!System.Text.RegularExpressions.Regex.IsMatch(local, "^[a-z0-9]([a-z0-9._-]*[a-z0-9])?$"))
                return (false, "Format d'adresse @adnpay.ma invalide.");

            await using var ctx = await _factory.CreateDbContextAsync();
            if (await ctx.UserProfiles.AnyAsync(x => x.AdnEmail == adnEmail))
                return (false, "Cette adresse @adnpay.ma est déjà réservée. Choisissez-en une autre.");

            var u = await ctx.UserProfiles.FirstOrDefaultAsync(x => x.Email == email);
            if (u == null)
                return (false, "Compte introuvable.");
            if (!string.IsNullOrEmpty(u.AdnEmail))
                return (true, "Adresse déjà réservée."); // idempotent : on n'écrase pas

            u.AdnEmail = adnEmail;
            await ctx.SaveChangesAsync();
            _logger.LogInformation("Adresse @adnpay.ma réservée : {Adn} pour {Email}", adnEmail, email);
            return (true, "Adresse @adnpay.ma réservée avec succès.");
        }

        // Réduit un nom à une partie locale d'e-mail : minuscules, sans accents, [a-z0-9] uniquement.
        private static string NormaliserLocalPart(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            var formD = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in formD)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return new string(sb.ToString().Where(char.IsLetterOrDigit).ToArray());
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
