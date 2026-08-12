#!/usr/bin/env python3
"""Collapse `anyOf: [<array>, null]` wrappers in an OpenAPI spec, on stdin to stdout.

Works around microsoft/kiota#2338: a schema reached only through an array inside
an `anyOf` generates with none of its own properties, so the model cannot say
which fields it knows. FastAPI emits that wrapper for every
``Optional[list[Model]]``, which is how Grimoire's `publishers`, `urls` and
`character_builder_urls` are declared.

Only the two-branch array-or-null shape is touched. Scalar wrappers
(`anyOf: [string, null]`) are left alone: they generate correctly, and
collapsing them would change 14 of GameSystemUpdate's 17 property types for no
gain. Run by tools/generate-api-client.sh; the count is reported on stderr so a
regeneration that stops needing this is visible.
"""
import json
import sys


def is_null_branch(branch):
    return isinstance(branch, dict) and branch.get("type") == "null"


def collapse(node, stats):
    if isinstance(node, list):
        return [collapse(item, stats) for item in node]
    if not isinstance(node, dict):
        return node

    branches = node.get("anyOf")
    if isinstance(branches, list) and len(branches) == 2:
        arrays = [b for b in branches if isinstance(b, dict) and b.get("type") == "array"]
        if len(arrays) == 1 and any(is_null_branch(b) for b in branches):
            collapsed = dict(arrays[0])
            # Keep the wrapper's own annotations; the branch has no title of its own.
            for key, value in node.items():
                if key != "anyOf" and key not in collapsed:
                    collapsed[key] = value
            stats.append(collapsed.get("title", "?"))
            return collapse(collapsed, stats)

    return {key: collapse(value, stats) for key, value in node.items()}


def main():
    spec = json.load(sys.stdin)
    stats = []
    normalized = collapse(spec, stats)
    print(f"normalized {len(stats)} anyOf-nullable arrays (kiota#2338)", file=sys.stderr)
    json.dump(normalized, sys.stdout)


if __name__ == "__main__":
    main()
