using System.Globalization;

namespace ADN_pay.Services
{
    public enum Devise { DH, EUR, XAF }

    // Convertit le solde (stocké en centimes de DH, ADR-001) en DH / EUR / FCFA
    // pour l'affichage. Chaque appelant demande explicitement la devise voulue
    // (plus de « devise courante » : le header affiche l'euro, le dashboard le
    // FCFA, les montants métier restent en DH).
    // Taux configurables (Devises:*) — pas de taux « live » pour l'instant.
    // Décision produit : 1 € = 10 DH = 650 FCFA.
    public class CurrencyState
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly decimal _dhParEur;   // 1 EUR = X DH
        private readonly decimal _xafParDh;   // 1 DH  = Y FCFA

        public CurrencyState(IConfiguration config)
        {
            _dhParEur = config.GetValue<decimal?>("Devises:DhParEur") ?? 10m;
            _xafParDh = config.GetValue<decimal?>("Devises:XafParDh") ?? 65m;
        }

        public static string Symbole(Devise devise) => devise switch
        {
            Devise.EUR => "€",
            Devise.XAF => "FCFA",
            _ => "DH"
        };

        // Valeur réelle du montant (centimes DH) dans la devise demandée.
        public decimal Convertir(long centimesDh, Devise devise)
        {
            var dh = centimesDh / 100m;
            return devise switch
            {
                Devise.EUR => dh / _dhParEur,
                Devise.XAF => dh * _xafParDh,
                _ => dh
            };
        }

        // Montant formaté avec symbole (le FCFA n'a pas de décimales).
        public string Formater(long centimesDh, Devise devise)
        {
            var v = Convertir(centimesDh, devise);
            var dec = devise == Devise.XAF ? 0 : 2;
            return v.ToString("N" + dec, Fr) + " " + Symbole(devise);
        }
    }
}
