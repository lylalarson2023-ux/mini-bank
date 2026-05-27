using Microsoft.AspNetCore.Components.Forms;

namespace MBANK_ETUDIANT.Services
{
    public class FileService
    {
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
            var ext = Path.GetExtension(f.Name).ToLower();
            if (!AllowedExtensions.Contains(ext))
            {
                Console.WriteLine($"[UPLOAD_BLOCKED] Type de fichier non autorisé : {ext}");
                return string.Empty;
            }

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var filePath = Path.Combine(uploadsDir, $"{Guid.NewGuid():N}{ext}");

            // Vérifier les magic bytes avant d'écrire
            using (var uploadStream = f.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024))
            {
                if (MagicBytes.TryGetValue(ext, out var signatures))
                {
                    var header = new byte[signatures[0].Length];
                    var bytesRead = await uploadStream.ReadAsync(header, 0, header.Length);
                    if (bytesRead < header.Length || !signatures.Any(sig => header.Take(sig.Length).SequenceEqual(sig)))
                    {
                        Console.WriteLine($"[UPLOAD_BLOCKED] Contenu invalide pour l'extension {ext} : {f.Name}");
                        return string.Empty;
                    }
                    uploadStream.Seek(0, SeekOrigin.Begin);
                }

                using var fs = new FileStream(filePath, FileMode.Create);
                await uploadStream.CopyToAsync(fs);
            }

            return $"/uploads/{Path.GetFileName(filePath)}";
        }
    }
}
