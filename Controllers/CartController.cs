using Microsoft.AspNetCore.Mvc;
using DonutShop.Services;

namespace DonutShop.Controllers;

public class CartController : Controller
{
    private readonly IDonutCatalog _catalog;
    private readonly CartService _cart;

    public CartController(IDonutCatalog catalog, CartService cart)
    {
        _catalog = catalog;
        _cart = cart;
    }

    public IActionResult Index() => View(_cart.GetCart());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Add(int id, int quantity = 1)
    {
        var donut = _catalog.GetById(id);
        if (donut is null)
            return NotFound();

        _cart.Add(donut, quantity < 1 ? 1 : quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(int id, int quantity)
    {
        _cart.UpdateQuantity(id, quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int id)
    {
        _cart.Remove(id);
        return RedirectToAction(nameof(Index));
    }
}
