#!/bin/bash
# Post-create setup for the grimoire-cli devcontainer.
set -euo pipefail

# --- Claude Code session path symlink ---
# Claude Code indexes sessions by project path, and the host path differs from
# the container path (/workspaces/grimoire-cli), so the same repo gets two
# histories. Link the container's key at whichever host key already exists.
# Globbed rather than hardcoded so this works from any checkout location; if the
# host has never opened this project in Claude Code there is nothing to link yet.
CONTAINER_KEY=$(pwd | sed 's|/|-|g')
HOST_KEY=$(find ~/.claude/projects -maxdepth 1 -name '*-grimoire-cli' \
  ! -name "$CONTAINER_KEY" -print -quit 2>/dev/null || true)
if [ -n "${HOST_KEY:-}" ]; then
  ln -sfn "$HOST_KEY" ~/.claude/projects/"$CONTAINER_KEY" 2>/dev/null || true
fi

# Ensure directories Claude Code expects exist
mkdir -p ~/.claude/plugins/cache

# Set peon-ping to use the frieren pack (matching the Mac's config)
python3 -c "
import json, os
cfg_path = os.path.expanduser('~/.claude/hooks/peon-ping/config.json')
with open(cfg_path) as f:
    cfg = json.load(f)
cfg['default_pack'] = 'frieren'
cfg['desktop_notifications'] = False
with open(cfg_path, 'w') as f:
    json.dump(cfg, f, indent=2)
" 2>/dev/null || true

# --- Claude Code statusline ---
# Install the statusline script and register it in settings.json.
install -m 755 .devcontainer/statusline.sh ~/.claude/statusline.sh
SETTINGS=~/.claude/settings.json
[ -f "$SETTINGS" ] || echo '{}' > "$SETTINGS"
tmp=$(mktemp)
jq '. + {statusLine: {type: "command", command: "/home/vscode/.claude/statusline.sh"}}' \
  "$SETTINGS" > "$tmp" && mv "$tmp" "$SETTINGS"

# --- Superpowers setup ---
# Structured development workflow (brainstorming, planning, TDD, debugging, code review).
claude plugin marketplace add obra/superpowers 2>/dev/null || true
claude plugin install superpowers@superpowers-dev 2>/dev/null || true

# --- Ponytail: general code-simplicity discipline (YAGNI, reuse, minimal diff) ---
claude plugin marketplace add DietrichGebert/ponytail 2>/dev/null || true
claude plugin install ponytail@ponytail 2>/dev/null || true

# --- answer-first: output-style skill (lead with the answer, cut preamble) ---
claude plugin marketplace add thomaslazar/answer-first 2>/dev/null || true
claude plugin install answer-first@razal-skills 2>/dev/null || true

# Reference material in temp/ is deliberately NOT fetched here: the workspace is
# a bind mount, so temp/ survives rebuilds and any fetch-on-create is a no-op
# after the first one — while quietly deciding which upstream ref you read.
# Fetch it by hand, at the ref your server runs; see CLAUDE.md.
