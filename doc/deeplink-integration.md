# MAUI deep-link integration

This guide summarizes the native link handling in the MAUI sample. It uses .NET MAUI 9 and **does not use the Branch.io SDK**. Custom-scheme links and associated HTTPS `/m/<shortId>` links share the same parser, resolver, and activation flow. It does not implement deferred links.

## 1. Configure the API

The Functions app supplies a resolver and an optional browser interstitial:

- `GET /resolve/{shortId}` returns the mobile link's metadata as JSON, `404` if missing, or `410` if archived.
- `GET /m/{shortId}` serves a browser interstitial that tries the custom URI and offers a portal fallback. When the OS verifies the installed app's domain association, the original HTTPS link may open the app directly instead of showing this page.

The Functions host has an empty route prefix, so routes are at the domain root. Use the same public HTTPS domain for the API and interstitial; the sample resolver defaults to `https://short.gochronicle.com/`.

The server-side mobile-link values are code-defined in [`MobileLinkSettings.cs`](../src/Core/Domain/MobileLinkSettings.cs). Edit that shared class and redeploy the Functions API and management API; these are not AppHost parameters or runtime environment settings.

| Constant | Current value |
| --- | --- |
| `UriScheme` | `ChronicleMobile` |
| `IosAppId` | `MQZQS24FH9.com.gochronicle.chroniclemobile` |
| `AndroidPackage` | `com.gochronicle.chroniclemobileapp` |
| `AndroidSigningFingerprints` | `20:65:EC:AC:40:82:27:29:45:9B:BE:67:43:8F:B4:83:5D:35:3F:83:E5:55:CB:36:8D:A6:48:ED:76:90:39:8C` |
| `PortalUrl` | `https://portal.gochronicle.com/?site=download-chronicle%2F` |

The iOS ID is the confirmed production app ID. Do not change it to match the sample app: the separate mobile-sample bundle ID is `com.gochronicle.chroniclemobileapp`. `AndroidSigningFingerprints` currently contains the fingerprint shown above; it must match the certificate signing the installed build (use the Play App Signing fingerprint for Play-distributed builds). If this setting is empty, the asset-links endpoint returns an empty target list and Android verification cannot succeed. These values are code-defined in `MobileLinkSettings`; they are not deployment environment variables.

The API also serves `/.well-known/assetlinks.json` and `/.well-known/apple-app-site-association`. With the sample's route configuration, these are:

```text
https://<your-api-domain>/.well-known/assetlinks.json
https://<your-api-domain>/.well-known/apple-app-site-association
```

## 2. Register and receive the custom URI in MAUI

The sample uses `ChronicleMobile://?shortid=<encoded-id>`. If you rename the scheme, also update the sample parser, Android/iOS registrations, and `MobileLinkSettings.UriScheme`.

- **Android:** add browsable `ACTION_VIEW` intent filters for the scheme. Handle the URI in both `OnCreate` (cold launch) and `OnNewIntent` (warm launch). The sample uses `LaunchMode.SingleTop` and registers both `ChronicleMobile` and `chroniclemobile` in [`MainActivity.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/Android/MainActivity.cs).
- **iOS:** register the scheme under `CFBundleURLTypes` in `Info.plist`; forward launch and open-URL callbacks to the app in `AppDelegate`. See [`Info.plist`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/iOS/Info.plist) and [`AppDelegate.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/Platforms/iOS/AppDelegate.cs).

Both platform handlers pass the incoming URI to `ActivationCoordinator.ProcessUriAsync`; see [`ActivationCoordinator.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/ActivationCoordinator.cs).

## 3. Parse and resolve the ID

- Reuse or copy the shared Core parser, resolver client, and result types: [`DeepLinkParser.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/DeepLinkParser.cs), [`ResolverClient.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/ResolverClient.cs), and [`ResolutionResult.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.Core/ResolutionResult.cs).
- The shared parser accepts `ChronicleMobile://?shortid=<URL-encoded-id>` and HTTPS links on `short.gochronicle.com` under `/m/`; it rejects unrelated hosts/paths and malformed links. The HTTPS host/path constants in `DeepLinkParser` define the accepted URL shape. Keep the literal `short.gochronicle.com` in the iOS Associated Domains entitlement synchronized with `DeepLinkParser.HttpsHost`.
- The client calls `GET /resolve/{escaped-id}` and maps `404`, `410`, and other failures to result statuses. Register the client/coordinator with MAUI DI as in [`MauiProgram.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MauiProgram.cs).

Render returned metadata as **untrusted display data**. The sample does not trigger actions or navigation from metadata; see [`MainPage.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MainPage.cs). A link must be a mobile link for the resolver to return its metadata.

