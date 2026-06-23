using System.Text.Json;
using DonutShop.Models;

namespace DonutShop.Services;

/// <summary>Stores the shopping cart in the user's session as JSON.</summary>
public class CartService
{
    private const string SessionKey = "DonutCart";
    private readonly IHttpContextAccessor _accessor;

    public CartService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ISession Session =>
        _accessor.HttpContext?.Session
        ?? throw new InvalidOperationException("No active session.");

    public Cart GetCart()
    {
        var json = Session.GetString(SessionKey);
        return string.IsNullOrEmpty(json)
            ? new Cart()
            : JsonSerializer.Deserialize<Cart>(json) ?? new Cart();
    }

    private void Save(Cart cart) =>
        Session.SetString(SessionKey, JsonSerializer.Serialize(cart));

    public Cart Add(Donut donut, int quantity = 1)
    {
        var cart = GetCart();
        var existing = cart.Items.FirstOrDefault(i => i.DonutId == donut.Id);
        if (existing is null)
        {
            cart.Items.Add(new CartItem
            {
                DonutId = donut.Id,
                Name = donut.Name,
                Price = donut.Price,
                Quantity = quantity,
            });
        }
        else
        {
            existing.Quantity += quantity;
        }
        Save(cart);
        return cart;
    }

    public Cart Remove(int donutId)
    {
        var cart = GetCart();
        cart.Items.RemoveAll(i => i.DonutId == donutId);
        Save(cart);
        return cart;
    }

    public Cart UpdateQuantity(int donutId, int quantity)
    {
        var cart = GetCart();
        var item = cart.Items.FirstOrDefault(i => i.DonutId == donutId);
        if (item is not null)
        {
            if (quantity <= 0)
                cart.Items.Remove(item);
            else
                item.Quantity = quantity;
        }
        Save(cart);
        return cart;
    }

    public void Clear() => Session.Remove(SessionKey);
}
