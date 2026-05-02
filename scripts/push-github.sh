#!/usr/bin/env bash
set -e

if [ -z "$GITHUB_PERSONAL_ACCESS_TOKEN" ]; then
  echo "❌ GITHUB_PERSONAL_ACCESS_TOKEN secret is not set."
  exit 1
fi

REPO_URL="https://TITANICBHAI:${GITHUB_PERSONAL_ACCESS_TOKEN}@github.com/TITANICBHAI/doodler.git"

echo "⬆  Pushing to github.com/TITANICBHAI/doodler ..."
git push "$REPO_URL" main

echo ""
echo "✅ Done — https://github.com/TITANICBHAI/doodler"
