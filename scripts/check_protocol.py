"""Request protocol regression checks. Run with the project's Python environment."""
import asyncio
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from contextlib import redirect_stderr
import io
import copy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))

import httpx
from pydantic import ValidationError
from jsonschema import Draft202012Validator
from unity_mcp.generated.requests import CreateUiPrefabRequest, UiNode, REQUEST_MODELS, prepare_ui_node
from unity_mcp.generated.requests import UiPrefabSpec, UiNodeAddition
from unity_mcp.bridge import EditorConnection, validate_response
from unity_mcp.generated.responses import RESPONSE_MODELS
from unity_mcp.generated.routes import Endpoints, HTTP_METHODS


class ProtocolTests(unittest.TestCase):
    def test_compact_tool_schemas_preserve_validation_and_title_fields(self):
        from unity_mcp.server import create_server, without_schema_titles
        schema = {"type": "object", "title": "Record", "required": ["title"],
            "properties": {"title": {"type": "string", "title": "Title", "enum": ["title"]}},
            "default": {"title": "title"}}
        original = copy.deepcopy(schema)
        compacted = without_schema_titles(schema)
        self.assertNotIn("title", compacted)
        self.assertEqual(compacted["properties"]["title"], {"type": "string", "enum": ["title"]})
        self.assertEqual(compacted["default"], {"title": "title"})
        self.assertEqual(schema, original)

        async def run():
            server = create_server()
            listed = {tool.name: tool for tool in await server.list_tools()}
            samples = {
                "create_ui_prefab": {"output_path": "Assets/Test.prefab", "spec": {"name": "Panel",
                    "children": [{"name": "Label", "kind": "text", "text": "Hello"}]}},
                "batch_edit_ui_prefab": {"asset_path": "Assets/Test.prefab", "expected_version": "a" * 64,
                    "operations": [{"action": "delete", "index_path": "0/0"}]},
                "run_ui_test": {"test_instance_id": "test", "steps": [{"action": "pointer_down"}]},
            }
            for name, sample in samples.items():
                full = server._tool_manager.get_tool(name).parameters
                compact = listed[name].inputSchema
                Draft202012Validator.check_schema(compact)
                variants = [sample, {}]
                invalid = copy.deepcopy(sample)
                if name == "create_ui_prefab": invalid["spec"]["children"][0]["kind"] = "unknown"
                elif name == "batch_edit_ui_prefab": invalid["operations"][0]["action"] = "unknown"
                else: invalid["steps"][0]["button"] = "unknown"
                variants.append(invalid)
                for value, expected in zip(variants, (True, False, False)):
                    self.assertEqual(Draft202012Validator(full).is_valid(value), expected, name)
                    self.assertEqual(Draft202012Validator(compact).is_valid(value), expected, name)
        asyncio.run(run())

    def test_pointer_steps_round_trip_and_reject_invalid_input(self):
        from unity_mcp.generated.requests import RunUiTestRequest
        envelope = {"id": "a" * 32, "deadline": 12345, "test_instance_id": "test"}
        steps = [
            {"action": "pointer_move", "target_path": "Panel/Button", "frames": 6},
            {"action": "pointer_down"},
            {"action": "pointer_move", "x": 300, "y": 200},
            {"action": "pointer_up"},
            {"action": "pointer_scroll", "scroll_y": -2},
        ]
        wire = RunUiTestRequest.model_validate({**envelope, "steps": steps}).model_dump()
        self.assertEqual(wire["steps"][2]["target_path"], "")
        self.assertEqual(wire["steps"][1]["button"], "left")
        self.assertEqual(wire["steps"][0]["frames"], 6)
        for invalid in [
            {"action": "pointer_move", "frames": 0},
            {"action": "pointer_move", "frames": 61},
            {"action": "pointer_down", "button": "unknown"},
            {"action": "pointer_scroll", "scroll_y": 101},
        ]:
            with self.subTest(step=invalid), self.assertRaises(ValidationError):
                RunUiTestRequest.model_validate({**envelope, "steps": [invalid]})

    def test_shared_image_utils_validate_png_and_local_import(self):
        from unity_mcp.image_utils import decode_base64, decode_png, encode_base64, read_image
        encoded = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1kAAAAASUVORK5CYII="
        png = decode_png(encoded, 1, 1)
        self.assertEqual(encode_base64(png), encoded)
        with self.assertRaisesRegex(ValueError, "dimensions"):
            decode_png(encoded, 2, 1)
        with self.assertRaisesRegex(ValueError, "end marker"):
            decode_png(encode_base64(png[:-12]), 1, 1)
        with self.assertRaisesRegex(ValueError, "Base64"):
            decode_png("invalid!", 1, 1)
        with self.assertRaisesRegex(ValueError, "size"):
            decode_base64("AAAA", max_bytes=1)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "image.png"
            path.write_bytes(png)
            data, kind, width, height = read_image(str(path.resolve()))
            self.assertEqual((data, kind, width, height), (png, "png", 1, 1))

    def test_image_import_adapter_reads_and_sends_local_image(self):
        from unity_mcp.image_utils import decode_base64
        from unity_mcp.server import create_server
        from types import SimpleNamespace
        async def run():
            encoded = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1kAAAAASUVORK5CYII="
            with tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / "source.png"
                source.write_bytes(decode_base64(encoded))
                def respond(request):
                    self.assertEqual(request.url.path, Endpoints.IMPORT_UI_IMAGE)
                    body = json.loads(request.content)
                    self.assertEqual(body["image_base64"], encoded)
                    self.assertEqual(body["border"], [0, 0, 0, 0])
                    result = self.response_samples()[Endpoints.IMPORT_UI_IMAGE]
                    result.update(id=body["id"], width=1, height=1)
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=result)
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path(directory), "test", client)
                    tool = create_server()._tool_manager.get_tool("import_ui_image").fn
                    ctx = SimpleNamespace(request_context=SimpleNamespace(lifespan_context={"editor": connection}))
                    result = await tool(str(source.resolve()), "Assets/Image.png", ctx)
                    self.assertTrue(result["ok"])
        asyncio.run(run())

    def test_multiple_screenshot_rule_is_deferred_to_unity(self):
        async def run():
            def respond(request):
                body = json.loads(request.content)
                self.assertEqual(len(body["steps"]), 2)
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                    "id": body["id"], "instance_id": "test", "ok": False,
                    "error": "At most one screenshot per test"})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                result = await connection.run_ui_test("owned", [{"action": "screenshot"}, {"action": "screenshot"}])
                self.assertFalse(result["ok"])
                self.assertEqual(result["error"], "At most one screenshot per test")
        asyncio.run(run())

    def test_common_request_handles_gets_and_rejects_bodies(self):
        async def run():
            samples = self.response_samples()
            def respond(request):
                self.assertEqual(request.method, "GET")
                self.assertEqual(request.content, b"")
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=samples[request.url.path])
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                for endpoint, method in HTTP_METHODS.items():
                    if method == "GET":
                        self.assertEqual(await connection.request(endpoint), samples[endpoint])
                with self.assertRaisesRegex(ValueError, "GET endpoints"):
                    await connection.request(Endpoints.PLAY_STATUS, {"unexpected": True})
        asyncio.run(run())

    def test_common_create_request_preserves_presence_timeout_and_target_identity(self):
        async def run():
            def respond(request):
                body = json.loads(request.content)
                self.assertEqual(request.extensions["timeout"]["read"], 9)
                nodes = body["spec"]["children"]
                self.assertFalse(nodes[0]["has_width"])
                self.assertTrue(nodes[1]["has_width"])
                self.assertEqual(nodes[1]["width"], 320)
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                    "id": body["id"], "instance_id": "test", "ok": True,
                    "asset_path": "Assets/Wrong.prefab", "node_count": 2})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                with self.assertRaisesRegex(RuntimeError, "target asset mismatch"):
                    await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/Test.prefab', 'spec': {'name': 'Root', 'children': [{'name': 'Inherited', 'kind': 'prefab', 'prefab_path': 'Assets/Button.prefab'}, {'name': 'Override', 'kind': 'prefab', 'prefab_path': 'Assets/Button.prefab', 'width': 320}]}}, timeout=7)
        asyncio.run(run())

    def test_common_request_rejects_oversized_body_before_sending(self):
        async def run():
            def respond(request):
                self.fail("Oversized request was sent")
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                with self.assertRaisesRegex(ValueError, "size limit"):
                    await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/Test.prefab', 'spec': {'name': 'Root', 'children': [{'name': 'Label' + str(i), 'kind': 'text', 'text': 'a' * 4096} for i in range(80)]}})
        asyncio.run(run())

    def test_bridge_discovery_errors_identify_target_and_failure(self):
        async def run():
            with tempfile.TemporaryDirectory() as directory:
                project = Path(directory).resolve()
                with self.assertRaisesRegex(RuntimeError, "discovery file is missing") as caught:
                    await EditorConnection.connect(project)
                self.assertIn(str(project), str(caught.exception))
                self.assertNotIn("Install unity-package", str(caught.exception))
                discovery = project / "Library/UnityMcp/bridge.json"
                discovery.parent.mkdir(parents=True)
                discovery.write_text('{"port":0,"token":"invalid"}')
                with self.assertRaisesRegex(RuntimeError, "Invalid Unity bridge discovery data"):
                    await EditorConnection.connect(project)
        asyncio.run(run())

    def test_route_bindings_cover_models_and_http_methods(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        routes = json.loads((ROOT / 'protocol/routes.json').read_text())['routes']
        requests = generator.normalize_definitions(generator.read_definitions(ROOT / 'protocol', False))
        responses = generator.normalize_definitions(generator.read_definitions(ROOT / 'protocol', True))
        generator.validate_routes(routes, requests, responses)
        self.assertEqual(set(HTTP_METHODS), {route['path'] for route in routes})
        for route in routes:
            self.assertEqual(HTTP_METHODS[route['path']], route['method'])
            self.assertEqual(RESPONSE_MODELS[route['path']].__name__, route['response'])
            if route['request'] is None:
                self.assertNotIn(route['path'], REQUEST_MODELS)
            else:
                self.assertEqual(REQUEST_MODELS[route['path']].__name__, route['request'])

    def test_invalid_route_bindings_are_rejected(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        routes = json.loads((ROOT / 'protocol/routes.json').read_text())['routes']
        requests = generator.normalize_definitions(generator.read_definitions(ROOT / 'protocol', False))
        responses = generator.normalize_definitions(generator.read_definitions(ROOT / 'protocol', True))
        changes = [
            (2, {'path': routes[0]['path']}),
            (2, {'name': routes[0]['name']}),
            (2, {'response': 'MissingReply'}),
            (2, {'request': 'MissingRequest'}),
            (2, {'handler': 'Invalid;Handler'}),
            (2, {'execution': 'unknown'}),
            (2, {'method': 'GET'}),
            (2, {'path': '/invalid?query=true'}),
            (0, {'request': 'CreateUiPrefabRequest'}),
            (0, {'method': 'POST'}),
        ]
        for index, patch_values in changes:
            with self.subTest(changes=patch_values), self.assertRaises(ValueError):
                changed = copy.deepcopy(routes)
                changed[index].update(patch_values)
                generator.validate_routes(changed, requests, responses)
        with self.assertRaises(ValueError):
            generator.validate_routes(routes[:-1], requests, responses)

    def test_route_path_changes_update_both_language_outputs(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        routes = json.loads((ROOT / 'protocol/routes.json').read_text())['routes']
        preview = next(route for route in routes if route['name'] == 'PreviewUiPrefab')
        preview.update(path='/ui/prefabs/render', method='PUT')
        namespace = {}
        exec(generator.emit_python_routes(routes), namespace)
        endpoint = namespace['Endpoints'].PREVIEW_UI_PREFAB
        self.assertEqual(endpoint, '/ui/prefabs/render')
        self.assertEqual(namespace['HTTP_METHODS'][endpoint], 'PUT')
        csharp = generator.emit_csharp_routes(routes)
        self.assertIn('("PUT", "/ui/prefabs/render", EditorRequestType.PreviewUiPrefab)', csharp)
        self.assertIn(f'("GET", {json.dumps(Endpoints.HEALTH)}, EditorRequestType.Health)', csharp)
        self.assertNotIn('RequestHandler', csharp)
        self.assertEqual(generator.endpoint_constant('CreateUiPrefab'), 'CREATE_UI_PREFAB')

    def test_generation_keeps_handwritten_handler_bindings_outside_outputs(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        outputs = generator.generate()
        manual_registry = ROOT / 'unity-package/Editor/Handlers/EditorRequestRegistry.cs'
        self.assertNotIn(manual_registry, outputs)
        for path in outputs:
            if path.suffix == '.cs':
                self.assertEqual(path.parent, ROOT / 'unity-package/Editor/Protocol/Generated')
        self.assertIn(ROOT / 'unity-package/Editor/Protocol/Generated/EditorRequestType.cs', outputs)
        self.assertIn(ROOT / 'unity-package/Editor/Protocol/Generated/EditorRequestRoutes.cs', outputs)

    def test_bridge_uses_generated_http_method(self):
        async def run():
            def respond(request):
                self.assertEqual(request.method, 'PUT')
                result = self.response_samples()[Endpoints.FIND_UI_ASSETS]
                result['id'] = json.loads(request.content)['id']
                return httpx.Response(200, headers={'X-Unity-Instance': 'test'}, json=result)
            async with httpx.AsyncClient(base_url='http://127.0.0.1', transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), 'test', client)
                with patch.dict(HTTP_METHODS, {Endpoints.FIND_UI_ASSETS: 'PUT'}):
                    await connection.request(Endpoints.FIND_UI_ASSETS, self.request_samples()[Endpoints.FIND_UI_ASSETS])
        asyncio.run(run())

    def test_numeric_array_bounds_are_emitted_for_both_languages(self):
        from typing import Annotated
        from pydantic import Field, TypeAdapter
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        cases = [
            ("minimum", "<", 10, 10, 9),
            ("maximum", ">", 10, 10, 11),
            ("exclusiveMinimum", "<=", 10, 11, 10),
            ("exclusiveMaximum", ">=", 10, 9, 10),
        ]
        for kind in ("integer", "number"):
            for bound, comparison, limit, valid, invalid in cases:
                with self.subTest(kind=kind, bound=bound):
                    field = {"type": "array", "items": {"type": kind, bound: limit}}
                    definition = {"required": ["values"]}
                    code = "\n".join(generator.emit_csharp_field_validation(
                        "NumericArray", "values", field, definition, {}))
                    self.assertIn(f"item {comparison} {limit}", code)
                    self.assertIn("foreach (var item", code)
                    annotation = generator.python_type(field, {})
                    adapter = TypeAdapter(eval(annotation, {"Annotated": Annotated, "Field": Field}))
                    self.assertEqual(adapter.validate_python([valid]), [valid])
                    Draft202012Validator(field).validate([valid])
                    with self.assertRaises(ValidationError):
                        adapter.validate_python([invalid])
                    self.assertFalse(Draft202012Validator(field).is_valid([invalid]))

    def test_numeric_arrays_combine_bounds_and_reject_nonfinite_values(self):
        from typing import Annotated
        from pydantic import Field, TypeAdapter
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        field = {"type": "array", "items": {"type": "number", "minimum": 0.5, "exclusiveMaximum": 1.5}}
        code = "\n".join(generator.emit_csharp_field_validation("NumericArray", "values", field, {"required": []}, {}))
        self.assertIn("float.IsNaN(item) || float.IsInfinity(item) || item < 0.5 || item >= 1.5", code)
        adapter = TypeAdapter(eval(generator.python_type(field, {}), {"Annotated": Annotated, "Field": Field}))
        self.assertEqual(adapter.validate_python([0.5, 1.0]), [0.5, 1.0])
        for invalid in (0.49, 1.5, float("nan"), float("inf"), float("-inf")):
            with self.subTest(invalid=invalid), self.assertRaises(ValidationError):
                adapter.validate_python([invalid])
        unbounded = {"type": "array", "items": {"type": "number"}}
        code = "\n".join(generator.emit_csharp_field_validation("NumericArray", "values", unbounded, {"required": []}, {}))
        self.assertIn("float.IsNaN(item) || float.IsInfinity(item)", code)

    @staticmethod
    def response_samples():
        common = {"id": "a" * 32, "instance_id": "test", "ok": True}
        version = "b" * 64
        asset = {"name": "Sprite", "asset_path": "Assets/a.png", "guid": "c" * 32, "local_id": "1", "asset_type": "sprite"}
        sprite = {"size": {"x": 32, "y": 32}, "texture_size": {"x": 32, "y": 32},
            "pivot": {"x": 16, "y": 16}, "rect": {"x": 0, "y": 0, "width": 32, "height": 32},
            "border": {"x": 0, "y": 0, "z": 0, "w": 0}, "pixels_per_unit": 100,
            "texture_path": "Assets/a.png", "filter_mode": "Bilinear"}
        return {
            Endpoints.CREATE_UI_TEST_SCENE: {**common, "scene_path": "Assets/UiTest.unity",
                "parent_path": "UnityMcpTestCanvas/Content", "prefab_instance_path": "", "saved": True},
            Endpoints.GAME_VIEW_RESOLUTION: {**common, "group": "Standalone", "selected_index": 1,
                "actual_width": 720, "actual_height": 1280, "is_playing": False, "can_restore": True,
                "presets": [{"index": 1, "name": "720x1280", "size_type": "FixedResolution",
                    "width": 720, "height": 1280, "is_custom": True}]},
            Endpoints.CREATE_TMP_FONT: {**common, "asset_path": "Assets/Font.asset", "guid": "c" * 32, "population_mode": "dynamic", "character_count": 2, "all_characters_present": False, "missing_codepoints": ["U+4E2D"]},
            Endpoints.UPDATE_TMP_FONT_CHARACTERS: {**common, "asset_path": "Assets/Font.asset", "guid": "c" * 32, "population_mode": "static", "character_count": 2, "all_characters_present": True, "missing_codepoints": []},
            Endpoints.CREATE_UI_PREFAB: {**common, "asset_path": "Assets/Test.prefab", "node_count": 1},
            Endpoints.FIND_UI_ASSETS: {**common, "results": [{"index": 0, "ok": True, "asset_type": "sprite", "total": 1, "next_offset": -1, "assets": [asset]}]},
            Endpoints.INSPECT_UI_ASSETS: {**common, "results": [{"index": 0, "ok": True, "asset_type": "sprite", "asset": asset, "sprite": sprite}]},
            Endpoints.PREVIEW_UI_PREFAB: {**common, "asset_path": "Assets/Test.prefab", "version": version, "png_base64": "AA==", "width": 1280, "height": 720},
            Endpoints.IMPORT_UI_IMAGE: {**common, "asset_path": "Assets/Image.png", "guid": "c" * 32, "filter_mode": "bilinear", "width": 32, "height": 32, "pixels_per_unit": 100, "border": {"x": 0, "y": 0, "z": 0, "w": 0}},
            Endpoints.LIST_UI_BACKUPS: {**common, "total": 0, "next_offset": -1, "backups": []},
            Endpoints.RESTORE_UI_PREFAB: {**common, "asset_path": "Assets/Test.prefab", "version": version, "backup_id": "c" * 32, "restored_from": "d" * 32, "saved": True},
            Endpoints.BATCH_EDIT_UI_PREFAB: {**common, "asset_path": "Assets/Test.prefab", "version": version, "backup_id": "c" * 32, "saved": True, "phase": "complete", "executed_operations": 1, "changed_properties": 1, "node_operations": 0, "added_components": 0},
            Endpoints.HEALTH: {"protocol": 2, "project_path": "D:/Test", "instance_id": "test"},
            Endpoints.PROJECT_INFO: {"protocol": 2, "project_path": "D:/Test", "instance_id": "test", "project_name": "Test", "unity_version": "6000.4.0f1", "source": "editor"},
            Endpoints.COMPILATION_STATUS: {"instance_id": "test", "is_compiling": False, "is_updating": False,
                "compilation_revision": "revision", "has_compilation_result": True, "errors": [], "errors_truncated": False,
                "refresh_id": "refresh", "refresh_pending": False},
            Endpoints.REFRESH_ASSETS: {**common, "refresh_requested": True, "refresh_id": "refresh"},
            Endpoints.PLAY_STATUS: {"instance_id": "test", "is_playing": True, "is_changing": False,
                "is_paused": False, "session_id": "play", "frame_count": 10, "test_running": False, "test_instances": []},
            Endpoints.ENTER_PLAY_MODE: {**common, "transition_requested": True, "session_id": ""},
            Endpoints.EXIT_PLAY_MODE: {**common, "transition_requested": True},
            Endpoints.INSPECT_RUNTIME_UI: {**common, "session_id": "play", "nodes": [], "truncated": False},
            Endpoints.MOUNT_TEST_PREFAB: {**common, "session_id": "play", "test_instance_id": "owned", "scene_path": "Scene:/Canvas/Test"},
            Endpoints.CLEAR_TEST_INSTANCES: {**common, "cleared_instances": 1},
            Endpoints.RUN_UI_TEST: {**common, "session_id": "play", "test_instance_id": "owned", "passed": True,
                "failed_step_index": -1, "steps": [], "interaction_mode": "callback", "saved_scene": False},
        }

    def test_all_response_models_and_schema(self):
        schema = json.loads((ROOT / "protocol/responses.schema.json").read_text())
        self.assertEqual(set(RESPONSE_MODELS), set(self.response_samples()))
        for endpoint, payload in self.response_samples().items():
            with self.subTest(endpoint=endpoint):
                self.assertIs(validate_response(endpoint, payload), payload)
                Draft202012Validator({**schema, "$ref": "#/$defs/" + RESPONSE_MODELS[endpoint].__name__}).validate(payload)
                if endpoint in REQUEST_MODELS:
                    error = {"id": "a" * 32, "instance_id": "test", "ok": False, "error": "Expected failure"}
                    validate_response(endpoint, error)
                    Draft202012Validator({**schema, "$ref": "#/$defs/" + RESPONSE_MODELS[endpoint].__name__}).validate(error)

    def test_response_rejects_missing_or_coerced_fields(self):
        for endpoint, sample in self.response_samples().items():
            for field in ("instance_id", "ok" if endpoint in REQUEST_MODELS else "is_compiling" if endpoint == Endpoints.COMPILATION_STATUS else "is_playing" if endpoint == Endpoints.PLAY_STATUS else "protocol"):
                payload = copy.deepcopy(sample)
                del payload[field]
                with self.subTest(endpoint=endpoint, field=field), self.assertRaises(RuntimeError):
                    validate_response(endpoint, payload)
        for field in ("node_count", "asset_path"):
            payload = self.response_samples()[Endpoints.CREATE_UI_PREFAB]
            del payload[field]
            with self.assertRaises(RuntimeError):
                validate_response(Endpoints.CREATE_UI_PREFAB, payload)
        for changes in ({"node_count": "1"}, {"ok": 1}, {"ok": False}, {"ok": False, "error": ""}, {"ok": False, "error": None}, {"unexpected": 1}):
            with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                validate_response(Endpoints.CREATE_UI_PREFAB, {**self.response_samples()[Endpoints.CREATE_UI_PREFAB], **changes})

    def test_nested_and_partial_response_validation(self):
        payload = self.response_samples()[Endpoints.INSPECT_UI_ASSETS]
        asset = payload['results'][0]['asset']
        payload['results'] += [
            {'index': 2, 'ok': True, 'asset_type': 'tmp_font', 'asset': {**asset, 'asset_type': 'tmp_font'}, 'font': {
                'family': 'Test', 'style': 'Regular', 'atlas_population_mode': 'Static', 'source_font_path': '',
                'atlas_width': 512, 'atlas_height': 512, 'atlas_count': 1, 'fallbacks': [],
                'fallbacks_truncated': False, 'text_checked': False, 'all_characters_present': False,
                'search_fallbacks': True, 'coverage_mode': 'existing_glyphs_only'}},
            {'index': 3, 'ok': True, 'asset_type': 'ui_prefab', 'asset': {**asset, 'asset_type': 'ui_prefab'}, 'prefab': {
                'asset_path': 'Assets/Test.prefab', 'version': 'b' * 64, 'truncated': False, 'total': 1, 'nodes': [{
                    'name': 'Canvas', 'index_path': '0', 'parent_index_path': '', 'active_self': True,
                    'components': ['UnityEngine.RectTransform'], 'rect': None, 'ui': [{
                        'type': 'Canvas', 'enabled': True, 'properties_truncated': False,
                        'properties': [{'name': 'renderMode', 'value': 'ScreenSpaceOverlay'}]}]}]}}
        ]
        payload["results"].append({"index": 1, "asset_type": "unsupported", "ok": False, "error": "Unknown type"})
        validate_response(Endpoints.INSPECT_UI_ASSETS, payload)
        payload['results'][1]['font']['atlas_count'] = '1'
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.INSPECT_UI_ASSETS, payload)
        payload['results'][1]['font']['atlas_count'] = 1
        del payload["results"][0]["sprite"]["size"]["x"]
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.INSPECT_UI_ASSETS, payload)
        payload = self.response_samples()[Endpoints.INSPECT_UI_ASSETS]
        del payload["results"][0]["sprite"]
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.INSPECT_UI_ASSETS, payload)
        payload = self.response_samples()[Endpoints.FIND_UI_ASSETS]
        payload["results"][0]["assets"][0]["local_id"] = 1
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.FIND_UI_ASSETS, payload)
        payload = self.response_samples()[Endpoints.LIST_UI_BACKUPS]
        payload.update(total=1, backups=[{'backup_id': 'c' * 32, 'asset_path': 'Assets/Test.prefab',
            'asset_guid': 'd' * 32, 'created_at': '2026-11-09T00:00:00Z', 'summary': 'Before edit',
            'before_version': 'b' * 64, 'after_version': None, 'sha256': 'e' * 64}])
        validate_response(Endpoints.LIST_UI_BACKUPS, payload)
        payload['backups'][0]['backup_id'] = 42
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.LIST_UI_BACKUPS, payload)

    def test_response_success_state_constraints(self):
        for endpoint in (Endpoints.BATCH_EDIT_UI_PREFAB, Endpoints.RESTORE_UI_PREFAB):
            payload = self.response_samples()[endpoint]
            payload['saved'] = False
            with self.assertRaises(RuntimeError):
                validate_response(endpoint, payload)
        payload = self.response_samples()[Endpoints.PREVIEW_UI_PREFAB]
        payload['width'] = 1
        with self.assertRaises(RuntimeError):
            validate_response(Endpoints.PREVIEW_UI_PREFAB, payload)

    def test_bridge_reports_invalid_responses_and_preserves_errors(self):
        async def run():
            for mode in ("missing", "identity", "failure"):
                def respond(request):
                    body = json.loads(request.content)
                    result = {"id": body["id"], "instance_id": "test", "ok": True}
                    if mode == "identity": result["id"] = "wrong"
                    if mode == "failure": result.update(ok=False, error="Saved but metadata failed", saved=True, backup_id="c" * 32)
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=result)
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path.cwd(), "test", client)
                    if mode == "failure":
                        result = await connection.request(Endpoints.BATCH_EDIT_UI_PREFAB, self.request_samples()[Endpoints.BATCH_EDIT_UI_PREFAB])
                        self.assertFalse(result["ok"])
                        self.assertTrue(result["saved"])
                        self.assertEqual(result["backup_id"], "c" * 32)
                        self.assertEqual(result["error"], "Saved but metadata failed")
                        continue
                    expected = {"missing": "Invalid Unity response", "identity": "identity mismatch"}
                    with self.assertRaisesRegex(RuntimeError, expected[mode]):
                        await connection.request(Endpoints.BATCH_EDIT_UI_PREFAB, self.request_samples()[Endpoints.BATCH_EDIT_UI_PREFAB])
        asyncio.run(run())

    def test_http_errors_remain_transport_failures(self):
        async def run():
            for status in (401, 413, 500):
                def respond(request):
                    return httpx.Response(status, headers={"X-Unity-Instance": "test"}, json={"error": "transport error"})
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path.cwd(), "test", client)
                    with self.assertRaisesRegex(RuntimeError, "Unity request failed"):
                        await connection.request(Endpoints.FIND_UI_ASSETS, self.request_samples()[Endpoints.FIND_UI_ASSETS])
        asyncio.run(run())
    @staticmethod
    def request_samples():
        return {
            Endpoints.GAME_VIEW_RESOLUTION: {"action": "set", "width": 720, "height": 1280},
            Endpoints.CREATE_UI_TEST_SCENE: {"scene_path": "Assets/UiTest.unity"},
            Endpoints.CREATE_TMP_FONT: {"source_font_path": "Assets/Noto.ttf", "asset_path": "Assets/Font.asset", "characters": "ABC"},
            Endpoints.UPDATE_TMP_FONT_CHARACTERS: {"asset_path": "Assets/Font.asset", "characters": "XYZ"},
            Endpoints.ENTER_PLAY_MODE: {}, Endpoints.EXIT_PLAY_MODE: {},
            Endpoints.INSPECT_RUNTIME_UI: {}, Endpoints.CLEAR_TEST_INSTANCES: {},
            Endpoints.MOUNT_TEST_PREFAB: {"prefab_path": "Assets/Test.prefab", "parent_path": "Canvas"},
            Endpoints.RUN_UI_TEST: {"test_instance_id": "owned", "steps": [
                {"action": "assert", "property": "active_in_hierarchy", "expected": "true"}]},
            Endpoints.REFRESH_ASSETS: {},
            Endpoints.CREATE_UI_PREFAB: {"output_path": "Assets/Test.prefab", "spec": {"name": "Canvas", "children": [{"name": "Panel"}]}},
            Endpoints.FIND_UI_ASSETS: {"queries": [{"asset_type": "sprite"}]},
            Endpoints.INSPECT_UI_ASSETS: {"assets": [{"asset_type": "ui_prefab", "asset_path": "Assets/Test.prefab"}]},
            Endpoints.PREVIEW_UI_PREFAB: {"asset_path": "Assets/Test.prefab"},
            Endpoints.IMPORT_UI_IMAGE: {"asset_path": "Assets/Image.png", "image_base64": "AA=="},
            Endpoints.LIST_UI_BACKUPS: {"asset_path": "Assets/Test.prefab"},
            Endpoints.RESTORE_UI_PREFAB: {"backup_id": "b" * 32, "expected_version": "c" * 64},
            Endpoints.BATCH_EDIT_UI_PREFAB: {"asset_path": "Assets/Test.prefab", "expected_version": "c" * 64, "operations": [
                {"action": "modify", "index_path": "0", "property": "name", "value": "New"},
                {"action": "add", "parent_path": "0", "node": {"name": "Panel"}},
                {"action": "delete", "index_path": "0/1"},
                {"action": "move", "index_path": "0/1", "parent_path": "0"},
                {"action": "add_component", "index_path": "0", "component": "CanvasGroup"},
            ]},
        }

    def test_all_requests_match_json_schema(self):
        definitions = {}
        for path in (ROOT / "protocol").glob("*.schema.json"):
            definitions.update(json.loads(path.read_text(encoding="utf-8"))["$defs"])

        def local_refs(value):
            if isinstance(value, dict):
                if "$ref" in value:
                    value["$ref"] = "#/$defs/" + value["$ref"].split("#/$defs/")[-1]
                for child in value.values():
                    local_refs(child)
            elif isinstance(value, list):
                for child in value:
                    local_refs(child)
        local_refs(definitions)
        self.assertEqual(set(REQUEST_MODELS), set(self.request_samples()))
        for endpoint, payload in self.request_samples().items():
            with self.subTest(endpoint=endpoint):
                model = REQUEST_MODELS[endpoint]
                request = model.model_validate({"id": "a" * 32, "deadline": 10, **payload})
                Draft202012Validator({"$defs": definitions, "$ref": "#/$defs/" + model.__name__}).validate(request.model_dump())

    def test_remaining_request_constraints(self):
        invalid = {
            Endpoints.CREATE_TMP_FONT: [{"atlas_size": 4096}, {"sampling_point_size": 0}, {"padding": 17}, {"population_mode": "unknown"}, {"characters": "a" * 4097}],
            Endpoints.UPDATE_TMP_FONT_CHARACTERS: [{"mode": "unknown"}, {"characters": ""}],
            Endpoints.FIND_UI_ASSETS: [{"queries": []}, {"queries": [{"asset_type": "unknown"}]}],
            Endpoints.INSPECT_UI_ASSETS: [{"assets": [{"asset_type": "sprite", "asset_path": "Assets/a.png", "max_nodes": 0}]}],
            Endpoints.PREVIEW_UI_PREFAB: [{"width": 63}, {"height": 2049}],
            Endpoints.IMPORT_UI_IMAGE: [{"border": [0, 0, 0]}, {"border": [-1, 0, 0, 0]}, {"pixels_per_unit": 0}, {"filter_mode": "unknown"}],
            Endpoints.LIST_UI_BACKUPS: [{"limit": 101}, {"offset": -1}],
            Endpoints.RESTORE_UI_PREFAB: [{"backup_id": "invalid"}, {"expected_version": "invalid"}],
            Endpoints.BATCH_EDIT_UI_PREFAB: [{"operations": []}, {"operations": [{"action": "unknown"}]},
                {"operations": [{"action": "modify", "index_path": "0", "property": "unknown", "value": "x"}]},
                {"operations": [{"action": "add", "parent_path": "0"}]},
                {"operations": [{"action": "delete", "index_path": "0"}]},
                {"operations": [{"action": "move", "index_path": "0/1", "parent_path": "0", "component": "Button"}]}],
        }
        for endpoint, patches in invalid.items():
            for changes in patches:
                with self.subTest(endpoint=endpoint, changes=changes), self.assertRaises(ValidationError):
                    REQUEST_MODELS[endpoint].model_validate({"id": "a" * 32, "deadline": 10,
                        **self.request_samples()[endpoint], **changes})

    def test_all_remaining_endpoint_wire_models(self):
        async def run():
            seen = []
            def respond(request):
                body = json.loads(request.content)
                self.assertEqual(len(body["id"]), 32)
                self.assertGreater(body["deadline"], 0)
                if request.url.path == Endpoints.BATCH_EDIT_UI_PREFAB:
                    self.assertNotIn("node", body["operations"][0])
                    self.assertFalse(body["operations"][1]["node"]["has_offset_min"])
                    self.assertEqual(body["operations"][1]["node"]["width"], 320)
                if request.url.path == Endpoints.IMPORT_UI_IMAGE:
                    self.assertEqual(body["border"], [0, 0, 0, 0])
                seen.append(request.url.path)
                result = self.response_samples()[request.url.path]
                result["id"] = body["id"]
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=result)
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                for endpoint, payload in self.request_samples().items():
                    await connection.request(endpoint, payload)
            self.assertEqual(len(seen), len(REQUEST_MODELS))
        asyncio.run(run())

    def test_all_endpoint_business_failures_preserve_response(self):
        async def run():
            for endpoint, payload in self.request_samples().items():
                failure = {"ok": False, "instance_id": "test", "error": "Expected business failure"}
                def respond(request):
                    failure["id"] = json.loads(request.content)["id"]
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=failure)
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path.cwd(), "test", client)
                    if endpoint == Endpoints.CREATE_UI_PREFAB:
                        result = await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/__ProtocolFailureTest.prefab', 'spec': payload['spec']})
                    elif endpoint == Endpoints.PREVIEW_UI_PREFAB:
                        from unity_mcp.server import create_server
                        from types import SimpleNamespace
                        tool = create_server()._tool_manager.get_tool("preview_ui_prefab").fn
                        ctx = SimpleNamespace(request_context=SimpleNamespace(lifespan_context={"editor": connection}))
                        result = await tool(payload["asset_path"], ctx)
                    else:
                        result = await connection.request(endpoint, payload)
                    self.assertEqual(result, failure)
        asyncio.run(run())

    def test_visual_properties_and_creation_constraints(self):
        node = {"name": "Grid", "layout": "grid", "grid_constraint": "FixedColumnCount",
                "grid_constraint_count": 4, "grid_cell_size": {"x": 80, "y": 90},
                "image_type": "Filled", "fill_method": "Horizontal", "fill_amount": .4,
                "text_alignment": "TopLeft", "word_wrap": False, "auto_size": True,
                "font_size_min": 10, "font_size_max": 32, "text_overflow": "Ellipsis",
                "line_spacing": 3, "child_control_width": False, "raycast_target": True}
        spec = UiPrefabSpec(name="Canvas", children=[node])
        self.assertEqual(spec.children[0].grid_cell_size.x, 80)
        self.assertEqual(spec.children[0].grid_spacing.x, 0)
        for changes in ({"fill_amount": 1.1},
                        {"grid_constraint_count": 0},
                        {"text_alignment": "invalid"}):
            with self.subTest(changes=changes), self.assertRaises(ValidationError):
                UiPrefabSpec(name="Canvas", children=[{**node, **changes}])
        for prop in ("text_alignment", "word_wrap", "auto_size", "font_size_min", "font_size_max",
                     "text_overflow", "line_spacing", "preserve_aspect", "fill_method", "fill_amount",
                     "fill_origin", "fill_clockwise", "raycast_target", "child_alignment",
                     "child_control_width", "child_force_expand_height", "grid_constraint", "grid_constraint_count"):
            REQUEST_MODELS[Endpoints.BATCH_EDIT_UI_PREFAB].model_validate({
                "id": "a" * 32, "deadline": 1, "asset_path": "Assets/Test.prefab", "expected_version": "a" * 64,
                "operations": [{"action": "modify", "index_path": "0", "property": prop, "value": "test"}]})

    def test_script_and_reference_wire_and_compilation_status(self):
        async def run():
            def respond(request):
                if request.url.path == Endpoints.COMPILATION_STATUS:
                    self.assertEqual(request.method, "GET")
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=self.response_samples()[Endpoints.COMPILATION_STATUS])
                body = json.loads(request.content)
                if request.url.path == Endpoints.BATCH_EDIT_UI_PREFAB:
                    self.assertEqual([op["action"] for op in body["operations"]], ["add_script", "set_reference"])
                    self.assertEqual(body["operations"][1]["component_index"], -1)
                result = self.response_samples()[request.url.path]
                result["id"] = body["id"]
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=result)
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                self.assertFalse((await connection.request(Endpoints.COMPILATION_STATUS))["is_compiling"])
                self.assertTrue((await connection.request(Endpoints.REFRESH_ASSETS, {}))["refresh_requested"])
                result = await connection.request(Endpoints.BATCH_EDIT_UI_PREFAB, {'asset_path': 'Assets/Test.prefab', 'expected_version': 'a' * 64, 'operations': [{'action': 'add_script', 'index_path': '0', 'script_path': 'Assets/DialogView.cs'}, {'action': 'set_reference', 'index_path': '0', 'component_type': 'DialogView', 'field_path': 'button', 'target_path': '0/0', 'target_component': 'UnityEngine.UI.Button'}]})
                self.assertTrue(result["ok"])
        asyncio.run(run())

    def test_runtime_test_failures_and_screenshot_validation(self):
        from unity_mcp.generated.requests import RunUiTestRequest
        base = {"id": "a" * 32, "deadline": 1, "test_instance_id": "owned"}
        for bad in ([], [{"action": "eval"}], [{"action": "wait_condition", "property": "text", "expected": "x", "timeout_seconds": 11}],
                    [{"action": "assert", "property": "active_self", "expected": True}]):
            with self.assertRaises(ValidationError):
                RunUiTestRequest.model_validate({**base, "steps": bad})
        async def run():
            for corrupt in (False, True):
                def respond(request):
                    body = json.loads(request.content)
                    step = {"index": 0, "action": "assert", "path": "0", "ok": False,
                        "expected": "true", "actual": "false", "error": "Assertion failed", "frame_count": 10,
                        "png_base64": "invalid" if corrupt else "", "width": 0, "height": 0, "interaction_mode": "callback"}
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                        "id": body["id"], "instance_id": "test", "ok": False, "error": "Step 0: Assertion failed",
                        "session_id": "play", "test_instance_id": "owned", "passed": False, "failed_step_index": 0,
                        "steps": [step], "interaction_mode": "callback", "saved_scene": False})
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path.cwd(), "test", client)
                    steps = [{"action": "assert", "property": "active_self", "expected": "true"}]
                    if corrupt:
                        with self.assertRaisesRegex(RuntimeError, "screenshot"):
                            await connection.run_ui_test("owned", steps)
                    else:
                        result = await connection.run_ui_test("owned", steps)
                        self.assertFalse(result["passed"])
                        self.assertEqual(result["steps"][0]["actual"], "false")
        asyncio.run(run())

    def test_runtime_screenshot_resolution_request_and_mismatch(self):
        from unity_mcp.generated.requests import ScreenshotUiStep
        self.assertEqual(ScreenshotUiStep(action="screenshot").width, 0)
        self.assertEqual(ScreenshotUiStep(action="screenshot", width=640, height=480).height, 480)
        with self.assertRaises(ValidationError):
            ScreenshotUiStep(action="screenshot", width=4096, height=480)
        async def run():
            seen = []
            def respond(request):
                body = json.loads(request.content)
                step = body["steps"][0]
                seen.append((step["width"], step["height"]))
                if (step["width"] == 0) != (step["height"] == 0):
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                        "id": body["id"], "instance_id": "test", "ok": False,
                        "error": "Screenshot width and height must both be supplied"})
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                    "id": body["id"], "instance_id": "test", "ok": True, "session_id": "play",
                    "test_instance_id": "owned", "passed": True, "failed_step_index": -1, "saved_scene": False,
                    "interaction_mode": "callback", "steps": [{"index": 0, "action": "screenshot", "path": "0", "ok": True,
                        "expected": "", "actual": "", "error": "", "frame_count": 10, "width": 1, "height": 1,
                        "interaction_mode": "callback", "png_base64": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1kAAAAASUVORK5CYII="}]})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                for step in ({"action": "screenshot", "width": 640}, {"action": "screenshot", "height": 480}):
                    result = await connection.run_ui_test("owned", [step])
                    self.assertFalse(result["ok"])
                    self.assertIn("width and height", result["error"])
                self.assertEqual(seen, [(640, 0), (0, 480)])
                result = await connection.run_ui_test("owned", [{"action": "screenshot", "width": 1, "height": 1}])
                self.assertTrue(result["passed"])
                with self.assertRaisesRegex(RuntimeError, "requested resolution"):
                    await connection.run_ui_test("owned", [{"action": "screenshot", "width": 640, "height": 480}])
                self.assertEqual(seen, [(640, 0), (0, 480), (1, 1), (640, 480)])
        asyncio.run(run())

    def test_isolated_play_defaults_and_optional_mount_parent(self):
        envelope = {"id": "a" * 32, "deadline": 1}
        enter = REQUEST_MODELS[Endpoints.ENTER_PLAY_MODE].model_validate(envelope)
        self.assertEqual(enter.mode, "isolated")
        self.assertEqual(enter.reference_width, 1920)
        self.assertEqual(REQUEST_MODELS[Endpoints.ENTER_PLAY_MODE].model_validate({**envelope, "mode": "current"}).mode, "current")
        for changes in ({"mode": "unknown"}, {"reference_width": 0}, {"reference_height": 20000}):
            with self.assertRaises(ValidationError):
                REQUEST_MODELS[Endpoints.ENTER_PLAY_MODE].model_validate({**envelope, **changes})
        mount = REQUEST_MODELS[Endpoints.MOUNT_TEST_PREFAB].model_validate({**envelope, "prefab_path": "Assets/Panel.prefab"})
        self.assertEqual(mount.parent_path, "")

    def test_batch_failure_preserves_structured_response(self):
        async def run():
            for saved in (False, True):
                failure = {
                    "ok": False, "instance_id": "test", "asset_path": "Assets/Test.prefab",
                    "error": "Operation 1 (modify): Target was removed by an earlier operation",
                    "phase": "execute", "failed_operation_index": 1, "failed_action": "modify",
                    "executed_operations": 1, "saved": saved, "backup_id": "test-backup",
                }
                def respond(request):
                    failure["id"] = json.loads(request.content)["id"]
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json=failure)
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(Path.cwd(), "test", client)
                    payload = self.request_samples()[Endpoints.BATCH_EDIT_UI_PREFAB]
                    result = await connection.request(Endpoints.BATCH_EDIT_UI_PREFAB, {'asset_path': payload['asset_path'], 'expected_version': payload['expected_version'], 'operations': payload['operations']})
                    self.assertEqual(result, failure)
        asyncio.run(run())

    def test_prefab_reuse_wire_preserves_explicit_layout_only(self):
        async def run():
            def respond(request):
                body = json.loads(request.content)
                if request.url.path == Endpoints.CREATE_UI_PREFAB:
                    nodes = body["spec"]["children"]
                else:
                    self.assertEqual([op["action"] for op in body["operations"]],
                                     ["duplicate", "instantiate_prefab", "add"])
                    nodes = [body["operations"][2]["node"]]
                for node in nodes:
                    self.assertEqual(node["kind"], "prefab")
                    self.assertFalse(node["has_width"])
                    self.assertFalse(node["has_anchor"])
                    self.assertTrue(node["has_x"])
                    self.assertEqual(node["x"], 0)
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                    "id": body["id"], "instance_id": "test", "ok": False, "error": "Fixture"})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                node = {"name": "Existing", "kind": "prefab", "prefab_path": "Assets/Button.prefab", "x": 0}
                await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/__ReuseTest.prefab', 'spec': {'name': 'Canvas', 'children': [node]}})
                await connection.request(Endpoints.BATCH_EDIT_UI_PREFAB, {'asset_path': 'Assets/Test.prefab', 'expected_version': 'a' * 64, 'operations': [{'action': 'duplicate', 'index_path': '0/0', 'parent_path': '0'}, {'action': 'instantiate_prefab', 'prefab_path': 'Assets/Button.prefab', 'parent_path': '0'}, {'action': 'add', 'parent_path': '0', 'node': node}]})
        asyncio.run(run())
        UiPrefabSpec(name="Canvas", children=[{"name": "Invalid", "kind": "prefab"}])
        UiPrefabSpec(name="Canvas", children=[{"name": "Invalid", "kind": "prefab",
            "prefab_path": "Assets/Button.prefab", "children": [{"name": "Child"}]}])

    def test_asset_and_batch_business_validation_is_deferred(self):
        from unity_mcp.generated.requests import UiAssetInspection, UiComponentAddition
        UiAssetInspection(asset_path="Assets/Test.asset", asset_type="tmp_font", text="\U0001f600" * 513)
        UiComponentAddition(action="add_component", index_path="0", component="ScrollRect")

    def test_font_utf16_error_is_returned_by_unity(self):
        async def run():
            text = "\U0001f600" * 513
            def respond(request):
                body = json.loads(request.content)
                self.assertEqual(body["assets"][0]["text"], text)
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                    "id": body["id"], "instance_id": "test", "ok": True,
                    "results": [{"index": 0, "asset_type": "tmp_font", "ok": False,
                        "error": "Text exceeds 1024 UTF-16 code units"}]})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                result = await connection.request(Endpoints.INSPECT_UI_ASSETS, {'assets': [{'asset_path': 'Assets/Test.asset', 'asset_type': 'tmp_font', 'text': text}]})
                self.assertFalse(result["results"][0]["ok"])
                self.assertEqual(result["results"][0]["error"], "Text exceeds 1024 UTF-16 code units")
        asyncio.run(run())
    def test_generated_files_are_current(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        for path, expected in generator.generate().items():
            self.assertEqual(path.read_text(encoding="utf-8"), expected, str(path))

    def test_check_rejects_stale_files_without_writing(self):
        spec = importlib.util.spec_from_file_location("generator", ROOT / "scripts/generate_protocol.py")
        generator = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(generator)
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "stale.py"
            target.write_text("old", encoding="utf-8")
            with patch.object(generator, "generate", return_value={target: "new"}), \
                    patch.object(generator, "ROOT", Path(directory)), \
                    patch.object(sys, "argv", ["generate_protocol.py", "--check"]), \
                    redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as result:
                generator.main()
            self.assertEqual(result.exception.code, 1)
            self.assertEqual(target.read_text(encoding="utf-8"), "old")

    def test_transport_fields_are_not_mcp_arguments(self):
        from unity_mcp.server import create_server
        tools = asyncio.run(create_server().list_tools())
        tool = next(tool for tool in tools if tool.name == "create_ui_prefab")
        self.assertNotIn("id", tool.inputSchema["properties"])
        self.assertNotIn("deadline", tool.inputSchema["properties"])
        self.assertIn("spec", tool.inputSchema["properties"])

    def test_schema_and_defaults(self):
        spec = UiPrefabSpec(name="Canvas", children=[{"name": "Panel"}])
        request = CreateUiPrefabRequest(id="a" * 32, deadline=10, output_path="Assets/Test.prefab", spec=spec)
        schema = json.loads((ROOT / "protocol/requests.schema.json").read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        # Resolve the actual cross-file reference rather than flattening definitions here.
        from referencing import Registry, Resource
        base = (ROOT / "protocol").as_uri() + "/"
        registry = Registry().with_resources((base + path.name, Resource.from_contents(json.loads(path.read_text(encoding="utf-8"))))
            for path in (ROOT / "protocol").glob("*.schema.json"))
        Draft202012Validator({"$ref": base + "requests.schema.json#/$defs/CreateUiPrefabRequest"}, registry=registry).validate(request.model_dump())
        self.assertEqual(spec.children[0].width, 320)
        self.assertEqual(spec.children[0].anchor, "center")
        self.assertIsNone(spec.children[0].offset_min)

    def test_presence_bits_and_recursive_children(self):
        node = UiNode(name="Panel", children=[UiNode(name="Child", offset_min={"x": 0, "y": 0})])
        wire = prepare_ui_node(node.model_dump())
        self.assertFalse(wire["has_offset_min"])
        self.assertNotIn("offset_min", wire)
        self.assertTrue(wire["children"][0]["has_offset_min"])
        self.assertEqual(wire["children"][0]["offset_min"], {"x": 0, "y": 0})
        self.assertFalse(wire["has_offset_max"])
        self.assertIsNone(node.offset_min)  # Encoding does not mutate the input model.

    def test_constraints(self):
        for patch in ({"width": 0}, {"height": 16385}, {"kind": "unknown"},
                      {"anchor": "unknown"}, {"spacing": float("nan")},
                      {"name": "bad/name"}, {"name": "bad\\name"},
                      {"offset_min": {"x": 0}}, {"unexpected": 1}):
            with self.subTest(patch=patch), self.assertRaises(ValidationError):
                UiNode.model_validate({"name": "Panel", **patch})

    def test_ui_business_rules_are_deferred_to_unity(self):
        for children in ([{"name": "Same"}, {"name": "Same"}],
                         [{"name": "Text", "kind": "text"}],
                         [{"name": "Button", "kind": "button", "children": [{"name": "Child"}]}]):
            UiPrefabSpec(name="Canvas", children=children)
        deep = {"name": "Leaf"}
        for _ in range(13):
            deep = {"name": "Parent", "children": [deep]}
        UiPrefabSpec(name="Canvas", children=[deep])
        UiNodeAddition(action="add", parent_path="0", node={"name": "EmptyText", "kind": "text"})

    def test_invalid_ui_tree_reaches_unity_and_preserves_business_error(self):
        async def run():
            with tempfile.TemporaryDirectory() as directory:
                project = Path(directory)
                (project / "Assets").mkdir()
                def respond(request):
                    body = json.loads(request.content)
                    self.assertEqual(body["spec"]["children"][0]["text"], "")
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                        "id": body["id"], "instance_id": "test", "ok": False, "error": "Text nodes require nonempty text"})
                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(project, "test", client)
                    result = await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/Test.prefab', 'spec': {'name': 'Panel', 'children': [{'name': 'Label', 'kind': 'text'}]}})
                    self.assertFalse(result["ok"])
                    self.assertEqual(result["error"], "Text nodes require nonempty text")
        asyncio.run(run())

    def test_bridge_sends_generated_request(self):
        async def run():
            with tempfile.TemporaryDirectory() as directory:
                project = Path(directory)
                (project / "Assets").mkdir()
                seen = []

                def respond(request):
                    body = json.loads(request.content)
                    seen.append(body)
                    self.assertEqual(request.url.path, Endpoints.CREATE_UI_PREFAB)
                    self.assertEqual(body["spec"]["children"][0]["width"], 320)
                    self.assertFalse(body["spec"]["children"][0]["has_offset_min"])
                    (project / body["output_path"]).write_text("mock prefab")
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                        "id": body["id"], "instance_id": "test", "ok": True,
                        "asset_path": body["output_path"], "node_count": 1,
                    })

                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(project, "test", client)
                    await connection.request(Endpoints.CREATE_UI_PREFAB, {'output_path': 'Assets/Test.prefab', 'spec': {'name': 'Canvas', 'children': [{'name': 'Panel'}]}})
                self.assertEqual(len(seen), 1)
                self.assertEqual(len(seen[0]["id"]), 32)
        asyncio.run(run())


if __name__ == "__main__":
    unittest.main()
