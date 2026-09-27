# Local testing guide

## Scope and status

The server API, Functions, and Azurite contracts, plus Android Debug and Release builds, were validated locally. Full visual Android acceptance was **not** validated: it requires a KVM-backed emulator or a physical device. This container has no `/dev/kvm`; software emulation ran at about 100% CPU and produced system, launcher, and app-startup ANRs. That is an environment limitation, not a validated app defect. Do not report this guide as full UI E2E success.

## Prerequisites

Known-good local tooling:

- .NET 10 for the `net10.0` server contract tests.
- Dedicated .NET SDK **9.0.318** for the API, Functions, MAUI, and mobile unit tests.
- MAUI Android workload manifest `maui-android` **9.0.0/9.0.100**.
- Azure Functions Core Tools **4.15.1**.
- Docker, JDK 17, Android platform-tools **37.0.1**, emulator **37.1.11**, Android API/build-tools **35**, and the API 35 x86_64 system image.

Use a KVM-backed emulator host or a physical Android device for visual E2E. Check KVM before attempting an emulator:

```sh
test -e /dev/kvm && test -r /dev/kvm && test -w /dev/kvm
```

Tools may be installed user-locally. Set locations for the current shell (do not record credentials in shell history or files):

```sh
export TOOLS_ROOT="$HOME/.local/azurlshortener-tools"
export DOTNET9_ROOT="$TOOLS_ROOT/dotnet-9"
export DOTNET10=/usr/bin/dotnet # .NET 10 executable for ServerContractTests
export JAVA_HOME="$TOOLS_ROOT/jdk-17"
export ANDROID_HOME="$TOOLS_ROOT/android-sdk"
export PATH="$DOTNET9_ROOT:$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
dotnet --info
"$DOTNET10" --info
java -version
adb version
emulator -version
func --version
```

On containers where the default home/cache is not writable, give MAUI/Android writable user state before builds:

```sh
export HOME="$PWD/.local-android-e2e/home"
export XDG_CACHE_HOME="$PWD/.local-android-e2e/cache"
export XDG_CONFIG_HOME="$PWD/.local-android-e2e/config"
mkdir -p "$HOME" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME"
```

## Local server stack

From the repository root, start a disposable pinned Azurite instance. The service-host settings make each Azurite service listen on all container interfaces.

```sh
docker run --rm --name azurlshortener-e2e-azurite \
  -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite:3.35.0 \
  azurite --blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0
```

Set `AZURITE_HOST=127.0.0.1` when processes run natively on the Docker host. In this devcontainer topology, Docker-published ports are reached through `host.docker.internal`, so use `AZURITE_HOST=host.docker.internal` instead. Use the Azure SDK development-storage shorthand; it uses Azurite's public default development credential and does not require a captured or private key.

```sh
export AZURITE_HOST=host.docker.internal # use 127.0.0.1 outside this devcontainer topology
if [ "$AZURITE_HOST" = 127.0.0.1 ]; then
  export AZURITE_CONNECTION_STRING='UseDevelopmentStorage=true'
else
  export AZURITE_CONNECTION_STRING="UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://$AZURITE_HOST"
fi
```

This shorthand applies to the disposable Azurite command above with its default development account. If you configure a custom Azurite account/key instead, use a connection string with that account and explicit blob, queue, and table endpoints at `$AZURITE_HOST:10000`–`10002`; keep the key out of committed files and logs.

The API and Functions both use the `strTables` connection name. Set the exact configuration variables used by each host, then generate an ephemeral API key under the exact `APIKey` setting name:

```sh
export ConnectionStrings__strTables="$AZURITE_CONNECTION_STRING"
export AzureWebJobsStorage="$AZURITE_CONNECTION_STRING"
export FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
export APIKey="$(openssl rand -hex 32)"
```

Each launch terminal must receive/source the shared values above. The API terminal needs `ConnectionStrings__strTables` and `APIKey`; the Functions terminal needs `ConnectionStrings__strTables`, `AzureWebJobsStorage`, and `FUNCTIONS_WORKER_RUNTIME`. Keep .NET 9 first in `PATH` for the Functions worker; Core Tools can otherwise select a global .NET 10 installation.

Start the API on loopback in one terminal:

```sh
ASPNETCORE_URLS=http://127.0.0.1:5288 \
  "$DOTNET9_ROOT/dotnet" run --project src/Api/Cloud5mins.ShortenerTools.Api.csproj
```

In a second terminal, start Functions from its project directory:

