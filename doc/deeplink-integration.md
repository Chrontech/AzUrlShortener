# MAUI deep-link integration

This guide covers the server-side mobile-link endpoints in this branch and how a native app can consume them. The MAUI client implementation is maintained separately in the [`mobile-sample` branch](https://github.com/Chrontech/AzUrlShortener/tree/mobile-sample/src/MobileSample), not in this branch; that sample accepts both custom-scheme and associated HTTPS mobile links through the same parser and resolver flow. It uses .NET MAUI 9 and **does not use the Branch.io SDK**. It does not implement deferred links.

## 1. Configure the API

The Functions app supplies a resolver and an optional browser interstitial:

- `GET /resolve/{shortId}` returns the mobile link's metadata as JSON, `404` if missing, or `410` if archived.
- `GET /m/{shortId}` serves a browser interstitial that tries the custom URI and offers a portal fallback. When the OS verifies the app's domain association and intercepts the original HTTPS link, the native app opens directly and the browser does not fetch this interstitial. The app must parse that HTTPS URL to the same `shortId` and use the same `GET /resolve/{shortId}` flow; without OS handoff, the browser continues to the interstitial.

The Functions host has an empty route prefix, so routes are at the domain root. Use the same public HTTPS domain for the API and interstitial; the sample resolver defaults to `https://short.gochronicle.com/`.

The server-side mobile-link values are code-defined in [`MobileLinkSettings.cs`](../src/Core/Domain/MobileLinkSettings.cs). Edit that shared class and redeploy the Functions API and management API; these are not AppHost parameters or runtime environment settings.

| Constant | Current value |
| --- | --- |
| `UriScheme` | `ChronicleMobile` |
| `IosAppId` | `MQZQS24FH9.com.gochronicle.chroniclemobile` |
| `AndroidPackage` | `com.gochronicle.chroniclemobileapp` |
| `AndroidSigningFingerprints` | `20:65:EC:AC:40:82:27:29:45:9B:BE:67:43:8F:B4:83:5D:35:3F:83:E5:55:CB:36:8D:A6:48:ED:76:90:39:8C` |
| `PortalUrl` | `https://portal.gochronicle.com/?site=download-chronicle%2F` |

These settings are code-defined, not environment variables. The Android fingerprint must match the certificate that signs the installed build (use the Play App Signing certificate for Play-distributed builds); an empty fingerprint list yields no Android asset-links targets and cannot verify an App Link. The confirmed production iOS app ID is `MQZQS24FH9.com.gochronicle.chroniclemobile`. Do not change it merely to match the separate sample app's `com.gochronicle.chroniclemobileapp` bundle ID. To test the sample's Universal Links, use a staging AASA association containing the sample's Team ID and bundle ID, or integrate it using the production app identity. The signing profile must authorize the Associated Domains entitlement, and the AASA app ID must match the app being installed.

The API also serves `/.well-known/assetlinks.json` and `/.well-known/apple-app-site-association`. With the sample's route configuration, these are:

```text
https://<your-api-domain>/.well-known/assetlinks.json
https://<your-api-domain>/.well-known/apple-app-site-association
```

## 2. Native app integration (separate sample)

The server's mobile URL has the form `https://short.gochronicle.com/m/<shortId>`. The separate [`mobile-sample` branch](https://github.com/Chrontech/AzUrlShortener/tree/mobile-sample/src/MobileSample) demonstrates handling both this HTTPS URL and `ChronicleMobile://?shortid=<encoded-id>`. Its shared parser extracts the same short ID from either format, then sends it through the resolver and activation flow. If you rename the custom scheme, update that sample's parser and platform registrations along with `MobileLinkSettings.UriScheme`.

- **Android:** the external sample keeps custom-scheme `ACTION_VIEW` filters and adds an auto-verified HTTPS filter for `short.gochronicle.com` under `/m/`. It forwards cold `OnCreate` and warm `OnNewIntent` links to the same activation flow. See its [`MainActivity.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/Android/MainActivity.cs).
- **iOS:** the external sample retains its custom-scheme registration and adds Universal Link entitlement/callback handling. See its [`Info.plist`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/iOS/Info.plist), [`AppDelegate.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/iOS/AppDelegate.cs), and [entitlements](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/iOS/Entitlements.plist).

Both formats are passed to the same resolver; the native registration does not itself establish domain verification. For OS interception, the Android association document and signer or the iOS AASA app ID and signed Associated Domains profile must match the installed app.

## 3. Parse and resolve the ID

- Reuse or copy the shared Core parser, resolver client, and result types: [`DeepLinkParser.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/DeepLinkParser.cs), [`ResolverClient.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/ResolverClient.cs), and [`ResolutionResult.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/ResolutionResult.cs).
- The external sample parser accepts the custom URI and HTTPS links on `short.gochronicle.com` under `/m/`; both resolve the same short ID. Keep the accepted HTTPS host/path and native platform registrations aligned with the server's mobile URL.
- The client calls `GET /resolve/{escaped-id}` and maps `404`, `410`, and other failures to result statuses. Register the client/coordinator with MAUI DI as in [`MauiProgram.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MauiProgram.cs).

Render returned metadata as **untrusted display data**. The sample does not trigger actions or navigation from metadata; see [`MainPage.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MainPage.cs). A link must be a mobile link for the resolver to return its metadata.

## 4. Create and test a mobile link

Create a mobile link on the management API with `POST /api/UrlCreate` and your API key (`x-api-key`). This is separate from the public Functions resolver; replace the management API host and key below with yours:

```sh
curl -X POST 'https://YOUR_API_HOST/api/UrlCreate' \
  -H 'x-api-key: YOUR_API_KEY' -H 'Content-Type: application/json' \
  --data '{"vanity":"my-mobile-link","linkType":"mobile","data":{"screen":"home"}}'
```

`data` is optional and must contain string values. See the [`UrlCreate` endpoint](https://github.com/Chrontech/AzUrlShortener/blob/branch-io-functionality/src/Api/ShortenerEnpoints.cs). The returned mobile URL uses `/m/<shortId>`; its interstitial attempts the custom URI and provides the configured portal fallback.

Use the returned `https://short.gochronicle.com/m/<shortId>` URL to exercise the browser interstitial or, on a device with a verified app association, the original-HTTPS OS handoff. Check `GET /resolve/<shortId>` returns `200` and JSON metadata for a valid mobile link; a missing ID returns `404`. The separate sample branch's [Android emulator testing guide](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/TESTING.md) documents app-level cold/warm checks. OS/browser handoff still requires testing on devices with correctly signed and associated builds.

## 5. Verify OS HTTPS interception

The server publishes association documents, while the external MAUI sample configures native HTTPS link handling. Neither configuration alone guarantees interception: the OS validates the association against the installed app identity and signing credentials.

- For Android App Links, the native app must declare an `https` `ACTION_VIEW` filter with `autoVerify=true` for the domain and `/m/` path. `MobileLinkSettings.AndroidPackage` and `AndroidSigningFingerprints` must match the installed package and signing certificate; use the Play App Signing certificate for Play builds. Check that the public asset-links URL returns the matching statement. Empty fingerprints mean Android verification cannot succeed.
- For iOS Universal Links, the signed app must include the `applinks:<domain>` Associated Domains entitlement and its provisioning profile must authorize that capability. The AASA app ID must match `<Team ID>.<bundle ID>` and allow `/m/*`. The current production ID is `MQZQS24FH9.com.gochronicle.chroniclemobile`; the external sample bundle ID is `com.gochronicle.chroniclemobileapp`, so sample testing requires a matching staging association or use of the production app identity. Preserve the production ID rather than changing it solely for the sample.
- When OS interception succeeds, the app receives the original HTTPS `/m/<shortId>` URL, parses it to `<shortId>`, and uses the same `/resolve/<shortId>` flow as the custom scheme. The interstitial is bypassed; if the OS does not hand off the URL, the browser requests `/m/<shortId>` instead.

Association documents are served by [`MobileRoutes.cs`](../src/FunctionsLight/Functions/MobileRoutes.cs). Confirm HTTPS availability, correct content, and OS association verification for the domain and release builds. A browser may still show the interstitial when no app is installed, association verification fails, or the OS/browser does not hand off the link.
