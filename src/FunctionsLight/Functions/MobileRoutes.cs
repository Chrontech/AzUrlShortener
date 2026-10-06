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
                        appID = MobileLinkSettings.IosAppId,
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
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

        if (MobileLinkSettings.AndroidSigningFingerprints.Length == 0)
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
                    package_name = MobileLinkSettings.AndroidPackage,
                    sha256_cert_fingerprints = MobileLinkSettings.AndroidSigningFingerprints
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
        response.Headers.Add("Expires", "Thu, 01 Jan 1970 00:00:00 GMT");
    }

    private static HttpResponseData RedirectToPortal(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", MobileLinkSettings.PortalUrl);
        return response;
    }

    private static string CreateInterstitial(string shortId)
    {
        var rawLaunchUri = $"{MobileLinkSettings.UriScheme}://?shortid={Uri.EscapeDataString(shortId)}";
        var rawPortalUrl = MobileLinkSettings.PortalUrl;

        var launchUriJs = JsonSerializer.Serialize(rawLaunchUri);
        var portalUrlJs = JsonSerializer.Serialize(rawPortalUrl);

        var launchUriHtml = WebUtility.HtmlEncode(rawLaunchUri);
        var portalUrlHtml = WebUtility.HtmlEncode(rawPortalUrl);

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Opening Chronicle</title>
  <style>
    *, *::before, *::after {
      box-sizing: border-box;
    }
    body {
      margin: 0;
      padding: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      background-color: #f8fafc;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      color: #0f172a;
      -webkit-font-smoothing: antialiased;
    }
    .container {
      text-align: center;
      max-width: 400px;
      width: 90%;
      padding: 2.5rem 2rem;
      background: #ffffff;
      border-radius: 16px;
      box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.05), 0 10px 15px -3px rgb(0 0 0 / 0.1);
    }
    .spinner {
      display: inline-block;
      width: 40px;
      height: 40px;
      border: 3px solid #e2e8f0;
      border-radius: 50%;
      border-top-color: #4f46e5;
      animation: spin 1s linear infinite;
      margin-bottom: 1.5rem;
    }
    h1 {
      font-size: 1.375rem;
      font-weight: 700;
      margin: 0 0 0.75rem 0;
      letter-spacing: -0.025em;
    }
    p {
      font-size: 0.95rem;
      color: #475569;
      line-height: 1.5;
      margin: 0 0 2rem 0;
    }
    .button-group {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .btn {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: 0.75rem 1.5rem;
      font-size: 0.95rem;
      font-weight: 600;
      text-decoration: none;
      border-radius: 8px;
      transition: background-color 0.15s ease-in-out;
      cursor: pointer;
    }
    .btn-primary {
      background-color: #4f46e5;
      color: #ffffff;
    }
    .btn-primary:hover {
      background-color: #4338ca;
    }
    .btn-primary:active {
      background-color: #3730a3;
    }
    .btn-secondary {
      background-color: #f1f5f9;
      color: #334155;
    }
    .btn-secondary:hover {
      background-color: #e2e8f0;
    }
    .btn-secondary:active {
      background-color: #cbd5e1;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    @media (prefers-reduced-motion: reduce) {
      .spinner {
        animation: none;
      }
    }
  </style>
</head>
<body>
  <div class="container">
    <div class="spinner"></div>
    <h1>Opening Chronicle…</h1>
    <p>Your browser may block automatic opening. Use Open Chronicle to try again, or download the app.</p>
    <div class="button-group">
      <a id="open-chronicle" class="btn btn-primary" href="{{launchUriHtml}}">Open Chronicle</a>
      <a id="download-chronicle" class="btn btn-secondary" href="{{portalUrlHtml}}">Download Chronicle</a>
    </div>
  </div>
  <script>
    (() => {
      const launchUri = {{launchUriJs}};
      const portalUrl = {{portalUrlJs}};

      let fallbackTimer = null;

      function clearFallback() {
        if (fallbackTimer !== null) {
          clearTimeout(fallbackTimer);
          fallbackTimer = null;
        }
      }

      function armFallback() {
        clearFallback();
        fallbackTimer = setTimeout(() => {
          if (document.visibilityState === 'visible') {
            window.location.replace(portalUrl);
          }
        }, 1500);
      }

      // Clear the fallback if the tab becomes hidden or the page is unloaded.
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
          clearFallback();
        }
      });
      window.addEventListener('pagehide', clearFallback);

      // Allow manual re-triggering of the deep link to reset the fallback timer.
      document.getElementById('open-chronicle').addEventListener('click', () => {
        armFallback();
      });

      // Cancel fallback if downloading is explicitly requested.
      document.getElementById('download-chronicle').addEventListener('click', () => {
        clearFallback();
      });

      // Set fallback and attempt the initial automatic redirection.
      armFallback();
      try {
        window.location.href = launchUri;
      } catch (e) {
        // Ignore early failures to allow fallback timer to run.
      }
    })();
  </script>
</body>
</html>
""";
    }

}
