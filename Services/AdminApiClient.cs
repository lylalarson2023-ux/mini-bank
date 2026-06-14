using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ADN_pay.Models;

namespace ADN_pay.Services;

public class AdminApiClient
{
    private readonly HttpClient _http;
    private string? _token;
    private string? _refreshToken;
    private readonly ILogger<AdminApiClient> _logger;

    public AdminApiClient(HttpClient http, ILogger<AdminApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    private void SetAuth() => _http.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

    private async Task<T?> GetJsonAsync<T>(string url) where T : class
    {
        var r = await _http.GetAsync(url);
        if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized && await TryRefreshAsync())
        {
            r = await _http.GetAsync(url);
        }
        if (!r.IsSuccessStatusCode) return null;
        return await r.Content.ReadFromJsonAsync<T>();
    }

    private async Task<bool> PostAsync(string url, object? body = null)
    {
        var r = body != null ? await _http.PostAsJsonAsync(url, body) : await _http.PostAsync(url, null);
        if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized && await TryRefreshAsync())
        {
            r = body != null ? await _http.PostAsJsonAsync(url, body) : await _http.PostAsync(url, null);
        }
        return r.IsSuccessStatusCode;
    }

    private async Task<bool> TryRefreshAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken)) return false;
        try
        {
            var r = await _http.PostAsJsonAsync("/api/token/refresh/", new { refresh = _refreshToken });
            if (!r.IsSuccessStatusCode) return false;
            var data = await r.Content.ReadFromJsonAsync<RefreshResponse>();
            if (data?.Access == null) return false;
            _token = data.Access;
            SetAuth();
            _logger.LogInformation("AdminApi: token refreshé");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminApi: échec refresh token");
            return false;
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            var r = await _http.PostAsJsonAsync("/api/auth/login/", new { username, password });
            if (!r.IsSuccessStatusCode) return false;
            var data = await r.Content.ReadFromJsonAsync<LoginResponse>();
            _token = data?.Access;
            _refreshToken = data?.Refresh;
            SetAuth();
            _logger.LogInformation("AdminApi: connecté en tant que {User}", username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminApi: échec connexion");
            return false;
        }
    }

    public async Task<DashboardTotals?> GetDashboardTotalsAsync() =>
        await GetJsonAsync<DashboardTotals>("/api/banking/dashboard/totals/");

    public async Task<List<UserProfile>?> GetPendingDossiersAsync()
    {
        var users = await GetJsonAsync<List<AdnUserJson>>("/api/banking/dossiers/pending/");
        return users?.Select(u => u.ToUserProfile()).ToList();
    }

    public async Task<bool> ApprovePremiumAsync(int userId) =>
        await PostAsync($"/api/banking/dossiers/{userId}/approve-premium/");

    public async Task<bool> ApproveCreditAsync(int userId, decimal montant) =>
        await PostAsync($"/api/banking/dossiers/{userId}/approve-credit/", new { montant });

    public async Task<bool> AdminDepotAsync(int userId, decimal montant) =>
        await PostAsync("/api/banking/depot/", new { user_id = userId, montant });

    public async Task<List<AdminLogEntry>?> GetLogsAsync() =>
        await GetJsonAsync<List<AdminLogEntry>>("/api/banking/logs/");

    public async Task<AdminDashboardResponse?> GetAdminDashboardAsync() =>
        await GetJsonAsync<AdminDashboardResponse>("/api/banking/admin-dashboard/");
}

public class LoginResponse
{
    [JsonPropertyName("access")] public string Access { get; set; } = "";
    [JsonPropertyName("refresh")] public string? Refresh { get; set; }
    [JsonPropertyName("user")] public LoginUser? User { get; set; }
}

public class RefreshResponse
{
    [JsonPropertyName("access")] public string? Access { get; set; }
}

public class LoginUser
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
}

public class DashboardTotals
{
    [JsonPropertyName("total_users")] public int TotalUsers { get; set; }
    [JsonPropertyName("total_balance")] public decimal TotalBalance { get; set; }
    [JsonPropertyName("pending_premium")] public int PendingPremium { get; set; }
    [JsonPropertyName("pending_credit")] public int PendingCredit { get; set; }
}

public class AdminLogEntry
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("Action")] public string? Action { get; set; }
    [JsonPropertyName("Cible")] public string? Cible { get; set; }
    [JsonPropertyName("Details")] public string? Details { get; set; }
    [JsonPropertyName("StatutResultat")] public string? StatutResultat { get; set; }

    public AdminLog ToAdminLog() => new()
    {
        Id = Id,
        Timestamp = DateTime.TryParse(Timestamp, out var dt) ? dt : DateTime.UtcNow,
        Action = Action ?? "",
        Cible = Cible ?? "",
        Details = Details ?? "",
        StatutResultat = StatutResultat ?? "SUCCESS"
    };
}

public class AdnUserJson
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Nom")] public string? Nom { get; set; }
    [JsonPropertyName("Prenom")] public string? Prenom { get; set; }
    [JsonPropertyName("Email")] public string? Email { get; set; }
    [JsonPropertyName("Solde")] public string? Solde { get; set; }
    [JsonPropertyName("Statut")] public int Statut { get; set; }
    [JsonPropertyName("NiveauEtude")] public string? NiveauEtude { get; set; }
    [JsonPropertyName("CvUrl")] public string? CvUrl { get; set; }
    [JsonPropertyName("DocIdentiteUrl")] public string? DocIdentiteUrl { get; set; }
    [JsonPropertyName("DocScolariteUrl")] public string? DocScolariteUrl { get; set; }
    [JsonPropertyName("PendingPremiumUpgrade")] public int PendingPremiumUpgrade { get; set; }
    [JsonPropertyName("PendingCreditRequest")] public int PendingCreditRequest { get; set; }

    public UserProfile ToUserProfile() => new()
    {
        Id = Id,
        Nom = Nom ?? "",
        Prenom = Prenom ?? "",
        Email = Email ?? "",
        Solde = long.TryParse(Solde, out var s) ? s : 0L,
        Statut = (UserStatus)Statut,
        NiveauEtude = NiveauEtude ?? "",
        CvUrl = CvUrl ?? "",
        DocIdentiteUrl = DocIdentiteUrl ?? "",
        DocScolariteUrl = DocScolariteUrl ?? "",
        PendingPremiumUpgrade = PendingPremiumUpgrade == 1,
        PendingCreditRequest = PendingCreditRequest == 1,
    };
}

public class AdminDashboardResponse
{
    [JsonPropertyName("dossiers")] public List<AdnUserJson>? Dossiers { get; set; }
    [JsonPropertyName("logs")] public List<AdminLogEntry>? Logs { get; set; }
    [JsonPropertyName("total_balance")] public decimal TotalBalance { get; set; }
}
