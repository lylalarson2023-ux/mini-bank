using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ADN_pay.Models
{
    public enum FlutterwaveDepositStatus
    {
        Pending,
        Completed,
        Failed
    }

    // Dépôt Mobile Money via la page de paiement hébergée Flutterwave.
    // Le compte est tenu en DH (centimes, ADR-001) mais Flutterwave facture en
    // XAF (Gabon) : on conserve les deux montants, figés à la création.
    public class FlutterwaveDeposit
    {
        [Key]
        public int Id { get; set; }

        // Référence marchand envoyée à Flutterwave (tx_ref) — unique, sert aussi
        // de référence d'idempotence au crédit (« flutterwave:<TxRef> »).
        [Required, MaxLength(64)]
        public string TxRef { get; set; } = "";

        public int UserId { get; set; }

        // Montant crédité au compte en centimes de DH.
        public long MontantDhCentimes { get; set; }

        // Montant facturé via Flutterwave (le XAF n'a pas de décimales).
        public long MontantXaf { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "XAF";

        public FlutterwaveDepositStatus Status { get; set; } = FlutterwaveDepositStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Dernière réponse de vérification (audit/débogage).
        public string? RawJson { get; set; }

        [ForeignKey(nameof(UserId))]
        public UserProfile? User { get; set; }
    }
}
