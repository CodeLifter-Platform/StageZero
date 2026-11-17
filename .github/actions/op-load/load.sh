#!/usr/bin/env bash
# VENDORED COPY of Platform-Standards actions/op-load/load.sh (branch claude/vault-naming-breakdown-9a77e7 @ 99eb88e). Do not edit here.
# The body of actions/op-load (see action.yml). Tested by tools/op-load.test.mjs.
set -euo pipefail

if [ -z "${OP_SERVICE_ACCOUNT_TOKEN:-}" ]; then
  echo "::warning::No 1Password token (a fork PR has none); nothing read from $VAULT."
  exit 0
fi

OP="${OP_BIN:-op}"
if ! command -v "$OP" >/dev/null; then
  case "${RUNNER_OS:-}-${RUNNER_ARCH:-}" in
    Linux-X64)   pkg=op_linux_amd64;   sum=$OP_SHA256_LINUX_AMD64;   exe=op ;;
    Windows-X64) pkg=op_windows_amd64; sum=$OP_SHA256_WINDOWS_AMD64; exe=op.exe ;;
    *) echo "::error::No 1Password CLI on this ${RUNNER_OS:-?}/${RUNNER_ARCH:-?} runner and no pinned download for it."; exit 1 ;;
  esac
  tmp="$RUNNER_TEMP"
  # Git Bash on Windows: D:\a\_temp -> /d/a/_temp, so curl and sha256sum take it.
  if [ "$RUNNER_OS" = Windows ]; then tmp=$(cygpath -u "$RUNNER_TEMP"); fi
  dir="$tmp/op-$OP_VERSION"
  zip="$tmp/$pkg.zip"
  if [ ! -x "$dir/$exe" ]; then
    curl -fsSL -o "$zip" "https://cache.agilebits.com/dist/1P/op2/pkg/v$OP_VERSION/${pkg}_v$OP_VERSION.zip"
    echo "$sum  $zip" | sha256sum -c --quiet -
    if [ "$RUNNER_OS" = Windows ]; then
      pwsh -NoProfile -Command "Expand-Archive -Force -Path '$(cygpath -w "$zip")' -DestinationPath '$(cygpath -w "$dir")'"
    else
      unzip -q -o "$zip" -d "$dir"
    fi
  fi
  OP="$dir/$exe"
fi
"$OP" --version >/dev/null

missing=()
for entry in $(for f in $FIELDS; do echo "mask:$f"; done; for f in $PLAIN; do echo "plain:$f"; done); do
  kind=${entry%%:*}; name=${entry#*:}
  if ! value=$("$OP" read "op://$VAULT/$ITEM/$name") || [ -z "$value" ]; then
    missing+=("$name")
    continue
  fi
  if [ "$kind" = mask ]; then
    # Every non-empty line: ::add-mask:: masks only the line it is given.
    while IFS= read -r line; do
      line=${line%$'\r'}
      [ -n "$line" ] && echo "::add-mask::$line"
    done <<< "$value"
  fi
  delim="EOF_$(openssl rand -hex 12)"
  printf '%s<<%s\n%s\n%s\n' "$name" "$delim" "$value" "$delim" >> "$GITHUB_ENV"
  if [ "$kind" = plain ]; then echo "loaded $name from $VAULT/$ITEM (unmasked)"; else echo "loaded $name from $VAULT/$ITEM"; fi
done

if [ ${#missing[@]} -gt 0 ]; then
  msg="Could not read ${missing[*]} from op://$VAULT/$ITEM. Check the field exists and has a value, and that the token's service account can read $VAULT."
  if [ "$OPTIONAL" = true ]; then echo "::warning::$msg"; else echo "::error::$msg"; exit 1; fi
fi
