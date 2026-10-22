using System.Net.Http.Json;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

internal static class ContractTestHttp
{
    internal static HttpClient CreateManagementClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL")!)
        };
        client.DefaultRequestHeaders.Add("x-api-key", Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY")!);
        return client;
    }

    internal static HttpClient CreatePublicClient(string? baseUrl = null, bool allowRedirect = false) =>
        new(new HttpClientHandler { AllowAutoRedirect = allowRedirect })
        {
            BaseAddress = new Uri(baseUrl ?? ServerContractAvailability.EffectivePublicUrl(
                Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL"),
                Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL"))!)
        };

    internal static string NewVanity(string prefix = "contract") => prefix + Guid.NewGuid().ToString("N")[..12];

    internal static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    internal static async Task<JsonElement> CreateMobile(HttpClient client, string vanity)
    {
        using var response = await client.PostAsJsonAsync("/api/UrlCreate", new
        {
            vanity,
            linkType = "mobile",
            data = new Dictionary<string, string> { ["screen"] = "home" }
        });
        Xunit.Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ReadJson(response);
    }
}
