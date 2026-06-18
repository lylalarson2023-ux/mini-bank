using ADN_pay.Services;

namespace ADN_pay.Api.Endpoints;

public static class SavingsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/v1/savings").WithTags("Savings").RequireAuthorization("ApiBearer");
        g.MapGet("/", GetPockets);
        g.MapPost("/", CreatePocket);
        g.MapDelete("/{id:int}", BreakPocket);
        g.MapPost("/{id:int}/boost", BoostPocket);
        g.MapGet("/tuteur", GetPocketsAsTuteur);
        g.MapPost("/{id:int}/boost-tuteur", BoostAsTuteur);
    }

    private record CreatePocketRequest(string Objectif, long MontantInitialCentimes, DateTime Cible,
        long MontantCibleCentimes = 0L, bool TuteurVisible = false);
    private record BoostRequest(long MontantCentimes);

    private static async Task<IResult> GetPockets(SavingsService savings)
    {
        var list = await savings.GetPocketsAsync();
        return Results.Ok(list.Select(p => new
        {
            p.Id, p.Objectif, p.TuteurVisible,
            MontantActuel = p.MontantActuel,
            MontantActuelDh = (p.MontantActuel / 100m).ToString("0.00"),
            MontantCible = p.MontantCible,
            MontantCibleDh = (p.MontantCible / 100m).ToString("0.00"),
            Cible = p.Cible.ToString("yyyy-MM-dd"),
            ProgressPct = p.MontantCible > 0 ? Math.Round(p.MontantActuel * 100m / p.MontantCible, 1) : 0m,
        }));
    }

    private static async Task<IResult> CreatePocket(CreatePocketRequest req, SavingsService savings)
    {
        if (string.IsNullOrWhiteSpace(req.Objectif) || req.MontantInitialCentimes <= 0)
            return Results.BadRequest(new { error = "Objectif et montant requis." });

        var ok = await savings.CreerPocheEpargne(
            req.Objectif, req.MontantInitialCentimes, req.Cible,
            req.MontantCibleCentimes, req.TuteurVisible);
        return ok
            ? Results.Created("", new { success = true })
            : Results.BadRequest(new { error = "Solde insuffisant." });
    }

    private static async Task<IResult> BreakPocket(int id, SavingsService savings)
    {
        var ok = await savings.CasserPocheEpargne(id);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Poche introuvable." });
    }

    private static async Task<IResult> BoostPocket(int id, BoostRequest req, SavingsService savings)
    {
        if (req.MontantCentimes <= 0)
            return Results.BadRequest(new { error = "Montant invalide." });

        var ok = await savings.BoosterPocheAsync(id, req.MontantCentimes);
        return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Boost refusé (solde insuffisant ou poche introuvable)." });
    }

    private static async Task<IResult> GetPocketsAsTuteur(SavingsService savings)
    {
        var list = await savings.GetPocketsForTuteurAsync();
        return Results.Ok(list.Select(v => new
        {
            v.StudentEmail, v.StudentName,
            Pocket = new
            {
                v.Pocket.Id, v.Pocket.Objectif,
                MontantActuel = v.Pocket.MontantActuel,
                MontantActuelDh = (v.Pocket.MontantActuel / 100m).ToString("0.00"),
                Cible = v.Pocket.Cible.ToString("yyyy-MM-dd"),
            },
        }));
    }

    private static async Task<IResult> BoostAsTuteur(int id, BoostRequest req, SavingsService savings)
    {
        var (ok, msg) = await savings.BoosterPocheCommeTuteurAsync(id, req.MontantCentimes);
        return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = msg });
    }
}
