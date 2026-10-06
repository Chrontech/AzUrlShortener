using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cloud5mins.ShortenerTools.Core.Messages;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ApiCrudContractTests
{
    private const string PortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";

    [ServerContractFact]
    public async Task Management_api_requires_authentication()
    {
        var baseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL")!;
        using var anonymous = new HttpClient { BaseAddress = new Uri(baseUrl) };
        using var response = await anonymous.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [ServerContractFact]
    public async Task Mobile_link_can_be_created_listed_updated_and_archived()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("lifecycle");
        using var create = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity,
            url = "https://ignored.example/",
            title = "before",
            linkType = "mobile",
            data = new Dictionary<string, string> { ["screen"] = "home" }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ContractTestHttp.ReadJson(create);
        Assert.Equal("mobile", created.GetProperty("linkType").GetString());
        Assert.Equal(PortalUrl, created.GetProperty("longUrl").GetString());
        Assert.Contains($"/m/{vanity}", created.GetProperty("shortUrl").GetString());
        Assert.Equal("home", created.GetProperty("data").GetProperty("screen").GetString());

        using var list = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await ContractTestHttp.ReadJson(list);
        var item = listed.GetProperty("urlList").EnumerateArray()
            .Single(link => link.GetProperty("rowKey").GetString() == vanity);
        Assert.Equal("mobile", item.GetProperty("linkType").GetString());

        using var update = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity,
            url = PortalUrl,
            title = "after",
            data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ContractTestHttp.ReadJson(update);
        Assert.Equal("after", updated.GetProperty("title").GetString());
        Assert.Empty(updated.GetProperty("data").EnumerateObject());

        using var archive = await client.PostAsJsonAsync("/api/UrlArchive", new
        {
            partitionKey = item.GetProperty("partitionKey").GetString(),
            rowKey = vanity
        });
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        using var afterArchive = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, afterArchive.StatusCode);
        Assert.DoesNotContain((await ContractTestHttp.ReadJson(afterArchive)).GetProperty("urlList").EnumerateArray(),
            link => link.GetProperty("rowKey").GetString() == vanity);
    }

    [ServerContractFact]
    public async Task Legacy_web_request_and_short_request_dto_create_web_links()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var legacyVanity = ContractTestHttp.NewVanity("legacy");
        using var legacyResponse = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = legacyVanity,
            url = "https://example.com/legacy"
        });
        Assert.Equal(HttpStatusCode.Created, legacyResponse.StatusCode);
        var legacy = await ContractTestHttp.ReadJson(legacyResponse);
        Assert.Equal("web", legacy.GetProperty("linkType").GetString());
        Assert.DoesNotContain("/m/", legacy.GetProperty("shortUrl").GetString());

        var dtoVanity = ContractTestHttp.NewVanity("dto");
        using var dtoResponse = await client.PostAsJsonAsync("/api/UrlCreate", new ShortRequest
        {
            Vanity = dtoVanity,
            Url = "https://example.com/dto",
            Title = "DTO client"
        });
        Assert.Equal(HttpStatusCode.Created, dtoResponse.StatusCode);
        Assert.Equal("web", (await ContractTestHttp.ReadJson(dtoResponse)).GetProperty("linkType").GetString());
    }

    [ServerContractFact]
    public async Task Mobile_create_allows_omitted_url_and_empty_metadata()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("optional");
        using var response = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity,
            linkType = "mobile",
            data = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ContractTestHttp.ReadJson(response);
        Assert.Equal(PortalUrl, created.GetProperty("longUrl").GetString());
        Assert.Empty(created.GetProperty("data").EnumerateObject());
    }

    [ServerContractFact]
    public async Task Mobile_create_rejects_schedules()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        using var response = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity = ContractTestHttp.NewVanity("schedule"),
            linkType = "mobile",
            schedules = new[] { new { alternativeUrl = "https://example.com/" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [ServerContractTheory]
    [InlineData("m", "reserved m")]
    [InlineData("resolve", "reserved resolve")]
    [InlineData(".well-known", "reserved association path")]
    public async Task Reserved_vanity_is_rejected(string vanity, string caseName)
    {
        _ = caseName;
        using var client = ContractTestHttp.CreateManagementClient();
        using var response = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity, url = "https://example.com/" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [ServerContractFact]
    public async Task Web_and_mobile_links_cannot_share_a_vanity()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("collision");
        using var web = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity, url = "https://example.com/" });
        Assert.Equal(HttpStatusCode.Created, web.StatusCode);
        using var mobile = await client.PostAsJsonAsync("/api/UrlCreate", new { vanity, linkType = "mobile" });
        Assert.Equal(HttpStatusCode.Conflict, mobile.StatusCode);
    }

    [ServerContractFact]
    public async Task Percent_vanity_is_created_listed_updated_and_publicly_encoded()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("encoded") + "%41";
        using var response = await client.PostAsJsonAsync("/api/UrlCreate", new ShortRequest
        {
            Vanity = vanity,
            LinkType = "mobile",
            Title = "Encoded"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/m/{Uri.EscapeDataString(vanity)}", (await ContractTestHttp.ReadJson(response)).GetProperty("shortUrl").GetString());

        using var listResponse = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var item = (await ContractTestHttp.ReadJson(listResponse)).GetProperty("urlList").EnumerateArray()
            .Single(link => link.GetProperty("rowKey").GetString() == vanity);
        Assert.EndsWith($"/m/{Uri.EscapeDataString(vanity)}", item.GetProperty("shortUrl").GetString());

        using var update = await client.PostAsJsonAsync("/api/UrlUpdate", new
        {
            partitionKey = vanity[..1], rowKey = vanity, title = "Updated encoded"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.EndsWith($"/m/{Uri.EscapeDataString(vanity)}", (await ContractTestHttp.ReadJson(update)).GetProperty("shortUrl").GetString());
    }

    [ServerContractTheory]
    [InlineData("\"data\":{\"screen\":\"home\"},\"data\":{\"screen\":\"other\"}", "duplicate exact name")]
    [InlineData("\"data\":{\"screen\":\"home\"},\"Data\":{\"screen\":\"other\"}", "duplicate mixed case")]
    [InlineData("\"data\":{\"screen\":\"home\"},\"data\":null", "null final exact duplicate")]
    [InlineData("\"data\":{\"screen\":\"home\"},\"Data\":null", "null final mixed-case duplicate")]
    [InlineData("\"data\":null", "null object")]
    [InlineData("\"data\":\"not-an-object\"", "string instead of object")]
    [InlineData("\"data\":{\"screen\":1}", "number metadata value")]
    [InlineData("\"data\":{\"screen\":false}", "boolean metadata value")]
    public async Task Invalid_mobile_metadata_create_is_rejected(string dataJson, string caseName)
    {
        _ = caseName;
        using var client = ContractTestHttp.CreateManagementClient();
        var body = "{\"vanity\":\"" + ContractTestHttp.NewVanity("invalid") + "\",\"linkType\":\"mobile\"," + dataJson + "}";
        using var response = await client.PostAsync("/api/UrlCreate", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [ServerContractTheory]
    [InlineData("\"data\":{\"screen\":\"changed\"},\"data\":{\"screen\":\"other\"}", "duplicate exact name")]
    [InlineData("\"data\":{\"screen\":\"changed\"},\"Data\":{\"screen\":\"other\"}", "duplicate mixed case")]
    [InlineData("\"data\":{\"screen\":\"changed\"},\"data\":null", "null final exact duplicate")]
    [InlineData("\"data\":{\"screen\":\"changed\"},\"Data\":null", "null final mixed-case duplicate")]
    [InlineData("\"data\":null", "null object")]
    [InlineData("\"data\":\"not-an-object\"", "string instead of object")]
    [InlineData("\"data\":{\"screen\":false}", "boolean metadata value")]
    public async Task Invalid_mobile_metadata_update_is_rejected_without_changing_metadata(string dataJson, string caseName)
    {
        _ = caseName;
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("updateinvalid");
        await ContractTestHttp.CreateMobile(client, vanity);
        var body = "{\"partitionKey\":\"" + vanity[..1] + "\",\"rowKey\":\"" + vanity + "\"," + dataJson + "}";
        using var response = await client.PostAsync("/api/UrlUpdate", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await ReadMobileMetadata(client, vanity);
        Assert.Equal("home", unchanged.GetProperty("data").GetProperty("screen").GetString());
    }

    [ServerContractFact]
    public async Task Non_string_mobile_metadata_update_is_rejected_without_changing_metadata()
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("badvalue");
        await ContractTestHttp.CreateMobile(client, vanity);
        var body = "{\"partitionKey\":\"" + vanity[..1] + "\",\"rowKey\":\"" + vanity + "\",\"data\":[\"not-a-dictionary\"]}";
        using var response = await client.PostAsync("/api/UrlUpdate", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("home", (await ReadMobileMetadata(client, vanity)).GetProperty("data").GetProperty("screen").GetString());
    }

    [ServerContractFact]
    public async Task Mobile_url_edit_is_rejected_without_changing_metadata()
    {
        await AssertForbiddenMobileUpdate(new { url = "https://example.com/changed" });
    }

    [ServerContractFact]
    public async Task Mobile_schedule_edit_is_rejected_without_changing_metadata()
    {
        await AssertForbiddenMobileUpdate(new { schedules = new[] { new { alternativeUrl = "https://example.com/" } } });
    }

    [ServerContractFact]
    public async Task Mobile_type_edit_is_rejected_without_changing_metadata()
    {
        await AssertForbiddenMobileUpdate(new { linkType = "web" });
    }

    private static async Task AssertForbiddenMobileUpdate(object changes)
    {
        using var client = ContractTestHttp.CreateManagementClient();
        var vanity = ContractTestHttp.NewVanity("protected");
        await ContractTestHttp.CreateMobile(client, vanity);
        var update = new Dictionary<string, object?>
        {
            ["partitionKey"] = vanity[..1],
            ["rowKey"] = vanity
        };
        foreach (var property in System.Text.Json.JsonSerializer.SerializeToElement(changes).EnumerateObject())
            update[property.Name] = property.Value.Clone();

        using var response = await client.PostAsJsonAsync("/api/UrlUpdate", update);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("home", (await ReadMobileMetadata(client, vanity)).GetProperty("data").GetProperty("screen").GetString());
    }

    private static async Task<System.Text.Json.JsonElement> ReadMobileMetadata(HttpClient client, string vanity)
    {
        using var response = await client.GetAsync("/api/UrlList");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ContractTestHttp.ReadJson(response)).GetProperty("urlList").EnumerateArray()
            .Single(link => link.GetProperty("rowKey").GetString() == vanity);
    }
}