```sh
(cd src/FunctionsLight && PATH="$DOTNET9_ROOT:$PATH" func start --port 7071)
```

`src/FunctionsLight/host.json` removes the normal Functions `/api` prefix. Use `/resolve/<id>`, `/m/<id>`, and `/.well-known/...`; do **not** use `/api/resolve/<id>`.

## Server validation

Probe the authenticated API and anonymous Functions association routes. Each command saves its body and tests the exact expected status without printing the API key.

```sh
API_LIST_STATUS="$(curl -sS -o .local-api-url-list.json -w '%{http_code}' -H "x-api-key: $APIKey" http://127.0.0.1:5288/api/UrlList)"
test "$API_LIST_STATUS" = 200
APPLE_ASSOCIATION_STATUS="$(curl -sS -o .local-apple-app-site-association.json -w '%{http_code}' http://127.0.0.1:7071/.well-known/apple-app-site-association)"
test "$APPLE_ASSOCIATION_STATUS" = 200
ANDROID_ASSOCIATION_STATUS="$(curl -sS -o .local-assetlinks.json -w '%{http_code}' http://127.0.0.1:7071/.well-known/assetlinks.json)"
test "$ANDROID_ASSOCIATION_STATUS" = 200
```

Create a unique mobile link using the supported payload. This stores the response and extracts the returned `/m/<id>` safely with Python's standard JSON/URL parser; it does not print the API key.

```sh
export VANITY="mobile$(date +%s)${RANDOM}"
CREATE_STATUS="$(curl -sS -o .local-create-response.json -w '%{http_code}' \
  -H "x-api-key: $APIKey" -H 'Content-Type: application/json' \
  --data "{\"vanity\":\"$VANITY\",\"url\":\"https://ignored.example/\",\"title\":\"local E2E\",\"linkType\":\"mobile\",\"data\":{\"screen\":\"home\"}}" \
  http://127.0.0.1:5288/api/UrlCreate)"
test "$CREATE_STATUS" = 201
export SHORT_ID="$(python3 -c 'import json,sys,urllib.parse; p=urllib.parse.urlparse(json.load(open(sys.argv[1]))["shortUrl"]).path.rstrip("/").split("/"); print(p[-1] if len(p) >= 2 and p[-2] == "m" else "")' .local-create-response.json)"
test -n "$SHORT_ID" && printf 'short ID: %s\n' "$SHORT_ID"
```

Expected create status is `201`. Verify that the item is listed and the resolver returns its metadata with `200`:

```sh
API_LIST_STATUS="$(curl -sS -o .local-api-url-list-after-create.json -w '%{http_code}' -H "x-api-key: $APIKey" http://127.0.0.1:5288/api/UrlList)"
test "$API_LIST_STATUS" = 200
RESOLVE_STATUS="$(curl -sS -o .local-resolve-response.json -w '%{http_code}' http://127.0.0.1:7071/resolve/"$SHORT_ID")"
test "$RESOLVE_STATUS" = 200
```

Run the live server contract suite using .NET 10 (not the .NET 9 MAUI SDK):

```sh
SERVER_CONTRACT_BASE_URL=http://127.0.0.1:5288 \
SERVER_CONTRACT_PUBLIC_BASE_URL=http://127.0.0.1:7071 \
SERVER_CONTRACT_API_KEY="$APIKey" \
  "$DOTNET10" test src/ServerContractTests/Cloud5mins.ShortenerTools.ServerContractTests.csproj
```

Expected result: all three contract tests pass. The test creates and archives its own records.

Run the mobile core unit tests with .NET 9:

```sh
PATH="$DOTNET9_ROOT:$PATH" dotnet test src/MobileSample/ChronicleMobile.Core.Tests/ChronicleMobile.Core.Tests.csproj
```

## Android builds on Linux

Use the .NET 9/JDK 17 environment above. This project targets Android and iOS; on Linux, specify both `-f net9.0-android` and `-p:TargetFrameworks=net9.0-android` so restore/build does not evaluate an unavailable iOS workload. The override also reaches project references during restore: restore the app for Android, then restore its Core reference separately for `net9.0` **last**, and build with `--no-restore`. Otherwise Core's assets can lose their `net9.0` target (`NETSDK1005`).

