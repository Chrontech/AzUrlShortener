using System.Net.Http.Json;
using System.Text.Json;
using Cloud5mins.ShortenerTools.Core.Messages;
using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ShortRequestSerializationTests
{
    [Fact]
    public async Task Null_data_is_omitted_and_supplied_data_is_serialized()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = new ShortRequest { Vanity = "example", Title = "Example" };

        using var nullDataContent = JsonContent.Create(request, options: options);
        using var nullDataDocument = JsonDocument.Parse(await nullDataContent.ReadAsStringAsync());
        Assert.False(nullDataDocument.RootElement.TryGetProperty("data", out _));

        request.Data = new Dictionary<string, string> { ["screen"] = "home" };
        using var suppliedDataContent = JsonContent.Create(request, options: options);
        using var suppliedDataDocument = JsonDocument.Parse(await suppliedDataContent.ReadAsStringAsync());
        Assert.Equal("home", suppliedDataDocument.RootElement.GetProperty("data").GetProperty("screen").GetString());
    }
}
