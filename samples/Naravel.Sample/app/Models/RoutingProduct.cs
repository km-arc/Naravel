namespace Naravel.Sample.App.Models;

public sealed record RoutingProduct(int Id, string Name)
{
    private static readonly IReadOnlyDictionary<string, RoutingProduct> Products = new Dictionary<string, RoutingProduct>
    {
        ["1"] = new(1, "Queue Worker"),
        ["2"] = new(2, "Route Registrar")
    };

    public static RoutingProduct? Find(string id) => Products.GetValueOrDefault(id);
}