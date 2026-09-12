using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ADN_pay.Services;

public class AirtelMoneyOptions
{
    public string BaseUrl { get; set; } = "https://openapiuat.airtel.ga";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public string CallbackUrl { get; set; } = "";
    public string Country { get; set; } = "CFA";
    public string Currency { get; set; } = "GA";
    public bool UseSandbox { get; set; } = true;
}

public interface IAirtelMoneyService
{
    bool IsConfigured { get; }
    string EncryptPin(string pin);
    Task<string> GetAccessTokenAsync();
    Task<AirtelApiResponse> CashInAsync(decimal amount, string msisdn, string reference, string pin, string transactionId, string remark = "");
    Task<AirtelApiResponse> DisburseAsync(decimal amount, string msisdn, string payeeName, string reference, string pin, string transactionId, string type = "B2B");
    Task<AirtelTransactionStatusResponse> GetTransactionStatusAsync(string referenceId);
}

public class AirtelMoneyService : IAirtelMoneyService
{
    private readonly HttpClient _httpClient;
    private readonly AirtelMoneyOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AirtelMoneyService(HttpClient httpClient, IOptions<AirtelMoneyOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            _options.BaseUrl = "https://openapiuat.airtel.ga";

        if (string.IsNullOrWhiteSpace(_options.Country))
            _options.Country = "CFA";

        if (string.IsNullOrWhiteSpace(_options.Currency))
            _options.Currency = "GA";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId)
        && !string.IsNullOrWhiteSpace(_options.ClientSecret)
        && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public string EncryptPin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            throw new ArgumentException("PIN requis.", nameof(pin));

        if (pin.Length != 4)
            throw new ArgumentException("Le PIN doit contenir 4 chiffres.", nameof(pin));

        if (string.IsNullOrWhiteSpace(_options.PublicKeyPem))
            throw new InvalidOperationException("Clé publique Airtel manquante. Ajoutez AirtelMoney:PublicKeyPem dans la config.");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PublicKeyPem);

        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(pin), RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(encrypted);
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("La configuration Airtel Money n'est pas complète. Vérifiez ClientId, ClientSecret et BaseUrl.");

        using var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("client_secret", _options.ClientSecret),
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/oauth/token")
        {
            Content = body,
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Erreur token Airtel : {response.StatusCode} - {content}");

        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Airtel n'a pas renvoyé d'access_token.");
    }

    public async Task<AirtelApiResponse> CashInAsync(decimal amount, string msisdn, string reference, string pin, string transactionId, string remark = "")
    {
        var token = await GetAccessTokenAsync();
        var encryptedPin = EncryptPin(pin);

        var payload = new
        {
            subscriber = new
            {
                msisdn,
            },
            transaction = new
            {
                amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
                id = transactionId,
            },
            additional_info = new
            {
                key = "remark",
                value = string.IsNullOrWhiteSpace(remark) ? "AIRTEL" : remark,
            },
            reference,
            pin = encryptedPin,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/standard/v1/cashin/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Country", _options.Country);
        request.Headers.Add("X-Currency", _options.Currency);
        request.Headers.Add("X-Correlation-ID", transactionId);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cash-in Airtel error: {response.StatusCode} - {body}");

        var result = JsonSerializer.Deserialize<AirtelApiResponse>(body, _jsonOptions);
        return result ?? throw new InvalidOperationException("Réponse Cash-in Airtel invalide.");
    }

    public async Task<AirtelApiResponse> DisburseAsync(decimal amount, string msisdn, string payeeName, string reference, string pin, string transactionId, string type = "B2B")
    {
        var token = await GetAccessTokenAsync();
        var encryptedPin = EncryptPin(pin);

        var payload = new
        {
            payee = new
            {
                currency = _options.Currency,
                msisdn,
                name = payeeName,
            },
            reference,
            pin = encryptedPin,
            transaction = new
            {
                amount,
                id = transactionId,
                type,
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/standard/v2/disbursements/")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json"),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Country", _options.Country);
        request.Headers.Add("X-Currency", _options.Currency);
        request.Headers.Add("X-Correlation-ID", transactionId);

        if (!string.IsNullOrWhiteSpace(_options.CallbackUrl))
            request.Headers.Add("X-Callback-Url", _options.CallbackUrl);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Disbursement Airtel error: {response.StatusCode} - {body}");

        var result = JsonSerializer.Deserialize<AirtelApiResponse>(body, _jsonOptions);
        return result ?? throw new InvalidOperationException("Réponse Disbursement Airtel invalide.");
    }

    public async Task<AirtelTransactionStatusResponse> GetTransactionStatusAsync(string referenceId)
    {
        var token = await GetAccessTokenAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}/transaction/v1/status/{referenceId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Status Airtel error: {response.StatusCode} - {body}");

        var result = JsonSerializer.Deserialize<AirtelTransactionStatusResponse>(body, _jsonOptions);
        return result ?? throw new InvalidOperationException("Réponse status Airtel invalide.");
    }
}

public class AirtelRequestBase
{
    public string Pin { get; set; } = "";
}

public class AirtelCashInRequest : AirtelRequestBase
{
    public decimal Amount { get; set; }
    public string Msisdn { get; set; } = "";
    public string Reference { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string Remark { get; set; } = "";
}

public class AirtelDisbursementRequest : AirtelRequestBase
{
    public decimal Amount { get; set; }
    public string Msisdn { get; set; } = "";
    public string PayeeName { get; set; } = "";
    public string Reference { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public string Type { get; set; } = "B2B";
}

public class AirtelApiResponse
{
    public AirtelApiData? Data { get; set; }
    public AirtelApiStatus? Status { get; set; }
}

public class AirtelApiData
{
    public string? Message { get; set; }
    public string? Status { get; set; }
    public AirtelApiTransaction? Transaction { get; set; }
}

public class AirtelApiTransaction
{
    public string? AirtelMoneyId { get; set; }
    public string? Id { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
}

public class AirtelApiStatus
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResultCode { get; set; }
    public bool Success { get; set; }
}

public class AirtelTransactionStatusResponse
{
    public AirtelStatusResult? Data { get; set; }
    public AirtelApiStatus? Status { get; set; }
}

public class AirtelStatusResult
{
    public string? Message { get; set; }
    public string? Status { get; set; }
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ExternalId { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
}
