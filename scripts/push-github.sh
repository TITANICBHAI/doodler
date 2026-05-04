#!/usr/bin/env bash
set -e

if [ -z "$GITHUB_PERSONAL_ACCESS_TOKEN" ]; then
  echo "❌ GITHUB_PERSONAL_ACCESS_TOKEN secret is not set."
  exit 1
fi

REPO_URL="https://${GITHUB_PERSONAL_ACCESS_TOKEN}@github.com/TITANICBHAI/doodler.git"

# Ensure git identity is set for this repo
git config user.email "doodleclimb@replit.dev" 2>/dev/null || true
git config user.name  "DoodleClimb Bot"        2>/dev/null || true

echo "📦 Staging & committing changes..."
git add -A
if git diff --cached --quiet; then
  echo "  (nothing new to commit)"
else
  git commit -m "loop: auto-commit $(date '+%Y-%m-%d %H:%M')"
fi

echo "⬆  Pushing to github.com/TITANICBHAI/doodler ..."
git push "$REPO_URL" HEAD:main

echo ""
echo "✅ Done — https://github.com/TITANICBHAI/doodler"
