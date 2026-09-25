#!/usr/bin/env bash
#
# Export project source into a single, self-describing text file.
#
# Improvements over the previous POSIX version:
#   * Uses `git ls-files` (tracked + untracked-but-not-ignored) for an accurate,
#     reproducible file list, with a `find` fallback outside a git checkout.
#   * NUL-safe throughout (handles paths with spaces or newlines).
#   * Records per-file SHA-256, byte size, and line count, plus a header with
#     the git commit, branch, generation time, dotnet version, and totals.
#   * Includes shell (.sh) and Markdown (.md) files, which the old exporter
#     skipped, so docs and scripts are captured too.
#   * Emits a SHA-256 of the finished dump so its integrity can be verified.
#
# Usage: ./export.sh [PROJECT_PATH] [OUTPUT_FILE]
#   PROJECT_PATH  defaults to the script's own directory
#   OUTPUT_FILE   defaults to docs/llm/dump.txt (relative to PROJECT_PATH)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

PROJECT_PATH="${1:-$SCRIPT_DIR}"
OUTPUT_FILE="${2:-docs/llm/dump.txt}"

# Extensions to include.
INCLUDE_EXTENSIONS="cs json xml csproj sln slnx props config cshtml razor js css scss html yml yaml sql sh md"

# Directory names to exclude anywhere in the tree.
EXCLUDE_DIRS="bin obj .vs .git node_modules packages .vscode .idea docs"

# Resolve to an absolute path and work from there so all paths are relative.
PROJECT_PATH="$(cd "$PROJECT_PATH" && pwd)"
cd "$PROJECT_PATH"

OUTPUT_PATH="$PROJECT_PATH/$OUTPUT_FILE"
OUTPUT_DIR="$(dirname "$OUTPUT_PATH")"
mkdir -p "$OUTPUT_DIR"

# Path of the output file relative to the project root (for self-exclusion).
OUTPUT_REL="${OUTPUT_PATH#"$PROJECT_PATH"/}"

# Colours only when writing to a terminal.
if [ -t 1 ]; then
    GREEN='\033[0;32m'; YELLOW='\033[0;33m'; CYAN='\033[0;36m'; NC='\033[0m'
else
    GREEN=''; YELLOW=''; CYAN=''; NC=''
fi
log() { printf "%b%s%b\n" "$1" "$2" "$NC"; }

# --- portable helpers --------------------------------------------------------

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{print $1}'
    else
        printf 'unavailable'
    fi
}

mod_time_of() {
    if stat --version >/dev/null 2>&1; then
        stat -c '%y' "$1" 2>/dev/null | cut -d'.' -f1
    else
        stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S' "$1" 2>/dev/null
    fi
}

has_included_extension() {
    local ext="${1##*.}"
    [ "$ext" != "$1" ] || return 1
    local inc
    for inc in $INCLUDE_EXTENSIONS; do
        [ "$ext" = "$inc" ] && return 0
    done
    return 1
}

