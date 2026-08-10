FROM mcr.microsoft.com/devcontainers/dotnet:10.0

# Native AOT compilation dependencies + Python (needed by peon-ping installer)
# + jq (post-create edits ~/.claude/settings.json; also handy for poking at
#   Grimoire responses, which are raw JSON pass-through).
# python3-fitz is the MuPDF binding — the same library Grimoire reads PDFs with,
# used by docker/seed.sh to generate fixture PDFs and cover images, so anything it
# writes is by definition parseable by the indexer.
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang \
    zlib1g-dev \
    python3 \
    python3-pip \
    python3-fitz \
    jq \
    && rm -rf /var/lib/apt/lists/*

# Claude Code CLI (native installer) + peon-ping both install to ~/.local/bin
ENV PATH="/home/vscode/.local/bin:${PATH}"

USER vscode
RUN curl -fsSL https://claude.ai/install.sh | bash
USER root

# peon-ping: install as vscode user so hooks land in /home/vscode/.claude/.
# REMOTE_CONTAINERS tricks the installer into devcontainer mode so it
# skips the audio-player check (audio goes through the relay on the host).
USER vscode
RUN export REMOTE_CONTAINERS=true \
    && curl -fsSL https://peonping.com/install | bash
USER root
