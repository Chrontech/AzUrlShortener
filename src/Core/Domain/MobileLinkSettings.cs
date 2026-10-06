namespace Cloud5mins.ShortenerTools.Core.Domain;

public static class MobileLinkSettings
{
    public const string UriScheme = "ChronicleMobile";
    public const string IosAppId = "MQZQS24FH9.com.gochronicle.chroniclemobile";
    public const string AndroidPackage = "com.gochronicle.chroniclemobileapp";
    public const string PortalUrl = "https://portal.gochronicle.com/?site=download-chronicle%2F";

    // Add actual release/Play App Signing SHA-256 public fingerprint(s) here.
    public static readonly string[] AndroidSigningFingerprints = [];
}
