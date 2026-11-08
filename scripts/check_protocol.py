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

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))

import httpx
from pydantic import ValidationError
from jsonschema import Draft202012Validator
from unity_mcp.generated.requests import CreateUiPrefabRequest, UiNode, REQUEST_MODELS, prepare_ui_node
from unity_mcp.ui_schema import UiPrefabSpec, UiNodeAddition
from unity_mcp.bridge import EditorConnection


class ProtocolTests(unittest.TestCase):
    @staticmethod
    def request_samples():
        return {
            "/ui/prefabs": {"output_path": "Assets/Test.prefab", "spec": {"name": "Canvas", "children": [{"name": "Panel"}]}},
            "/ui/assets/find": {"queries": [{"asset_type": "sprite"}]},
            "/ui/assets/inspect": {"assets": [{"asset_type": "ui_prefab", "asset_path": "Assets/Test.prefab"}]},
            "/ui/prefabs/preview": {"asset_path": "Assets/Test.prefab"},
            "/ui/images/import": {"asset_path": "Assets/Image.png", "image_base64": "AA=="},
            "/ui/backups/list": {"asset_path": "Assets/Test.prefab"},
            "/ui/backups/restore": {"backup_id": "b" * 32, "expected_version": "c" * 64},
            "/ui/prefabs/batch": {"asset_path": "Assets/Test.prefab", "expected_version": "c" * 64, "operations": [
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
            "/ui/assets/find": [{"queries": []}, {"queries": [{"asset_type": "unknown"}]}],
            "/ui/assets/inspect": [{"assets": [{"asset_type": "sprite", "asset_path": "Assets/a.png", "max_nodes": 0}]}],
            "/ui/prefabs/preview": [{"width": 63}, {"height": 2049}],
            "/ui/images/import": [{"border": [0, 0, 0]}, {"border": [-1, 0, 0, 0]}, {"pixels_per_unit": 0}, {"filter_mode": "unknown"}],
            "/ui/backups/list": [{"limit": 101}, {"offset": -1}],
            "/ui/backups/restore": [{"backup_id": "invalid"}, {"expected_version": "invalid"}],
            "/ui/prefabs/batch": [{"operations": []}, {"operations": [{"action": "unknown"}]},
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
                if request.url.path == "/ui/prefabs/batch":
                    self.assertNotIn("node", body["operations"][0])
                    self.assertFalse(body["operations"][1]["node"]["has_offset_min"])
                    self.assertEqual(body["operations"][1]["node"]["width"], 320)
                if request.url.path == "/ui/images/import":
                    self.assertEqual(body["border"], [0, 0, 0, 0])
                seen.append(request.url.path)
                return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={"id": body["id"], "instance_id": "test", "ok": True})
            async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                connection = EditorConnection(Path.cwd(), "test", client)
                for endpoint, payload in self.request_samples().items():
                    if endpoint != "/ui/prefabs":
                        await connection._request_ui_query(endpoint, payload)
            self.assertEqual(len(seen), 7)
        asyncio.run(run())

    def test_asset_and_batch_business_validation(self):
        from unity_mcp.asset_schema import UiAssetInspection
        from unity_mcp.ui_schema import UiComponentAddition
        with self.assertRaises(ValidationError):
            UiAssetInspection(asset_path="Assets/Test.prefab", asset_type="ui_prefab", text="\U0001f600" * 513)
        with self.assertRaises(ValidationError):
            UiComponentAddition(action="add_component", index_path="0", component="ScrollRect")
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
        schema = json.loads((ROOT / "protocol/create_ui_prefab.schema.json").read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        Draft202012Validator(schema).validate(request.model_dump())
        self.assertEqual(spec.reference_width, 1920)
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

    def test_business_validators_remain(self):
        for children in ([{"name": "Same"}, {"name": "Same"}],
                         [{"name": "Text", "kind": "text"}],
                         [{"name": "Button", "kind": "button", "children": [{"name": "Child"}]}]):
            with self.assertRaises(ValidationError):
                UiPrefabSpec(name="Canvas", children=children)
        deep = {"name": "Leaf"}
        for _ in range(13):
            deep = {"name": "Parent", "children": [deep]}
        with self.assertRaises(ValidationError):
            UiPrefabSpec(name="Canvas", children=[deep])
        with self.assertRaises(ValidationError):
            UiNodeAddition(action="add", parent_path="0", node={"name": "EmptyText", "kind": "text"})

    def test_bridge_sends_generated_request(self):
        async def run():
            with tempfile.TemporaryDirectory() as directory:
                project = Path(directory)
                (project / "Assets").mkdir()
                seen = []

                def respond(request):
                    body = json.loads(request.content)
                    seen.append(body)
                    self.assertEqual(request.url.path, "/ui/prefabs")
                    self.assertEqual(body["spec"]["children"][0]["width"], 320)
                    self.assertFalse(body["spec"]["children"][0]["has_offset_min"])
                    (project / body["output_path"]).write_text("mock prefab")
                    return httpx.Response(200, headers={"X-Unity-Instance": "test"}, json={
                        "id": body["id"], "instance_id": "test", "ok": True,
                        "asset_path": body["output_path"],
                    })

                async with httpx.AsyncClient(base_url="http://127.0.0.1", transport=httpx.MockTransport(respond)) as client:
                    connection = EditorConnection(project, "test", client)
                    await connection.create_prefab("Assets/Test.prefab", {"name": "Canvas", "children": [{"name": "Panel"}]})
                self.assertEqual(len(seen), 1)
                self.assertEqual(len(seen[0]["id"]), 32)
        asyncio.run(run())


if __name__ == "__main__":
    unittest.main()
