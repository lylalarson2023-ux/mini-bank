using ADN_pay.Models;

namespace ADN_pay.Tests;

// Frais de change du corridor Gabon<->Maroc (canal Alex) : calcul pur, sans I/O.
// Dépôt : marge déduite du crédit → frais = montant × m/(1−m).
// Retrait : marge ajoutée au débit → frais = montant × m/(1+m).
public class TransfertInternationalPricingTests
{
    [Fact]
    public void DepotFraisCentimes_MargeDeduite()
    {
        // 140,40 DH crédités, marge 10% : 14040 × 0,10/0,90 = 15,60 DH de frais.
        Assert.Equal(1_560L, TransfertInternationalPricing.DepotFraisCentimes(14_040L, 0.10m));
    }

    [Fact]
    public void RetraitFraisCentimes_MargeAjoutee()
    {
        // 107 DH débités, marge 7% : 10700 × 0,07/1,07 = 7,00 DH de frais.
        Assert.Equal(700L, TransfertInternationalPricing.RetraitFraisCentimes(10_700L, 0.07m));
    }

    [Fact]
    public void Frais_MargeNulle_EstZero()
    {
        Assert.Equal(0L, TransfertInternationalPricing.DepotFraisCentimes(14_040L, 0m));
        Assert.Equal(0L, TransfertInternationalPricing.RetraitFraisCentimes(10_700L, 0m));
        Assert.Equal(0L, TransfertInternationalPricing.FraisFcfa(10_000L, 0m));
    }

    [Fact]
    public void Frais_MontantNonPositif_EstZero()
    {
        Assert.Equal(0L, TransfertInternationalPricing.DepotFraisCentimes(0L, 0.10m));
        Assert.Equal(0L, TransfertInternationalPricing.RetraitFraisCentimes(-1L, 0.07m));
    }

    [Fact]
    public void FraisFcfa_FractionMargeDuMontantConverti()
    {
        // Cohérent avec l'écran : 10 000 FCFA envoyés, marge 10% → 1 000 FCFA de frais.
        Assert.Equal(1_000L, TransfertInternationalPricing.FraisFcfa(10_000L, 0.10m));
        // Retrait : 8 444 FCFA reçus, marge 7% → ~591 FCFA de frais.
        Assert.Equal(591L, TransfertInternationalPricing.FraisFcfa(8_444L, 0.07m));
    }

    [Fact]
    public void FraisCentimes_CoherentAvecLaConversionFcfa()
    {
        // Le frais DH doit correspondre (à l'arrondi près) au FCFA de marge converti :
        // dépôt 140,40 DH → 10 000 FCFA envoyés, dont 1 000 FCFA de frais = 15,60 DH.
        var fcfaEnvoyes = TransfertInternationalPricing.DepotFcfaAEnvoyer(14_040L, 0.0156m, 0.10m);
        Assert.Equal(10_000L, fcfaEnvoyes);
        var fraisFcfa = TransfertInternationalPricing.FraisFcfa(fcfaEnvoyes, 0.10m);
        Assert.Equal(1_000L, fraisFcfa);
        Assert.Equal(1_560L, TransfertInternationalPricing.DepotFraisCentimes(14_040L, 0.10m));
    }
}
