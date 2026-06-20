using ADN_pay.Services;

namespace ADN_pay.Api.Endpoints;

// Endpoints REST réservés à l'administrateur (rôle Admin via JWT, policy "ApiAdmin").
// Donne une vue programmatique complète sur l'activité des utilisateurs (mobile, scripts, BI).
public static class AdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization("ApiAdmin");
        g.MapGet("/transactions", GetTransactions);
        g.MapGet("/users/scored", GetScored);
    }

    private static async Task<IResult> GetTransactions(
        AdminService admin,
        int? userId = null, string? type = null,
        DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 50)
    {
        var r = await admin.GetAllTransactionsAsync(userId, type, from, to, page, pageSize);
        return Results.Ok(new
        {
            page,
            pageSize,
            total = r.Total,
            totalEntreesDh = (r.TotalEntrees / 100m).ToString("0.00"),
            totalSortiesDh = (r.TotalSorties / 100m).ToString("0.00"),
            items = r.Items.Select(t => new
            {
                t.Id,
                t.UserId,
                t.UserNom,
                t.UserEmail,
                t.Type,
                t.Montant,
                MontantDh = (t.Montant / 100m).ToString("0.00"),
                t.Libelle,
                t.Motif,
                SoldeApresDh = (t.SoldeApres / 100m).ToString("0.00"),
                Date = t.Date.ToString("O"),
            }),
        });
    }

    private static async Task<IResult> GetScored(AdminService admin, string mode = "Composite")
    {
        if (!Enum.TryParse<ScoringMode>(mode, ignoreCase: true, out var m))
            m = ScoringMode.Composite;

        var list = await admin.GetUsersScoredAsync(m);
        return Results.Ok(list.Select(s => new
        {
            s.User.Id,
            Nom = s.User.Prenom + " " + s.User.Nom,
            s.User.Email,
            Statut = s.User.Statut.ToString(),
            SoldeDh = (s.User.Solde / 100m).ToString("0.00"),
            EpargneDh = (s.EpargneTotale / 100m).ToString("0.00"),
            s.NbTransactions,
            s.NbTransactions30j,
            s.AncienneteJours,
            s.ScoreValeur,
            s.ScoreEngagement,
            s.ScoreFidelite,
            s.ScoreStatut,
            s.ScoreRisque,
            s.ScoreComposite,
        }));
    }
}
