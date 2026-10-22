namespace ChronicleMobile.Core;

public static class DeepLinkParser
{
    public const string HttpsHost = "short.gochronicle.com";
    public const string MobilePathPrefix = "/m/";

    public static bool TryParse(string? rawUri, out string shortId)
    {
        shortId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUri)
            || !Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Scheme, "ChronicleMobile", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseCustomScheme(rawUri, uri, out shortId);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !rawUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, HttpsHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var authorityStart = "https://".Length;
        var pathStart = rawUri.IndexOfAny(['/', '?', '#'], authorityStart);
        var rawPath = pathStart < 0 ? string.Empty : rawUri[pathStart..];
        if (rawPath.Contains('?') || rawPath.Contains('#') || rawPath.Contains('\\')
            || !rawPath.StartsWith(MobilePathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var encodedShortId = rawPath[MobilePathPrefix.Length..];
        if (encodedShortId.Contains('/'))
        {
            return false;
        }

        return TryDecodeShortId(encodedShortId, out shortId, rejectDotSegments: true);
    }

    private static bool TryParseCustomScheme(string rawUri, Uri uri, out string shortId)
    {
        shortId = string.Empty;
        if (!rawUri.StartsWith("ChronicleMobile://?", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.Host)
            || rawUri.Contains('#'))
        {
            return false;
        }

        var rawQuery = rawUri["ChronicleMobile://?".Length..];
        var separator = rawQuery.IndexOf('=');
        if (rawQuery.Contains('&')
            || separator < 0
            || !string.Equals(rawQuery[..separator], "shortid", StringComparison.Ordinal))
        {
            return false;
        }

        return TryDecodeShortId(rawQuery[(separator + 1)..], out shortId);
    }

    private static bool TryDecodeShortId(string encodedShortId, out string shortId, bool rejectDotSegments = false)
    {
        shortId = string.Empty;
        if (string.IsNullOrEmpty(encodedShortId) || !HasValidEscapes(encodedShortId))
        {
            return false;
        }

        try
        {
            var decodedShortId = Uri.UnescapeDataString(encodedShortId);
            if (string.IsNullOrWhiteSpace(decodedShortId)
                || (rejectDotSegments && decodedShortId is "." or ".."))
            {
                return false;
            }

            shortId = decodedShortId;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool HasValidEscapes(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])))
            {
                return false;
            }
        }

        return true;
    }
}
