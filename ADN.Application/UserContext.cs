using ADN_pay.Models;

namespace ADN_pay.Services
{
    public class UserContext
    {
        public UserProfile Profil { get; set; } = new();
        public bool EstConnecte { get; set; }
        public UserProfile? KycFormState { get; set; }
    }
}
