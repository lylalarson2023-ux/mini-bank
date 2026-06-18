using System.Security.Cryptography;
using ADN_pay.Data;
using ADN_pay.Models;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Services
{
    public class TwoFactorService
    {
        private readonly IDbContextFactory<BankDbContext> _factory;
        private readonly UserContext _user;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(IDbContextFactory<BankDbContext> factory, UserContext user, ILogger<TwoFactorService> logger)
        {
            _factory = factory;
            _user = user;
            _logger = logger;
        }

        public string GenerateSecret()
        {
            var key = RandomNumberGenerator.GetBytes(20);
            return Base32Encode(key);
        }

        public string GenerateQrUri(string secret, string email)
        {
            return $"otpauth://totp/ADN_pay:{email}?secret={secret}&issuer=ADN_pay&algorithm=SHA1&digits=6&period=30";
        }

        public bool VerifyCode(string secret, string code)
        {
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
                return false;
            if (code.Length != 6 || !code.All(char.IsDigit))
                return false;

            var expected = GenerateTotp(secret, out _);
            return code == expected;
        }

        public bool VerifyCodeWithWindow(string secret, string code, int window = 1)
        {
            if (VerifyCode(secret, code)) return true;
            for (int i = -window; i <= window; i++)
            {
                if (i == 0) continue;
                var expected = GenerateTotp(secret, out _, i);
                if (code == expected) return true;
            }
            return false;
        }

        private static string GenerateTotp(string secret, out long timeStep, int offset = 0)
        {
            var key = Base32Decode(secret);
            var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            timeStep = unix / 30 + offset;
            var timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);

            var hmac = HMACSHA1.HashData(key, timeBytes);

            var offsetBits = hmac[^1] & 0x0F;
            var binary = (hmac[offsetBits] & 0x7F) << 24
                       | (hmac[offsetBits + 1] & 0xFF) << 16
                       | (hmac[offsetBits + 2] & 0xFF) << 8
                       | (hmac[offsetBits + 3] & 0xFF);

            var otp = binary % 1000000;
            return otp.ToString("D6");
        }

        public async Task<(bool Success, string Message, List<string> RecoveryCodes)> EnableAsync(string code)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable", new());
            var secret = _user.Profil.TwoFactorSecret ?? u.TwoFactorSecret;
            if (string.IsNullOrEmpty(secret))
                return (false, "Aucun secret configuré. Générez d'abord un secret.", new());

            if (!VerifyCodeWithWindow(secret, code))
                return (false, "Code invalide. Vérifiez votre application d'authentification.", new());

            var plainCodes = NewPlainCodes();
            u.TwoFactorSecret = secret;
            u.TwoFactorEnabled = true;
            u.TwoFactorRecoveryCodes = HashCodes(plainCodes);
            ctx.UserProfiles.Update(u);
            await ctx.SaveChangesAsync();
            _user.Profil.TwoFactorEnabled = true;
            _user.Profil.TwoFactorRecoveryCodes = u.TwoFactorRecoveryCodes;
            _logger.LogInformation("2FA activée pour {Email}", _user.Profil.Email);
            return (true, "Authentification à deux facteurs activée.", plainCodes);
        }

        public async Task DisableAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return;
            u.TwoFactorEnabled = false;
            u.TwoFactorSecret = null;
            u.TwoFactorRecoveryCodes = null;
            ctx.UserProfiles.Update(u);
            await ctx.SaveChangesAsync();
            _user.Profil.TwoFactorEnabled = false;
            _user.Profil.TwoFactorSecret = null;
            _user.Profil.TwoFactorRecoveryCodes = null;
            _logger.LogInformation("2FA désactivée pour {Email}", _user.Profil.Email);
        }

        // Régénère un nouveau jeu de codes (invalide les anciens). 2FA doit être active.
        public async Task<List<string>> RegenerateRecoveryCodesAsync()
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null || !u.TwoFactorEnabled) return new();
            var plainCodes = NewPlainCodes();
            u.TwoFactorRecoveryCodes = HashCodes(plainCodes);
            await ctx.SaveChangesAsync();
            _user.Profil.TwoFactorRecoveryCodes = u.TwoFactorRecoveryCodes;
            _logger.LogInformation("Codes de secours 2FA régénérés pour {Email}", _user.Profil.Email);
            return plainCodes;
        }

        public int CountRemainingRecoveryCodes()
        {
            var raw = _user.Profil.TwoFactorRecoveryCodes;
            return string.IsNullOrEmpty(raw) ? 0 : raw.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        // Vérifie un code de secours et le consomme (usage unique). Utilisé à la connexion.
        public async Task<bool> VerifyAndConsumeRecoveryCodeAsync(int userId, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            if (u == null || string.IsNullOrEmpty(u.TwoFactorRecoveryCodes)) return false;

            var normalized = code.Trim().Replace(" ", "").Replace("-", "").ToLowerInvariant();
            var hashes = u.TwoFactorRecoveryCodes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
            var match = hashes.FirstOrDefault(h => BCrypt.Net.BCrypt.Verify(normalized, h));
            if (match == null) return false;

            hashes.Remove(match); // consomme le code
            u.TwoFactorRecoveryCodes = string.Join(';', hashes);
            await ctx.SaveChangesAsync();
            _logger.LogWarning("Connexion via code de secours 2FA pour UserId={UserId} ({Remaining} restants)", userId, hashes.Count);
            return true;
        }

        private static List<string> NewPlainCodes(int n = 8)
        {
            const string chars = "abcdefghijkmnpqrstuvwxyz23456789"; // sans caractères ambigus
            var codes = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var bytes = RandomNumberGenerator.GetBytes(10);
                var raw = new char[10];
                for (int j = 0; j < 10; j++) raw[j] = chars[bytes[j] % chars.Length];
                codes.Add(new string(raw, 0, 5) + "-" + new string(raw, 5, 5));
            }
            return codes;
        }

        private static string HashCodes(List<string> codes)
            => string.Join(';', codes.Select(c =>
                BCrypt.Net.BCrypt.HashPassword(c.Replace("-", "").ToLowerInvariant())));

        public async Task<bool> UserHasTwoFactorAsync(int userId)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var u = await ctx.UserProfiles.FindAsync(userId);
            return u?.TwoFactorEnabled == true && !string.IsNullOrEmpty(u.TwoFactorSecret);
        }

        // --- Base32 encoding/decoding ---
        private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private static string Base32Encode(byte[] data)
        {
            var result = new System.Text.StringBuilder();
            int bits = 0, bitCount = 0;
            foreach (var b in data)
            {
                bits = (bits << 8) | b;
                bitCount += 8;
                while (bitCount >= 5)
                {
                    result.Append(Base32Chars[(bits >> (bitCount - 5)) & 0x1F]);
                    bitCount -= 5;
                }
            }
            if (bitCount > 0)
                result.Append(Base32Chars[(bits << (5 - bitCount)) & 0x1F]);
            return result.ToString();
        }

        private static byte[] Base32Decode(string input)
        {
            input = input.Trim().Replace(" ", "").Replace("-", "").ToUpper();
            var bits = 0;
            var bitCount = 0;
            using var ms = new MemoryStream();
            foreach (var c in input)
            {
                var idx = Base32Chars.IndexOf(c);
                if (idx < 0) continue;
                bits = (bits << 5) | idx;
                bitCount += 5;
                if (bitCount >= 8)
                {
                    ms.WriteByte((byte)((bits >> (bitCount - 8)) & 0xFF));
                    bitCount -= 8;
                }
            }
            return ms.ToArray();
        }

        public bool IsTwoFactorRequired => _user.Profil.TwoFactorEnabled && !string.IsNullOrEmpty(_user.Profil.TwoFactorSecret);
    }
}
