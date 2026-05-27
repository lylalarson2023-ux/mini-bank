using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using AppAccount = MBANK_ETUDIANT.Services.AccountService;

namespace MBANK_ETUDIANT.Services
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

        public async Task<string?> CreerSessionDepotAsync(decimal montantDh)
        {
            var montantCentimes = (long)(montantDh * 100);
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
                                Description = $"Dépôt de {montantDh:N2} DH"
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
                    { "amount_dh", montantDh.ToString("F2") },
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

            var montantDh = decimal.Parse(session.Metadata["amount_dh"]);
            var userId = int.Parse(session.Metadata["user_id"]);

            await _auth.InitializeAsync(userId);
            return await _account.ExecuterOperationAsync(montantDh, "Dépôt par carte Stripe", "DEPOT");
        }

        private string GetBaseUrl()
        {
            var req = _http.HttpContext?.Request;
            if (req == null) return "http://localhost:5163";
            return $"{req.Scheme}://{req.Host}";
        }
    }
}
