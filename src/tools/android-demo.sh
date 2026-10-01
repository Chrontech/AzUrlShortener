#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="$ROOT/compose.demo.yaml"
LOG_DIR="$ROOT/.local-android-e2e/demo-$(date +%Y%m%d-%H%M%S)-$$"
COMPOSE_LOG="$LOG_DIR/compose.log"
PROJECT="azurlshortener-demo-$$-${RANDOM}"
COMPOSE=(docker compose -f "$COMPOSE_FILE" -p "$PROJECT")
API_KEY=""
SERIAL=""
COMPOSE_STARTED=0
REVERSE_ADDED=0

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

cleanup() {
  local status=$? cleanup_failed=0
  trap - EXIT INT TERM
  if [[ "$COMPOSE_STARTED" == 1 ]]; then
    if ! timeout --kill-after=5s 60s "${COMPOSE[@]}" logs --no-color >"$COMPOSE_LOG" 2>&1; then
      printf 'WARNING: Could not save all Compose logs to %s.\n' "$COMPOSE_LOG" >&2
    fi
  fi
  if [[ -n "$SERIAL" && "$REVERSE_ADDED" == 1 ]]; then
    # Remove only the mapping owned on the emulator's device-side port 7071.
    if ! timeout 5s adb -s "$SERIAL" reverse --remove tcp:7071 >/dev/null 2>&1; then
      printf 'WARNING: Could not remove this demo adb reverse mapping.\n' >&2
      cleanup_failed=1
    fi
  fi
  if [[ "$COMPOSE_STARTED" == 1 ]]; then
    if ! timeout --kill-after=5s 60s "${COMPOSE[@]}" down --volumes --remove-orphans --timeout 5; then
      printf 'WARNING: Docker Compose cleanup did not complete.\n' >&2
      cleanup_failed=1
    fi
  fi
  if [[ "$status" != 0 ]]; then
    if [[ -d "$LOG_DIR" ]]; then
      printf 'Demo failed (status %s). Logs and artifacts: %s\n' "$status" "$LOG_DIR" >&2
    else
      printf 'Demo failed during preflight (status %s); no logs or demo resources were created.\n' "$status" >&2
    fi
  elif [[ -d "$LOG_DIR" ]]; then
    printf 'Compose logs and demo artifacts retained at: %s\n' "$LOG_DIR"
  fi
  if [[ "$cleanup_failed" == 1 ]]; then
    printf 'WARNING: Cleanup failed. If Compose resources remain, run: APIKey=unused docker compose -f ' >&2
    printf '%q' "$COMPOSE_FILE" >&2
    printf ' -p %q down --volumes --remove-orphans --timeout 5\n' "$PROJECT" >&2
    printf 'Check adb reverse --list for any remaining device mapping.\n' >&2
  fi
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

[[ -t 0 ]] || die 'Run from an interactive terminal so each visible check can be observed.'
for tool in docker dotnet adb emulator python3 curl timeout; do
  command -v "$tool" >/dev/null 2>&1 || die "Missing required command: $tool"
done
[[ -n "${DOTNET_ROOT:-}" && -x "$DOTNET_ROOT/dotnet" ]] || die 'Set DOTNET_ROOT to the installed .NET 9 SDK directory.'
[[ -n "${ANDROID_HOME:-}" && -d "$ANDROID_HOME" ]] || die 'Set ANDROID_HOME to the Android SDK directory.'
[[ -n "${JAVA_HOME:-}" && -x "$JAVA_HOME/bin/java" ]] || die 'Set JAVA_HOME to JDK 17.'
[[ "$("$DOTNET_ROOT/dotnet" --version)" == 9.0.318 ]] || die 'DOTNET_ROOT must point to .NET SDK 9.0.318 for the Android build.'
[[ "$(dotnet --version)" == "$("$DOTNET_ROOT/dotnet" --version)" ]] || die 'Put DOTNET_ROOT first in PATH for the Android build.'
"$DOTNET_ROOT/dotnet" workload list | python3 -c 'import sys; raise SystemExit(0 if "maui-android" in sys.stdin.read() else 1)' || die 'Install the maui-android workload into the selected .NET 9 SDK.'
docker compose version >/dev/null 2>&1 || die 'Docker Compose v2 is required (docker compose version failed).'
docker info >/dev/null 2>&1 || die 'Docker is unavailable to this user; verify Docker Engine access.'
[[ -f "$COMPOSE_FILE" ]] || die "Compose file not found: $COMPOSE_FILE"
emulator -accel-check >/dev/null 2>&1 || die 'Android emulator acceleration is unavailable; use a KVM-capable Linux host.'

DEVICES="$(adb devices)"
DEVICE_COUNT="$(python3 -c 'import sys; print(sum(1 for line in sys.stdin.read().splitlines()[1:] if len(line.split()) >= 2 and line.split()[1] == "device"))' <<<"$DEVICES")"
[[ "$DEVICE_COUNT" == 1 ]] || die 'Exactly one booted emulator/device must be listed by adb.'
SERIAL="$(python3 -c 'import sys; rows=[line.split() for line in sys.stdin.read().splitlines()[1:] if len(line.split()) >= 2 and line.split()[1] == "device"]; print(rows[0][0] if len(rows) == 1 else "")' <<<"$DEVICES")"
[[ "$SERIAL" == emulator-* ]] || die 'The single booted device must be an emulator (this helper targets x86_64 emulators).'
[[ "$(adb -s "$SERIAL" shell getprop ro.product.cpu.abi | tr -d '\r')" == x86_64 ]] || die 'The booted emulator must use the x86_64 ABI.'
SDK_LEVEL="$(adb -s "$SERIAL" shell getprop ro.build.version.sdk | tr -d '\r')"
[[ "$SDK_LEVEL" =~ ^[0-9]+$ ]] && (( SDK_LEVEL >= 35 )) || die 'The booted emulator must run Android API 35 or newer.'
[[ "$(adb -s "$SERIAL" shell getprop sys.boot_completed | tr -d '\r')" == 1 ]] || die 'Wait for the emulator to finish booting.'
REVERSE_LIST="$(adb -s "$SERIAL" reverse --list)"
if [[ "$REVERSE_LIST" == *tcp:7071* ]]; then die 'Emulator already has a device-side tcp:7071 reverse mapping; remove or handle it before running.'; fi

python3 - <<'PY' || die 'Host port 5288 or 17071 is already in use; stop the conflicting service yourself and retry.'
import socket
for port in (5288, 17071):
    with socket.socket() as sock:
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            sock.bind(("127.0.0.1", port))
        except OSError:
            raise SystemExit(1)
PY

mkdir -p "$LOG_DIR"
API_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export APIKey="$API_KEY"
COMPOSE_STARTED=1
"${COMPOSE[@]}" up -d --build

wait_http() {
  local url=$1 expected=$2 header=${3:-}
  for _ in $(seq 1 60); do
    local status
    if [[ -n "$header" ]]; then
      status="$(curl --noproxy '*' --connect-timeout 2 --max-time 5 -sS -o /dev/null -w '%{http_code}' -H "$header" "$url" 2>/dev/null || true)"
    else
      status="$(curl --noproxy '*' --connect-timeout 2 --max-time 5 -sS -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || true)"
    fi
    [[ "$status" == "$expected" ]] && return 0
    sleep 1
  done
  printf 'Readiness check failed for %s (expected HTTP %s).\n' "$url" "$expected" >&2
  return 1
}
wait_http http://127.0.0.1:5288/api/UrlList 200 "x-api-key: $API_KEY" || die 'API did not become ready; see the saved Compose logs.'
wait_http http://127.0.0.1:17071/.well-known/assetlinks.json 200 || die 'Functions did not become ready at host port 17071; see the saved Compose logs.'

VANITY="mobile$(python3 -c 'import secrets; print(secrets.token_hex(8))')"
CREATE_STATUS="$(curl --noproxy '*' --connect-timeout 2 --max-time 30 -sS -o "$LOG_DIR/create.json" -w '%{http_code}' \
  -H "x-api-key: $API_KEY" -H 'Content-Type: application/json' \
  --data "{\"vanity\":\"$VANITY\",\"linkType\":\"mobile\",\"data\":{\"screen\":\"home\"}}" \
  http://127.0.0.1:5288/api/UrlCreate)" || die 'Mobile link creation request failed.'
[[ "$CREATE_STATUS" == 201 ]] || die "Mobile link creation returned HTTP $CREATE_STATUS."
SHORT_ID="$(python3 - "$LOG_DIR/create.json" <<'PY'
import json, sys, urllib.parse
path = urllib.parse.urlparse(json.load(open(sys.argv[1], encoding="utf-8"))["shortUrl"]).path.rstrip("/").split("/")
print(path[-1] if len(path) > 1 and path[-2] == "m" else "")
PY
)"
[[ -n "$SHORT_ID" ]] || die 'Could not extract the mobile short ID from the API response.'
RESOLVE_STATUS="$(curl --noproxy '*' --connect-timeout 2 --max-time 30 -sS -o "$LOG_DIR/resolve.json" -w '%{http_code}' "http://127.0.0.1:17071/resolve/$SHORT_ID")" || die 'Resolver request failed.'
[[ "$RESOLVE_STATUS" == 200 ]] || die "Resolver returned HTTP $RESOLVE_STATUS."
python3 - "$LOG_DIR/resolve.json" <<'PY' || die 'Resolver response metadata did not contain screen=home.'
import json, sys
if json.load(open(sys.argv[1], encoding="utf-8")).get("screen") != "home":
    raise SystemExit(1)
PY

APP="$ROOT/src/MobileSample/ChronicleMobile.App/ChronicleMobile.App.csproj"
CORE="$ROOT/src/MobileSample/ChronicleMobile.Core/ChronicleMobile.Core.csproj"
SDK_ARGS=("-p:AndroidSdkDirectory=$ANDROID_HOME" "-p:JavaSdkDirectory=$JAVA_HOME")
"$DOTNET_ROOT/dotnet" restore "$APP" -p:TargetFrameworks=net9.0-android \
  -p:Configuration=Debug -p:RuntimeIdentifier=android-x64 -p:SelfContained=true "${SDK_ARGS[@]}"
"$DOTNET_ROOT/dotnet" restore "$CORE"
"$DOTNET_ROOT/dotnet" publish "$APP" -c Debug -f net9.0-android -r android-x64 \
  --self-contained true --no-restore -p:TargetFrameworks=net9.0-android \
  -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormats=apk "${SDK_ARGS[@]}"
APK="$ROOT/src/MobileSample/ChronicleMobile.App/bin/Debug/net9.0-android/android-x64/publish/com.gochronicle.chroniclemobileapp-Signed.apk"
[[ -f "$APK" ]] || die "Standalone APK not found at $APK."
adb -s "$SERIAL" install -r "$APK"
# The app uses device localhost:7071; forward it to the host's Functions port 17071.
REVERSE_ADDED=1
adb -s "$SERIAL" reverse tcp:7071 tcp:17071

pause_for_observation() { printf '\n%s\n' "$1"; read -r -p 'Inspect the emulator, then press Enter to continue (Ctrl-C to stop): '; }
PACKAGE=com.gochronicle.chroniclemobileapp
adb -s "$SERIAL" shell am force-stop "$PACKAGE"
adb -s "$SERIAL" shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$SHORT_ID"
pause_for_observation 'Cold launch: visually check CHRONICLE Link Resolved, the short ID, and SCREEN/home.'

adb -s "$SERIAL" shell am force-stop "$PACKAGE"
adb -s "$SERIAL" shell monkey -p "$PACKAGE" 1
pause_for_observation 'Launcher-first: visually check Awaiting Link before activating the warm deep link.'
adb -s "$SERIAL" shell am start -W -a android.intent.action.VIEW -d "ChronicleMobile://?shortid=$SHORT_ID"
pause_for_observation 'Warm link: visually check the same resolved ID and SCREEN/home.'

adb -s "$SERIAL" shell am start -W -a android.intent.action.VIEW -d 'ChronicleMobile://?shortid=missing-local-id'
pause_for_observation 'Missing ID: visually check Short Link Missing (404).'
adb -s "$SERIAL" shell am start -W -a android.intent.action.VIEW -d 'ChronicleMobile://host?shortid=bad'
pause_for_observation 'Invalid URI: visually check Invalid Deep Link and confirm no browser opened.'
printf '\nVisual checks are complete only if you observed the expected screens without ANRs or browser navigation.\n'
read -r -p 'Press Enter to stop demo containers and clean up this demo (Ctrl-C also cleans up): '