```sh
APP_PROJECT=src/MobileSample/ChronicleMobile.App/ChronicleMobile.App.csproj
CORE_PROJECT=src/MobileSample/ChronicleMobile.Core/ChronicleMobile.Core.csproj
"$DOTNET9_ROOT/dotnet" restore "$APP_PROJECT" -p:TargetFrameworks=net9.0-android -p:Configuration=Debug \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME"
"$DOTNET9_ROOT/dotnet" restore "$CORE_PROJECT"
"$DOTNET9_ROOT/dotnet" build "$APP_PROJECT" -c Debug -f net9.0-android \
  -p:TargetFrameworks=net9.0-android -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" --no-restore

"$DOTNET9_ROOT/dotnet" restore "$APP_PROJECT" -p:TargetFrameworks=net9.0-android -p:Configuration=Release \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME"
"$DOTNET9_ROOT/dotnet" restore "$CORE_PROJECT"
"$DOTNET9_ROOT/dotnet" build "$APP_PROJECT" -c Release -f net9.0-android \
  -p:TargetFrameworks=net9.0-android -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" --no-restore
```

Inspect merged/packaged manifests as part of build validation. Expected invariants: both builds request `INTERNET`; `MainActivity` is exported and handles the custom `ChronicleMobile` scheme; Debug has cleartext enabled; Release has the `usesCleartextTraffic` flag absent or not true; and neither manifest declares Universal/App Links.

After both builds, inspect the generated manifests and signed build APKs. The Python check asserts the manifest-specific invariants; `aapt2` output is the packaged-manifest evidence, while `dump badging`, `unzip`, and `apksigner` show package, native libraries, and signature details.

```sh
APP_DIR=src/MobileSample/ChronicleMobile.App
DEBUG_MANIFEST="$APP_DIR/obj/Debug/net9.0-android/AndroidManifest.xml"
RELEASE_MANIFEST="$APP_DIR/obj/Release/net9.0-android/AndroidManifest.xml"
DEBUG_APK="$APP_DIR/bin/Debug/net9.0-android/com.gochronicle.chroniclemobileapp-Signed.apk"
RELEASE_APK="$APP_DIR/bin/Release/net9.0-android/com.gochronicle.chroniclemobileapp-Signed.apk"
BUILD_TOOLS_DIR="$(printf '%s\n' "$ANDROID_HOME"/build-tools/* | sort -V | tail -n 1)"
AAPT2="$BUILD_TOOLS_DIR/aapt2"
APKSIGNER="$BUILD_TOOLS_DIR/apksigner"
test -f "$DEBUG_MANIFEST" && test -f "$RELEASE_MANIFEST"
test -x "$AAPT2" && test -x "$APKSIGNER"
test -f "$DEBUG_APK" && test -f "$RELEASE_APK"
python3 - "$DEBUG_MANIFEST" "$RELEASE_MANIFEST" <<'PY'
import sys
import xml.etree.ElementTree as ET

android = '{http://schemas.android.com/apk/res/android}'
for path, debug in zip(sys.argv[1:], (True, False)):
    root = ET.parse(path).getroot()
    permissions = {node.get(android + 'name') for node in root.findall('uses-permission')}
    assert 'android.permission.INTERNET' in permissions, path
    app = root.find('application')
    cleartext = app.get(android + 'usesCleartextTraffic')
    assert cleartext == 'true' if debug else cleartext != 'true', path
    activities = app.findall('activity')
    main = next((a for a in activities if 'MainActivity' in (a.get(android + 'name') or '')), None)
    assert main is not None and main.get(android + 'exported') == 'true', path
    main_schemes = [data.get(android + 'scheme') for data in main.findall('.//data')]
    all_schemes = [data.get(android + 'scheme') for activity in activities for data in activity.findall('.//data')]
    assert 'ChronicleMobile' in main_schemes, path
    assert not {'http', 'https'} & set(filter(None, all_schemes)), path
    print(f'{path}: manifest invariants verified')
PY
for apk in "$DEBUG_APK" "$RELEASE_APK"; do
  "$AAPT2" dump xmltree "$apk" AndroidManifest.xml
  "$AAPT2" dump badging "$apk" | grep -E "package: name='com.gochronicle.chroniclemobileapp'|native-code:"
  unzip -Z1 "$apk" | grep -E '^lib/(x86_64|arm64-v8a)/'
  "$APKSIGNER" verify --verbose --print-certs "$apk"
done
```

### Standalone APK for `adb install`

An ordinary Debug build expects Fast Deployment and can crash if directly installed without IDE deployment. Publish a self-contained standalone APK instead:

