namespace ADN_pay.Services
{
    // Gabarit d'e-mail unique et « client-safe » (tables + CSS inline, compatible
    // Outlook/Gmail/Apple Mail). Toutes les notifications transactionnelles passent
    // par EmailTemplate.Wrap(...) pour une identité de marque cohérente : en-tête
    // dégradé #0099CC→#30B880, corps clair lisible, pied de page discret.
    public static class EmailTemplate
    {
        private const string Blue = "#0099CC";
        private const string Green = "#30B880";
        private const string Ink = "#0c1f3f";
        private const string Text = "#2b2f36";
        private const string Muted = "#8a94a6";
        private const string PageBg = "#eef2f6";
        private const string CardBg = "#ffffff";
        private const string Border = "#e3e9f0";
        private const string LogoUrl = "https://adnpay.net/images/logo_adn_pay_auth.png";

        // Enveloppe une contenu HTML dans la charte ADN_pay.
        //   titre     : titre affiché en haut du corps (H1).
        //   corpsHtml : contenu déjà en HTML (paragraphes via Paragraphe(), CodeBox()…).
        //   preheader : texte d'aperçu (masqué) affiché par la boîte mail avant ouverture.
        public static string Wrap(string titre, string corpsHtml, string? preheader = null)
        {
            var annee = DateTime.UtcNow.Year;
            var preheaderHtml = string.IsNullOrEmpty(preheader) ? "" :
                $@"<div style=""display:none;max-height:0;overflow:hidden;opacity:0;color:{PageBg};font-size:1px;line-height:1px;"">{Escape(preheader)}</div>";

            return $@"<!DOCTYPE html>
<html lang=""fr"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
<meta name=""color-scheme"" content=""light"" />
<title>{Escape(titre)}</title>
</head>
<body style=""margin:0;padding:0;background:{PageBg};"">
{preheaderHtml}
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:{PageBg};padding:24px 12px;"">
  <tr>
    <td align=""center"">
      <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:{CardBg};border:1px solid {Border};border-radius:14px;overflow:hidden;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;"">
        <!-- En-tête dégradé de marque -->
        <tr>
          <td align=""center"" bgcolor=""{Blue}"" style=""background:{Blue};background:linear-gradient(135deg,{Blue} 0%,{Green} 100%);padding:28px 24px;"">
            <img src=""{LogoUrl}"" alt=""ADN_pay"" width=""150"" style=""display:block;border:0;max-width:150px;height:auto;"" />
          </td>
        </tr>
        <!-- Corps -->
        <tr>
          <td style=""padding:32px 32px 12px;"">
            <h1 style=""margin:0 0 18px;font-size:20px;line-height:1.3;color:{Ink};font-weight:700;"">{Escape(titre)}</h1>
            {corpsHtml}
          </td>
        </tr>
        <!-- Pied de page -->
        <tr>
          <td style=""padding:20px 32px 28px;border-top:1px solid {Border};"">
            <p style=""margin:0 0 4px;font-size:12px;line-height:1.6;color:{Muted};"">
              Cet e-mail vous a été envoyé automatiquement par ADN_pay — merci de ne pas y répondre.
            </p>
            <p style=""margin:0;font-size:12px;line-height:1.6;color:{Muted};"">
              ADN_pay · by ADN Corp · <a href=""https://adnpay.net"" style=""color:{Blue};text-decoration:none;"">adnpay.net</a> · © {annee}
            </p>
          </td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</body>
</html>";
        }

        // Paragraphe de corps à la charte.
        public static string Paragraphe(string html) =>
            $@"<p style=""margin:0 0 14px;font-size:15px;line-height:1.65;color:{Text};"">{html}</p>";

        // Encadré de code de vérification (grand, espacé, couleur de marque).
        public static string CodeBox(string code) =>
            $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:8px 0 20px;"">
  <tr><td align=""center"">
    <div style=""display:inline-block;padding:16px 28px;background:#f2f9fc;border:1px solid {Blue};border-radius:10px;
                font-size:30px;font-weight:700;letter-spacing:10px;color:{Ink};font-family:'Courier New',monospace;"">
      {Escape(code)}
    </div>
  </td></tr>
</table>";

        // Encadré d'avertissement discret (ex. « ignorez cet e-mail si… »).
        public static string Note(string html) =>
            $@"<p style=""margin:14px 0 0;font-size:13px;line-height:1.6;color:{Muted};"">{html}</p>";

        // Bouton d'appel à l'action (dégradé de marque, table-based pour Outlook).
        public static string Bouton(string libelle, string url) =>
            $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" style=""margin:10px 0 6px;"">
  <tr><td align=""center"" bgcolor=""{Blue}"" style=""background:{Blue};background:linear-gradient(135deg,{Blue} 0%,{Green} 100%);border-radius:9px;"">
    <a href=""{url}"" style=""display:inline-block;padding:12px 30px;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif;"">{Escape(libelle)}</a>
  </td></tr>
</table>";

        // Échappe un texte utilisateur avant insertion dans le corps HTML.
        public static string Escape(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
