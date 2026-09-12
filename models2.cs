namespace Ordering;

public sealed record RedeemedPromo(string Code, Guid ExternalId);

public sealed record BasketRequest(
    string RestaurantRef,
    string MenuRef,
    BasketItem[] Items);

public sealed record BasketItem(
    string MenuItemId,
    string Code,
    Guid PromoCodeExternalId)
{
    public int Quantity { get; init; } = 1;
    public object[] AdditionalCondiments { get; init; } = Array.Empty<object>();
    public object[] ExcludeCondiments { get; init; } = Array.Empty<object>();
    public object[] Sides { get; init; } = Array.Empty<object>();
    public object[] AdditionalSides1 { get; init; } = Array.Empty<object>();
    public object[] AdditionalSides2 { get; init; } = Array.Empty<object>();
    public object[] AdditionalSides3 { get; init; } = Array.Empty<object>();
    public object[] AdditionalProducts { get; init; } = Array.Empty<object>();
    public object[] Drinks { get; init; } = Array.Empty<object>();
    public object[] Sauces { get; init; } = Array.Empty<object>();
    public bool IsComboMeal { get; init; }
}

public sealed record PickupRequest
{
    public string PickupType { get; init; } = "TakeAway";
    public string BayNumber { get; init; } = "";
    public string TableNumber { get; init; } = "";
    public string VehicleRegistration { get; init; } = "";
    public string VehicleColor { get; init; } = "";
}