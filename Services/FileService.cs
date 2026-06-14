using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;

namespace ADN_pay.Services
{
    public class FileService
    {
        private readonly ILogger<FileService> _logger;

        public FileService(ILogger<FileService> logger)
        {
            _logger = logger;
        }

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx" };

        private static readonly Dictionary<string, byte[][]> MagicBytes = new()
        {
            [".jpg"] = [new byte[] { 0xFF, 0xD8, 0xFF }],
            [".jpeg"] = [new byte[] { 0xFF, 0xD8, 0xFF }],
            [".png"] = [new byte[] { 0x89, 0x50, 0x4E, 0x47 }],
            [".pdf"] = [new byte[] { 0x25, 0x50, 0x44, 0x46 }],
            [".doc"] = [new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }],
            [".docx"] = [new byte[] { 0x50, 0x4B, 0x03, 0x04 }],
        };

        public async Task<string> EnregistrerFichierSurDisque(IBrowserFile f)
        {
            using var stream = f.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024);
            return await SaveFileAsync(f.Name, stream);
        }

        public async Task<string> EnregistrerFichierSurDisque(IFormFile f)
        {
            using var stream = f.OpenReadStream();
            return await SaveFileAsync(f.FileName, stream);
        }

        private async Task<string> SaveFileAsync(string fileName, Stream contentStream)
        {
            var ext = Path.GetExtension(fileName).ToLower();
            if (!AllowedExtensions.Contains(ext))
            {
                _logger.LogWarning("[UPLOAD_BLOCKED] Type de fichier non autorisé : {Ext}", ext);
                return string.Empty;
            }

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var filePath = Path.Combine(uploadsDir, $"{Guid.NewGuid():N}{ext}");

            using var ms = new MemoryStream();
            await contentStream.CopyToAsync(ms);
            var fileBytes = ms.ToArray();

            if (MagicBytes.TryGetValue(ext, out var signatures))
            {
                var header = fileBytes.Take(signatures[0].Length).ToArray();
                if (!signatures.Any(sig => header.SequenceEqual(sig)))
                {
                    _logger.LogWarning("[UPLOAD_BLOCKED] Contenu invalide pour l'extension {Ext} : {Name}", ext, fileName);
                    return string.Empty;
                }
            }

            await File.WriteAllBytesAsync(filePath, fileBytes);
            return $"/uploads/{Path.GetFileName(filePath)}";
        }
    }
}
