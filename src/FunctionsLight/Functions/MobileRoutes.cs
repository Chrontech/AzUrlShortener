using Azure;
using Azure.Data.Tables;
using Cloud5mins.ShortenerTools.Core.Domain;
using Cloud5mins.ShortenerTools.Core.Service;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Functions;

public class MobileRoutes
{
    private const string DefaultPortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";
    private const string DefaultUriScheme = "ChronicleMobile";
    private const string DefaultIosAppId = "MQZQS24FH9.com.gochronicle.chroniclemobile";
    private const string DefaultAndroidPackage = "com.gochronicle.chroniclemobileapp";

    private readonly ILogger _logger;
    private readonly TableServiceClient _tblClient;

    public MobileRoutes(ILoggerFactory loggerFactory, TableServiceClient tblClient)
    {
        _logger = loggerFactory.CreateLogger<MobileRoutes>();
        _tblClient = tblClient;
    }

    [Function("MobileResolve")]
    public async Task<HttpResponseData> Resolve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resolve/{shortId}")] HttpRequestData req,
        string shortId)
    {
        var link = await GetMobileLink(shortId);
        var response = req.CreateResponse(link switch
        {
            null => HttpStatusCode.NotFound,
            { IsArchived: true } => HttpStatusCode.Gone,
            _ => HttpStatusCode.OK
        });
        SetNoCache(response);

        if (link != null && !(link.IsArchived ?? false))
        {
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(link.Data));
        }

        return response;
    }

    [Function("MobileRedirect")]
    public async Task<HttpResponseData> MobileRedirect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "m/{shortId}")] HttpRequestData req,
        string shortId)
    {
        var link = await GetMobileLink(shortId);
        if (link == null || (link.IsArchived ?? false))
        {
            return RedirectToPortal(req);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(CreateInterstitial(shortId));
        return response;
    }

    [Function("AppleAppSiteAssociation")]
    public async Task<HttpResponseData> AppleAppSiteAssociation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/apple-app-site-association")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            applinks = new
            {
                details = new[]
                {
                    new
                    {
                        appID = GetSetting("ChronicleIosAppId", DefaultIosAppId),
                        components = new[] { new Dictionary<string, string> { ["/"] = "/m/*" } }
                    }
                }
            }
        }));
        return response;
    }

    [Function("AndroidAssetLinks")]
    public async Task<HttpResponseData> AndroidAssetLinks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/assetlinks.json")] HttpRequestData req)
    {
        var fingerprints = GetFingerprints();
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        if (fingerprints.Length == 0)
        {
            await response.WriteStringAsync("[]");
            return response;
        }

        await response.WriteStringAsync(JsonSerializer.Serialize(new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = GetSetting("ChronicleAndroidPackage", DefaultAndroidPackage),
                    sha256_cert_fingerprints = fingerprints
                }
            }
        }));
        return response;
    }

    private async Task<ShortUrlEntity?> GetMobileLink(string shortId)
    {
        try
        {
            var storage = new AzStrorageTablesService(_tblClient);
            var link = await storage.GetShortUrlEntity(new ShortUrlEntity(string.Empty, shortId));
            return string.Equals(link.LinkType, LinkTypes.Mobile, StringComparison.OrdinalIgnoreCase) ? link : null;
        }
        catch (RequestFailedException exception) when (exception.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to resolve mobile link {ShortId}.", shortId);
            throw;
        }
    }

    private static void SetNoCache(HttpResponseData response)
    {
        response.Headers.Add("Cache-Control", "no-store, no-cache");
        response.Headers.Add("Pragma", "no-cache");
        response.Headers.Add("Expires", "0");
    }

    private static HttpResponseData RedirectToPortal(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", GetSetting("ChroniclePortalUrl", DefaultPortalUrl));
        return response;
    }

    private static string CreateInterstitial(string shortId)
    {
        var launchUri = JsonSerializer.Serialize($"{GetSetting("ChronicleUriScheme", DefaultUriScheme)}://?shortid={Uri.EscapeDataString(shortId)}");
        var portalUrl = JsonSerializer.Serialize(GetSetting("ChroniclePortalUrl", DefaultPortalUrl));
        return $$"""<!doctype html><html><head><meta charset="utf-8"><title>Opening Chronicle</title></head><body><p>Opening Chronicle…</p><script>(() => { let launched = false; const markLaunched = () => { launched = true; }; document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') markLaunched(); }); window.addEventListener('pagehide', markLaunched); window.addEventListener('beforeunload', markLaunched); window.location.href = {{launchUri}}; window.setTimeout(() => { if (!launched) window.location.replace({{portalUrl}}); }, 1500); })();</script></body></html>""";
    }

    private static string[] GetFingerprints()
    {
        return (Environment.GetEnvironmentVariable("ChronicleAndroidSigningFingerprints") ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetSetting(string name, string defaultValue)
    {
        return Environment.GetEnvironmentVariable(name) ?? defaultValue;
    }
}
