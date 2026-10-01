using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class PublicMobileContractTests
{
    private static string PortalUrl => Environment.GetEnvironmentVariable("SERVER_CONTRACT_PORTAL_URL") ?? "https://portal.gochronicle.com/?site=download-chronicle%2F";
    private static string UriScheme => Environment.GetEnvironmentVariable("SERVER_CONTRACT_URI_SCHEME") ?? "ChronicleMobile";
    private static string IosAppId => Environment.GetEnvironmentVariable("SERVER_CONTRACT_IOS_APP_ID") ?? "MQZQS24FH9.com.gochronicle.chroniclemobile";
    private static string AndroidPackage => Environment.GetEnvironmentVariable("SERVER_CONTRACT_ANDROID_PACKAGE") ?? "com.gochronicle.chroniclemobileapp";

    [ServerContractFact]
    public async Task Mobile_routes_resolve_metadata_and_fall_back_without_forwarding_it()
    {
        var apiBaseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL")!;
        var publicBaseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL") ?? apiBaseUrl;
        var apiKey = Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY")!;
        var vanity = "public" + Guid.NewGuid().ToString("N")[..12];

        using var api = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        api.DefaultRequestHeaders.Add("x-api-key", apiKey);
        var create = await api.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity,
            linkType = "mobile",
            data = new Dictionary<string, string> { ["screen"] = "home" }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var publicClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(publicBaseUrl)
        };

        var resolve = await publicClient.GetAsync($"/resolve/{vanity}");
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.StartsWith("application/json", resolve.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", resolve.Headers.CacheControl?.ToString());
        var metadata = JsonDocument.Parse(await resolve.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal("home", metadata.GetProperty("screen").GetString());

        var mobile = await publicClient.GetAsync($"/m/{vanity}");
        Assert.Equal(HttpStatusCode.OK, mobile.StatusCode);
        Assert.StartsWith("text/html", mobile.Content.Headers.ContentType?.MediaType);
        var html = await mobile.Content.ReadAsStringAsync();
        Assert.Contains("Opening Chronicle", html);
        var launchUrl = $"{UriScheme}://?shortid={Uri.EscapeDataString(vanity)}";
        var escapedLaunchUrl = WebUtility.HtmlEncode(launchUrl);
        var escapedPortalUrl = WebUtility.HtmlEncode(PortalUrl);
        Assert.Contains(launchUrl, html);
        Assert.Matches($"<a\\b(?=[^>]*\\bid=\"open-chronicle\")(?=[^>]*\\bhref=\"{Regex.Escape(escapedLaunchUrl)}\")[^>]*>[\\s\\S]*?Open Chronicle[\\s\\S]*?</a>", html);
        Assert.Matches($"<a\\b(?=[^>]*\\bid=\"download-chronicle\")(?=[^>]*\\bhref=\"{Regex.Escape(escapedPortalUrl)}\")[^>]*>[\\s\\S]*?Download Chronicle[\\s\\S]*?</a>", html);
        Assert.DoesNotContain("screen", html);

        var missingResolve = await publicClient.GetAsync($"/resolve/missing{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, missingResolve.StatusCode);
        Assert.Contains("no-store", missingResolve.Headers.CacheControl?.ToString());

        var missingMobile = await publicClient.GetAsync($"/m/missing{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Redirect, missingMobile.StatusCode);
        Assert.Equal(PortalUrl, missingMobile.Headers.Location?.ToString());

        var archive = await api.PostAsJsonAsync("/api/UrlArchive", new
        {
            partitionKey = vanity[..1],
            rowKey = vanity
        });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var archivedResolve = await publicClient.GetAsync($"/resolve/{vanity}");
        Assert.Equal(HttpStatusCode.Gone, archivedResolve.StatusCode);
        Assert.Contains("no-store", archivedResolve.Headers.CacheControl?.ToString());

        var archivedMobile = await publicClient.GetAsync($"/m/{vanity}");
        Assert.Equal(HttpStatusCode.Redirect, archivedMobile.StatusCode);
        Assert.Equal(PortalUrl, archivedMobile.Headers.Location?.ToString());

        var webVanity = "web" + Guid.NewGuid().ToString("N")[..12];
        var web = await api.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = webVanity,
            url = "https://example.com/ordinary"
        });
        Assert.Equal(HttpStatusCode.Created, web.StatusCode);

        var webRedirect = await publicClient.GetAsync($"/{webVanity}");
        Assert.Equal(HttpStatusCode.Redirect, webRedirect.StatusCode);
        Assert.Equal("https://example.com/ordinary", webRedirect.Headers.Location?.ToString());

        var nonMobileResolve = await publicClient.GetAsync($"/resolve/{webVanity}");
        Assert.Equal(HttpStatusCode.NotFound, nonMobileResolve.StatusCode);
        Assert.Contains("no-store", nonMobileResolve.Headers.CacheControl?.ToString());

        var nonMobileRoute = await publicClient.GetAsync($"/m/{webVanity}");
        Assert.Equal(HttpStatusCode.Redirect, nonMobileRoute.StatusCode);
        Assert.Equal(PortalUrl, nonMobileRoute.Headers.Location?.ToString());

        var emptyVanity = "empty" + Guid.NewGuid().ToString("N")[..12];
        var emptyMobile = await api.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = emptyVanity,
            linkType = "mobile",
            data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, emptyMobile.StatusCode);

        var emptyResolve = await publicClient.GetAsync($"/resolve/{emptyVanity}");
        Assert.Equal(HttpStatusCode.OK, emptyResolve.StatusCode);
        var emptyMetadata = JsonDocument.Parse(await emptyResolve.Content.ReadAsStreamAsync()).RootElement;
        Assert.Empty(emptyMetadata.EnumerateObject());
    }

    [ServerContractFact(false)]
    public async Task Association_documents_are_direct_json()
    {
        var baseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL")
            ?? Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL")!;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl)
        };

        var apple = await client.GetAsync("/.well-known/apple-app-site-association");
        Assert.Equal(HttpStatusCode.OK, apple.StatusCode);
        Assert.StartsWith("application/json", apple.Content.Headers.ContentType?.MediaType);
        var appleDocument = JsonDocument.Parse(await apple.Content.ReadAsStreamAsync()).RootElement;
        var appleDetail = appleDocument.GetProperty("applinks").GetProperty("details")[0];
        Assert.Equal(IosAppId, appleDetail.GetProperty("appID").GetString());
        Assert.Equal("/m/*", appleDetail.GetProperty("components")[0].GetProperty("/").GetString());

        var android = await client.GetAsync("/.well-known/assetlinks.json");
        Assert.Equal(HttpStatusCode.OK, android.StatusCode);
        Assert.StartsWith("application/json", android.Content.Headers.ContentType?.MediaType);
        var androidDocument = JsonDocument.Parse(await android.Content.ReadAsStreamAsync()).RootElement;
        var expectedFingerprints = (Environment.GetEnvironmentVariable("SERVER_CONTRACT_ANDROID_SIGNING_FINGERPRINTS") ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (expectedFingerprints.Length == 0)
        {
            Assert.Empty(androidDocument.EnumerateArray());
        }
        else
        {
            var target = androidDocument[0].GetProperty("target");
            Assert.Equal(AndroidPackage, target.GetProperty("package_name").GetString());
            Assert.Equal(expectedFingerprints, target.GetProperty("sha256_cert_fingerprints").EnumerateArray().Select(value => value.GetString()));
        }
    }
}
