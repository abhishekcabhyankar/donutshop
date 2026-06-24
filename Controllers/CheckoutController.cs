using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using DonutShop.Models;
using DonutShop.Services;

namespace DonutShop.Controllers;

public class CheckoutController : Controller
{
    private readonly CartService _cart;
    private readonly IPaymentService _payments;
    private readonly AuthorizeNetOptions _authNet;
    private readonly ApplePayOptions _applePay;
    private readonly GooglePayOptions _googlePay;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        CartService cart,
        IPaymentService payments,
        IOptions<AuthorizeNetOptions> authNet,
        IOptions<ApplePayOptions> applePay,
        IOptions<GooglePayOptions> googlePay,
        IWebHostEnvironment env,
        ILogger<CheckoutController> logger)
    {
        _cart = cart;
        _payments = payments;
        _authNet = authNet.Value;
        _applePay = applePay.Value;
        _googlePay = googlePay.Value;
        _env = env;
        _logger = logger;
    }

    public IActionResult Index()
    {
        var cart = _cart.GetCart();
        if (cart.Count == 0)
            return RedirectToAction("Index", "Cart");

        PopulateClientConfig();
        return View(new CheckoutViewModel { Cart = cart });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CheckoutViewModel model, CancellationToken cancellationToken)
    {
        var cart = _cart.GetCart();
        model.Cart = cart;

        if (cart.Count == 0)
            return RedirectToAction("Index", "Cart");

        if (string.IsNullOrWhiteSpace(model.OpaqueDataValue) ||
            string.IsNullOrWhiteSpace(model.OpaqueDataDescriptor))
        {
            ModelState.AddModelError(string.Empty, "Payment details were not captured. Please re-enter your card.");
        }

        if (!ModelState.IsValid)
        {
            ClearPaymentToken(model);
            PopulateClientConfig();
            return View(model);
        }

        var result = await _payments.ChargeAsync(
            cart.Total,
            model.OpaqueDataDescriptor,
            model.OpaqueDataValue,
            model,
            cart,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Payment failed. Please try again.");
            ClearPaymentToken(model);
            PopulateClientConfig();
            return View(model);
        }

        var receipt = new CheckoutResultViewModel
        {
            Success = true,
            TransactionId = result.TransactionId,
            AmountCharged = cart.Total,
            Message = result.Message,
        };

        _cart.Clear();
        return View("Success", receipt);
    }

    private void PopulateClientConfig()
    {
        ViewBag.AcceptJsUrl = _authNet.AcceptJsUrl;
        ViewBag.ApiLoginId = _authNet.ApiLoginId;
        ViewBag.PublicClientKey = _authNet.PublicClientKey;

        ViewBag.ApplePayEnabled = _applePay.IsConfigured;
        ViewBag.ApplePayMerchantId = _applePay.MerchantIdentifier;
        ViewBag.ApplePayDisplayName = _applePay.DisplayName;
        ViewBag.ApplePayDomain = _applePay.DomainName;

        // Google Pay needs the Authorize.NET API Login ID as the gatewayMerchantId
        // so Google encrypts the token for our gateway.
        ViewBag.GooglePayEnabled =
            _googlePay.IsConfigured && !string.IsNullOrWhiteSpace(_authNet.ApiLoginId);
        ViewBag.GooglePayEnvironment = _googlePay.ClientEnvironment;
        ViewBag.GooglePayMerchantId = _googlePay.MerchantId;
        ViewBag.GooglePayMerchantName = _googlePay.MerchantName;
        // Authorize.NET requires the account's Payment Gateway ID here (NOT the API
        // Login ID). Fall back to the API Login ID only if it isn't configured.
        ViewBag.GooglePayGatewayMerchantId = string.IsNullOrWhiteSpace(_googlePay.GatewayMerchantId)
            ? _authNet.ApiLoginId
            : _googlePay.GatewayMerchantId;
    }

    // The Accept.js payment nonce is single-use. After a redisplay we must drop the
    // consumed token (from the model AND ModelState, since asp-for re-renders the
    // posted value) so the browser generates a fresh one on the next attempt.
    private void ClearPaymentToken(CheckoutViewModel model)
    {
        model.OpaqueDataValue = string.Empty;
        model.OpaqueDataDescriptor = string.Empty;
        ModelState.Remove(nameof(CheckoutViewModel.OpaqueDataValue));
        ModelState.Remove(nameof(CheckoutViewModel.OpaqueDataDescriptor));
    }

    // Apple Pay (web) merchant validation. The browser hands us a one-time Apple
    // validation URL; we call it over mutual-TLS using the Merchant Identity
    // certificate and return Apple's opaque merchant session to the page.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateMerchant(
        [FromBody] ApplePayValidationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_applePay.IsConfigured)
            return BadRequest(new { error = "Apple Pay is not enabled." });

        // SSRF guard: only ever connect to an Apple-controlled HTTPS host.
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ValidationUrl) ||
            !Uri.TryCreate(request.ValidationUrl, UriKind.Absolute, out var validationUri) ||
            validationUri.Scheme != Uri.UriSchemeHttps ||
            !IsApplePayHost(validationUri.Host))
        {
            return BadRequest(new { error = "Invalid validation URL." });
        }

        X509Certificate2 clientCert;
        try
        {
            // Allow the configured paths to be relative to the app content root
            // (e.g. a "certs/" folder shipped in the deploy bundle).
            var certPath = ResolveContentPath(_applePay.MerchantIdCertPath);
            var keyPath = ResolveContentPath(_applePay.MerchantIdKeyPath);

            // CreateFromPemFile gives an ephemeral key; re-import via PKCS12 so the
            // private key is usable for TLS client authentication on every platform.
            using var pem = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            clientCert = new X509Certificate2(pem.Export(X509ContentType.Pkcs12));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the Apple Pay merchant identity certificate.");
            return StatusCode(500, new { error = "Merchant certificate unavailable." });
        }

        using (clientCert)
        using (var handler = new HttpClientHandler())
        {
            handler.ClientCertificates.Add(clientCert);
            using var client = new HttpClient(handler);

            var payload = new
            {
                merchantIdentifier = _applePay.MerchantIdentifier,
                displayName = _applePay.DisplayName,
                initiative = "web",
                initiativeContext = _applePay.DomainName
            };

            try
            {
                using var appleResponse =
                    await client.PostAsJsonAsync(validationUri, payload, cancellationToken);
                var body = await appleResponse.Content.ReadAsStringAsync(cancellationToken);

                if (!appleResponse.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Apple Pay merchant validation failed ({Status}): {Body}",
                        appleResponse.StatusCode, body);
                    return StatusCode(502, new { error = "Merchant validation failed." });
                }

                // Hand Apple's merchant session back to the browser verbatim.
                return Content(body, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling the Apple Pay merchant validation endpoint.");
                return StatusCode(502, new { error = "Merchant validation failed." });
            }
        }
    }

    private static bool IsApplePayHost(string host) =>
        host.Equals("apple.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".apple.com", StringComparison.OrdinalIgnoreCase);

    private string ResolveContentPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(_env.ContentRootPath, path);
}
