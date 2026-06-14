using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using AppAccount = ADN_pay.Services.AccountService;

namespace ADN_pay.Services
{
    public class StripeOptions
    {
        public string SecretKey { get; set; } = "";
        public string PublishableKey { get; set; } = "";
        public string WebhookSecret { get; set; } = "";
    }

    public class StripeService
    {
        private readonly StripeOptions _options;
        private readonly UserContext _user;
        private readonly AppAccount _account;
        private readonly AuthService _auth;
        private readonly IHttpContextAccessor _http;

        public StripeService(IOptions<StripeOptions> options, UserContext user, AppAccount account, AuthService auth, IHttpContextAccessor http)
        {
            _options = options.Value;
            _user = user;
            _account = account;
            _auth = auth;
            _http = http;
            StripeConfiguration.ApiKey = _options.SecretKey;
        }

        public string PublishableKey => _options.PublishableKey;

        // ADR-001 : montantCentimes déjà en centimes (long), pas de conversion ici
        public async Task<string?> CreerSessionDepotAsync(long montantCentimes)
        {
            var userId = _user.Profil?.Id;
            if (userId == null) return null;

            var baseUrl = GetBaseUrl();

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = ["card"],
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "mad",
                            UnitAmount = montantCentimes,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "Dépôt compte ADN.eco+",
                                Description = $"Dépôt de {montantCentimes / 100m:N2} DH"
                            }
                        },
                        Quantity = 1
                    }
                ],
                Mode = "payment",
                SuccessUrl = $"{baseUrl}/api/stripe/success?session_id={{CHECKOUT_SESSION_ID}}&user_id={userId}",
                CancelUrl = $"{baseUrl}/depot",
                Metadata = new Dictionary<string, string>
                {
                    { "user_id", userId.ToString() ?? "" },
                    { "amount_cents", montantCentimes.ToString() },
                    { "type", "depot" }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return session.Url;
        }

        public async Task<bool> ConfirmerDepotAsync(string sessionId)
        {
            var service = new SessionService();
            var session = await service.GetAsync(sessionId);

            if (session.PaymentStatus != "paid") return false;

            if (!session.Metadata.TryGetValue("amount_cents", out var amountStr)
                || !long.TryParse(amountStr, out var montantCentimes)
                || montantCentimes <= 0)
                return false;

            if (!session.Metadata.TryGetValue("user_id", out var userIdStr)
                || !int.TryParse(userIdStr, out var userId))
                return false;

            await _auth.InitializeAsync(userId);
            return await _account.ExecuterOperationAsync(montantCentimes, "Dépôt par carte Stripe", "DÉPÔT");
        }

        private string GetBaseUrl()
        {
            var req = _http.HttpContext?.Request;
            if (req == null) return "http://localhost:5163";
            return $"{req.Scheme}://{req.Host}";
        }
    }
}
