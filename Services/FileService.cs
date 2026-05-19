using Microsoft.AspNetCore.Components.Forms;
using System.Text.RegularExpressions;

namespace MBANK_ETUDIANT.Services
{
    public class FileService
    {
        public async Task<string> EnregistrerFichierSurDisque(IBrowserFile f)
        {
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);
            var safeName = Regex.Replace(f.Name, @"[^a-zA-Z0-9._-]", "_");
            var fileName = $"{Guid.NewGuid()}_{safeName}";
            var filePath = Path.Combine(uploadsDir, fileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            await f.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024).CopyToAsync(stream);
            return $"/uploads/{fileName}";
        }
    }
}
