using System.Text.Json.Serialization;

namespace DonutShop.Models;

public class CartItem
{
    public int DonutId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }

    [JsonIgnore]
    public decimal LineTotal => Price * Quantity;
}

public class Cart
{
    public List<CartItem> Items { get; set; } = new();

    [JsonIgnore]
    public decimal Total => Items.Sum(i => i.LineTotal);

    [JsonIgnore]
    public int Count => Items.Sum(i => i.Quantity);
}