```sh
export ANDROID_RID=android-x64 # use android-arm64 for a typical physical device
"$DOTNET9_ROOT/dotnet" restore "$APP_PROJECT" -p:TargetFrameworks=net9.0-android \
  -p:Configuration=Debug -p:RuntimeIdentifier="$ANDROID_RID" -p:SelfContained=true \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME"
"$DOTNET9_ROOT/dotnet" restore "$CORE_PROJECT"
"$DOTNET9_ROOT/dotnet" publish "$APP_PROJECT" -c Debug \
  -f net9.0-android -r "$ANDROID_RID" --self-contained true \
  -p:TargetFrameworks=net9.0-android -p:EmbedAssembliesIntoApk=true \
  -p:AndroidPackageFormats=apk -p:AndroidSdkDirectory="$ANDROID_HOME" \
  -p:JavaSdkDirectory="$JAVA_HOME" --no-restore
```

Use `ANDROID_RID=android-arm64` for a physical arm64 device. If installation reports a signing mismatch, uninstall only this test package and reinstall:

Verify the publish artifact, including its embedded managed assembly payload, before direct installation:

```sh
STANDALONE_APK="$APP_DIR/bin/Debug/net9.0-android/$ANDROID_RID/publish/com.gochronicle.chroniclemobileapp-Signed.apk"
test -n "$STANDALONE_APK" && test -f "$STANDALONE_APK"
"$AAPT2" dump badging "$STANDALONE_APK" | grep -E "package: name='com.gochronicle.chroniclemobileapp'|native-code:"
unzip -Z1 "$STANDALONE_APK" | grep -E '^lib/[^/]+/lib_ChronicleMobile\.(App|Core)\.dll\.so$'
"$APKSIGNER" verify --verbose --print-certs "$STANDALONE_APK"
```

```sh
adb uninstall com.gochronicle.chroniclemobileapp
adb install -r "$STANDALONE_APK"
```

## Device E2E checklist

Use a booted KVM-backed emulator or connected physical device. `10.0.2.2` failed in this nested environment; Debug uses `http://127.0.0.1:7071/`, so reissue the reverse after **every** boot or reconnect and verify it:

```sh
adb devices
adb wait-for-device
adb reverse tcp:7071 tcp:7071
adb reverse --list
adb shell am force-stop com.gochronicle.chroniclemobileapp
adb install -r '<path-to-standalone.apk>'
curl --fail-with-body http://127.0.0.1:7071/resolve/"$SHORT_ID"
```

Run these cases (substitute the ID created above) and observe the screen, focused activity, and Functions request log:

```sh
# Cold custom-scheme launch
adb shell am force-stop com.gochronicle.chroniclemobileapp
adb shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$SHORT_ID"

# Launcher-first, then warm custom-scheme activation
adb shell am force-stop com.gochronicle.chroniclemobileapp
adb shell monkey -p com.gochronicle.chroniclemobileapp 1
for _ in $(seq 1 30); do
  adb shell dumpsys activity activities | grep -q 'mResumedActivity.*com.gochronicle.chroniclemobileapp' && \
    adb shell dumpsys activity activities | grep -q 'reportedDrawn=true' && break
  sleep 1
done
adb shell dumpsys activity activities | grep -q 'mResumedActivity.*com.gochronicle.chroniclemobileapp'
adb shell dumpsys activity activities | grep -q 'reportedDrawn=true'
adb shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$SHORT_ID"

# Missing ID and invalid URI
adb shell am start -W -a android.intent.action.VIEW -d 'ChronicleMobile://?shortid=missing-local-id'
adb shell am start -W -a android.intent.action.VIEW -d 'ChronicleMobile://host?shortid=bad'

# Inspect foreground activity/drawn state
adb shell dumpsys activity activities | grep -E 'mResumedActivity|reportedDrawn'
```

Visible acceptance matrix:

| Case | Setup/activation | Expected visible state |
| --- | --- | --- |
| Metadata success | Launch `ChronicleMobile://?shortid=$SHORT_ID` | `Link Resolved`, short ID, and `screen` / `home`; exactly one resolver request. |
| Empty metadata success | Create a mobile link through `POST /api/UrlCreate` with `data: {}`, then launch its custom URI. | `Link Resolved` and `Empty metadata dictionary received ({}).` |
| Missing ID | Launch `ChronicleMobile://?shortid=missing-local-id`. | `Short Link Missing (404)`. |
| Archived ID | Archive a created mobile ID through `POST /api/UrlArchive`, then launch its custom URI. | `Link Archived (410)`. |
| Network failure | Temporarily remove the reverse, launch a known ID, then restore it. | `Resolution Failed`. |
| Non-200 or malformed JSON | Use the isolated fixture requirement below. | `Resolution Failed`. |
| Invalid URI | Launch `ChronicleMobile://host?shortid=bad`. | `Invalid Deep Link`. |

