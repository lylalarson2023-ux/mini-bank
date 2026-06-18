using System.Security.Claims;
using ADN_pay.Data;
using ADN_pay.Services;
using Microsoft.EntityFrameworkCore;

namespace ADN_pay.Api.Middleware;

// IMiddleware = résolu depuis le DI par requête → supporte les dépendances scoped.
public class UserContextMiddleware : IMiddleware
{
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly UserContext _userCtx;

    public UserContextMiddleware(IDbContextFactory<BankDbContext> dbFactory, UserContext userCtx)
    {
        _dbFactory = dbFactory;
        _userCtx = userCtx;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.UserProfiles.FindAsync(userId);
                if (user != null && !user.CompteCloture)
                {
                    _userCtx.Profil = user;
                    _userCtx.EstConnecte = true;
                }
            }
        }
        await next(context);
    }
}
