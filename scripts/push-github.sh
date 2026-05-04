#!/usr/bin/env bash
set -e

if [ -z "$GITHUB_PERSONAL_ACCESS_TOKEN" ]; then
  echo "❌ GITHUB_PERSONAL_ACCESS_TOKEN secret is not set."
  exit 1
fi

# Use token-only auth (no username prefix) — required for fine-grained PATs
REPO_URL="https://${GITHUB_PERSONAL_ACCESS_TOKEN}@github.com/TITANICBHAI/doodler.git"

echo "⬆  Pushing to github.com/TITANICBHAI/doodler ..."
git push "$REPO_URL" HEAD:main

echo ""
echo "✅ Done — https://github.com/TITANICBHAI/doodler"
