"""Generate request/response Python/C# models; no third-party dependencies.

Only the schema subset used by these requests is supported. Unknown keywords fail
instead of silently generating a different contract.

Pipeline: read -> normalize -> validate -> emit Python/C# -> write or check.
Generation builds text in memory; only the CLI output stage touches files.
"""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PYTHON = ROOT / "src/unity_mcp/generated/requests.py"
CSHARP = ROOT / "unity-package/Editor/Protocol/Generated/RequestProtocol.cs"
RESPONSE_PYTHON = ROOT / "src/unity_mcp/generated/responses.py"
RESPONSE_CSHARP = ROOT / "unity-package/Editor/Protocol/Generated/ResponseProtocol.cs"
PYTHON_ROUTES = ROOT / "src/unity_mcp/generated/routes.py"
CSHARP_ROUTE_TYPES = ROOT / "unity-package/Editor/Protocol/Generated/EditorRequestType.cs"
CSHARP_ROUTES = ROOT / "unity-package/Editor/Protocol/Generated/EditorRequestRoutes.cs"


FIELD_KEYWORDS = {
    "type", "enum", "$ref", "items", "default", "minimum", "maximum",
    "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength",
    "minItems", "maxItems", "pattern", "description", "x-python-alias",
    "x-nullable", "x-unity-presence", "x-csharp-default",
}
OBJECT_KEYWORDS = {
    "type", "additionalProperties", "x-python-name", "x-request", "x-response",
    "x-csharp-external", "x-csharp-base", "x-csharp-omit",
    "allOf", "properties", "required",
}
RESPONSE_CONSTRAINTS = {
    "type", "const", "enum", "minLength", "minimum", "maximum",
    "exclusiveMinimum", "pattern",
}
CSHARP_NUMERIC_BOUNDS = (
    ("minimum", "<"),
    ("maximum", ">"),
    ("exclusiveMinimum", "<="),
    ("exclusiveMaximum", ">="),
)


def read_definitions(directory: Path, responses: bool) -> dict:
    """Load one protocol family, preserving schema declaration order."""
    definitions = {}
    for path in sorted(directory.glob("*.schema.json")):
        if (path.name == 'responses.schema.json') != responses:
            continue
        for name, definition in json.loads(path.read_text(encoding="utf-8"))["$defs"].items():
            if name in definitions:
                raise ValueError(f"Duplicate protocol definition: {name}")
            definitions[name] = definition

    return definitions


def normalize_references(value) -> None:
    if isinstance(value, dict):
        if "$ref" in value:
            value["$ref"] = "#/$defs/" + value["$ref"].split("#/$defs/")[-1]
        for item in value.values():
            normalize_references(item)
    elif isinstance(value, list):
        for item in value:
            normalize_references(item)


def normalize_definitions(definitions: dict) -> dict:
    """Normalize references and nullable fields in the freshly loaded definitions."""
    normalize_references(definitions)
    # Normalize the supported JSON Schema nullable-reference form for both emitters.
    for definition in definitions.values():
        if "oneOf" in definition:
            continue
        for field in definition["properties"].values():
            if isinstance(field.get('type'), list):
                types = field['type']
                if len(types) != 2 or types[1] != 'null':
                    raise ValueError('Only a type plus null is supported')
                field['type'] = types[0]
                field['x-nullable'] = True
            if "anyOf" in field:
                variants = field.pop("anyOf")
                if len(variants) != 2 or variants[1] != {"type": "null"} or set(variants[0]) != {"$ref"}:
                    raise ValueError("Only nullable references are supported in anyOf fields")
                field.update(variants[0])
                field["x-nullable"] = True
    return definitions


def validate_field(field: dict, definitions: dict) -> None:
    if set(field) - FIELD_KEYWORDS:
        raise ValueError(f"Unsupported schema keywords: {set(field) - FIELD_KEYWORDS}")
    if "$ref" in field and field["$ref"].removeprefix("#/$defs/") not in definitions:
        raise ValueError(f"Unknown reference: {field['$ref']}")
    if "items" in field:
        validate_field(field["items"], definitions)


def validate_union(definition: dict, definitions: dict) -> None:
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


