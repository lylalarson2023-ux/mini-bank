using Microsoft.AspNetCore.Components.Forms;
using System.Text.RegularExpressions;

namespace MBANK_ETUDIANT.Services
{
    public class FileService
    {
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx" };

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
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            await f.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024).CopyToAsync(stream);
            return $"/uploads/{fileName}";
        }
    }
}
