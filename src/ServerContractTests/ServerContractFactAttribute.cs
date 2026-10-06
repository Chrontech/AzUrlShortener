using Xunit;

namespace Cloud5mins.ShortenerTools.ServerContractTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerContractFactAttribute : FactAttribute
{
    public ServerContractFactAttribute(bool requiresManagement = true)
    {
        var available = ServerContractAvailability.IsAvailable(
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL"),
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL"),
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY"),
            requiresManagement);

        if (!available)
            Skip = requiresManagement
                ? "Set SERVER_CONTRACT_BASE_URL and SERVER_CONTRACT_API_KEY to run management server contract tests."
                : "Set SERVER_CONTRACT_PUBLIC_BASE_URL or SERVER_CONTRACT_BASE_URL to run public server contract tests.";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerContractTheoryAttribute : TheoryAttribute
{
    public ServerContractTheoryAttribute(bool requiresManagement = true)
    {
        var available = ServerContractAvailability.IsAvailable(
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_BASE_URL"),
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_PUBLIC_BASE_URL"),
            Environment.GetEnvironmentVariable("SERVER_CONTRACT_API_KEY"),
            requiresManagement);

        if (!available)
            Skip = requiresManagement
                ? "Set SERVER_CONTRACT_BASE_URL and SERVER_CONTRACT_API_KEY to run management server contract tests."
                : "Set SERVER_CONTRACT_PUBLIC_BASE_URL or SERVER_CONTRACT_BASE_URL to run public server contract tests.";
    }
}

internal static class ServerContractAvailability
{
    internal static string? EffectivePublicUrl(string? managementUrl, string? publicUrl) =>
        string.IsNullOrWhiteSpace(publicUrl) ? managementUrl : publicUrl;

    internal static bool IsAvailable(string? managementUrl, string? publicUrl, string? apiKey, bool requiresManagement)
    {
        if (string.IsNullOrWhiteSpace(EffectivePublicUrl(managementUrl, publicUrl)))
            return false;

        return !requiresManagement ||
            (!string.IsNullOrWhiteSpace(managementUrl) && !string.IsNullOrWhiteSpace(apiKey));
    }
}
