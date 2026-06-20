using ADN_pay.Models;

namespace ADN_pay.Services
{
    // Axe de tri choisi par l'administrateur dans la liste classée.
    public enum ScoringMode
    {
        Composite,   // valeur + engagement + fidélité + statut (défaut)
        Valeur,      // patrimoine : solde + épargne
        Engagement,  // activité : nb de transactions (+ activité récente)
        Risque       // exposition : dette, ratio dette / actifs
    }

    // Vue d'un utilisateur enrichie de ses sous-scores (chacun normalisé sur 0..100
    // par rapport au maximum de la population, pour rester comparable et lisible).
    public class ScoredUser
    {
        public UserProfile User { get; set; } = null!;

        public long EpargneTotale { get; set; }
        public int NbTransactions { get; set; }
        public int NbTransactions30j { get; set; }
        public int AncienneteJours { get; set; }

        public double ScoreValeur { get; set; }
        public double ScoreEngagement { get; set; }
        public double ScoreFidelite { get; set; }
        public double ScoreStatut { get; set; }
        public double ScoreRisque { get; set; }
        public double ScoreComposite { get; set; }

        // Valeur affichée/triée selon l'axe actif.
        public double ScorePour(ScoringMode mode) => mode switch
        {
            ScoringMode.Valeur => ScoreValeur,
            ScoringMode.Engagement => ScoreEngagement,
            ScoringMode.Risque => ScoreRisque,
            _ => ScoreComposite
        };
    }
}
