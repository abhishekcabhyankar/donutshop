using DonutShop.Models;

namespace DonutShop.Services;

public interface IDonutCatalog
{
    IReadOnlyList<Donut> GetAll();
    Donut? GetById(int id);
}

public class DonutCatalog : IDonutCatalog
{
    private static readonly List<Donut> Donuts = new()
    {
        new Donut { Id = 1, Name = "Classic Glazed",     Description = "Light, fluffy and dipped in our signature glaze.",        Price = 1.99m, Emoji = "🍩" },
        new Donut { Id = 2, Name = "Chocolate Frosted",  Description = "Rich chocolate frosting on a soft ring donut.",          Price = 2.49m, Emoji = "🍫" },
        new Donut { Id = 3, Name = "Strawberry Sprinkle", Description = "Strawberry icing topped with rainbow sprinkles.",        Price = 2.79m, Emoji = "🍓" },
        new Donut { Id = 4, Name = "Boston Cream",        Description = "Custard-filled and crowned with chocolate ganache.",     Price = 3.29m, Emoji = "🍮" },
        new Donut { Id = 5, Name = "Maple Bacon",         Description = "Maple glaze with crispy candied bacon bits.",            Price = 3.99m, Emoji = "🥓" },
        new Donut { Id = 6, Name = "Cinnamon Sugar",      Description = "Warm cake donut rolled in cinnamon sugar.",              Price = 2.29m, Emoji = "🌀" },
    };

    public IReadOnlyList<Donut> GetAll() => Donuts;

    public Donut? GetById(int id) => Donuts.FirstOrDefault(d => d.Id == id);
}
