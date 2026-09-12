using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using GuerrillaMailDemo;
using Ordering;

// Empty scaffold. Program.cs remains the web application's entry point.
internal static class RegisterFlow
{
    public sealed record FoodSelection(
        string Name,
        string rewardID,
        string MenuItemID);

    public static class MockRegisterFlow
    {
        public static FoodSelection GetSelection(string food) => food switch
        {
            "Biscuit" => new(
                "Biscuit",
                "661015",
                "3655585c-579e-4d67-b0eb-a245d4499d04"),

            "Regular Fries" => new(
                "Regular Fries",
                "659475",
                "25bdf484-b228-4bab-b4af-52eabe9047d9"),

            "Chicken Cruncher" => new(
                "Chicken Cruncher",
                "659474",
                "e953518b-30b5-4373-9426-e0a43850997e"),

            _ => throw new ArgumentException("Unknown food selection.")
        };
#pragma warning disable CS7022 // The web app uses top-level statements for its entry point.
        static Task Main() => Task.CompletedTask;
#pragma warning restore CS7022
        const string Host = "pe-uk-ordering-api-fd-eecsdkg6btfeg0cc.z01.azurefd.net";

        static string _refreshToken = "";

        static readonly HttpClient _http = new(new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip,
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = Host,
                EnabledSslProtocols = SslProtocols.None,
                ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 }
            }
        });


        static readonly HttpClient _plain = new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false
        });

        private static readonly Regex VerifyLinkRegex = new(
            @"https://www\.popeyesuk\.com/verify-loyalty-email\?[^\s""'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task<Task> Run(string menu, string food)
        {
            using var client = new GuerrillaMailClient();
            Console.WriteLine(menu);
            Console.WriteLine(food);

            var session = await client.CreateMailboxAsync();
            Console.WriteLine($"✅ Mailbox Created: {session.EmailAddress}");
            const string chars = "0123456789abcdef";
            string result = string.Concat(
                Enumerable.Range(0, 5)
                    .Select(_ => chars[Random.Shared.Next(chars.Length)])
            ) + "52c6d33bc9f";
            Console.WriteLine($"✅ Device ID Generated");
            string DeviceId = result;

            string email = session.EmailAddress;

            string password = "Password123!";

            string name = "Jake";

            string dob = "2001-02-02";

            string pushToken = "none";


            string bearer = await RegisterAsync(email, name, password, dob, true, pushToken);


            if ("Hi".Contains("H"))
            {

                await SendVerificationEmailAsync(bearer);

                Console.WriteLine("ℹ️ Polling for verification email...");
                var orderEmail = await client.WaitForEmailFromAsync("order@t.popeyesuk.com", TimeSpan.FromMinutes(5));

                if (orderEmail is null)
                {
                    Console.WriteLine("Timed out.");
                    return Task.CompletedTask;
                }

                var verifyLink = ExtractVerifyLink(orderEmail.Body);
                if (verifyLink is null)
                {
                    Console.WriteLine("Email arrived but no verify link found.");
                    return Task.CompletedTask;
                }

                Console.WriteLine($"✅ Verify link located!");



                string emailLink = verifyLink;

                string verifyToken = await ExtractVerifyTokenAsync(emailLink);
                Console.WriteLine($"✅Extracted token");

                await VerifyEmailAsync(bearer, verifyToken);


                await GetAsync("/api/v2/loyalty/capillary/rewards", null);
                await GetAsync("/api/v2/loyalty/capillary/rewards", bearer);
                Console.WriteLine(bearer);
                string recheck = await PostAsync("/api/v2/loyalty/capillary/user/registration-eligibility",
                    bearer, JsonSerializer.Serialize(new { deviceId = DeviceId }));

                if (!recheck.Contains("\"hasErrors\":true"))
                {
                    await PostAsync("/api/v2/loyalty/capillary/user/register",
                        bearer, JsonSerializer.Serialize(new { deviceId = DeviceId }));
                }

                Console.WriteLine($"✅ Rewards Registered");
                Console.WriteLine($"✅ Account Created");
            }
            else
            {

            }

            var token = "";
            using var orderClient = new OrderingClient(bearer.Replace("Bearer ", ""));
            var choice =  MockRegisterFlow.GetSelection(food);
            var promo = await orderClient.RedeemRewardAsync(choice.rewardID);
            await orderClient.GetPromoDetailsAsync(promo.ExternalId);
          
            await orderClient.UpdateBasketAsync(new BasketRequest(
                "birminghamnews",
                menu,
                new[] { new BasketItem(choice.MenuItemID, promo.Code, promo.ExternalId) }));
// 626b930b-1e1a-49e5-ba26-9e858c6380a4 - hasbrown 25bdf484-b228-4bab-b4af-52eabe9047d9 - fries
            var orderId = await orderClient.ConfirmOrderAsync();
            await orderClient.GetOrderAsync(orderId);
            await orderClient.CompleteOrderAsync(orderId, new PickupRequest());
            await orderClient.GetOrderAsync(orderId);
            //     Console.WriteLine("HELLO!!");
            await Task.Delay(1000);
            var json = await GetAsync("/ordering/orders/en", bearer);
           

            using var doc = JsonDocument.Parse(json);

            var orderNumber = doc.RootElement
                .GetProperty("data")
                .GetProperty("list")[0]
                .GetProperty("orderNumber")
                .GetString();

            Console.WriteLine("Order Number: " + orderNumber); // 5874
            Console.WriteLine("http://popeyes.cadcolbpclub.co.uk/order-status.html?order=" + orderNumber);
            return Task.FromResult(orderNumber);
        }   

        public static string? ExtractVerifyLink(string htmlBody)
        {
            var match = VerifyLinkRegex.Match(htmlBody);
            return match.Success ? WebUtility.HtmlDecode(match.Value) : null;
        }

        static async Task<string> RegisterAsync(string email, string name, string password,
            string dateOfBirth, bool registerToMarketingList, string pushNotificationId)
        {
            string body = JsonSerializer.Serialize(new
            {
                email,
                name,
                password,
                registerToMarketingList,
                pushNotificationId,
                mobileDeviceType = "Android",
                dateOfBirth
            });

            string response = await SendAsync(HttpMethod.Post, "/identity/register", null, body);
            Console.WriteLine($"✅ Account registered");
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("hasErrors", out var err) && err.GetBoolean())
                throw new HttpRequestException($"register failed: {response}");

            var data = root.GetProperty("data");
            _refreshToken = data.GetProperty("refreshToken").GetString()!;
            return data.GetProperty("token").GetString()!;
        }


        static Task<string> GetAsync(string path, string? bearer) =>
            SendAsync(HttpMethod.Get, path, bearer, body: null);

        static Task<string> PostAsync(string path, string bearer, string body) =>
            SendAsync(HttpMethod.Post, path, bearer, body);

        static Task<string> SendVerificationEmailAsync(string bearer) =>
            SendAsync(HttpMethod.Post, "/api/v2/loyalty/capillary/user/email/send", bearer, body: null);

        static Task<string> VerifyEmailAsync(string bearer, string token) =>
            SendAsync(HttpMethod.Post, "/api/v2/loyalty/capillary/user/email/verify", bearer,
                JsonSerializer.Serialize(new { token }));

        static async Task<string> SendAsync(HttpMethod method, string path, string? bearer, string? body)
        {
            using var request = new HttpRequestMessage(method, $"https://{Host}{path}")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            request.Headers.TryAddWithoutValidation("user-agent", "okhttp/5.3.2");
            if (bearer != null)
                request.Headers.TryAddWithoutValidation("authorization", bearer);
            if (body != null)
                request.Content = new StringContent(body,
                    new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" });

            using var response = await _http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();

            return text;
        }


        static async Task<string> ExtractVerifyTokenAsync(string emailUrl)
        {
            string url = emailUrl;
            for (int hop = 0; hop < 10; hop++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("user-agent", "okhttp/5.3.2");
                using var res = await _plain.SendAsync(req);

                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } loc)
                {
                    string next = loc.IsAbsoluteUri
                        ? loc.AbsoluteUri
                        : new Uri(new Uri(url), loc).AbsoluteUri;

                    string? token = FindToken(next) ?? FindToken(url);
                    if (token != null) return token;

                    if (next.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        url = next;
                        continue;
                    }

                    throw new Exception($"Deep link had no token: {next}");
                }

                string body = await res.Content.ReadAsStringAsync();
                string? found = FindToken(body) ?? FindToken(url);
                if (found != null) return found;

                throw new Exception(
                    $"No token found at {url} (status {(int)res.StatusCode}). " +
                    $"Body head: {body[..Math.Min(400, body.Length)]}");
            }

            throw new Exception("Too many redirects without finding a token.");
        }


        static string? FindToken(string text)
        {
            string? m1 = Regex.Match(text, @"CfDJ8[A-Za-z0-9+/=_\-]+") is { Success: true } m ? m.Value : null;
            string? m2 = Regex.Match(Uri.UnescapeDataString(text), @"CfDJ8[A-Za-z0-9+/=_\-]+") is { Success: true } u
                ? u.Value
                : null;
            return m2?.Length > m1?.Length ? m2 : m1;
        }
    }
}