def validate_response_conditions(definition: dict, responses: bool) -> None:
    for rule in definition.get('allOf', []):
        if not responses or set(rule) != {'if', 'then'}:
            raise ValueError('Conditional schemas are supported only for responses')
        if set(rule['if']) != {'properties', 'required'} or set(rule['then']) - {'properties', 'required'}:
            raise ValueError('Unsupported response condition')
        for key, condition in rule['if']['properties'].items():
            if key not in definition['properties'] or set(condition) != {'const'}:
                raise ValueError('Response conditions require known constant fields')
        for key, constraints in rule['then'].get('properties', {}).items():
            if key not in definition['properties'] or set(constraints) - RESPONSE_CONSTRAINTS:
                raise ValueError('Unsupported response field constraint')


def validate_definitions(definitions: dict, responses: bool) -> dict:
    """Reject unsupported schemas and return the Python literal aliases."""
    aliases = {}
    for definition in definitions.values():
        if "oneOf" in definition:
            validate_union(definition, definitions)
            continue
        if set(definition) - OBJECT_KEYWORDS:
            raise ValueError("Unsupported object schema")
        if definition["type"] != "object" or definition["additionalProperties"] is not False:
            raise ValueError("Expected a closed object schema")
        for field in definition["properties"].values():
            validate_field(field, definitions)
            if "x-python-alias" in field:
                aliases[field["x-python-alias"]] = field["enum"]
        validate_response_conditions(definition, responses)

    return aliases


def python_type(field: dict, definitions: dict) -> str:
    if "x-python-alias" in field:
        result = field["x-python-alias"]
    elif "$ref" in field:
        name = field["$ref"].split("/")[-1]
        result = definitions[name].get("x-python-name", name)
    elif "enum" in field:
        result = "Literal[" + ", ".join(map(repr, field["enum"])) + "]"
    elif field["type"] == "array":
        item = field['items']
        item_type = python_type(item, definitions)
        constraints = ', '.join(f'{target}={item[source]!r}' for source, target in
            [('minimum', 'ge'), ('maximum', 'le'), ('exclusiveMinimum', 'gt'), ('exclusiveMaximum', 'lt')]
            if source in item)
        if constraints:
            item_type = f'Annotated[{item_type}, Field({constraints})]'
        result = f"list[{item_type}]"
    else:
        result = {"string": "str", "number": "float", "integer": "int", "boolean": "bool"}[field["type"]]
    return result + " | None" if field.get("x-nullable") else result


def emit_python_condition_helper() -> list[str]:
    py = []
    py += [
        'def _check_conditions(model, rules):',
        '    for rule in rules:',
        '        when = rule["if"]["properties"]',
        '        if not all(getattr(model, key) == condition["const"] for key, condition in when.items()):',
        '            continue',
        '        then = rule["then"]',
        '        for key in then.get("required", []):',
        '            if key not in model.model_fields_set or getattr(model, key) is None:',
        '                raise ValueError(f"Missing response field: {key}")',
        '        for key, constraints in then.get("properties", {}).items():',
        '            value = getattr(model, key)',
        '            for constraint, expected in constraints.items():',
        '                valid = True',
        '                if constraint == "type":',
        '                    valid = value is not None',
        '                elif constraint == "const": valid = value == expected',
        '                elif constraint == "enum": valid = value in expected',
        '                elif constraint == "minLength": valid = isinstance(value, str) and len(value) >= expected',
        '                elif constraint == "minimum": valid = value is not None and value >= expected',
        '                elif constraint == "maximum": valid = value is not None and value <= expected',
        '                elif constraint == "exclusiveMinimum": valid = value is not None and value > expected',
        '                elif constraint == "pattern":',
        '                    import re',
        '                    valid = isinstance(value, str) and re.search(expected, value) is not None',
        '                if not valid: raise ValueError(f"Invalid response field: {key}")',
        '    return model', '', '']
    return py


def emit_python_model(name: str, definition: dict, definitions: dict, responses: bool) -> list[str]:
    py = []
    if "oneOf" in definition:
        return []
    py += [f"class {definition.get('x-python-name', name)}(BaseModel):",
           '    model_config = ConfigDict(extra="forbid", allow_inf_nan=False' + (', strict=True)' if responses else ')')]
    for key, field in definition["properties"].items():
        options = []
        if "default" in field and not (responses and key in definition['required']):
            default = field['default']
            options.append(f"default_factory=lambda: {default!r}" if isinstance(default, list) else f"default={default!r}")
            if isinstance(default, dict):
                options.append('validate_default=True')
        mapping = {"minimum": "ge", "maximum": "le", "exclusiveMinimum": "gt", "exclusiveMaximum": "lt",
                   "minLength": "min_length", "maxLength": "max_length", "minItems": "min_length",
                   "maxItems": "max_length", "pattern": "pattern", "description": "description"}
        options += [f"{target}={field[source]!r}" for source, target in mapping.items() if source in field]
        suffix = " = Field(" + ", ".join(options) + ")" if options else ""
        py.append(f"    {key}: {python_type(field, definitions)}{suffix}")
    if definition.get('allOf'):
        py += ['', '    @model_validator(mode="after")', '    def _validate_conditions(self):',
               f'        return _check_conditions(self, {definition["allOf"]!r})']
    py += ['', '']
    return py


