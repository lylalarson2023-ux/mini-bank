using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MBANK_ETUDIANT.Models
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
        
        public decimal Solde { get; set; } = 0m;
        public decimal Dette { get; set; } = 0m;
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
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString();

        // --- PLAFONDS TRANSACTIONS ---
        public decimal PlafondJournalier { get; set; } = 5000m;
        public decimal PlafondMensuel { get; set; } = 50000m;
        public DateTime DerniereReinitPlafond { get; set; } = DateTime.UtcNow;
        public decimal MontantJournalierUtilise { get; set; }
        public decimal MontantMensuelUtilise { get; set; }

        // --- PRÉFÉRENCES NOTIFICATIONS ---
        public bool NotifConnexion { get; set; } = true;
        public bool NotifVirement { get; set; } = true;
        public bool NotifDepot { get; set; } = true;
        public bool NotifRetrait { get; set; } = true;
        public bool NotifEpargne { get; set; } = true;
        public bool NotifCredit { get; set; } = true;
        public bool NotifPromo { get; set; } = false;

        // --- WORKFLOW ADMINISTRATIF ---
        public bool PendingPremiumUpgrade { get; set; } = false;
        public bool PendingCreditRequest { get; set; } = false;
        public decimal PendingCreditAmount { get; set; } = 0m;
        public string PendingCreditMotif { get; set; } = "";
        public bool IsAdmin { get; set; } = false;
        public DateTime? PremiumValidatedAt { get; set; }
        public DateTime? PremiumRejectedAt { get; set; }

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
