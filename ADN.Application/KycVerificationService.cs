using Microsoft.EntityFrameworkCore;
using ADN_pay.Data;
using ADN_pay.Models;

namespace ADN_pay.Services
{
    // Contrôles automatiques de cohérence / anti-fraude sur un dossier KYC.
    // Produit une liste de signalements (KycFlag) surfacés à l'admin lors de la revue :
    // doublons (CIN, téléphone, identité), pièces manquantes, âge invraisemblable,
    // e-mail non vérifié. Ne bloque jamais : l'admin garde la décision finale.
    public class KycVerificationService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;

        public KycVerificationService(IDbContextFactory<BankDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<KycFlag>> AnalyserAsync(int userId)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null) return new();

            var flags = new List<KycFlag>();

            // E-mail confirmé ?
            if (!u.EmailVerifie)
                flags.Add(new(KycSeverite.Attention, "Adresse e-mail non vérifiée."));

            // Doublon de pièce d'identité (CIN / passeport) — le plus grave.
            if (!string.IsNullOrWhiteSpace(u.PassportOuCIN))
            {
                var cin = u.PassportOuCIN.Trim();
                var dup = await ctx.UserProfiles
                    .AnyAsync(x => x.Id != u.Id && !x.CompteCloture && x.PassportOuCIN == cin);
                if (dup)
                    flags.Add(new(KycSeverite.Alerte, $"CIN/Passeport « {cin} » déjà utilisé par un autre compte."));
            }
            else
                flags.Add(new(KycSeverite.Attention, "CIN/Passeport manquant."));

            // Doublon de téléphone.
            if (!string.IsNullOrWhiteSpace(u.Telephone))
            {
                var tel = u.Telephone.Trim();
                var dupTel = await ctx.UserProfiles
                    .AnyAsync(x => x.Id != u.Id && !x.CompteCloture && x.Telephone == tel);
                if (dupTel)
                    flags.Add(new(KycSeverite.Attention, $"Téléphone « {tel} » déjà utilisé par un autre compte."));
            }

            // Doublon d'identité (nom + prénom + date de naissance).
            if (u.DateNaissance.HasValue && !string.IsNullOrWhiteSpace(u.Nom) && !string.IsNullOrWhiteSpace(u.Prenom))
            {
                var nom = u.Nom.Trim();
                var prenom = u.Prenom.Trim();
                var dupId = await ctx.UserProfiles.AnyAsync(x => x.Id != u.Id && !x.CompteCloture
                    && x.Nom == nom && x.Prenom == prenom && x.DateNaissance == u.DateNaissance);
                if (dupId)
                    flags.Add(new(KycSeverite.Attention, "Même nom, prénom et date de naissance qu'un autre compte."));
            }

            // Âge plausible.
            if (u.DateNaissance.HasValue)
            {
                var age = CalculerAge(u.DateNaissance.Value);
                if (age < 15)
                    flags.Add(new(KycSeverite.Alerte, $"Âge déclaré {age} ans (moins de 15 ans)."));
                else if (age > 100)
                    flags.Add(new(KycSeverite.Attention, $"Âge déclaré {age} ans (invraisemblable)."));
            }
            else
                flags.Add(new(KycSeverite.Attention, "Date de naissance manquante."));

            // Pièces justificatives.
            if (string.IsNullOrEmpty(u.DocIdentiteUrl))
                flags.Add(new(KycSeverite.Attention, "Scan de pièce d'identité manquant."));
            if (string.IsNullOrEmpty(u.SelfieUrl))
                flags.Add(new(KycSeverite.Attention, "Selfie de vérification manquant."));

            // Les plus graves en premier.
            return flags.OrderByDescending(f => f.Severite).ToList();
        }

        private static int CalculerAge(DateTime naissance)
        {
            var today = DateTime.Today;
            var age = today.Year - naissance.Year;
            if (naissance.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}
