using Microsoft.EntityFrameworkCore;
using MBANK_ETUDIANT.Data;
using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class AuthService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<AuthService> _logger;

        public AuthService(BankDbContext context, UserContext user, ILogger<AuthService> logger)
        {
            _context = context;
            _user = user;
            _logger = logger;
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
            var user = await _context.UserProfiles.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLower());
            if (user == null || string.IsNullOrEmpty(user.MotDePasseHash))
            {
                _logger.LogWarning("Échec connexion : {Email} introuvable", email);
                return false;
            }
            if (!BCrypt.Net.BCrypt.Verify(password, user.MotDePasseHash))
            {
                _logger.LogWarning("Échec connexion : mot de passe incorrect pour {Email}", email);
                return false;
            }
            _user.Profil = user;
            _user.EstConnecte = true;
            _logger.LogInformation("Connexion réussie : {Email}", email);
            return true;
        }

        public Task<bool> VerifierSessionExistante() => Task.FromResult(_user.EstConnecte);

        public void Deconnexion()
        {
            _logger.LogInformation("Déconnexion : {Email}", _user.Profil.Email);
            _user.Profil = new();
            _user.EstConnecte = false;
        }

        public async Task<bool> CreerNouveauCompte(UserProfile u, string motDePasse)
        {
            u.Email = u.Email.Trim().ToLower();
            if (await _context.UserProfiles.AnyAsync(x => x.Email == u.Email))
            {
                _logger.LogWarning("Création compte échouée : {Email} déjà existant", u.Email);
                return false;
            }
            u.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(motDePasse);
            u.MotDePasse = "";
            _context.UserProfiles.Add(u);
            var result = await _context.SaveChangesAsync() > 0;
            if (result) _logger.LogInformation("Nouveau compte créé : {Email}", u.Email);
            return result;
        }
    }
}