Create the empty-metadata link and archive an existing link with the supported API lifecycle endpoints before the corresponding rows above:

```sh
EMPTY_VANITY="empty$(date +%s)${RANDOM}"
EMPTY_CREATE_STATUS="$(curl -sS -o .local-empty-create-response.json -w '%{http_code}' -H "x-api-key: $APIKey" -H 'Content-Type: application/json' --data "{\"vanity\":\"$EMPTY_VANITY\",\"linkType\":\"mobile\",\"data\":{}}" http://127.0.0.1:5288/api/UrlCreate)"
test "$EMPTY_CREATE_STATUS" = 201
EMPTY_SHORT_ID="$(python3 -c 'import json,sys,urllib.parse; p=urllib.parse.urlparse(json.load(open(sys.argv[1]))["shortUrl"]).path.rstrip("/").split("/"); print(p[-1] if len(p) >= 2 and p[-2] == "m" else "")' .local-empty-create-response.json)"
test -n "$EMPTY_SHORT_ID"
PARTITION_KEY="$(printf %s "$SHORT_ID" | cut -c1)"
ARCHIVE_STATUS="$(curl -sS -o .local-archive-response.json -w '%{http_code}' -H "x-api-key: $APIKey" -H 'Content-Type: application/json' --data "{\"partitionKey\":\"$PARTITION_KEY\",\"rowKey\":\"$SHORT_ID\"}" http://127.0.0.1:5288/api/UrlArchive)"
test "$ARCHIVE_STATUS" = 200
adb shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$EMPTY_SHORT_ID"
adb shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$SHORT_ID"
```

For the network row, then restore connectivity before subsequent cases:

```sh
adb reverse --remove tcp:7071
adb shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$EMPTY_SHORT_ID"
adb reverse tcp:7071 tcp:7071
adb reverse --list
```

Non-200 and malformed/unexpected-JSON UI evidence requires an external, controllable fixture bound to host `127.0.0.1:7071` (replacing the Functions listener only for that isolated test) and reached through `adb reverse`. It must return a non-200 response or an HTTP 200 body such as `[]`, `{"count":1}`, or non-JSON for `/resolve/<id>`, then be removed and the real Functions listener and reverse restored. No such fixture was run here.

For every Android case, the app must be focused, `reportedDrawn=true`, and visible text must include `CHRONICLE`; there must be no browser navigation and no app or system ANR. A surviving PID alone is not a pass. Android visible UI evidence remains outstanding until run on KVM-backed Android hardware or a physical device. iOS visible UI evidence also remains outstanding until a macOS/iOS environment runs the equivalent custom URI, for example: `xcrun simctl openurl <simulator-udid> "ChronicleMobile://?shortid=<id>"`.

## Production safety

Only the Android Debug build uses local `http://127.0.0.1:7071/`. Release and non-Android builds retain `https://short.gochronicle.com/`, use HTTPS, and do not enable cleartext. Do not change production configuration for local testing.

## Troubleshooting and cleanup

- Functions routes have no `/api` prefix.
- Put local .NET 9 first in `PATH` before `func start`.
- Set workspace-writable `HOME`/XDG variables if MAUI/Xamarin cache permission errors occur.
- Re-run `adb reverse tcp:7071 tcp:7071` after every device boot/reconnect; nested-container `10.0.2.2` is not usable here.
- Direct installation needs the standalone publish APK, not the Fast Deployment Debug build.
- For a signature mismatch, uninstall `com.gochronicle.chroniclemobileapp` and reinstall.
- Without KVM, software emulation can cause system/launcher/app ANRs; use KVM or a physical device rather than treating those as an app failure.
- On Linux always override `TargetFrameworks` to Android-only as shown above.

Stop only local test resources; do not use destructive repository cleanup:

```sh
adb emu kill                 # emulator only; harmless to omit for a physical device
adb uninstall com.gochronicle.chroniclemobileapp  # optional test-app cleanup
docker stop azurlshortener-e2e-azurite
rm -f .local-api-url-list*.json .local-apple-app-site-association.json .local-assetlinks.json \
  .local-create-response.json .local-resolve-response.json .local-empty-create-response.json \
  .local-archive-response.json
```
