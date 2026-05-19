using MBANK_ETUDIANT.Models;

namespace MBANK_ETUDIANT.Services
{
    public class UserContext
    {
        public UserProfile Profil { get; set; } = new();
        public bool EstConnecte { get; set; }
        public UserProfile? KycFormState { get; set; }
    }
}
