using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ordering;

public sealed class OrderingClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;

    public OrderingClient(string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(
                "https://pe-uk-ordering-api-fd-eecsdkg6btfeg0cc.z01.azurefd.net/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<RedeemedPromo> RedeemRewardAsync(string rewardId)
    {
        var response = await SendAsync(HttpMethod.Post,
            "api/v2/loyalty/capillary/rewards/redeem", new { rewardId });
        var data = response.GetProperty("data");
        return new RedeemedPromo(
            RequiredString(data, "promoCode"),
            Guid.Parse(RequiredString(data, "promoCodeExternalId")));
    }

    public Task<JsonElement> GetPromoDetailsAsync(Guid promoId) =>
        SendAsync(HttpMethod.Post, $"api/v2/promocodes/details/{promoId}");

    public Task<JsonElement> UpdateBasketAsync(BasketRequest basket) =>
        SendAsync(HttpMethod.Post, "ordering/basket/en", basket);

    public async Task<Guid> ConfirmOrderAsync()
    {
        var response = await SendAsync(HttpMethod.Post, "ordering/orders/en/confirm");
        return Guid.Parse(RequiredString(response.GetProperty("data"), "externalId"));
    }

    public Task<JsonElement> GetOrderAsync(Guid orderId) =>
        SendAsync(HttpMethod.Get, $"ordering/orders/en/{orderId}");

    public Task<JsonElement> CompleteOrderAsync(Guid orderId, PickupRequest pickup) =>
        SendAsync(HttpMethod.Post, $"ordering/orders/en/{orderId}/complete", pickup);

    private async Task<JsonElement> SendAsync(
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new ByteArrayContent(Array.Empty<byte>());
        }

        // Do not retry mutations automatically: the server may have processed them.
        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"\n{method} {path} -> {(int)response.StatusCode}");
        Console.WriteLine(text);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var root = json.RootElement;
        if (root.TryGetProperty("hasErrors", out var hasErrors) && hasErrors.GetBoolean())
            throw new InvalidOperationException($"API reported an error: {text}");
        return root.Clone();
    }

    private static string RequiredString(JsonElement data, string name)
    {
        var value = data.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing {name} in API response.");
    }

    public void Dispose() => _http.Dispose();
}