def emit_python(definitions: dict, aliases: dict, responses: bool, routes: list[dict]) -> str:
    py = ['# Generated by scripts/generate_protocol.py. Do not edit.',
          'from __future__ import annotations', 'from typing import Annotated, Literal',
          'from pydantic import BaseModel, ConfigDict, Field, model_validator',
          'from .routes import Endpoints', '']
    if responses:
        py.extend(emit_python_condition_helper())
    for name, values in aliases.items():
        py += [name + " = Literal[" + ", ".join(map(repr, values)) + "]", '']
    for name, definition in definitions.items():
        py.extend(emit_python_model(name, definition, definitions, responses))
    for name, definition in definitions.items():
        if "oneOf" in definition:
            cases = ' | '.join(ref['$ref'].split('/')[-1] for ref in definition['oneOf'])
            py += [f"{name} = Annotated[{cases}, Field(discriminator={definition['x-discriminator']!r})]", '']
    for name, definition in definitions.items():
        if "oneOf" in definition:
            continue
        py.append(f"{definition.get('x-python-name', name)}.model_rebuild()")
    py += ['', ('RESPONSE_MODELS' if responses else 'REQUEST_MODELS') + ' = {']
    for route in routes:
        name = route['response' if responses else 'request']
        if name is not None:
            py.append(f"    Endpoints.{endpoint_constant(route['name'])}: {name},")
    py += ['}', '']
    presence = [key for key, f in definitions.get('UiPrefabNode', {}).get('properties', {}).items() if f.get('x-unity-presence')]
    if not responses:
        py += emit_python_node_adapter(presence)

    return "\n".join(py)


def emit_python_node_adapter(presence: list[str]) -> list[str]:
    return ['', '', 'def prepare_ui_node(node: dict, explicit: dict | None = None) -> dict:',
           '    """Encode explicitly supplied layout fields for Unity JsonUtility."""',
           '    explicit = node if explicit is None else explicit',
           '    result = dict(node)', f'    for key in {tuple(presence)!r}:',
           '        present = explicit.get(key) is not None', '        result["has_" + key] = present',
           '        if node.get(key) is None:', '            result.pop(key, None)',
           '    children = explicit.get("children", [])',
           '    result["children"] = [prepare_ui_node(child, children[i] if i < len(children) else {})',
           '                          for i, child in enumerate(node.get("children", []))]',
           '    return result', '']


def csharp_type(field: dict, definitions: dict) -> str:
    if "$ref" in field:
        name = field["$ref"].split("/")[-1]
        return definitions[name].get('x-csharp-external', name)
    if field["type"] == "array":
        return csharp_type(field["items"], definitions) + "[]"
    return {"string": "string", "number": "float", "integer": "int", "boolean": "bool"}[field["type"]]


def csharp_literal(value, field: dict, definitions: dict) -> str:
    if isinstance(value, dict) and '$ref' in field:
        properties = definitions[field['$ref'].split('/')[-1]]['properties']
        return 'new ' + csharp_type(field, definitions) + ' { ' + ', '.join(
            key + ' = ' + csharp_literal(item, properties[key], definitions)
            for key, item in value.items()) + ' }'
    if isinstance(value, list):
        return f"new {csharp_type(field['items'], definitions)}[] {{ " + ', '.join(csharp_literal(v, field['items'], definitions) for v in value) + ' }'
    if field.get("type") == "number":
        return str(value) + "f"
    return json.dumps(value, ensure_ascii=True)


