#!/usr/bin/env python3
"""Regenerate docs/grimoire-api-coverage.md from the live spec and the upstream source.

Two inputs, both required:

  the local stack's /api/openapi.json   paths, methods, summaries — fetched from
                               the running container, which pins the exact
                               release this CLI targets. No snapshot file is kept
                               anywhere: a file has to be remembered and can be
                               stale, whereas the container cannot disagree with
                               itself. Start it first:
                                 docker compose -f docker/docker-compose.yml up -d --wait
  temp/grimoire/               the upstream source at the deployed release tag,
                               read for the role each route requires

Roles are not in the spec: only 10 of 178 operations mention one in their
description, while the rest carry it as a FastAPI dependency at the route
registration (``dependencies=[Depends(require_not_guest)]``) or on the handler.
This script reads those registrations with ``ast`` so the Perm column is derived
rather than transcribed.

Usage: tools/generate-api-coverage.py [output-path]
"""
import ast
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SERVER = os.environ.get("GRIMOIRE_SERVER", "http://host.docker.internal:9481")
SPEC_URL = f"{SERVER}/api/openapi.json"
SOURCE = REPO / "temp" / "grimoire"
DEFAULT_OUT = REPO / "docs" / "grimoire-api-coverage.md"

# Commands that implement an operation, keyed by "METHOD /path". Update this in
# the same PR as any change to which endpoints the CLI calls.
IMPLEMENTED = {
    "POST /api/auth/login": "`login` ✅",
    "GET /api/auth/me": "`me` ✅",
    "GET /api/about": "🔒 version check inside `login`",
    "GET /api/systems": "`systems list` ✅",
    "GET /api/systems/{system_id}": "`systems get` ✅",
}

ROLE_LABEL = {
    "require_admin": "admin",
    "require_gm_or_admin": "gm or admin",
    "require_not_guest": "not guest",
    "get_current_user": "",
    "get_current_user_optional": "",
}


HTTP_DECORATORS = {"get", "post", "patch", "put", "delete"}


def dependency_roles(source_root: Path) -> dict[tuple[str, str], str]:
    """Map "METHOD /prefix/path" to a role label, read from route registrations.

    Routers register in two styles and both are in use: ``add_api_route`` calls in
    a package's ``__init__.py``, and ``@router.post("/path")`` decorators on the
    handlers themselves (``POST /api/rescan`` is one of the latter).
    """
    roles: dict[tuple[str, str], str] = {}
    for package in sorted(p for p in (source_root / "backend" / "routers").iterdir() if p.is_dir()):
        handler_roles = handler_dependency_roles(package)
        for module in sorted(package.glob("*.py")):
            tree = ast.parse(module.read_text(encoding="utf-8"))
            prefixes = router_prefixes(tree)
            for node in ast.walk(tree):
                if isinstance(node, ast.Call) and getattr(node.func, "attr", "") == "add_api_route":
                    receiver = getattr(node.func.value, "id", "")
                    path = node.args[0].value if node.args and isinstance(node.args[0], ast.Constant) else None
                    handler = getattr(node.args[1], "id", "") if len(node.args) > 1 else ""
                    methods, role = [], None
                    for kw in node.keywords:
                        if kw.arg == "methods":
                            methods = [e.value for e in kw.value.elts if isinstance(e, ast.Constant)]
                        elif kw.arg == "dependencies":
                            role = first_dependency_name(kw.value)
                    if path is None:
                        continue
                    label = role_label(role or handler_roles.get(handler, ""))
                    suffix = prefixes.get(receiver, "") + path
                    for method in methods:
                        roles[(method, suffix)] = label
                elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                    for dec in node.decorator_list:
                        if not isinstance(dec, ast.Call):
                            continue
                        verb = getattr(dec.func, "attr", "")
                        if verb not in HTTP_DECORATORS or not dec.args:
                            continue
                        if not isinstance(dec.args[0], ast.Constant):
                            continue
                        role = None
                        for kw in dec.keywords:
                            if kw.arg == "dependencies":
                                role = first_dependency_name(kw.value)
                        label = role_label(role or handler_roles.get(node.name, ""))
                        receiver = getattr(dec.func.value, "id", "")
                        roles[(verb.upper(), prefixes.get(receiver, "") + dec.args[0].value)] = label
    return roles


def router_prefixes(tree: ast.Module) -> dict[str, str]:
    """Map each ``NAME = APIRouter(prefix=...)`` variable to its prefix.

    A module can declare several routers with different prefixes, so the prefix
    has to be tracked per variable rather than per file — ``library/core.py``
    declares an unprefixed ``router`` alongside a ``/api``-prefixed
    ``public_router``.
    """
    found: dict[str, str] = {}
    for node in ast.walk(tree):
        if not isinstance(node, ast.Assign) or not isinstance(node.value, ast.Call):
            continue
        if getattr(node.value.func, "id", "") != "APIRouter":
            continue
        prefix = ""
        for kw in node.value.keywords:
            if kw.arg == "prefix" and isinstance(kw.value, ast.Constant):
                prefix = kw.value.value
        for target in node.targets:
            if isinstance(target, ast.Name):
                found[target.id] = prefix
    return found


