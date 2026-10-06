# MAUI deep-link integration

This guide summarizes the working native custom-scheme path in the [`mobile-sample` branch](https://github.com/Chrontech/AzUrlShortener/tree/mobile-sample/src/MobileSample). It uses .NET MAUI 9 and **does not use the Branch.io SDK**. It does not implement deferred links or verified Android App Links / iOS Universal Links.

## 1. Configure the API

The Functions app supplies a resolver and an optional browser interstitial:

- `GET /resolve/{shortId}` returns the mobile link's metadata as JSON, `404` if missing, or `410` if archived.
- `GET /m/{shortId}` serves a page that tries the custom URI and offers a portal fallback.

The Functions host has an empty route prefix, so routes are at the domain root. Use the same public HTTPS domain for the API and interstitial; the sample resolver defaults to `https://short.gochronicle.com/`.

The server-side mobile-link values are code-defined in [`MobileLinkSettings.cs`](../src/Core/Domain/MobileLinkSettings.cs). Edit that shared class and redeploy the Functions API and management API; these are not AppHost parameters or runtime environment settings.

| Constant | Current value |
| --- | --- |
| `UriScheme` | `ChronicleMobile` |
| `IosAppId` | `MQZQS24FH9.com.gochronicle.chroniclemobile` |
| `AndroidPackage` | `com.gochronicle.chroniclemobileapp` |
| `AndroidSigningFingerprints` | `[]` (intentionally empty; assetlinks responds with `[]`) |
| `PortalUrl` | `https://portal.gochronicle.com/?site=download-chronicle%2F` |

The iOS ID is the confirmed production app ID. Do not change it to match the sample app: the separate mobile-sample bundle ID is `com.gochronicle.chroniclemobileapp`. When Android release/Play App Signing SHA-256 fingerprints are available, add the public fingerprint(s) to `AndroidSigningFingerprints`; leave them empty until then. These values are public and safe to commit.

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
- The parser accepts only `ChronicleMobile://?shortid=<URL-encoded-id>`; it rejects hosts, fragments, and extra query parameters. Its scheme is hardcoded, so changing the scheme also requires changing the parser, platform registrations, and server constant.
- The client calls `GET /resolve/{escaped-id}` and maps `404`, `410`, and other failures to result statuses. Register the client/coordinator with MAUI DI as in [`MauiProgram.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MauiProgram.cs).

Render returned metadata as **untrusted display data**. The sample does not trigger actions or navigation from metadata; see [`MainPage.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/MobileSample/ChronicleMobile.App/MainPage.cs). A link must be a mobile link for the resolver to return its metadata.

## 4. Create a link and test the custom-scheme path

Create a mobile link on the management API with `POST /api/UrlCreate` and your API key (`x-api-key`). This is separate from the public Functions resolver; replace the management API host and key below with yours:

```sh
curl -X POST 'https://YOUR_API_HOST/api/UrlCreate' \
  -H 'x-api-key: YOUR_API_KEY' -H 'Content-Type: application/json' \
  --data '{"vanity":"my-mobile-link","linkType":"mobile","data":{"screen":"home"}}'
```

`data` is optional and must contain string values. See the [`UrlCreate` endpoint](https://github.com/Chrontech/AzUrlShortener/blob/branch-io-functionality/src/Api/ShortenerEnpoints.cs). The returned mobile URL uses `/m/<shortId>`; its interstitial attempts the custom URI and provides the configured portal fallback.

On an Android emulator using the local Functions resolver, run `adb reverse tcp:7071 tcp:7071` (Debug builds use `http://127.0.0.1:7071/`), then test a real ID:

```sh
adb shell am start -a android.intent.action.VIEW -d 'ChronicleMobile://?shortid=YOUR_SHORT_ID'
curl -i 'http://127.0.0.1:7071/resolve/YOUR_SHORT_ID'
```

Replace `YOUR_SHORT_ID` with the ID from the create response; URL-encode it when needed. For a deployed resolver, use its HTTPS base URL instead. Check a valid ID returns `200` and JSON metadata; a missing ID returns `404`. [`TESTING.md`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/TESTING.md) documents cold/warm launch, invalid URI, and interstitial checks. The helper is [`src/tools/android-demo.sh`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/tools/android-demo.sh). Test on real iOS and Android devices too; handoff behavior varies by browser and OS.

## Optional: verified HTTPS links (additional implementation required)

The API endpoints above provide association documents, but the MAUI sample does **not** configure or handle verified links. Publishing association JSON alone does not enable them.

- For Android App Links, configure an `https` `ACTION_VIEW` filter for your domain and `/m/*` path with `autoVerify=true`. Set `MobileLinkSettings.AndroidPackage` and `AndroidSigningFingerprints` for your release; use the Play App Signing certificate for Play-distributed builds. Verify that the public asset-links URL returns the matching statement.
- For iOS Universal Links, add the `applinks:<your-domain>` Associated Domains entitlement and ensure `MobileLinkSettings.IosAppId` matches `<Team ID>.<bundle ID>`. Verify the public AASA URL has that app ID and allows `/m/*`.
- Add native handling that receives HTTPS `/m/<shortId>` links and extracts/resolves the ID. The sample handlers only process the custom URI scheme; association configuration by itself will not make these links work in this app.

Association documents are served by [`MobileRoutes.cs`](https://github.com/Chrontech/AzUrlShortener/blob/mobile-sample/src/FunctionsLight/Functions/MobileRoutes.cs). Confirm HTTPS availability, correct content, and OS association verification for your own domain and release builds.
