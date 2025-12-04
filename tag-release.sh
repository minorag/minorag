#!/usr/bin/env bash
set -euo pipefail

# ---- SAFETY CHECK: Fail if repo is dirty ----
if [[ -n "$(git status --porcelain)" ]]; then
    echo "❌ ERROR: You have uncommitted changes."
    echo "Please commit or stash them before tagging."
    echo
    git status --short
    exit 1
fi

CSProj="src/Minorag.Cli/Minorag.Cli.csproj"

# Extract <Version> value using sed (portable across macOS/Linux)
VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$CSProj")

if [[ -z "$VERSION" ]]; then
    echo "❌ ERROR: Could not find <Version> in $CSProj"
    exit 1
fi

TAG="v$VERSION"

echo "📦 Found version in csproj: $VERSION"
echo "🏷  Tag to create: $TAG"
echo

# Ask to proceed
read -p "Create Git tag $TAG? (y/n) " yn
case $yn in
    [Yy]* ) ;;
    * ) echo "Aborted."; exit ;;
esac

# Ask for message
read -p "Enter release message (press Enter to type interactively): " MSG

if [[ -z "$MSG" ]]; then
    # User pressed Enter without input → ask again interactively
    echo
    read -e -p "Release message: " MSG
fi

# Last fallback
if [[ -z "$MSG" ]]; then
    MSG="Release $TAG"
fi

echo
echo "🏷  Tag message: $MSG"
echo

git tag -a "$TAG" -m "$MSG"

echo "⬆️  Pushing tag $TAG..."
git push origin "$TAG"

echo "✅ Tag $TAG created and pushed!"