def resolve_roles(raw: dict[tuple[str, str], str], spec_paths: dict[str, dict]) -> dict[str, str]:
    """Attach extracted roles to full spec paths by matching on path suffix.

    A router's full path is composed from an ``APIRouter(prefix=...)`` plus the
    prefix ``include_router`` adds, and a package can declare several routers with
    different prefixes (``library/core.py`` has both ``router`` and a ``/api``
    -prefixed ``public_router``). Rather than reimplement that composition, match
    each extracted route to the spec by suffix and take the longest match, which
    the spec — the server's own description of itself — makes unambiguous.
    """
    resolved: dict[str, str] = {}
    for path, ops in spec_paths.items():
        for method in ops:
            method = method.upper()
            if method not in {"GET", "POST", "PATCH", "PUT", "DELETE"}:
                continue
            best, best_len = None, -1
            for (raw_method, raw_path), role in raw.items():
                if raw_method != method:
                    continue
                if path == raw_path or path.endswith(raw_path if raw_path.startswith("/") else "/" + raw_path):
                    if len(raw_path) > best_len:
                        best, best_len = role, len(raw_path)
            if best is not None:
                resolved[f"{method} {path}"] = best
    return resolved


def role_label(dependency: str) -> str:
    """Human label for a role dependency; '?' when it names something unrecognised."""
    if not dependency:
        return ""
    return ROLE_LABEL.get(dependency, "?")


def first_dependency_name(node: ast.AST) -> str | None:
    """Pull the first Depends(name) out of a dependencies=[...] list."""
    for call in ast.walk(node):
        if isinstance(call, ast.Call) and getattr(call.func, "id", "") == "Depends":
            if call.args:
                return getattr(call.args[0], "id", None)
    return None


def handler_dependency_roles(package: Path) -> dict[str, str]:
    """Map handler function name to the role in its own signature, as a fallback.

    Routes that omit a route-level dependency often declare it on the handler
    instead, e.g. ``_: CurrentUser = Depends(require_gm_or_admin)``.
    """
    found: dict[str, str] = {}
    for module in sorted(package.glob("*.py")):
        tree = ast.parse(module.read_text(encoding="utf-8"))
        for node in tree.body:
            if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            # A handler signature carries several Depends — the role one plus
            # Depends(get_db) and friends. Keep the first that names a role, or
            # get_db wins by being last.
            for default in node.args.defaults:
                dep = first_dependency_name(default)
                if dep in ROLE_LABEL and node.name not in found:
                    found[node.name] = dep
    return found


def main() -> int:
    if not SOURCE.exists():
        sys.exit(f"missing {SOURCE} — clone the upstream source at the deployed tag (see CLAUDE.md)")

    try:
        with urllib.request.urlopen(SPEC_URL, timeout=30) as response:
            spec = json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, TimeoutError) as exc:
        sys.exit(
            f"cannot reach {SPEC_URL} ({exc}) — start the stack first:\n"
            "  docker compose -f docker/docker-compose.yml up -d --wait\n"
            "Override the host with GRIMOIRE_SERVER."
        )
    version = spec["info"]["version"]
    roles = resolve_roles(dependency_roles(SOURCE), spec["paths"])

    by_tag: dict[str, list[tuple[str, str, str, str, str]]] = {}
    total = covered = internal = 0
    for path, ops in sorted(spec["paths"].items()):
        for method, op in ops.items():
            if method.upper() not in {"GET", "POST", "PATCH", "PUT", "DELETE"}:
                continue
            method = method.upper()
            key = f"{method} {path}"
            tag = (op.get("tags") or ["(untagged)"])[0]
            summary = op.get("summary") or ""
            cli = IMPLEMENTED.get(key, "—")
            total += 1
            if "✅" in cli:
                covered += 1
            elif "🔒" in cli:
                internal += 1
            by_tag.setdefault(tag, []).append((method, path, summary, roles.get(key, ""), cli))

    out = [
        "# Grimoire API coverage",
        "",
        f"Map of every Grimoire HTTP API operation and the `grimoire-cli` command "
        f"(if any) that implements it.",
        "",
        f"- **Reference:** spec fetched live from the pinned stack's `/api/openapi.json` "
        f"(v{version}, {len(spec['paths'])} paths, {total} operations) and the upstream "
        f"source at `temp/grimoire/backend/routers/`. Tested range: `{version}` only "
        f"(`GrimoireApiClient.cs`).",
        "- **Perm** column uses Grimoire's roles (`admin` / `gm or admin` / `not guest`); "
        "blank = any authenticated user. `?` = a dependency this script could not resolve.",
        "- ✅ = covered by a CLI command · — = not implemented · 🔒 = internal-only "
        "(no user-facing verb); 🔒 rows never count as covered.",
        "- **Regenerate with `tools/generate-api-coverage.py`; update `IMPLEMENTED` there "
        "in the same PR as any change to which endpoints the CLI calls.**",
        "",
        "## Coverage summary",
        "",
        "| Tag | Covered / Total |",
        "|-----|-----------------|",
    ]
    for tag, rows in sorted(by_tag.items()):
        n = sum(1 for r in rows if "✅" in r[4])
        out.append(f"| {tag} | {n} / {len(rows)} |")
    out += [f"| **Total** | **{covered} / {total}** |", ""]
    if internal:
        out += [f"{internal} operation(s) are internal-only (🔒) and excluded from covered counts.", ""]

    for tag, rows in sorted(by_tag.items()):
        out += [f"## {tag}", "", "| Method | Path | Description | Perm | CLI |",
                "|--------|------|-------------|------|-----|"]
        for method, path, summary, perm, cli in rows:
            # A pipe inside a summary would split the row into extra cells.
            out.append(f"| {method} | `{path}` | {summary.replace('|', r'\|')} | {perm} | {cli} |")
        out.append("")

    target = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUT
    target.write_text("\n".join(out).rstrip() + "\n", encoding="utf-8")
    print(f"wrote {target}: {total} operations, {covered} covered, {internal} internal-only")
    return 0


if __name__ == "__main__":
    sys.exit(main())