## 4. Create and test mobile links

Create a mobile link on the management API with `POST /api/UrlCreate` and your API key (`x-api-key`). This is separate from the public Functions resolver; replace the management API host and key below with yours:

```sh
curl -X POST 'https://YOUR_API_HOST/api/UrlCreate' \
  -H 'x-api-key: YOUR_API_KEY' -H 'Content-Type: application/json' \
  --data '{"vanity":"my-mobile-link","linkType":"mobile","data":{"screen":"home"}}'
```

`data` is optional and must contain string values. See the [`UrlCreate` endpoint](https://github.com/Chrontech/AzUrlShortener/blob/branch-io-functionality/src/Api/ShortenerEnpoints.cs). The returned mobile URL uses `/m/<shortId>`; its interstitial attempts the custom URI and provides the configured portal fallback.

On an Android emulator using the local Functions resolver, run `adb reverse tcp:7071 tcp:7071` (Debug builds use `http://127.0.0.1:7071/`), then test a real ID using either supported link form:

```sh
adb shell am start -a android.intent.action.VIEW -d 'ChronicleMobile://?shortid=YOUR_SHORT_ID'
adb shell am start -a android.intent.action.VIEW -d 'https://short.gochronicle.com/m/YOUR_SHORT_ID'
curl -i 'http://127.0.0.1:7071/resolve/YOUR_SHORT_ID'
```

Replace `YOUR_SHORT_ID` with the ID from the create response; URL-encode it when needed. For a deployed resolver, use its HTTPS base URL instead. Check a valid ID returns `200` and JSON metadata; a missing ID returns `404`. [`TESTING.md`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/TESTING.md) documents cold/warm launch, invalid URI, and interstitial checks. The helper is [`src/tools/android-demo.sh`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/tools/android-demo.sh). Test on real iOS and Android devices too; handoff behavior varies by browser and OS.

## 5. Enable and verify OS HTTPS interception

The sample declares Android App Links and iOS Universal Links for `https://short.gochronicle.com/m/...` and routes an accepted HTTPS URL into the same activation/resolution path as the custom scheme. This does not prove that an OS or browser will hand off links: each platform's association document, app identity, signing profile, and installed build must match.

- Android's filter targets `short.gochronicle.com` and `/m/`. Set `MobileLinkSettings.AndroidPackage` and `AndroidSigningFingerprints` to the actual release package and signing certificate; the current code values are listed above. Verify that `https://short.gochronicle.com/.well-known/assetlinks.json` returns a matching statement. Empty fingerprints mean Android app verification is not configured, so installed verified App Links are not guaranteed by the app code alone.
- iOS has `applinks:short.gochronicle.com` in the sample entitlement. The provisioning profile used to sign/install the app must authorize Associated Domains. `MobileLinkSettings.IosAppId` is the confirmed production app ID and differs from the sample's Team ID plus `com.gochronicle.chroniclemobileapp` bundle ID. To test Universal Links for the sample, use a staging AASA association that includes the sample Team ID and bundle ID, or integrate the sample under the production app identity; do not overwrite the production ID merely to make the sample match. The public AASA document must allow `/m/*`. Verify `https://short.gochronicle.com/.well-known/apple-app-site-association` and test on a signed iOS device.
- HTTPS parser acceptance and the resolver host are independent: Android Debug builds accept production HTTPS link URLs but `MauiProgram` overrides their resolver base to `http://127.0.0.1:7071/`. Release Android and iOS use the production resolver by default.

Association documents are served by [`MobileRoutes.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/FunctionsLight/Functions/MobileRoutes.cs). Confirm HTTPS availability, correct content, and OS association verification for your domain and release builds. An HTTPS URL can still open the browser interstitial when the device has no installed/verified app or the browser/OS does not hand off the link.
