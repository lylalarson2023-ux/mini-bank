using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ADN_pay.Models
{
    public enum UserStatus { STANDARD, PREMIUM, VIP, PENDING }

    public class UserProfile
    {
        [Key]
        public int Id { get; set; }
        public string Nom { get; set; } = "";
        public string Prenom { get; set; } = "";
        public string Email { get; set; } = "";

        [JsonIgnore]
        public string MotDePasse { get; set; } = "";
        public string MotDePasseHash { get; set; } = "";

        // ADR-001 : montants en centimes (long)
        public long Solde { get; set; } = 0L;
        public long Dette { get; set; } = 0L;
        public UserStatus Statut { get; set; } = UserStatus.STANDARD;
        public int NombreTransactions { get; set; } = 0;
        public DateTime DerniereMajInterets { get; set; } = DateTime.UtcNow;
        public DateTime DateInscription { get; set; } = DateTime.UtcNow;

        // --- MODULE KYC ---
        public DateTime? DateNaissance { get; set; }
        public string Genre { get; set; } = "";
        public string LieuNaissance { get; set; } = "";
        public string Nationalite { get; set; } = "";
        public string PassportOuCIN { get; set; } = "";
        public string SituationMatrimoniale { get; set; } = "";
        public string AdresseCasablanca { get; set; } = "";
        public string Ville { get; set; } = "";
        public string CodePostal { get; set; } = "";
        public string Telephone { get; set; } = "";
        public bool CguAcceptees { get; set; } = false;

        // --- MODULE ACADÉMIQUE ---
        public string StatutEtudiant { get; set; } = "";
        public string Etablissement { get; set; } = "";
        public string Filiere { get; set; } = "";
        public string NiveauEtude { get; set; } = "";
        public string AnneeEtude { get; set; } = "";
        public string MatriculeEtudiant { get; set; } = "";

        // --- KYC PREMIUM ADAPTÉ AU STATUT (refonte UI) ---
        public string StatutKyc { get; set; } = "";        // "ETUDIANT" | "TRAVAILLEUR"
        public string UrgenceNom { get; set; } = "";       // contact d'urgence
        public string UrgenceTelephone { get; set; } = "";
        public string SourceFonds { get; set; } = "";      // liste d'options selon le statut
        public string SelfieUrl { get; set; } = "";        // selfie de vérification
        // Volet travailleur
        public string Profession { get; set; } = "";
        public string Employeur { get; set; } = "";
        public string Secteur { get; set; } = "";
        public string TrancheRevenu { get; set; } = "";

        // --- DOCUMENTS (URLs) ---
        public string ReseauPrincipal { get; set; } = "";
        public string DocIdentiteUrl { get; set; } = "";
        public string DocDomicileUrl { get; set; } = "";
        public string DocScolariteUrl { get; set; } = "";
        public string PhotoUrl { get; set; } = "";
        public string CvUrl { get; set; } = "";

        // --- TUTEUR ---
        public string TuteurEmail { get; set; } = "";
        public bool TuteurAutorise { get; set; } = false;

        // --- SÉCURITÉ ---
        public string SecurityStamp { get; set; } = "";

        // --- PLAFONDS TRANSACTIONS (centimes) ---
        public long PlafondJournalier { get; set; } = 500_000L;   // 5 000 DH
        public long PlafondMensuel { get; set; } = 5_000_000L;    // 50 000 DH
        public DateTime DerniereReinitPlafond { get; set; } = DateTime.UtcNow;
        public long MontantJournalierUtilise { get; set; } = 0L;
        public long MontantMensuelUtilise { get; set; } = 0L;

        // --- PRÉFÉRENCES NOTIFICATIONS ---
        public bool NotifConnexion { get; set; } = true;
        public bool NotifVirement { get; set; } = true;
        public bool NotifDepot { get; set; } = true;
        public bool NotifRetrait { get; set; } = true;
        public bool NotifEpargne { get; set; } = true;
        public bool NotifCredit { get; set; } = true;
        public bool NotifPromo { get; set; } = false;

        // --- 2FA ---
        public bool TwoFactorEnabled { get; set; } = false;
        public string? TwoFactorSecret { get; set; }
        // Codes de secours à usage unique : hachés (BCrypt), séparés par ';'
        public string? TwoFactorRecoveryCodes { get; set; }

        // --- WORKFLOW ADMINISTRATIF ---
        public bool PendingPremiumUpgrade { get; set; } = false;
        public bool PendingCreditRequest { get; set; } = false;
        public long PendingCreditAmount { get; set; } = 0L;
        public string PendingCreditMotif { get; set; } = "";
        public bool IsAdmin { get; set; } = false;
        public DateTime? PremiumValidatedAt { get; set; }
        public DateTime? PremiumRejectedAt { get; set; }
        public string? KycRejetMotif { get; set; }

        public string Role { get; set; } = "";

        // --- CLÔTURE DE COMPTE (droit à l'effacement RGPD/Loi 09-08 + rétention AML loi 43-05) ---
        public bool CompteCloture { get; set; } = false;
        public DateTime? DateCloture { get; set; }

        // --- BLOCAGE ADMIN (suspension réversible : connexion + opérations refusées) ---
        public bool Bloque { get; set; } = false;

        // --- VÉRIFICATION CHANGEMENT D'E-MAIL (code envoyé à la nouvelle adresse) ---
        public string? PendingEmail { get; set; }
        public string? EmailChangeCodeHash { get; set; }
        public DateTime? EmailChangeCodeExpiry { get; set; }

        // --- ADRESSE ADN_pay RÉSERVÉE (@adnpay.ma) ---
        // Identifiant réservé à l'inscription (la vraie boîte mail sera branchée plus tard).
        public string AdnEmail { get; set; } = "";

        // --- PERSONNALISATION ---
        // Design de carte choisi dans la galerie (slug du catalogue CarteDesigns).
        // Vide = design par défaut. Déblocage cumulatif selon le statut (validé serveur).
        public string CarteDesign { get; set; } = "";

        // --- RELATIONS ---
        public List<Transaction> Transactions { get; set; } = new();
        public List<SavingsPocket> SavingsPockets { get; set; } = new();

        // --- MÉTHODES UTILES ---
        public string GetFullName() => $"{Prenom} {Nom}";
        public string GetInitials() => $"{Prenom.FirstOrDefault()}{Nom.FirstOrDefault()}".ToUpper();
        public string GetRank()
        {
            return Statut switch
            {
                UserStatus.STANDARD => "Standard",
                UserStatus.PREMIUM => "Premium",
                UserStatus.VIP => "VIP",
                _ => "Inconnu"
            };
        }
    }
}
