using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

public class ServerContractAvailabilityTests
{
    [Theory]
    [InlineData(null, null, null, false, false, null)]
    [InlineData(null, "https://public.example", null, false, true, "https://public.example")]
    [InlineData("https://management.example", null, "key", true, true, "https://management.example")]
    [InlineData("https://management.example", "https://public.example", "key", true, true, "https://public.example")]
    [InlineData("https://management.example", "https://public.example", null, true, false, "https://public.example")]
    [InlineData(null, "https://public.example", "key", true, false, "https://public.example")]
    [InlineData("https://management.example", "", null, false, true, "https://management.example")]
    [InlineData("https://management.example", "   ", null, false, true, "https://management.example")]
    [InlineData(null, "", null, false, false, null)]
    public void Availability_requires_the_configuration_used_by_each_contract(
        string? managementUrl,
        string? publicUrl,
        string? apiKey,
        bool requiresManagement,
        bool expected,
        string? expectedEffectivePublicUrl)
    {
        Assert.Equal(expected, ServerContractAvailability.IsAvailable(managementUrl, publicUrl, apiKey, requiresManagement));
        Assert.Equal(expectedEffectivePublicUrl, ServerContractAvailability.EffectivePublicUrl(managementUrl, publicUrl));
    }
}
