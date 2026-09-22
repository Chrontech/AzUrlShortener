using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerContractFactAttribute : FactAttribute
{
    public ServerContractFactAttribute(bool requiresApiKey = true)
    {
        var baseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL");
        var publicBaseUrl = Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL") ?? baseUrl;
        var apiKey = Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY");

        if (string.IsNullOrWhiteSpace(publicBaseUrl) || (requiresApiKey && string.IsNullOrWhiteSpace(apiKey)))
        {
            Skip = "Set SERVER_CONTRACT_PUBLIC_BASE_URL (or SERVER_CONTRACT_BASE_URL) and SERVER_CONTRACT_API_KEY to run server contract tests.";
        }
    }
}
