namespace ADN_pay.Models
{
    // Niveau d'un signalement de contrôle KYC (du plus léger au plus grave).
    public enum KycSeverite { Info, Attention, Alerte }

    // Un signalement produit par les contrôles automatiques de cohérence / anti-fraude
    // sur un dossier KYC. Purement informatif : n'empêche pas la décision de l'admin,
    // mais attire son attention (doublons, pièces manquantes, incohérences…).
    public record KycFlag(KycSeverite Severite, string Message);
}
