#!/usr/bin/env bash
# Builds the static GitHub Pages demo: the real UI page with a snapshot of TransitStop.Demo's
# message list (so the samples always match the current generator) and demo.js, which fakes the
# API, inlined. The result is a single index.html that also works when opened from disk.
#
#   bash pages/build.sh [output directory, default ./_site]
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/_site}"
port=5299
build="$(mktemp -d)"

dotnet build "$root/TransitStop.Demo/TransitStop.Demo.csproj" -c Release -o "$build" --nologo -v quiet

# TransitStop is only enabled in Development
ASPNETCORE_ENVIRONMENT=Development dotnet "$build/TransitStop.Demo.dll" --urls "http://localhost:$port" > "$build/demo.log" 2>&1 &
pid=$!
trap 'kill $pid 2>/dev/null || true; rm -rf "$build"' EXIT

for _ in $(seq 60); do
	if curl -fsS "http://localhost:$port/transit-stop/api/messages" -o "$build/messages.json" 2>/dev/null; then
		break
	fi
	if ! kill -0 $pid 2>/dev/null; then
		cat "$build/demo.log" >&2
		exit 1
	fi
	sleep 1
done
[ -s "$build/messages.json" ] || { echo "TransitStop.Demo did not answer on port $port" >&2; exit 1; }

# the snapshot and demo.js go right before the UI's own script, so the fetch stand-in is in place
# first. "</" is escaped so no string in the JSON can end the script element
{
	printf '<script>window.transitStopDemo = '
	sed 's#</#<\\/#g' "$build/messages.json"
	printf ';</script>\n<script>\n'
	cat "$root/pages/demo.js"
	printf '</script>\n'
} > "$build/inject.html"

mkdir -p "$out"
sed 's/{{TITLE}}/Demo/g' "$root/TransitStop/ui/index.html" \
	| awk -v inject="$build/inject.html" '$0 == "<script>" && !done { while ((getline line < inject) > 0) print line; done = 1 } { print }' \
	> "$out/index.html"
grep -q 'window.transitStopDemo = ' "$out/index.html" || { echo "Could not add the demo script to the page" >&2; exit 1; }

echo "Static demo written to $out/index.html"
