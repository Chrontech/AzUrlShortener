using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ApiCrudContractTests
{
    private const string PortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";

    [Fact]
    public async Task Mobile_link_can_be_created_listed_updated_and_archived()
    {
        var baseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

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

        var list = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await ReadJson(list);
        var item = listed.GetProperty("urlList").EnumerateArray()
            .Single(link => link.GetProperty("rowKey").GetString() == vanity);
        Assert.Equal("mobile", item.GetProperty("linkType").GetString());

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
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
