using ADN_pay.Services;

namespace ADN_pay.Api.Endpoints;

public static class CreditEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/v1/credit").WithTags("Credit").RequireAuthorization("ApiBearer");
        g.MapGet("/mes-demandes", GetMesDemandes);
        g.MapPost("/", SoumettreDemandeCredit);
        g.MapGet("/eligibilite", VerifierEligibilite);
    }

    private record CreditRequest(long MontantCentimes, string Categorie, int DureeMois);

    private static async Task<IResult> GetMesDemandes(CreditService credit)
    {
        var list = await credit.GetMesDemandesAsync();
        return Results.Ok(list.Select(c => new
        {
            c.Id, c.Categorie, c.Statut, c.DureeMois,
            Montant = c.Montant,
            MontantDh = (c.Montant / 100m).ToString("0.00"),
            TauxAnnuel = c.TauxAnnuel,
            DateDemande = c.DateDemande.ToString("O"),
            c.MotifRejet,
        }));
    }

    private static async Task<IResult> SoumettreDemandeCredit(
        CreditRequest req, CreditService credit, UserContext user)
    {
        if (req.MontantCentimes <= 0 || string.IsNullOrWhiteSpace(req.Categorie) || req.DureeMois <= 0)
            return Results.BadRequest(new { error = "Montant, catégorie et durée requis." });

        var categories = new[] { "MICRO", "PERSONNEL", "BUSINESS" };
        if (!categories.Contains(req.Categorie.ToUpper()))
            return Results.BadRequest(new { error = $"Catégorie invalide. Valeurs : {string.Join(", ", categories)}" });

        var eligible = await credit.VerifierEligibiliteCredit(user.Profil.Id);
        if (!eligible)
            return Results.BadRequest(new { error = "Non éligible au crédit (statut STANDARD ou solde insuffisant)." });

        var ok = await credit.SoumettreDemandeCredit(req.MontantCentimes, req.Categorie.ToUpper(), req.DureeMois);
        return ok
            ? Results.Created("", new { success = true, message = "Demande soumise, en attente de validation." })
            : Results.BadRequest(new { error = "Impossible de soumettre la demande." });
    }

    private static async Task<IResult> VerifierEligibilite(
        CreditService credit, UserContext user)
    {
        var eligible = await credit.VerifierEligibiliteCredit(user.Profil.Id);
        return Results.Ok(new { eligible });
    }
}
