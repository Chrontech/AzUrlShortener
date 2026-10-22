using System.Net;
using System.Text.Json;

namespace ChronicleMobile.Core;

public sealed class ResolverClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly Uri ProductionResolverBaseUri = new($"https://{DeepLinkParser.HttpsHost}/");
    private readonly HttpClient httpClient;
    private readonly Uri resolverBaseUri;

    public ResolverClient(HttpClient httpClient, Uri? resolverBaseUri = null)
    {
        if (resolverBaseUri is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The resolver base URI must be absolute.", nameof(resolverBaseUri));
        }

        this.httpClient = httpClient;
        var baseUri = resolverBaseUri ?? ProductionResolverBaseUri;
        this.resolverBaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public async Task<ResolutionResult> ResolveAsync(string? rawUri, CancellationToken cancellationToken = default)
    {
        if (!DeepLinkParser.TryParse(rawUri, out var shortId))
        {
            return ResolutionResult.InvalidUri();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(resolverBaseUri, $"resolve/{Uri.EscapeDataString(shortId)}"));
            request.Headers.Authorization = null;
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ResolutionResult(ResolutionStatus.Missing, shortId);
            }

            if (response.StatusCode == HttpStatusCode.Gone)
            {
                return new ResolutionResult(ResolutionStatus.Archived, shortId);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return ResolutionResult.Error(shortId);
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!TryReadMetadata(body, out var metadata))
            {
                return ResolutionResult.Error(shortId);
            }

            return new ResolutionResult(ResolutionStatus.Resolved, shortId, metadata);
        }
        catch (Exception)
        {
            return ResolutionResult.Error(shortId);
        }
    }

    private static bool TryReadMetadata(string body, out IReadOnlyDictionary<string, string>? metadata)
    {
        metadata = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                values.Add(property.Name, property.Value.GetString()!);
            }

            metadata = values;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return false;
        }
    }
}
