using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Cloud5mins.ShortenerTools.Core.Domain;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class PublicMobileContractTests
{
    [ServerContractFact]
    public async Task Mobile_public_route_resolves_metadata_renders_without_forwarding_it_and_archives()
    {
        using var api = ContractTestHttp.CreateManagementClient();
        using var publicClient = ContractTestHttp.CreatePublicClient();
        var vanity = ContractTestHttp.NewVanity("public") + "%41";
        var created = await ContractTestHttp.CreateMobile(api, vanity);
        var advertisedUrl = created.GetProperty("shortUrl").GetString()!;
        Assert.EndsWith($"/m/{Uri.EscapeDataString(vanity)}", advertisedUrl);
        var advertisedPath = advertisedUrl[advertisedUrl.IndexOf("/m/", StringComparison.Ordinal)..];

        using var resolve = await publicClient.GetAsync($"/resolve/{Uri.EscapeDataString(vanity)}");
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.StartsWith("application/json", resolve.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", resolve.Headers.CacheControl?.ToString());
        var metadata = await ContractTestHttp.ReadJson(resolve);
        Assert.Equal("home", metadata.GetProperty("screen").GetString());

        using var mobile = await publicClient.GetAsync(advertisedPath);
        Assert.Equal(HttpStatusCode.OK, mobile.StatusCode);
        Assert.StartsWith("text/html", mobile.Content.Headers.ContentType?.MediaType);
        var html = await mobile.Content.ReadAsStringAsync();
        var launchUrl = $"{MobileLinkSettings.UriScheme}://?shortid={Uri.EscapeDataString(vanity)}";
        var escapedLaunchUrl = WebUtility.HtmlEncode(launchUrl);
        var escapedPortalUrl = WebUtility.HtmlEncode(MobileLinkSettings.PortalUrl);
        Assert.Contains("Opening Chronicle", html);
        Assert.Contains(launchUrl, html);
        Assert.Matches($"<a\\b(?=[^>]*\\bid=\"open-chronicle\")(?=[^>]*\\bhref=\"{Regex.Escape(escapedLaunchUrl)}\")[^>]*>[\\s\\S]*?Open Chronicle[\\s\\S]*?</a>", html);
        Assert.Matches($"<a\\b(?=[^>]*\\bid=\"download-chronicle\")(?=[^>]*\\bhref=\"{Regex.Escape(escapedPortalUrl)}\")[^>]*>[\\s\\S]*?Download Chronicle[\\s\\S]*?</a>", html);
        Assert.DoesNotContain("screen", html);

        using var archive = await api.PostAsJsonAsync("/api/UrlArchive", new
        {
            partitionKey = vanity[..1], rowKey = vanity
        });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var archivedResolve = await publicClient.GetAsync($"/resolve/{Uri.EscapeDataString(vanity)}");
        Assert.Equal(HttpStatusCode.Gone, archivedResolve.StatusCode);
        Assert.Contains("no-store", archivedResolve.Headers.CacheControl?.ToString());
        using var archivedMobile = await publicClient.GetAsync(advertisedPath);
        Assert.Equal(HttpStatusCode.Redirect, archivedMobile.StatusCode);
        Assert.Equal(MobileLinkSettings.PortalUrl, archivedMobile.Headers.Location?.ToString());
    }

    [ServerContractFact(false)]
    public async Task Missing_mobile_vanity_has_not_found_resolve_and_portal_fallback()
    {
        using var client = ContractTestHttp.CreatePublicClient();
        var missing = ContractTestHttp.NewVanity("missing");
        using var resolve = await client.GetAsync($"/resolve/{missing}");
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
        Assert.Contains("no-store", resolve.Headers.CacheControl?.ToString());
        using var mobile = await client.GetAsync($"/m/{missing}");
        Assert.Equal(HttpStatusCode.Redirect, mobile.StatusCode);
        Assert.Equal(MobileLinkSettings.PortalUrl, mobile.Headers.Location?.ToString());
    }

    [ServerContractFact]
    public async Task Non_mobile_vanity_is_not_resolvable_as_mobile()
    {
        using var api = ContractTestHttp.CreateManagementClient();
        using var publicClient = ContractTestHttp.CreatePublicClient();
        var vanity = ContractTestHttp.NewVanity("web");
        using var create = await api.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity, url = "https://example.com/ordinary"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var webRedirect = await publicClient.GetAsync($"/{vanity}");
        Assert.Equal(HttpStatusCode.Redirect, webRedirect.StatusCode);
        Assert.Equal("https://example.com/ordinary", webRedirect.Headers.Location?.ToString());
        using var resolve = await publicClient.GetAsync($"/resolve/{vanity}");
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
        Assert.Contains("no-store", resolve.Headers.CacheControl?.ToString());
        using var mobile = await publicClient.GetAsync($"/m/{vanity}");
        Assert.Equal(HttpStatusCode.Redirect, mobile.StatusCode);
        Assert.Equal(MobileLinkSettings.PortalUrl, mobile.Headers.Location?.ToString());
    }

    [ServerContractFact]
    public async Task Empty_mobile_record_resolves_to_empty_metadata()
    {
        using var api = ContractTestHttp.CreateManagementClient();
        using var publicClient = ContractTestHttp.CreatePublicClient();
        var vanity = ContractTestHttp.NewVanity("empty");
        using var create = await api.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity, linkType = "mobile", data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var resolve = await publicClient.GetAsync($"/resolve/{vanity}");
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Empty((await ContractTestHttp.ReadJson(resolve)).EnumerateObject());
    }

    [ServerContractFact(false)]
    public async Task Association_documents_are_direct_json()
    {
        using var client = ContractTestHttp.CreatePublicClient();
        using var apple = await client.GetAsync("/.well-known/apple-app-site-association");
        Assert.Equal(HttpStatusCode.OK, apple.StatusCode);
        Assert.StartsWith("application/json", apple.Content.Headers.ContentType?.MediaType);
        var appleDocument = await ContractTestHttp.ReadJson(apple);
        var appleDetail = appleDocument.GetProperty("applinks").GetProperty("details")[0];
        Assert.Equal(MobileLinkSettings.IosAppId, appleDetail.GetProperty("appID").GetString());
        Assert.Equal("/m/*", appleDetail.GetProperty("components")[0].GetProperty("/").GetString());

        using var android = await client.GetAsync("/.well-known/assetlinks.json");
        Assert.Equal(HttpStatusCode.OK, android.StatusCode);
        Assert.StartsWith("application/json", android.Content.Headers.ContentType?.MediaType);
        var androidDocument = await ContractTestHttp.ReadJson(android);
        var expectedFingerprints = MobileLinkSettings.AndroidSigningFingerprints;
        if (expectedFingerprints.Length == 0)
        {
            Assert.Empty(androidDocument.EnumerateArray());
        }
        else
        {
            var target = androidDocument[0].GetProperty("target");
            Assert.Equal(MobileLinkSettings.AndroidPackage, target.GetProperty("package_name").GetString());
            Assert.Equal(expectedFingerprints, target.GetProperty("sha256_cert_fingerprints").EnumerateArray().Select(value => value.GetString()));
        }
    }
}
