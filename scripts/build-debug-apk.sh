#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────
#  DoodleClimb — Android debug bundle export
#  Exports the JS bundle + assets for Android (no native build).
#  For a full APK you need EAS Build (cloud) or a local Android SDK.
# ─────────────────────────────────────────────────────────────────
set -e

OUTDIR="dist-android"
echo "🤖  Exporting Android JS bundle → $OUTDIR/ ..."
rm -rf "$OUTDIR"

npx expo export \
  --platform android \
  --output-dir "$OUTDIR" \
  --no-minify

echo ""
echo "✅  Bundle written to $OUTDIR/"
echo ""
echo "──────────────────────────────────────────────────"
echo " To build a signed APK you have two options:"
echo ""
echo " Option A — EAS Cloud Build (recommended):"
echo "   npx eas build --platform android --profile preview"
echo "   (requires an Expo account and eas.json)"
echo ""
echo " Option B — Replit Expo Launch:"
echo "   Click the Publish button in the Replit toolbar."
echo "   iOS is submitted automatically; Android coming soon."
echo "──────────────────────────────────────────────────"
