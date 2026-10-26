using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteLauncher.Core.Steam;

/// <summary>
/// "Sign in through Steam" using Steam's OpenID 2.0 endpoint. The user signs in on
/// steamcommunity.com in their own browser; this app only ever receives the SteamID64,
/// never a password. A one-shot listener on 127.0.0.1 catches the redirect and the
/// result is verified directly with Steam before it is trusted.
/// </summary>
public static class SteamOpenId
{
    private const string Endpoint = "https://steamcommunity.com/openid/login";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";
    private const string CallbackPath = "/steam-callback";

    private static readonly Regex ClaimedIdPattern =
        new(@"^https://steamcommunity\.com/openid/id/(\d{17})$", RegexOptions.Compiled);

    public static string BuildLoginUrl(string returnTo, string realm)
    {
        var parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnTo,
            ["openid.realm"] = realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };

        return Endpoint + "?" + string.Join("&",
            parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>Extracts the SteamID64 from an OpenID claimed_id, or null if it isn't a Steam one.</summary>
    public static string? ParseClaimedId(string? claimedId)
    {
        var match = ClaimedIdPattern.Match(claimedId ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Opens the Steam sign-in page via <paramref name="openUrl"/> and waits for the result.
    /// Returns the verified SteamID64, or null if cancelled, timed out or not verifiable.
    /// </summary>
    public static async Task<string?> SignInAsync(
        HttpClient http, Action<string> openUrl, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var port = GetFreePort();
        var realm = $"http://127.0.0.1:{port}/";
        var returnTo = realm.TrimEnd('/') + CallbackPath;

        using var listener = new HttpListener();
        listener.Prefixes.Add(realm);
        listener.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        // GetContextAsync has no token overload; stopping the listener is how we abort the wait.
        using var registration = cts.Token.Register(() =>
        {
            try { listener.Stop(); } catch (ObjectDisposedException) { }
        });

        openUrl(BuildLoginUrl(returnTo, realm));

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return null; // cancelled or timed out
            }

            if (context.Request.Url?.AbsolutePath != CallbackPath)
            {
                Respond(context, 404, "Not found."); // favicon requests and the like
                continue;
            }

            string? steamId;
            try
            {
                steamId = await VerifyAsync(http, context.Request.QueryString, returnTo, cts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                steamId = null;
            }

            Respond(context, 200, steamId is null
                ? "Sign-in could not be verified. You can close this tab and try again."
                : "Signed in. You can close this tab and return to LiteLauncher.");
            return steamId;
        }
    }

    private static async Task<string?> VerifyAsync(
        HttpClient http, NameValueCollection query, string returnTo, CancellationToken cancellationToken)
    {
        var steamId = ParseClaimedId(query["openid.claimed_id"]);
        if (steamId is null) return null;

        // Cheap local checks before asking Steam to validate the signature.
        if (query["openid.mode"] != "id_res") return null;
        if (query["openid.return_to"] != returnTo) return null;
        if (query["openid.op_endpoint"] != Endpoint) return null;
        if (ParseClaimedId(query["openid.identity"]) != steamId) return null;

        var form = new List<KeyValuePair<string, string>>();
        foreach (var key in query.AllKeys)
        {
            if (key is null || !key.StartsWith("openid.", StringComparison.Ordinal) || key == "openid.mode") continue;
            form.Add(new KeyValuePair<string, string>(key, query[key] ?? string.Empty));
        }
        form.Add(new KeyValuePair<string, string>("openid.mode", "check_authentication"));

        using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return body.Contains("is_valid:true", StringComparison.Ordinal) ? steamId : null;
    }

    private static void Respond(HttpListenerContext context, int status, string message)
    {
        try
        {
            var html = $"<!doctype html><meta charset=\"utf-8\"><title>LiteLauncher</title>" +
                       $"<body style=\"font-family:Segoe UI,sans-serif;background:#0f1115;color:#f2f4f8;padding:3rem\">" +
                       $"<h2>LiteLauncher</h2><p>{WebUtility.HtmlEncode(message)}</p></body>";
            var bytes = Encoding.UTF8.GetBytes(html);

            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // Browser went away; nothing to do.
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }
}