def emit_csharp_model(name: str, definition: dict, definitions: dict) -> list[str]:
    cs = []
    if definition.get('x-csharp-external'):
        return []
    if 'oneOf' in definition:
        # JsonUtility cannot deserialize discriminated unions. Merge the case
        # fields into one wire class; validate by the action-specific schema.
        fields = {}
        for ref in definition['oneOf']:
            for key, field in definitions[ref['$ref'].split('/')[-1]]['properties'].items():
                fields.setdefault(key, field)
        definition = {'properties': fields}
    base = definition.get('x-csharp-base')
    omitted = set(definition.get('x-csharp-omit', []))
    if base:
        omitted |= set(definitions[base]['properties']) - set(definitions[base].get('x-csharp-omit', []))
    interface = " : IEditorRequest" if definition.get("x-request") else (' : ' + base if base else '')
    visibility = "internal" if interface else "public"
    if base:
        visibility = 'public'
    modifier = '' if any(d.get('x-csharp-base') == name for d in definitions.values()) else 'sealed '
    cs += [f'    [Serializable] {visibility} {modifier}class {name}{interface}', '    {']
    for key, field in definition["properties"].items():
        if key in omitted:
            continue
        kind = "double" if definition.get("x-request") and key == "deadline" else csharp_type(field, definitions)
        default = field.get("x-csharp-default")
        if default is None and "default" in field and field["default"] is not None:
            default = csharp_literal(field["default"], field, definitions)
        cs.append(f'        public {kind} {key}' + (f' = {default}' if default is not None else '') + ';')
        if field.get("x-unity-presence"):
            cs.append(f'        public bool has_{key};')
    if definition.get('x-request'):
        cs += ['        string IEditorRequest.Id => id;', '        double IEditorRequest.Deadline => deadline;']
    cs += ['    }', '']
    return cs


def emit_csharp_field_validation(name: str, key: str, field: dict,
                                 definition: dict, definitions: dict) -> list[str]:
    cs = []
    access = f'value.{key}'
    checks = []
    if field.get("type") == "number":
        kind = "double" if key == "deadline" and definition.get("x-request") else "float"
        checks.append(f'{kind}.IsNaN({access}) || {kind}.IsInfinity({access})')
    for bound, op in CSHARP_NUMERIC_BOUNDS:
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
        external = definitions[field['$ref'].split('/')[-1]].get('x-csharp-external')
        if field.get('x-unity-presence'):
            cs.append(f'            if (value.has_{key}) Validate({access}, depth + 1);')
        elif field.get('x-nullable') and not external:
            cs.append(f'            if ({access} != null) Validate({access}, depth + 1);')
        else:
            cs.append(f'            Validate({access}, depth + 1);')
    if field.get('type') == 'array' and '$ref' in field['items']:
        cs += [f'            if ({access} != null)', f'                foreach (var item in {access}) Validate(item, depth + 1);']
    elif field.get('type') == 'array' and field['items'].get('type') in ('integer', 'number'):
        item_schema = field['items']
        item_checks = []
        if item_schema['type'] == 'number':
            item_checks.append('float.IsNaN(item) || float.IsInfinity(item)')
        item_checks += [f'item {op} {item_schema[bound]}'
                        for bound, op in CSHARP_NUMERIC_BOUNDS if bound in item_schema]
        if item_checks:
            cs += [f'            if ({access} != null)',
                   f'                foreach (var item in {access}) if ({" || ".join(item_checks)}) throw new Exception("Invalid {name}.{key} item");']
    return cs


def emit_csharp_conditions(name: str, definition: dict, definitions: dict) -> list[str]:
    cs = []
    omitted = set(definition.get('x-csharp-omit', []))
    for rule in definition.get('allOf', []):
        if omitted & (set(rule['if']['properties']) | set(rule['then'].get('required', [])) | set(rule['then'].get('properties', {}))):
            continue
        when = ' && '.join(f'value.{key} == {json.dumps(condition["const"])}' for key, condition in rule['if']['properties'].items())
        cs += [f'            if ({when})', '            {']
        then = rule['then']
        for key in sorted(set(then.get('required', [])) | set(then.get('properties', {}))):
            field = definition['properties'][key]
            access = 'value.' + key
            is_external = '$ref' in field and definitions[field['$ref'].split('/')[-1]].get('x-csharp-external')
            checks = []
            if key in then.get('required', []) and (field.get('type') in ('string', 'array') or ('$ref' in field and not is_external)):
                checks.append(f'{access} == null')
            for constraint, value in then.get('properties', {}).get(key, {}).items():
                if constraint == 'const': checks.append(f'{access} != {json.dumps(value)}')
                elif constraint == 'enum': checks.append('(' + ' && '.join(f'{access} != {json.dumps(v)}' for v in value) + ')')
                elif constraint == 'minLength': checks.append(f'({access} == null || {access}.Length < {value})')
                elif constraint in ('minimum', 'maximum', 'exclusiveMinimum'):
                    op = {'minimum':'<', 'maximum':'>', 'exclusiveMinimum':'<='}[constraint]
                    checks.append(f'{access} {op} {value}')
                elif constraint == 'pattern': checks.append(f'({access} == null || !Regex.IsMatch({access}, {json.dumps(value)}))')
                elif constraint == 'type' and not is_external: checks.append(f'{access} == null')
            if checks:
                cs += [f'                if ({" || ".join(checks)}) throw new Exception("Invalid {name}.{key}");']
        cs += ['            }']
    return cs


