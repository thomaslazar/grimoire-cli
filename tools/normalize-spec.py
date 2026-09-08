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
            # Carry over the wrapper's annotations only. Anything else it grows
            # later (a `default`, say) would be a constraint on the union, not on
            # the array, and pasting it onto the branch would change its meaning.
            for key in ("title", "description"):
                if key in node and key not in collapsed:
                    collapsed[key] = node[key]
            stats.append(collapsed.get("title", "?"))
            return collapse(collapsed, stats)

    return {key: collapse(value, stats) for key, value in node.items()}


def explode_array_queries(spec):
    """Make every array-typed query parameter generate as repeated keys.

    OpenAPI 3.x already defaults a query parameter to ``style: form,
    explode: true`` — repeated keys — which is what FastAPI reads, so it states
    neither. Kiota renders a *required* query parameter with simple expansion
    regardless: the template comes out ``?ids={ids}``, which comma-joins the
    values, and FastAPI then parses ``ids=a,b`` as the single string "a,b". So
    ``GET /api/duplicates/compare`` answers 400 for what should be a two-item
    compare, and the endpoint cannot be called at all.

    Setting ``explode`` alone does not move it, nor does adding ``style: form``
    — both were tried against Kiota 1.34.1 and left the template unchanged.
    Only dropping ``required`` moves the parameter into the exploded group
    (``{&ids*,token*}``). That is a lie about the spec, confined to array query
    parameters, and it costs nothing here: the parameter is still sent on every
    call, because the CLI declares its own ``--ids`` flag Required. The
    alternative is hand-assembling the query string at the call site, which the
    generated-client rule exists to prevent.

    Recheck when the Kiota pin moves: if a required array query parameter starts
    generating as ``{&name*}`` on its own, delete this pass.
    """
    marked = []
    for operations in spec.get("paths", {}).values():
        for operation in operations.values():
            if not isinstance(operation, dict):
                continue
            for parameter in operation.get("parameters", []):
                if (
                    parameter.get("in") == "query"
                    and parameter.get("schema", {}).get("type") == "array"
                    and "explode" not in parameter
                ):
                    parameter["explode"] = True
                    parameter["required"] = False
                    marked.append(parameter.get("name", "?"))
    return marked


def main():
    spec = json.load(sys.stdin)
    stats = []
    normalized = collapse(spec, stats)
    exploded = explode_array_queries(normalized)
    print(f"normalized {len(stats)} anyOf-nullable arrays (kiota#2338)", file=sys.stderr)
    print(f"marked {len(exploded)} array query parameters exploded: {', '.join(exploded) or 'none'}", file=sys.stderr)
    json.dump(normalized, sys.stdout)


if __name__ == "__main__":
    main()
