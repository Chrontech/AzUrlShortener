namespace ChronicleMobile.Core;

public static class DeepLinkParser
{
    public static bool TryParse(string? rawUri, out string shortId)
    {
        shortId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUri)
            || !Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "ChronicleMobile", StringComparison.OrdinalIgnoreCase)
            || !rawUri.StartsWith("ChronicleMobile://?", StringComparison.OrdinalIgnoreCase)
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

        var encodedShortId = rawQuery[(separator + 1)..];
        if (string.IsNullOrEmpty(encodedShortId) || !HasValidEscapes(encodedShortId))
        {
            return false;
        }

        try
        {
            shortId = Uri.UnescapeDataString(encodedShortId);
            return !string.IsNullOrWhiteSpace(shortId);
        }
        catch (UriFormatException)
        {
            shortId = string.Empty;
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
