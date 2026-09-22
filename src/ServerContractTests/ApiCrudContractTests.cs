using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ApiCrudContractTests
{
    private const string PortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";

    [ServerContractFact]
    public async Task Mobile_link_can_be_created_listed_updated_and_archived()
    {
        var baseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL")!;
        var apiKey = Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY")!;
        using var anonymous = new HttpClient { BaseAddress = new Uri(baseUrl) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/UrlList")).StatusCode);

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        var vanity = "contract" + Guid.NewGuid().ToString("N")[..12];

        var create = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity,
            url = "https://ignored.example/",
            title = "before",
            linkType = "mobile",
            data = new Dictionary<string, string> { ["screen"] = "home" }
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJson(create);
        Assert.Equal("mobile", created.GetProperty("linkType").GetString());
        Assert.Equal(PortalUrl, created.GetProperty("longUrl").GetString());
        Assert.Contains($"/m/{vanity}", created.GetProperty("shortUrl").GetString());
        Assert.Equal("home", created.GetProperty("data").GetProperty("screen").GetString());

        var optionalVanity = "optional" + Guid.NewGuid().ToString("N")[..12];
        var optionalCreate = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = optionalVanity,
            linkType = "mobile",
            data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, optionalCreate.StatusCode);
        var optional = await ReadJson(optionalCreate);
        Assert.Equal(PortalUrl, optional.GetProperty("longUrl").GetString());
        Assert.Empty(optional.GetProperty("data").EnumerateObject());

        var legacyVanity = "legacy" + Guid.NewGuid().ToString("N")[..12];
        var legacyCreate = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = legacyVanity,
            url = "https://example.com/legacy"
        });
        Assert.Equal(HttpStatusCode.Created, legacyCreate.StatusCode);
        var legacy = await ReadJson(legacyCreate);
        Assert.Equal("web", legacy.GetProperty("linkType").GetString());
        Assert.DoesNotContain("/m/", legacy.GetProperty("shortUrl").GetString());

        foreach (var reserved in new[] { "m", "resolve", ".well-known" })
        {
            var reservedCreate = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity = reserved, url = "https://example.com/" });
            Assert.Equal(HttpStatusCode.BadRequest, reservedCreate.StatusCode);
        }

        var scheduleCreate = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = "schedule" + Guid.NewGuid().ToString("N")[..12],
            linkType = "mobile",
            schedules = new[] { new { alternativeUrl = "https://example.com/" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, scheduleCreate.StatusCode);

        var invalidCreate = await client.PostAsync("/api/UrlCreate", new StringContent(
            "{\"vanity\":\"invalid" + Guid.NewGuid().ToString("N")[..12] + "\",\"linkType\":\"mobile\",\"data\":{\"screen\":1}}",
            Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);

        var list = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await ReadJson(list);
        var item = listed.GetProperty("urlList").EnumerateArray()
            .Single(link => link.GetProperty("rowKey").GetString() == vanity);
        Assert.Equal("mobile", item.GetProperty("linkType").GetString());

        var collisionVanity = "collision" + Guid.NewGuid().ToString("N")[..12];
        var webCollision = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity = collisionVanity, url = "https://example.com/" });
        Assert.Equal(HttpStatusCode.Created, webCollision.StatusCode);
        var mobileCollision = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity = collisionVanity, linkType = "mobile" });
        Assert.Equal(HttpStatusCode.Conflict, mobileCollision.StatusCode);

        var urlProtection = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity,
            url = "https://example.com/changed"
        });
        Assert.Equal(HttpStatusCode.BadRequest, urlProtection.StatusCode);

        var scheduleProtection = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity,
            schedules = new[] { new { alternativeUrl = "https://example.com/" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, scheduleProtection.StatusCode);

        var typeProtection = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity,
            linkType = "web"
        });
        Assert.Equal(HttpStatusCode.BadRequest, typeProtection.StatusCode);

        var invalidUpdate = await client.PostAsync("/api/UrlUpdate", new StringContent(
            "{\"partitionKey\":\"" + item.GetProperty("partitionKey").GetString() + "\",\"rowKey\":\"" + vanity + "\",\"data\":[\"not-a-dictionary\"]}",
            Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);

        var update = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity,
            url = PortalUrl,
            title = "after",
            data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJson(update);
        Assert.Equal("after", updated.GetProperty("title").GetString());
        Assert.Empty(updated.GetProperty("data").EnumerateObject());

        var archive = await client.PostAsJsonAsync("/api/UrlArchive", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity
        });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var afterArchive = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, afterArchive.StatusCode);
        Assert.DoesNotContain((await ReadJson(afterArchive)).GetProperty("urlList").EnumerateArray(), link => link.GetProperty("rowKey").GetString() == vanity);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
