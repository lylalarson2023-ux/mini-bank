using ADN_pay.Data;
using ADN_pay.Models;
using ADN_pay.Services;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Api.Endpoints;

public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/v1/account").WithTags("Account").RequireAuthorization("ApiBearer");
        g.MapGet("/me", GetMe);
        g.MapGet("/transactions", GetTransactions);
        g.MapPost("/virement", Virement);
        g.MapGet("/beneficiaires", GetBeneficiaires);
        g.MapPost("/beneficiaires", AddBeneficiaire);
        g.MapDelete("/beneficiaires/{id:int}", DeleteBeneficiaire);
        g.MapGet("/balance-curve", GetBalanceCurve);
    }

    private record VirementRequest(string EmailDestinataire, long MontantCentimes, string Motif);
    private record AddBeneficiaireRequest(string Nom, string Email, string? Banque, string? Rib);

    private static IResult GetMe(UserContext user) => Results.Ok(new
    {
        user.Profil.Id,
        user.Profil.Email,
        user.Profil.Prenom,
        user.Profil.Nom,
        user.Profil.Telephone,
        Solde = user.Profil.Solde,
        SoldeDh = (user.Profil.Solde / 100m).ToString("0.00"),
        Statut = user.Profil.Statut.ToString(),
        user.Profil.IsAdmin,
        user.Profil.TwoFactorEnabled,
        DateInscription = user.Profil.DateInscription.ToString("O"),
        user.Profil.TuteurEmail,
        user.Profil.TuteurAutorise,
        Plafonds = new
        {
            JournalierCentimes = user.Profil.PlafondJournalier,
            MensuelCentimes = user.Profil.PlafondMensuel,
            JournalierUtilise = user.Profil.MontantJournalierUtilise,
            MensuelUtilise = user.Profil.MontantMensuelUtilise,
        },
    });

    private static async Task<IResult> GetTransactions(
        UserContext user, AccountService accountService,
        int page = 1, int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var all = await accountService.GetHistoriqueAsync();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).Select(t => new
        {
            t.Id, t.Type, t.Montant,
            MontantDh = (t.Montant / 100m).ToString("0.00"),
            t.Motif, t.Libelle,
            SoldeApresDh = (t.SoldeApres / 100m).ToString("0.00"),
            Date = t.Date.ToString("O"),
        });
        return Results.Ok(new { page, pageSize, total = all.Count, items });
    }

    private static async Task<IResult> Virement(
        VirementRequest req, AccountService accountService)
    {
        if (string.IsNullOrWhiteSpace(req.EmailDestinataire) || req.MontantCentimes <= 0)
            return Results.BadRequest(new { error = "Destinataire et montant requis." });

        var ok = await accountService.EffectuerVirementAsync(req.EmailDestinataire, req.MontantCentimes, req.Motif ?? "");
        return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "Virement refusé (solde insuffisant ou destinataire introuvable)." });
    }

    private static async Task<IResult> GetBeneficiaires(AccountService accountService)
    {
        var list = await accountService.GetBeneficiairesAsync();
        return Results.Ok(list.Select(b => new
        {
            b.Id, b.Nom, b.Email, b.Banque, b.RIB,
            DateAjout = b.DateAjout.ToString("O"),
        }));
    }

    private static async Task<IResult> AddBeneficiaire(
        AddBeneficiaireRequest req, AccountService accountService)
    {
        var (ok, msg) = await accountService.AjouterBeneficiaireAsync(req.Nom, req.Email, req.Banque, req.Rib);
        return ok ? Results.Created("", new { success = true }) : Results.BadRequest(new { error = msg });
    }

    private static async Task<IResult> DeleteBeneficiaire(int id, AccountService accountService)
    {
        var ok = await accountService.SupprimerBeneficiaireAsync(id);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { error = "Bénéficiaire introuvable." });
    }

    private static async Task<IResult> GetBalanceCurve(AccountService accountService)
    {
        var curve = await accountService.GetBalanceCurve30DaysAsync();
        return Results.Ok(curve.Select(c => new
        {
            Jour = c.Jour.ToString("yyyy-MM-dd"),
            Solde = c.Solde,
            SoldeDh = (c.Solde / 100m).ToString("0.00"),
        }));
    }
}