is_in_excluded_dir() {
    local path="$1" dir
    for dir in $EXCLUDE_DIRS; do
        case "$path" in
            "$dir"/*|*/"$dir"/*) return 0 ;;
        esac
    done
    return 1
}

in_git_repo() { git rev-parse --is-inside-work-tree >/dev/null 2>&1; }

# --- gather the file list (NUL-delimited) ------------------------------------

log "$GREEN" "Starting project export..."
log "$YELLOW" "Project Path: $PROJECT_PATH"
log "$YELLOW" "Output File:  $OUTPUT_PATH"

RAW_LIST="$(mktemp)"
FILE_LIST="$(mktemp)"
trap 'rm -f "$RAW_LIST" "$FILE_LIST"' EXIT

if in_git_repo; then
    log "$CYAN" "Listing files via git..."
    git ls-files -z --cached --others --exclude-standard > "$RAW_LIST"
else
    log "$YELLOW" "Not a git repo; falling back to find..."
    find . -type f -print0 > "$RAW_LIST"
fi

# Filter: drop excluded dirs, the output file itself, and non-included types.
while IFS= read -r -d '' file; do
    rel="${file#./}"
    [ "$rel" = "$OUTPUT_REL" ] && continue
    is_in_excluded_dir "$rel" && continue
    has_included_extension "$rel" || continue
    printf '%s\0' "$rel"
done < "$RAW_LIST" | LC_ALL=C sort -z -u > "$FILE_LIST"

FILE_COUNT=0
while IFS= read -r -d '' _; do FILE_COUNT=$((FILE_COUNT + 1)); done < "$FILE_LIST"
log "$GREEN" "Found $FILE_COUNT files to export"

# --- header ------------------------------------------------------------------

GIT_COMMIT="n/a"; GIT_BRANCH="n/a"
if in_git_repo; then
    # --verify --quiet prints nothing (rather than echoing "HEAD") when there
    # is no commit yet, so an unborn branch does not corrupt the header.
    commit="$(git rev-parse --verify --quiet HEAD 2>/dev/null || true)"
    [ -n "$commit" ] && GIT_COMMIT="$commit"
    branch="$(git branch --show-current 2>/dev/null || true)"
    [ -n "$branch" ] && GIT_BRANCH="$branch"
fi
DOTNET_VERSION="$(dotnet --version 2>/dev/null || echo 'not installed')"

{
    echo "==============================================================================="
    echo "PROJECT EXPORT"
    echo "Generated (UTC): $(date -u '+%Y-%m-%d %H:%M:%S')"
    echo "Project Path:    $PROJECT_PATH"
    echo "Git Commit:      $GIT_COMMIT"
    echo "Git Branch:      $GIT_BRANCH"
    echo ".NET SDK:        $DOTNET_VERSION"
    echo "Files Exported:  $FILE_COUNT"
    echo "==============================================================================="
    echo
    echo "DIRECTORY STRUCTURE:"
    echo "==================="
    echo
} > "$OUTPUT_PATH"

if command -v tree >/dev/null 2>&1; then
    TREE_IGNORE="$(echo "$EXCLUDE_DIRS" | tr ' ' '|')"
    tree -a -I "$TREE_IGNORE" --noreport >> "$OUTPUT_PATH" 2>/dev/null || true
else
    while IFS= read -r -d '' rel; do
        depth="$(printf '%s' "$rel" | tr -cd '/' | wc -c | tr -d ' ')"
        indent=""; i=0
        while [ "$i" -lt "$depth" ]; do indent="$indent    "; i=$((i + 1)); done
        printf '%s+-- %s\n' "$indent" "$(basename "$rel")" >> "$OUTPUT_PATH"
    done < "$FILE_LIST"
fi

printf '\n\n' >> "$OUTPUT_PATH"

# --- file contents -----------------------------------------------------------

{
    echo "FILE CONTENTS:"
    echo "=============="
    echo
} >> "$OUTPUT_PATH"

TOTAL_BYTES=0
CURRENT=0
while IFS= read -r -d '' rel; do
    CURRENT=$((CURRENT + 1))
    full="$PROJECT_PATH/$rel"
    [ -f "$full" ] || continue

    size="$(wc -c < "$full" | tr -d ' ')"
    lines="$(wc -l < "$full" | tr -d ' ')"
    size_kb="$(awk "BEGIN {printf \"%.2f\", $size / 1024}")"
    hash="$(sha256_of "$full")"
    modified="$(mod_time_of "$full")"
    TOTAL_BYTES=$((TOTAL_BYTES + size))

    log "$CYAN" "Processing ($CURRENT/$FILE_COUNT): $rel"

    {
        echo "================================================================================"
        echo "FILE:     $rel"
        echo "SIZE:     ${size_kb} KB (${size} bytes)"
        echo "LINES:    $lines"
        echo "SHA256:   $hash"
        echo "MODIFIED: $modified"
        echo "================================================================================"
        echo
    } >> "$OUTPUT_PATH"

    if [ -s "$full" ]; then
        cat "$full" >> "$OUTPUT_PATH" 2>/dev/null || echo "[ERROR READING FILE]" >> "$OUTPUT_PATH"
    else
        echo "[EMPTY FILE]" >> "$OUTPUT_PATH"
    fi

    printf '\n\n' >> "$OUTPUT_PATH"
done < "$FILE_LIST"

TOTAL_MB="$(awk "BEGIN {printf \"%.2f\", $TOTAL_BYTES / 1048576}")"

# --- footer ------------------------------------------------------------------

{
    echo "==============================================================================="
    echo "EXPORT COMPLETED (UTC): $(date -u '+%Y-%m-%d %H:%M:%S')"
    echo "Total Files Exported:   $FILE_COUNT"
    echo "Total Source Size:      ${TOTAL_MB} MB (${TOTAL_BYTES} bytes)"
    echo "Output File:            $OUTPUT_PATH"
    echo "==============================================================================="
} >> "$OUTPUT_PATH"

# Self-hash: covers everything written so far (i.e. the whole file except the
# single line we are about to append). Verify with:
#   head -n -1 dump.txt | sha256sum
DUMP_HASH="$(sha256_of "$OUTPUT_PATH")"
echo "DUMP SHA256 (of all lines above this one): $DUMP_HASH" >> "$OUTPUT_PATH"

log "$GREEN" ""
log "$GREEN" "Export completed successfully!"
log "$YELLOW" "Output file: $OUTPUT_PATH"
log "$CYAN" "Total source size: ${TOTAL_MB} MB across ${FILE_COUNT} files"
