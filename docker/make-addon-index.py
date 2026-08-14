#!/usr/bin/env python3
"""Generate docker/addon-index/index.json from the checked-in fixture manifest(s).

This is the only thing that writes index.json: install verifies a downloaded
manifest's bytes against the digest recorded here, so hand-editing index.json
— or editing a manifest without re-running this — makes every install fail
with a checksum mismatch. index.json itself is not checked in; only the
manifest(s) it describes are.

Usage: python3 docker/make-addon-index.py
"""
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
INDEX_DIR = HERE / "addon-index"

# One entry per manifest file in docker/addon-index/. id/name/kind/target/
# version mirror the manifest's own fields; sha256 is computed below so it can
# never drift from what's on disk.
ADDONS = [
    {
        "id": "fixture-source",
        "name": "Fixture Source",
        "kind": "scraper",
        "target": "game-system",
        "version": "1.0.0",
        "path": "fixture-source.yml",
        "requires_script": False,
    },
]


def build_index() -> dict:
    addons = []
    for addon in ADDONS:
        manifest_path = INDEX_DIR / addon["path"]
        digest = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        addons.append({**addon, "sha256": digest})
    return {
        "version": 1,
        "generated": datetime.now(timezone.utc).isoformat(),
        "addons": addons,
    }


if __name__ == "__main__":
    index = build_index()
    out_path = INDEX_DIR / "index.json"
    out_path.write_text(json.dumps(index, indent=2) + "\n")
    print(f"wrote {out_path} ({len(index['addons'])} addon(s))", file=sys.stderr)
