using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DonutShop.Models;
using DonutShop.Services;

namespace DonutShop.Controllers;

public class HomeController : Controller
{
    private readonly IDonutCatalog _catalog;
    private readonly CartService _cart;

    public HomeController(IDonutCatalog catalog, CartService cart)
    {
        _catalog = catalog;
        _cart = cart;
    }

    public IActionResult Index()
    {
        ViewBag.CartCount = _cart.GetCart().Count;
        return View(_catalog.GetAll());
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
