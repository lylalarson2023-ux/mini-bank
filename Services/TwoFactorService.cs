using System.Security.Cryptography;
using ADN_pay.Data;
using ADN_pay.Models;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Services
{
    public class TwoFactorService
    {
        private readonly BankDbContext _context;
        private readonly UserContext _user;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(BankDbContext context, UserContext user, ILogger<TwoFactorService> logger)
        {
            _context = context;
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

        public async Task<(bool Success, string Message)> EnableAsync(string code)
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return (false, "Utilisateur introuvable");
            var secret = _user.Profil.TwoFactorSecret ?? u.TwoFactorSecret;
            if (string.IsNullOrEmpty(secret))
                return (false, "Aucun secret configuré. Générez d'abord un secret.");

            if (!VerifyCodeWithWindow(secret, code))
                return (false, "Code invalide. Vérifiez votre application d'authentification.");

            u.TwoFactorSecret = secret;
            u.TwoFactorEnabled = true;
            _context.UserProfiles.Update(u);
            await _context.SaveChangesAsync();
            _user.Profil.TwoFactorEnabled = true;
            _logger.LogInformation("2FA activée pour {Email}", _user.Profil.Email);
            return (true, "Authentification à deux facteurs activée.");
        }

        public async Task DisableAsync()
        {
            var u = await _context.UserProfiles.FindAsync(_user.Profil.Id);
            if (u == null) return;
            u.TwoFactorEnabled = false;
            u.TwoFactorSecret = null;
            _context.UserProfiles.Update(u);
            await _context.SaveChangesAsync();
            _user.Profil.TwoFactorEnabled = false;
            _user.Profil.TwoFactorSecret = null;
            _logger.LogInformation("2FA désactivée pour {Email}", _user.Profil.Email);
        }

        public async Task<bool> UserHasTwoFactorAsync(int userId)
        {
            var u = await _context.UserProfiles.FindAsync(userId);
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
