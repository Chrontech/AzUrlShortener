# Android emulator demo

The demo runs the real API, Functions, and Azurite backend in Docker Compose; only the MAUI Android APK build and emulator run on the host. Visual emulator validation remains **outstanding** until someone observes the checks below on a working emulator. This container has no `/dev/kvm`; do not treat software-emulator runs or this guide as visual acceptance.

## One-time Linux host setup

Use a graphical Ubuntu/Linux x86_64 host with hardware virtualization. The emulator must run on the host, not in Docker.

1. Install Bash, `curl`, Python 3, and Docker Engine with the Compose v2 plugin, following [Docker's Ubuntu instructions](https://docs.docker.com/engine/install/ubuntu/). Verify `docker compose version` and `docker run --rm hello-world` work for your user.
2. Install **.NET SDK 9.0.318** user-locally. The host needs .NET only to build the Android app; the backend uses its Docker images.

   ```bash
   mkdir -p "$HOME/.local/share/dotnet9"
   curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
   bash /tmp/dotnet-install.sh --version 9.0.318 --install-dir "$HOME/.local/share/dotnet9"
   export DOTNET_ROOT="$HOME/.local/share/dotnet9"
   export PATH="$DOTNET_ROOT:$PATH"
   dotnet --version   # must print 9.0.318, even if .NET 10 is also installed
   dotnet workload install maui-android
   ```

   See [scripted/manual .NET installation](https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual). Keep these exports in the demo shell and reapply them in a new shell.
3. Install JDK 17 and KVM support; set the actual Ubuntu JDK path. Log out/in after joining the `kvm` group, and enable VT-x/AMD-V in firmware if needed:

   ```bash
   sudo apt update && sudo apt install -y openjdk-17-jdk qemu-kvm
   sudo usermod -aG kvm "$USER"
   export JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64
   export PATH="$JAVA_HOME/bin:$PATH"
   java -version
   ```

4. Install Android Studio using [Android's Linux installation guide](https://developer.android.com/studio/install). In SDK Manager install Android SDK Platform **35**, Build-Tools **35.x**, Platform-Tools, Android Emulator, and an **x86_64 Google APIs** system image at API 35 or newer. Platform/build-tools 35 are for the .NET 9 Android build; the emulator OS may be newer. Accept SDK licenses and set SDK paths in the demo shell:

   ```bash
   export ANDROID_HOME="$HOME/Android/Sdk"
   export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
   ```

   If your SDK is elsewhere, use that path. Before creating or booting an AVD, run `emulator -accel-check` and do not continue unless acceleration succeeds; see [emulator acceleration](https://developer.android.com/studio/run/emulator-acceleration). Then use Device Manager to create, boot, and unlock an x86_64 API 35+ emulator. If KVM is unavailable, use a native KVM-capable Linux host or Android device rather than Docker/software emulation; this helper specifically requires an x86_64 emulator.

## Run and observe

At the repository root, with the emulator booted and unlocked and Docker available, run:

```bash
bash src/tools/android-demo.sh
```

The helper preflights prerequisites and host ports **5288** and **17071**, then builds/starts the real API, Functions, and Azurite Compose services. First run may take longer while Docker builds images and pulls dependencies. The helper creates a real mobile link, verifies API/Functions HTTP responses and metadata, publishes and installs a standalone Debug APK, then maps the emulator's device-side port **7071** to host port **17071**. Compose publishes only loopback ports. If either host port is occupied, stop the conflicting service yourself and rerun; the helper will not overwrite it. API and Functions are reached at `127.0.0.1` on the host, so this native-host topology does not depend on `host.docker.internal`.

With WSL and Docker Desktop, a WSL-side port check cannot establish whether a Windows port is free. If Docker reports `/forwards/expose ... 500`, check VS Code's **Ports** panel: Remote/WSL forwarding can occupy the same Windows ports Docker needs. Choose **Stop Forwarding** for conflicting entries (5288 or 17071 for this demo), then retry; do not kill VS Code or unrelated services. Functions uses host port 17071 rather than the commonly used 7071. Full WSL/Docker Desktop validation remains outstanding.

After installation, the helper automatically checks that both custom-scheme casing variants resolve to the Chronicle app through Android's `BROWSABLE` intent matching. This confirms package registration only; it does not establish that Chrome accepts or launches those links, or that HTTPS domain verification succeeded.

To check HTTPS activity-filter delivery independently of domain verification, with the emulator booted and app installed, run:

```bash
adb shell am start -W -a android.intent.action.VIEW -d 'https://short.gochronicle.com/m/YOUR_SHORT_ID'
```

Repeat with the app stopped for a cold launch and already running for a warm launch. This implicit command checks activity resolution; if Android asks which app to use, choose Chronicle. To force delivery to the app and specifically check its callback/parser/resolver path regardless of domain verification, use:

```bash
adb shell am start -W -a android.intent.action.VIEW -d 'https://short.gochronicle.com/m/YOUR_SHORT_ID' -p com.gochronicle.chroniclemobileapp
```

Neither command proves that a browser will hand off the link or that Android verified the domain. On Android 12+, check verification state and request/re-run verification with:

```bash
adb shell pm get-app-links com.gochronicle.chroniclemobileapp
adb shell pm verify-app-links --re-verify com.gochronicle.chroniclemobileapp
```

Successful OS verification additionally requires the public `assetlinks.json` to match the installed package and signing certificate. With the current source settings, check that the returned fingerprint matches this APK's signer; an empty fingerprints setting would return no Android targets.

On iOS, install a build signed with a provisioning profile that authorizes the `applinks:short.gochronicle.com` Associated Domains entitlement. Confirm the public AASA contains the app ID being tested and allows `/m/*`. The configured production app ID differs from the sample bundle ID; testing this sample requires a staging association with its Team ID and bundle ID or integrating it under the production identity—do not replace the confirmed production ID just for the sample. On a physical device, tap `https://short.gochronicle.com/m/YOUR_SHORT_ID` from another app or a suitable test page while Chronicle is stopped (cold launch). Then tap that exact same URL again while Chronicle is running (warm continuation), and confirm both activations reach the link-resolution result. Repeating the same URL verifies that a later warm activation is not suppressed after cold launch. Browser behavior can vary; association configuration alone is not evidence of successful handoff.

The helper pauses so you can actually inspect each screen:

1. Cold deep-link launch: expect **CHRONICLE Link Resolved**, the created ID, and `SCREEN` / `home`.
2. After Enter, launcher-first startup should show **Awaiting Link**. After Enter again, warm activation should show the same ID and `SCREEN` / `home`.
3. After Enter, the missing-ID link should show **Short Link Missing (404)**.
4. After Enter, the malformed-host URI should show **Invalid Deep Link**, with no browser navigation.

Confirm no app/system ANR or unexpected browser launch. Device commands and HTTP checks are not visual evidence. Services remain up for inspection until the final Enter or Ctrl-C. The helper cleans up only its uniquely named Compose project and its own ADB reverse mapping; it never stops the emulator or uninstalls the app. Compose logs and response artifacts remain under ignored `.local-android-e2e/`. If cleanup reports failure, use the exact project-scoped recovery command it prints. A package signature mismatch can be resolved by manually uninstalling `com.gochronicle.chroniclemobileapp` and rerunning, but this deletes that app's local data.

Visual Android acceptance remains outstanding until all expected screens have been observed on a working emulator. This guide does not validate iOS behavior.

At the final browser-test pause, open the printed URL in emulator Chrome and allow JavaScript to try opening the app on page load. There is no timed portal redirect; if Chrome blocks automatic app opening, use **Open Chronicle**. **Download Chronicle** remains available as the portal link. Do not rush to click: browser acceptance still requires emulator retesting. Do not treat the automatic intent-resolution checks as confirmation of Chrome behavior.
