#!/usr/bin/env bash
# Everything that must be true before a push.
#
# This exists because a broken build reached main. The command that pushed it chained on a grep
# of the compiler output, so the chain asked "did grep find a line?" and not "did the build
# succeed?" — grep found the errors, reported success, and the push went ahead.
#
# Exit codes, not output matching. Run it, read the last line.
set -euo pipefail

cd "$(dirname "$0")"

echo "── build ──────────────────────────────"
dotnet build -v q --nologo

# A database for the integration tests, started once and reused.
#
# The suite will download and run its own MongoDB if told nothing, which is right on a clean
# machine and unusable on a slow connection: the Windows build of 8.3 is an 881 MB archive, and a
# link that drops long transfers never finishes it. Tests then fail in their constructors rather
# than at an assertion, so a dozen unrelated classes go red together.
#
# So if a mongod is already listening, use it. Nothing is downloaded and nothing is started.
if [ -z "${RADAR_MONGO_URL:-}" ]; then
  MONGOD="$LOCALAPPDATA/ephemeral-mongo/bin/mongod/mongod-base-8/8.3.13/mongod.exe"

  if [ -x "$MONGOD" ] || [ -f "$MONGOD" ]; then
    if ! (exec 3<>/dev/tcp/127.0.0.1/27018) 2>/dev/null; then
      echo "── starting mongod on 27018 ───────────"
      mkdir -p "$TEMP/radar-mongo-data"
      "$MONGOD" --dbpath "$TEMP/radar-mongo-data" --port 27018 --bind_ip 127.0.0.1 --quiet &
      sleep 5
    fi
    export RADAR_MONGO_URL="mongodb://127.0.0.1:27018"
  fi
fi

echo "── tests ──────────────────────────────"
dotnet test tests/RadarV2.Tests --nologo -v q

if [ -d radar-v2-frontend ]; then
  echo "── frontend ───────────────────────────"
  (cd radar-v2-frontend && npx tsc -b && npm test --silent && npx vite build >/dev/null)
fi

echo
echo "OK — safe to push."