def emit_csharp_union_validation(name: str, definition: dict, definitions: dict) -> list[str]:
    cs = []
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
    return cs


def emit_csharp_validation(name: str, definition: dict, definitions: dict) -> list[str]:
    cs = []
    if 'oneOf' in definition:
        return emit_csharp_union_validation(name, definition, definitions)
    cs += [f'        public static void Validate({definition.get("x-csharp-external", name)} value, int depth = 0)', '        {']
    if not definition.get('x-csharp-external'):
        cs += [f'            if (value == null) throw new Exception("Missing {name}");']
    cs += ['            if (depth > 16) throw new Exception("Protocol nesting exceeds limit");']
    omitted = set(definition.get('x-csharp-omit', []))
    for key, field in definition["properties"].items():
        if key not in omitted:
            cs.extend(emit_csharp_field_validation(name, key, field, definition, definitions))
    cs.extend(emit_csharp_conditions(name, definition, definitions))
    for child, child_definition in definitions.items():
        if child_definition.get('x-csharp-base') == name:
            cs += [f'            if (value is {child} typed{child}) Validate(typed{child}, depth + 1);']
    cs += ['        }', '']
    return cs


def emit_csharp(definitions: dict, responses: bool) -> str:
    cs = ['// Generated by scripts/generate_protocol.py. Do not edit.', 'using System;',
          'using System.Text.RegularExpressions;', '', 'namespace UnityMcp.Editor', '{']
    for name, definition in definitions.items():
        cs.extend(emit_csharp_model(name, definition, definitions))
    cs += ['    internal static class ' + ('ResponseProtocolValidation' if responses else 'RequestProtocolValidation'), '    {']
    for name, definition in definitions.items():
        cs.extend(emit_csharp_validation(name, definition, definitions))
    cs += ['    }', '}', '']
    return "\n".join(cs)


def endpoint_constant(name: str) -> str:
    words = re.sub(r'(.)([A-Z][a-z]+)', r'\1_\2', name)
    return re.sub(r'([a-z0-9])([A-Z])', r'\1_\2', words).upper()


def validate_routes(routes: list[dict], requests: dict, responses: dict) -> None:
    """Require one unambiguous route for every transport request and response."""
    if not isinstance(routes, list) or not routes:
        raise ValueError('Routes must be a nonempty list')
    names, constants, paths, request_names, response_names = set(), set(), set(), set(), set()
    keys = {'name', 'method', 'path', 'request', 'response'}
    for route in routes:
        if not isinstance(route, dict) or set(route) != keys:
            raise ValueError('Route fields must be name/method/path/request/response')
        for field in ('name', 'response'):
            if not isinstance(route[field], str) or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', route[field]):
                raise ValueError(f'Invalid route identifier: {field}')
        name, path, method = route['name'], route['path'], route['method']
        constant = endpoint_constant(name)
        if name in names or constant in constants:
            raise ValueError(f'Duplicate route name: {name}')
        names.add(name)
        constants.add(constant)
        if (not isinstance(path, str) or not path.startswith('/') or
                any(c.isspace() or ord(c) < 32 or c in '?#\\' for c in path)):
            raise ValueError(f'Invalid route path: {path}')
        # Python model lookup is path-based, so multiple methods on one path are not supported yet.
        if path in paths:
            raise ValueError(f'Duplicate route path: {path}')
        paths.add(path)
        if method not in ('GET', 'POST', 'PUT', 'PATCH', 'DELETE'):
            raise ValueError(f'Unsupported HTTP method: {method}')
        request, response = route['request'], route['response']
        if request is None:
            if method != 'GET':
                raise ValueError('Routes without a request model must use GET')
        elif method == 'GET' or not isinstance(request, str):
            raise ValueError('Routes with a request model require a non-GET method')
        if request is not None:
            if request not in requests or not requests[request].get('x-request'):
                raise ValueError(f'Unknown request model: {request}')
            if request in request_names:
                raise ValueError(f'Request model registered more than once: {request}')
            request_names.add(request)
        if response not in responses or not responses[response].get('x-response'):
            raise ValueError(f'Unknown response model: {response}')
        if response in response_names:
            raise ValueError(f'Response model registered more than once: {response}')
        response_names.add(response)
    declared_requests = {name for name, definition in requests.items() if definition.get('x-request')}
    declared_responses = {name for name, definition in responses.items() if definition.get('x-response')}
    if request_names != declared_requests or response_names != declared_responses:
        raise ValueError('Request/response models and routes must have complete one-to-one coverage')


