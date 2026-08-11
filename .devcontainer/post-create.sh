#!/bin/bash
# Post-create setup for the grimoire-cli devcontainer.
set -euo pipefail

# --- Claude Code session path symlink ---
# Claude Code keys sessions by project path, which differs between host and
# container. Link the container's key at the host's so both share one history.
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

# --- Kiota: generates the API client from Grimoire's OpenAPI spec ---
# The client's request surface is generated, not transcribed (see CLAUDE.md).
# Installed here so a fresh container can regenerate without hunting for the tool.
# Pinned to the version that produced the committed tree
# (src/GrimoireCli/Generated/kiota-lock.json) so a container rebuild can't pick up
# a newer generator and mix generator churn into a version-bump diff.
# tools/generate-api-client.sh checks this at run time; bump both together.
KIOTA_VERSION=$(jq -r '.kiotaVersion' src/GrimoireCli/Generated/kiota-lock.json 2>/dev/null || echo 1.34.1)
dotnet tool install --global Microsoft.OpenApi.Kiota --version "$KIOTA_VERSION" 2>/dev/null || \
  dotnet tool update --global Microsoft.OpenApi.Kiota --version "$KIOTA_VERSION" 2>/dev/null || true

# `dotnet tool install --global` writes to ~/.dotnet/tools, which is not on PATH
# in a non-login shell. Add it once rather than per-invocation.
if ! grep -q '.dotnet/tools' ~/.bashrc 2>/dev/null; then
  echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
fi

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
