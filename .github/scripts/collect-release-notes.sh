#!/usr/bin/env bash
# Collect the pull requests a build shipped and push them to the release notes
# API on codelifter.net. Copy to .github/scripts/collect-release-notes.sh.
#
# The site does the curation: this script only reports what merged. It never
# fails a release it cannot report — a repo with no token, or an API that is
# briefly down, gets a warning annotation, not a red release.
#
# Environment (the workflow sets all of these):
#   APP                  release_apps.key on the site, e.g. dockerizit
#   VERSION              the version that was built, e.g. 0.9.14 or 0.9.14-pre
#   TAG                  its tag (default v$VERSION)
#   COMMIT               the commit that was built (default HEAD)
#   PRERELEASE           true|false — false publishes to the forum
#   PROMOTED_FROM        for a promoted release, the pre-release tag it came from
#   PREVIOUS_TAG         override for the start of the commit range
#   RELEASE_NOTES_TOKEN  this app's push token (repo secret)
#   RELEASE_NOTES_API    default https://codelifter.net/api/releases
#   GH_TOKEN             for `gh api` (the workflow passes GITHUB_TOKEN)
#
# Full spec: Platform-Standards/process/release-notes.md

set -euo pipefail

APP="${APP:?APP is required}"
VERSION="${VERSION:?VERSION is required}"
TAG="${TAG:-v$VERSION}"
COMMIT="${COMMIT:-$(git rev-parse HEAD)}"
PRERELEASE="${PRERELEASE:-true}"
PROMOTED_FROM="${PROMOTED_FROM:-}"
API="${RELEASE_NOTES_API:-https://codelifter.net/api/releases}"
REPO="${GITHUB_REPOSITORY:-}"
SERVER="${GITHUB_SERVER_URL:-https://github.com}"
MAX_PRS=40

if [ -z "${RELEASE_NOTES_TOKEN:-}" ]; then
  echo "::warning::RELEASE_NOTES_TOKEN is not set. Skipping the release notes push."
  echo "::warning::Mint one on the site (npm run release-tokens -- mint $APP) and set it as a repo secret."
  exit 0
fi

# ── The commit range this build shipped ──────────────────────────────────────
# A promoted release rebuilds the pre-release's commit, and every pull request
# up to that point is already recorded on the site, so the range starts at the
# pre-release tag and is normally empty. That is correct, not a bug: the site
# consolidates from what it already holds.
PREV="${PREVIOUS_TAG:-$PROMOTED_FROM}"
if [ -z "$PREV" ]; then
  # The nearest tag reachable from the built commit's first parent: the
  # previous release point on this history, whatever it was called.
  PREV="$(git describe --tags --abbrev=0 "${COMMIT}^" 2>/dev/null || true)"
fi
if [ -z "$PREV" ]; then
  # No tags at all (a repo that publishes packages rather than tagging, like
  # StageZero). Ask the site for the commit it last heard about, so the range
  # is still bounded instead of replaying the whole history every run.
  LAST="$(curl -sS --max-time 30 -H "authorization: Bearer ${RELEASE_NOTES_TOKEN}" \
    "${API}/previous" 2>/dev/null | jq -r '.release.commit // ""' 2>/dev/null || true)"
  if [ -n "$LAST" ] && git cat-file -e "${LAST}^{commit}" 2>/dev/null; then
    PREV="$LAST"
    echo "No tags in this repo. Measuring from the last commit the site recorded."
  fi
fi

if [ -n "$PREV" ]; then
  RANGE="${PREV}..${COMMIT}"
  echo "Collecting pull requests in ${RANGE}"
else
  RANGE="$COMMIT"
  echo "No previous tag found. Collecting the whole history up to ${COMMIT}."
fi

# ── Pull requests, else the commits themselves ───────────────────────────────
# Squash merges put the number in the subject as "(#123)"; merge commits as
# "Merge pull request #123". Either way the number is what we need.
NUMBERS="$(git log --format='%s' "$RANGE" 2>/dev/null | grep -oE '#[0-9]+' | tr -d '#' | sort -un | head -n "$MAX_PRS" || true)"