def emit_python_routes(routes: list[dict]) -> str:
    lines = ['# Generated by scripts/generate_protocol.py. Do not edit.', '', 'class Endpoints:']
    for route in routes:
        lines.append(f"    {endpoint_constant(route['name'])} = {route['path']!r}")
    lines += ['', '', 'HTTP_METHODS = {']
    for route in routes:
        lines.append(f"    Endpoints.{endpoint_constant(route['name'])}: {route['method']!r},")
    lines += ['}', '']
    return '\n'.join(lines)


def emit_csharp_route_types(routes: list[dict]) -> str:
    lines = ['// Generated by scripts/generate_protocol.py. Do not edit.',
             'namespace UnityMcp.Editor', '{', '    internal enum EditorRequestType', '    {']
    lines += [f"        {route['name']}," for route in routes]
    return '\n'.join(lines + ['    }', '}', ''])


def emit_csharp_routes(routes: list[dict]) -> str:
    entries = [
        f'                ({json.dumps(route["method"])}, {json.dumps(route["path"])}, EditorRequestType.{route["name"]}),'
        for route in routes
    ]
    return """// Generated by scripts/generate_protocol.py. Do not edit.
using System;

namespace UnityMcp.Editor
{
    internal static class EditorRequestRoutes
    {
        static readonly (string Method, string Path, EditorRequestType Type)[] entries = {
""" + '\n'.join(entries) + """
        };

        public static bool TryResolve(string method, string path, out EditorRequestType type)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.Method, method, StringComparison.Ordinal)
                    && string.Equals(entry.Path, path, StringComparison.Ordinal))
                {
                    type = entry.Type;
                    return true;
                }
            type = default;
            return false;
        }
    }
}
"""


def generate() -> dict[Path, str]:
    """Build all outputs in memory; file writes are confined to the CLI layer."""
    families = {}
    for responses, python_path, csharp_path in (
        (False, PYTHON, CSHARP),
        (True, RESPONSE_PYTHON, RESPONSE_CSHARP),
    ):
        definitions = normalize_definitions(read_definitions(ROOT / "protocol", responses))
        aliases = validate_definitions(definitions, responses)
        families[responses] = (definitions, aliases, python_path, csharp_path)
    route_document = json.loads((ROOT / "protocol/routes.json").read_text(encoding='utf-8'))
    if set(route_document) != {'routes'}:
        raise ValueError('Route document must contain only routes')
    routes = route_document['routes']
    validate_routes(routes, families[False][0], families[True][0])
    outputs = {}
    for responses, (definitions, aliases, python_path, csharp_path) in families.items():
        outputs[python_path] = emit_python(definitions, aliases, responses, routes)
        outputs[csharp_path] = emit_csharp(definitions, responses)
    outputs[PYTHON_ROUTES] = emit_python_routes(routes)
    outputs[CSHARP_ROUTE_TYPES] = emit_csharp_route_types(routes)
    outputs[CSHARP_ROUTES] = emit_csharp_routes(routes)
    return outputs


def write_or_check_outputs(outputs: dict[Path, str], check: bool) -> list[str]:
    """Write generated text, or report stale paths without modifying files."""
    stale = []
    for path, text in outputs.items():
        if check:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                stale.append(str(path.relative_to(ROOT)))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding='utf-8', newline='\n')
    return stale


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Fail if generated files are stale; write nothing')
    args = parser.parse_args()
    stale = write_or_check_outputs(generate(), args.check)
    if stale:
        parser.exit(1, 'Stale generated files: ' + ', '.join(stale) + '\nRun scripts/generate_protocol.py\n')
    print('Protocol generation check passed.' if args.check else 'Generated request and response protocol models.')


if __name__ == '__main__':
    main()
