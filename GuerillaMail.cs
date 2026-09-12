using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GuerrillaMailDemo;

// ---------- DTOs ----------

public sealed class GuerrillaSession
{
    [JsonPropertyName("email_addr")] public string EmailAddress { get; set; } = "";
    [JsonPropertyName("alias")]      public string Alias       { get; set; } = "";
    [JsonPropertyName("sid_token")]  public string SidToken    { get; set; } = "";
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public class GuerrillaMailSummary   // <- was 'sealed', which blocks GuerrillaFullMail
{
    [JsonPropertyName("mail_id")]        public long   MailId    { get; set; }
    [JsonPropertyName("mail_from")]      public string From      { get; set; } = "";
    [JsonPropertyName("mail_subject")]   public string Subject   { get; set; } = "";
    [JsonPropertyName("mail_excerpt")]   public string Excerpt   { get; set; } = "";
    [JsonPropertyName("mail_timestamp")] public long   Timestamp { get; set; }
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class GuerrillaFullMail : GuerrillaMailSummary
{
    [JsonPropertyName("mail_body")]      public string Body      { get; set; } = "";
    [JsonPropertyName("mail_recipient")] public string Recipient { get; set; } = "";
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class CheckEmailResult
{
    [JsonPropertyName("list")] public List<GuerrillaMailSummary> Mails      { get; set; } = new();
    [JsonPropertyName("count")] public int                       Count      { get; set; }
    [JsonPropertyName("ts")]    public long                      ServerTime { get; set; }
}

// ---------- Client ----------

public sealed class GuerrillaMailClient : IDisposable
{
    private const string DefaultBaseUrl = "https://api.guerrillamail.com/ajax.php";
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public string? SidToken    { get; private set; }
    public string? EmailAddress { get; private set; }

    /// <summary>Raised for every new mail found by <see cref="MonitorAsync"/>.</summary>
    public event Func<GuerrillaMailSummary, Task>? NewMail;

    public GuerrillaMailClient(string baseUrl = DefaultBaseUrl, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl;
        _http    = httpClient ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuerrillaMailCSharp/1.0");
    }

    // ----- low-level GET (all API calls are GETs on ajax.php with f=<action>) -----
    private async Task<JsonElement> GetAsync(string action, Dictionary<string, string>? args, CancellationToken ct)
    {
        var qs = new List<string> { $"f={action}" };
        if (SidToken is not null) qs.Add($"sid_token={SidToken}");
        if (args is not null)
            foreach (var (k, v) in args) qs.Add($"{k}={Uri.EscapeDataString(v)}");

        using var resp = await _http.GetAsync($"{_baseUrl}?{string.Join('&', qs)}", ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("sid_token", out var sid) && sid.ValueKind == JsonValueKind.String)
            SidToken = sid.GetString(); // every response echoes a fresh session token

        return doc.RootElement.Clone();
    }

    // ----- API actions -----

    /// <summary>Creates a random mailbox. Pass desiredUser to pick the local part (e.g. "order-4711").</summary>
    public async Task<GuerrillaSession> CreateMailboxAsync(string? desiredUser = null, CancellationToken ct = default)
    {
        var el = await GetAsync("get_email_address", null, ct);
        var session = el.Deserialize<GuerrillaSession>() ?? throw new InvalidOperationException("Unexpected response");

        if (!string.IsNullOrWhiteSpace(desiredUser))
        {
            // Must be called with the sid_token from the get_email_address session above.
            el = await GetAsync("set_email_user", new Dictionary<string, string> { ["email_user"] = desiredUser }, ct);
            session = el.Deserialize<GuerrillaSession>() ?? session; // fails if the name is taken
        }

        EmailAddress = session.EmailAddress;
        SidToken     = session.SidToken;
        return session;
    }

    /// <summary>Returns only mail newer than 'seq' (the highest seen mail_id). Start at 1 to skip the welcome mail.</summary>
    public async Task<CheckEmailResult> CheckEmailAsync(long seq, CancellationToken ct = default)
    {
        var el = await GetAsync("check_email",
            new Dictionary<string, string> { ["seq"] = seq.ToString(CultureInfo.InvariantCulture) }, ct);
        return el.Deserialize<CheckEmailResult>() ?? new CheckEmailResult();
    }

    public async Task<GuerrillaFullMail> FetchEmailAsync(long mailId, CancellationToken ct = default)
    {
        var el = await GetAsync("fetch_email", new Dictionary<string, string> { ["email_id"] = mailId.ToString() }, ct);
        return el.Deserialize<GuerrillaFullMail>() ?? throw new InvalidOperationException("Unexpected response");
    }
    /// <summary>
    /// Blocks until an email from <paramref name="senderFilter"/> arrives, or the timeout expires.
    /// Returns the full message, or null if it timed out.
    /// </summary>
    public async Task<GuerrillaFullMail?> WaitForEmailFromAsync(
        string senderFilter,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        timeout      ??= TimeSpan.FromMinutes(5);
        pollInterval ??= TimeSpan.FromSeconds(1);

        // linked token so we cancel on EITHER the caller's token or our own timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout.Value);

        long seq = 1; // skip the auto-generated "Welcome" mail
        var timer = new PeriodicTimer(pollInterval.Value);

        try
        {
            while (true)
            {
                var result = await CheckEmailAsync(seq, cts.Token);

                foreach (var mail in result.Mails)
                {
                    if (mail.MailId > seq) seq = mail.MailId;

                    // From is usually 'Popeyes UK <order@t.popeyesuk.com>', so Contains is the safe match
                    if (mail.From.Contains(senderFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        return await FetchEmailAsync(mail.MailId, cts.Token);
                    }
                }

                await timer.WaitForNextTickAsync(cts.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // our own CancelAfter fired -> timeout, not user cancellation
            return null;
        }
    }
    /// <summary>Deletes/bans the mailbox server-side (optional cleanup).</summary>
    public async Task ForgetMeAsync(CancellationToken ct = default)
    {
        await GetAsync("forget_me", new Dictionary<string, string> { ["email_addr"] = EmailAddress ?? "" }, ct);
    }

    // ----- monitoring loop -----

    public async Task MonitorAsync(TimeSpan? pollInterval = null, CancellationToken ct = default)
    {
        using var timer = new PeriodicTimer(pollInterval ?? TimeSpan.FromSeconds(15));
        long seq = 1; // seq 0 replays the automatic "Welcome to Guerrilla Mail" message

        while (await timer.WaitForNextTickAsync(ct))
        {
            CheckEmailResult result;
            try
            {
                result = await CheckEmailAsync(seq, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                Console.Error.WriteLine($"[warn] poll failed: {ex.Message}");
                continue;
            }

            foreach (var mail in result.Mails)
            {
                if (mail.MailId > seq) seq = mail.MailId;
                try { if (NewMail is not null) await NewMail.Invoke(mail); }
                catch (Exception ex) { Console.Error.WriteLine($"[warn] handler failed: {ex.Message}"); }
            }
        }
    }

    public void Dispose() => _http.Dispose();
}

