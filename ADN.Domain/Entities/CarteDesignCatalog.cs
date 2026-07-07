namespace ADN_pay.Models
{
    // Un design de carte de la galerie : dégradé CSS + couleur de texte,
    // et le statut minimum qui le débloque (déblocage cumulatif : Premium
    // voit Standard+Premium, VIP voit tout).
    public sealed record CarteDesign(
        string Id,           // slug persisté dans UserProfile.CarteDesign
        string Nom,          // nom affiché dans la galerie
        UserStatus StatutMin,
        string Gradient,     // background CSS complet
        string Texte,        // couleur du texte de la carte
        string? Bord = null, // liseré optionnel (designs VIP noirs)
        bool FondClair = false); // fond pastel → filigrane sombre côté CSS

    // Catalogue statique des 18 designs (specs refonte UI, validées).
    // La validation serveur (AccountService) et l'UI partagent cette source unique.
    public static class CarteDesigns
    {
        public const string DefautId = "bleu";

        public static readonly IReadOnlyList<CarteDesign> Tous = new List<CarteDesign>
        {
            // ── Standard (4) ──
            new("bleu",          "Bleu ADN",           UserStatus.STANDARD, "linear-gradient(135deg, #0c1f3f 0%, #005f7a 40%, #007a5e 100%)", "#ffffff"),
            new("menthe-vif",    "Menthe vive",        UserStatus.STANDARD, "linear-gradient(135deg, #003b4d 0%, #00a8a0 40%, #30e0a0 100%)", "#ffffff"),
            new("cuivre",        "Cuivre",             UserStatus.STANDARD, "linear-gradient(135deg, #2b1810 0%, #8a4a3a 40%, #d98a70 100%)", "#ffffff"),
            new("emeraude",      "Émeraude profond",   UserStatus.STANDARD, "linear-gradient(135deg, #06120d 0%, #0f3d2a 40%, #1c6b46 100%)", "#ffffff"),

            // ── Premium (+6 = 10 cumulés) ──
            new("gold",          "Gold",               UserStatus.PREMIUM,  "linear-gradient(135deg, #1a1305 0%, #7a5c05 40%, #f59e0b 100%)", "#ffffff"),
            new("graphite",      "Graphite",           UserStatus.PREMIUM,  "linear-gradient(135deg, #1c1c22 0%, #3a3a45 40%, #6b6b78 100%)", "#ffffff"),
            new("menthe-pastel", "Menthe pastel",      UserStatus.PREMIUM,  "linear-gradient(135deg, #eafff9 0%, #d3f5ec 40%, #b8ecd8 100%)", "#04463c", FondClair: true),
            new("glace",         "Glace",              UserStatus.PREMIUM,  "linear-gradient(135deg, #f2fbff 0%, #dff3f2 40%, #cdeee0 100%)", "#062a26", FondClair: true),
            new("blush",         "Blush",              UserStatus.PREMIUM,  "linear-gradient(135deg, #fff3ee 0%, #fbdccb 40%, #f2c3a8 100%)", "#5c2e1a", FondClair: true),
            new("champagne",     "Champagne",          UserStatus.PREMIUM,  "linear-gradient(135deg, #f7fbf5 0%, #e3f0da 40%, #cfe6c2 100%)", "#123d22", FondClair: true),

            // ── VIP (+8 = 18 cumulés) ──
            new("dore-pale",     "Doré pâle",          UserStatus.VIP,      "linear-gradient(135deg, #fff8e8 0%, #fde8c0 40%, #f5d38a 100%)", "#5c3d05", FondClair: true),
            new("platine",       "Platine",            UserStatus.VIP,      "linear-gradient(135deg, #f4f4f7 0%, #e2e2ea 40%, #c9c9d6 100%)", "#2c2c34", FondClair: true),
            new("noir-or",       "Noir & or",          UserStatus.VIP,      "linear-gradient(135deg, #000000 0%, #0d0d12 40%, #1a1a22 100%)", "#d4af37", Bord: "#d4af37"),
            new("bleu-nuit",     "Bleu nuit profond",  UserStatus.VIP,      "linear-gradient(135deg, #071022 0%, #0d2b3d 40%, #146356 100%)", "#ffffff"),
            new("onyx-teal",     "Onyx teal",          UserStatus.VIP,      "linear-gradient(135deg, #000000 0%, #060b0c 40%, #0c1414 100%)", "#30e0a0", Bord: "#30e0a0"),
            new("noir-emeraude", "Noir émeraude & or", UserStatus.VIP,      "linear-gradient(135deg, #000000 0%, #04110a 40%, #0a1f14 100%)", "#d4af37", Bord: "#d4af37"),
            new("noir-argent",   "Noir & argent",      UserStatus.VIP,      "linear-gradient(135deg, #000000 0%, #0a0a0f 40%, #141420 100%)", "#e5e5ea", Bord: "#e5e5ea"),
            new("violet-royal",  "Violet royal",       UserStatus.VIP,      "linear-gradient(135deg, #140a24 0%, #3b1f63 40%, #6a3fa0 100%)", "#ffffff"),
        };

        // Slug inconnu ou vide → design par défaut (jamais null côté UI).
        public static CarteDesign ParId(string? id) =>
            Tous.FirstOrDefault(d => d.Id == id) ?? Tous[0];

        public static bool EstDebloque(CarteDesign design, UserStatus statut) =>
            Rang(statut) >= Rang(design.StatutMin);

        public static List<CarteDesign> Debloques(UserStatus statut) =>
            Tous.Where(d => EstDebloque(d, statut)).ToList();

        // PENDING est traité comme STANDARD (rang 0).
        private static int Rang(UserStatus statut) => statut switch
        {
            UserStatus.VIP => 2,
            UserStatus.PREMIUM => 1,
            _ => 0
        };
    }
}