PRS='[]'
for n in $NUMBERS; do
  if [ -z "$REPO" ]; then break; fi
  pr="$(gh api "repos/${REPO}/pulls/${n}" \
        --jq '{number: .number, title: .title, body: (.body // ""), url: .html_url,
               author: (.user.login // ""), labels: [.labels[].name]}' 2>/dev/null || true)"
  if [ -n "$pr" ]; then
    PRS="$(jq -c --argjson pr "$pr" '. + [$pr]' <<<"$PRS")"
  fi
done

# A repo that pushes straight to main has no pull requests to read, so the
# commit subjects stand in for them and the site curates those instead.
if [ "$(jq 'length' <<<"$PRS")" -eq 0 ]; then
  echo "No pull requests in range. Falling back to commit subjects."
  while IFS= read -r subject; do
    [ -z "$subject" ] && continue
    case "$subject" in
      "chore: release"*|"Merge "*) continue ;;
    esac
    PRS="$(jq -c --arg t "$subject" \
      '. + [{number: null, title: $t, body: "", url: "", author: "", labels: []}]' <<<"$PRS")"
  done < <(git log --no-merges --format='%s' "$RANGE" 2>/dev/null | head -n "$MAX_PRS")
fi

echo "Reporting $(jq 'length' <<<"$PRS") change(s) for ${APP} ${VERSION}."

# ── Push ─────────────────────────────────────────────────────────────────────
RELEASE_URL=""
if [ -n "$REPO" ]; then RELEASE_URL="${SERVER}/${REPO}/releases/tag/${TAG}"; fi

jq -n \
  --arg app "$APP" \
  --arg version "$VERSION" \
  --arg tag "$TAG" \
  --arg commit "$COMMIT" \
  --arg promotedFrom "$PROMOTED_FROM" \
  --arg releaseUrl "$RELEASE_URL" \
  --argjson prerelease "$([ "$PRERELEASE" = "true" ] && echo true || echo false)" \
  --argjson pullRequests "$PRS" \
  '{app: $app, version: $version, tag: $tag, commit: $commit, prerelease: $prerelease,
    promotedFrom: $promotedFrom, releaseUrl: $releaseUrl, pullRequests: $pullRequests}' \
  > payload.json

HTTP="$(curl -sS --max-time 120 --retry 2 --retry-connrefused \
  -o response.json -w '%{http_code}' \
  -X POST "$API" \
  -H "authorization: Bearer ${RELEASE_NOTES_TOKEN}" \
  -H 'content-type: application/json' \
  --data @payload.json || echo 000)"

if [ "$HTTP" != "200" ]; then
  echo "::warning::Release notes push failed with HTTP ${HTTP}."
  head -c 500 response.json || true
  echo
  rm -f payload.json response.json
  exit 1
fi

STATUS="$(jq -r '.release.status // "unknown"' response.json)"
FORUM="$(jq -r '.release.forumUrl // ""' response.json)"
COUNT="$(jq -r '.release.features | length' response.json)"
PUBLISH_ERROR="$(jq -r '.publishError // ""' response.json)"

echo "Recorded ${COUNT} change(s). Status: ${STATUS}."
if [ -n "$PUBLISH_ERROR" ]; then
  echo "::warning::Notes recorded, but the forum post failed: ${PUBLISH_ERROR}"
  echo "::warning::Retry from /api/admin/releases/<id>/publish once the cause is fixed."
fi

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### Release notes"
    echo
    echo "- App: \`${APP}\`  Version: \`${VERSION}\`  Status: \`${STATUS}\`"
    echo "- Changes recorded: ${COUNT}"
    if [ "$STATUS" = "published" ] && [ -n "$FORUM" ]; then
      echo "- Posted to https://codelifter.net${FORUM}"
    elif [ "$STATUS" = "recorded" ]; then
      echo "- Held for the next full release (pre-release notes accumulate)."
    fi
    [ -n "$PUBLISH_ERROR" ] && echo "- Forum post failed: ${PUBLISH_ERROR}"
  } >> "$GITHUB_STEP_SUMMARY"
fi

rm -f payload.json response.json
