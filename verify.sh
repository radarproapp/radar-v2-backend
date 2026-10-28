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

echo "── tests ──────────────────────────────"
dotnet test tests/RadarV2.Tests --nologo -v q

if [ -d radar-v2-frontend ]; then
  echo "── frontend ───────────────────────────"
  (cd radar-v2-frontend && npx tsc -b && npm test --silent && npx vite build >/dev/null)
fi

echo
echo "OK — safe to push."
