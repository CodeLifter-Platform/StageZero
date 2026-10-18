#!/usr/bin/env bash
# next-version.sh for a second product in the same repo, in its own tag namespace.
#
# StageZero tags the app `vX.Y.Z[-pre]` (next-version.sh, verbatim from the platform
# template) and the Lifted.BlazorAuth.Basic library `basicauth-vX.Y.Z[-pre]`. The two must
# never read each other's tags, or a merge to one would bump the other's version. This is
# the same logic with the prefix as a parameter:
#
#   TAG_PREFIX=basicauth-v next-prefixed-version.sh prerelease   # patch + 1, -pre
#   TAG_PREFIX=basicauth-v next-prefixed-version.sh minor        # minor + 1, patch -> 0
#   TAG_PREFIX=basicauth-v next-prefixed-version.sh major        # major + 1, minor/patch -> 0
#   TAG_PREFIX=basicauth-v next-prefixed-version.sh latest-pre   # the newest -pre tag, or nothing
#
# VERSION_FLOOR (optional) stands in for the highest version while the namespace has no
# tags yet — a package that already shipped versions under the retired run-number scheme
# must continue above them, or its first tag-derived version sorts below what consumers
# already have.
#
# Writes version / version_tag / is_prerelease / base_tag to $GITHUB_OUTPUT when set, and
# echoes the version (or, for latest-pre, the tag) either way so it can be run by hand.

set -euo pipefail
KIND="${1:?usage: next-prefixed-version.sh prerelease|minor|major|latest-pre}"
PREFIX="${TAG_PREFIX:?TAG_PREFIX is required, e.g. basicauth-v}"

# Versions in this namespace only. sort -V orders 0.9.9 < 0.9.14 correctly, where a lexical
# sort would not. `--list` takes a glob anchored at the start, so `v*` (the app) never
# matches `basicauth-v*`, and this never matches the app's tags.
versions() {
  git tag --list "${PREFIX}*" \
    | sed -E "s/^${PREFIX//./\\.}//" \
    | grep -E "^[0-9]+\.[0-9]+\.[0-9]+${1:-(-pre)?}$" \
    | sed -E 's/-pre$//' \
    | sort -V || true
}

if [ "$KIND" = "latest-pre" ]; then
  pre="$(versions '-pre' | tail -1)"
  if [ -n "$pre" ]; then echo "${PREFIX}${pre}-pre"; fi
  exit 0
fi

highest="$(versions | tail -1)"
highest="${highest:-${VERSION_FLOOR:-0.0.0}}"
base_tag=""
if git rev-parse -q --verify "refs/tags/${PREFIX}${highest}" >/dev/null; then
  base_tag="${PREFIX}${highest}"
elif git rev-parse -q --verify "refs/tags/${PREFIX}${highest}-pre" >/dev/null; then
  base_tag="${PREFIX}${highest}-pre"
fi

IFS=. read -r MAJOR MINOR PATCH <<<"$highest"

case "$KIND" in
  prerelease) PATCH=$((PATCH + 1)); SUFFIX="-pre"; PRE=true ;;
  minor)      MINOR=$((MINOR + 1)); PATCH=0; SUFFIX=""; PRE=false ;;
  major)      MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0; SUFFIX=""; PRE=false ;;
  *) echo "!! unknown bump kind: $KIND" >&2; exit 1 ;;
esac

VERSION="${MAJOR}.${MINOR}.${PATCH}${SUFFIX}"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "version=$VERSION"
    echo "version_tag=${PREFIX}${VERSION}"
    echo "is_prerelease=$PRE"
    echo "base_tag=${base_tag}"
  } >> "$GITHUB_OUTPUT"
fi
echo "$VERSION"
