using ADN_pay.Services;

namespace ADN_pay.Tests;

// Gabarit d'e-mail de marque : structure attendue + échappement du titre.
public class EmailTemplateTests
{
    [Fact]
    public void Wrap_ContientTitreLogoContenuEtPreheader()
    {
        var html = EmailTemplate.Wrap("Mon titre", EmailTemplate.Paragraphe("Bonjour"), preheader: "apercu masque");

        Assert.Contains("Mon titre", html);                     // H1 + <title>
        Assert.Contains("logo_adn_pay_auth.png", html);         // logo de marque
        Assert.Contains("adnpay.net", html);                    // pied de page
        Assert.Contains("Bonjour", html);                       // corps injecté
        Assert.Contains("apercu masque", html);                 // preheader
    }

    [Fact]
    public void CodeBox_ContientLeCode()
    {
        Assert.Contains("482913", EmailTemplate.CodeBox("482913"));
    }

    [Fact]
    public void Wrap_EchappeLeTitre_PasDInjection()
    {
        var html = EmailTemplate.Wrap("<script>alert(1)</script>", "", null);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
