"""Generate request protocol Python/C# models; no third-party dependencies.

Only the schema subset used by these requests is supported. Unknown keywords fail
instead of silently generating a different contract.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYTHON = ROOT / "src/unity_mcp/generated/requests.py"
CSHARP = ROOT / "unity-package/Editor/Protocol/Generated/RequestProtocol.cs"


def generate():
    definitions = {}
    for path in sorted((ROOT / "protocol").glob("*.schema.json")):
        for name, definition in json.loads(path.read_text(encoding="utf-8"))["$defs"].items():
            if name in definitions:
                raise ValueError(f"Duplicate protocol definition: {name}")
            definitions[name] = definition

    def local_refs(value):
        if isinstance(value, dict):
            if "$ref" in value:
                value["$ref"] = "#/$defs/" + value["$ref"].split("#/$defs/")[-1]
            for item in value.values():
                local_refs(item)
        elif isinstance(value, list):
            for item in value:
                local_refs(item)
    local_refs(definitions)
    # Normalize the supported JSON Schema nullable-reference form for both emitters.
    for definition in definitions.values():
        if "oneOf" in definition:
            continue
        for field in definition["properties"].values():
            if "anyOf" in field:
                variants = field.pop("anyOf")
                if len(variants) != 2 or variants[1] != {"type": "null"} or set(variants[0]) != {"$ref"}:
                    raise ValueError("Only nullable references are supported by this pilot")
                field.update(variants[0])
                field["x-nullable"] = True
    allowed = {"type", "enum", "$ref", "items", "default", "minimum", "maximum",
               "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength",
               "minItems", "maxItems", "pattern", "description", "x-python-alias",
               "x-nullable", "x-unity-presence", "x-csharp-default"}

    def validate_field(field):
        if set(field) - allowed:
            raise ValueError(f"Unsupported schema keywords: {set(field) - allowed}")
        if "$ref" in field and field["$ref"].removeprefix("#/$defs/") not in definitions:
            raise ValueError(f"Unknown reference: {field['$ref']}")
        if "items" in field:
            validate_field(field["items"])

    aliases = {}
    endpoints = set()
    for definition in definitions.values():
        if "oneOf" in definition:
            if set(definition) != {'oneOf', 'x-discriminator'}:
                raise ValueError('Unsupported union schema')
            actions = set()
            for ref in definition['oneOf']:
                if set(ref) != {'$ref'} or ref['$ref'].split('/')[-1] not in definitions:
                    raise ValueError('Unknown union reference')
                case = definitions[ref['$ref'].split('/')[-1]]
                values = case['properties'][definition['x-discriminator']].get('enum', [])
                if len(values) != 1 or values[0] in actions:
                    raise ValueError('Union cases require distinct, single-value discriminators')
                actions.add(values[0])
            continue
        if set(definition) - {"type", "additionalProperties", "x-python-name", "x-request", "x-endpoint", "properties", "required"}:
            raise ValueError("Unsupported object schema")
        if definition["type"] != "object" or definition["additionalProperties"] is not False:
            raise ValueError("Expected a closed object schema")
        if definition.get('x-request'):
            endpoint = definition.get('x-endpoint')
            if not endpoint or endpoint in endpoints:
                raise ValueError('Request endpoints must be present and unique')
            endpoints.add(endpoint)
        for field in definition["properties"].values():
            validate_field(field)
            if "x-python-alias" in field:
                aliases[field["x-python-alias"]] = field["enum"]

    def py_type(field):
        if "x-python-alias" in field:
            result = field["x-python-alias"]
        elif "$ref" in field:
            name = field["$ref"].split("/")[-1]
            result = definitions[name].get("x-python-name", name)
        elif "enum" in field:
            result = "Literal[" + ", ".join(map(repr, field["enum"])) + "]"
        elif field["type"] == "array":
            item = field['items']
            item_type = py_type(item)
            constraints = ', '.join(f'{target}={item[source]!r}' for source, target in
                [('minimum', 'ge'), ('maximum', 'le'), ('exclusiveMinimum', 'gt'), ('exclusiveMaximum', 'lt')]
                if source in item)
            if constraints:
                item_type = f'Annotated[{item_type}, Field({constraints})]'
            result = f"list[{item_type}]"
        else:
            result = {"string": "str", "number": "float", "integer": "int", "boolean": "bool"}[field["type"]]
        return result + " | None" if field.get("x-nullable") else result

    py = ['# Generated by scripts/generate_protocol.py. Do not edit.',
          'from __future__ import annotations', 'from typing import Annotated, Literal',
          'from pydantic import BaseModel, ConfigDict, Field', '']
    for name, values in aliases.items():
        py += [name + " = Literal[" + ", ".join(map(repr, values)) + "]", '']
    for name, definition in definitions.items():
        if "oneOf" in definition:
            continue
        py += [f"class {definition.get('x-python-name', name)}(BaseModel):",
               '    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)']
        for key, field in definition["properties"].items():
            options = []
            if "default" in field:
                default = field['default']
                options.append(f"default_factory=lambda: {default!r}" if isinstance(default, list) else f"default={default!r}")
            mapping = {"minimum": "ge", "maximum": "le", "exclusiveMinimum": "gt", "exclusiveMaximum": "lt",
                       "minLength": "min_length", "maxLength": "max_length", "minItems": "min_length",
                       "maxItems": "max_length", "pattern": "pattern", "description": "description"}
            options += [f"{target}={field[source]!r}" for source, target in mapping.items() if source in field]
            suffix = " = Field(" + ", ".join(options) + ")" if options else ""
            py.append(f"    {key}: {py_type(field)}{suffix}")
        py += ['', '']
    for name, definition in definitions.items():
        if "oneOf" in definition:
            cases = ' | '.join(ref['$ref'].split('/')[-1] for ref in definition['oneOf'])
            py += [f"{name} = Annotated[{cases}, Field(discriminator={definition['x-discriminator']!r})]", '']
    for name, definition in definitions.items():
        if "oneOf" in definition:
            continue
        py.append(f"{definition.get('x-python-name', name)}.model_rebuild()")
    py += ['', 'REQUEST_MODELS = {']
    for name, definition in definitions.items():
        if definition.get('x-request'):
            py.append(f"    {definition['x-endpoint']!r}: {name},")
    py += ['}', '']
    presence = [key for key, f in definitions['UiPrefabNode']['properties'].items() if f.get('x-unity-presence')]
    py += ['', '', 'def prepare_ui_node(node: dict) -> dict:',
           '    """Encode explicit nullable-offset presence for Unity JsonUtility."""',
           '    result = dict(node)', f'    for key in {tuple(presence)!r}:',
           '        present = node.get(key) is not None', '        result["has_" + key] = present',
           '        if not present:', '            result.pop(key, None)',
           '    result["children"] = [prepare_ui_node(child) for child in node.get("children", [])]',
           '    return result', '']

    def cs_type(field):
        if "$ref" in field:
            return field["$ref"].split("/")[-1]
        if field["type"] == "array":
            return cs_type(field["items"]) + "[]"
        return {"string": "string", "number": "float", "integer": "int", "boolean": "bool"}[field["type"]]

    def literal(value, field):
        if isinstance(value, list):
            return f"new {cs_type(field['items'])}[] {{ " + ', '.join(literal(v, field['items']) for v in value) + ' }'
        if field.get("type") == "number":
            return str(value) + "f"
        return json.dumps(value, ensure_ascii=True)

    cs = ['// Generated by scripts/generate_protocol.py. Do not edit.', 'using System;',
          'using System.Text.RegularExpressions;', '', 'namespace UnityMcp.Editor', '{']
    for name, definition in definitions.items():
        if 'oneOf' in definition:
            # JsonUtility cannot deserialize discriminated unions. Merge the case
            # fields into one wire class; validate by the action-specific schema.
            fields = {}
            for ref in definition['oneOf']:
                for key, field in definitions[ref['$ref'].split('/')[-1]]['properties'].items():
                    fields.setdefault(key, field)
            definition = {'properties': fields}
        interface = " : IEditorRequest" if definition.get("x-request") else ""
        visibility = "internal" if interface else "public"
        cs += [f'    [Serializable] {visibility} sealed class {name}{interface}', '    {']
        for key, field in definition["properties"].items():
            kind = "double" if definition.get("x-request") and key == "deadline" else cs_type(field)
            default = field.get("x-csharp-default")
            if default is None and "default" in field and field["default"] is not None:
                default = literal(field["default"], field)
            cs.append(f'        public {kind} {key}' + (f' = {default}' if default is not None else '') + ';')
            if field.get("x-unity-presence"):
                cs.append(f'        public bool has_{key};')
        if interface:
            cs += ['        string IEditorRequest.Id => id;', '        double IEditorRequest.Deadline => deadline;']
        cs += ['    }', '']
    cs += ['    internal static class RequestProtocolValidation', '    {']
    for name, definition in definitions.items():
        if 'oneOf' in definition:
            discriminator = definition['x-discriminator']
            cs += [f'        public static void Validate({name} value, int depth = 0)', '        {',
                   f'            if (value == null) throw new Exception("Missing {name}");',
                   f'            switch (value.{discriminator})', '            {']
            for ref in definition['oneOf']:
                case = ref['$ref'].split('/')[-1]
                fields = definitions[case]['properties']
                action = fields[discriminator]['enum'][0]
                assignments = ', '.join(f'{key} = value.{key}' for key in fields)
                cs += [f'                case "{action}": Validate(new {case} {{ {assignments} }}, depth + 1); break;']
            cs += ['                default: throw new Exception("Unknown operation action");', '            }', '        }', '']
            continue
        cs += [f'        public static void Validate({name} value, int depth = 0)', '        {',
               f'            if (value == null) throw new Exception("Missing {name}");',
               '            if (depth > 16) throw new Exception("Protocol nesting exceeds limit");']
        for key, field in definition["properties"].items():
            access = f'value.{key}'
            checks = []
            if field.get("type") == "number":
                kind = "double" if key == "deadline" and definition.get("x-request") else "float"
                checks.append(f'{kind}.IsNaN({access}) || {kind}.IsInfinity({access})')
            for bound, op in [('minimum', '<'), ('maximum', '>'), ('exclusiveMinimum', '<='), ('exclusiveMaximum', '>=')]:
                if bound in field:
                    checks.append(f'{access} {op} {field[bound]}')
            if field.get("type") in ("string", "array"):
                if key in definition["required"]:
                    checks.append(f'{access} == null')
                for bound, op in [('minLength', '<'), ('maxLength', '>'), ('minItems', '<'), ('maxItems', '>')]:
                    if bound in field:
                        checks.append(f'({access} != null && {access}.Length {op} {field[bound]})')
            if "enum" in field:
                checks.append('(' + ' && '.join(f'{access} != {json.dumps(v)}' for v in field['enum']) + ')')
            if "pattern" in field:
                checks.append(f'({access} != null && !Regex.IsMatch({access}, {json.dumps(field["pattern"])}))')
            if checks:
                cs.append(f'            if ({" || ".join(checks)}) throw new Exception("Invalid {name}.{key}");')
            if "$ref" in field:
                if field.get('x-unity-presence'):
                    cs.append(f'            if (value.has_{key}) Validate({access}, depth + 1);')
                elif field.get('x-nullable'):
                    cs.append(f'            if ({access} != null) Validate({access}, depth + 1);')
                else:
                    cs.append(f'            Validate({access}, depth + 1);')
            if field.get('type') == 'array' and '$ref' in field['items']:
                cs += [f'            if ({access} != null)', f'                foreach (var item in {access}) Validate(item, depth + 1);']
            elif field.get('type') == 'array' and 'minimum' in field['items']:
                minimum = field['items']['minimum']
                cs += [f'            if ({access} != null)', f'                foreach (var item in {access}) if (item < {minimum}) throw new Exception("Invalid {name}.{key} item");']
        cs += ['        }', '']
    cs += ['    }', '}', '']
    return {PYTHON: '\n'.join(py), CSHARP: '\n'.join(cs)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Fail if generated files are stale; write nothing')
    args = parser.parse_args()
    stale = []
    for path, text in generate().items():
        if args.check:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                stale.append(str(path.relative_to(ROOT)))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding='utf-8', newline='\n')
    if stale:
        parser.exit(1, 'Stale generated files: ' + ', '.join(stale) + '\nRun scripts/generate_protocol.py\n')
    print('Protocol generation check passed.' if args.check else 'Generated request protocol models.')


if __name__ == '__main__':
    main()
