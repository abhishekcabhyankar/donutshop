using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DonutShop.Models;
using DonutShop.Services;

namespace DonutShop.Controllers;

public class CheckoutController : Controller
{
    private readonly CartService _cart;
    private readonly IPaymentService _payments;
    private readonly AuthorizeNetOptions _authNet;

    public CheckoutController(
        CartService cart,
        IPaymentService payments,
        IOptions<AuthorizeNetOptions> authNet)
    {
        _cart = cart;
        _payments = payments;
        _authNet = authNet.Value;
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
    }
}
