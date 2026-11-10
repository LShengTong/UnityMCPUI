"""Check MCP selection using a local HTTP stub, or a real Editor with --project."""
import argparse
import asyncio
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sys
import tempfile
import threading
import uuid

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
from unity_mcp.generated.routes import Endpoints


@contextmanager
def fixture_editor(project: Path, transient_status_failure: bool = False):
    """Editor-info HTTP stub: does not validate Unity compilation or prefab creation."""
    descriptor = {"protocol": 2, "project_path": str(project.resolve()),
                  "instance_id": uuid.uuid4().hex, "token": uuid.uuid4().hex}
    failed_status_paths = set()

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            if self.headers.get("Authorization") != "Bearer " + descriptor["token"]:
                self.send_error(401)
                return
            request = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            if self.path == Endpoints.ENTER_PLAY_MODE:
                assert request["mode"] in ("isolated", "current")
                assert request["reference_width"] > 0 and request["reference_height"] > 0
            body = json.dumps({"id": request["id"], "instance_id": descriptor["instance_id"],
                               "ok": False, "error": "Fixture business failure"}).encode()
            if self.path == Endpoints.REFRESH_ASSETS:
                body = json.dumps({"id": request["id"], "instance_id": descriptor["instance_id"],
                    "ok": True, "refresh_requested": True, "refresh_id": "fixture-refresh"}).encode()
            if self.path == Endpoints.RUN_UI_TEST:
                # Tiny valid PNG; verifies MCP image conversion, not native rendering.
                png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1kAAAAASUVORK5CYII="
                results = [{"index": i, "action": step["action"], "path": step.get("path", "0"), "ok": True,
                    "expected": step.get("expected", ""), "actual": step.get("expected", ""), "error": "", "frame_count": 10,
                    "png_base64": png if step["action"] == "screenshot" else "", "width": 1 if step["action"] == "screenshot" else 0,
                    "height": 1 if step["action"] == "screenshot" else 0, "interaction_mode": "callback"} for i, step in enumerate(request["steps"])]
                body = json.dumps({"id": request["id"], "instance_id": descriptor["instance_id"], "ok": True,
                    "session_id": "fixture-play", "test_instance_id": request["test_instance_id"], "passed": True,
                    "failed_step_index": -1, "steps": results, "interaction_mode": "callback", "saved_scene": False}).encode()
            self.send_response(200)
            self.send_header("X-Unity-Instance", descriptor["instance_id"])
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            # Handshake can succeed just before another reload interrupts the first status read.
            if transient_status_failure and self.path in (Endpoints.COMPILATION_STATUS, Endpoints.PLAY_STATUS) and self.path not in failed_status_paths:
                failed_status_paths.add(self.path)
                self.send_error(503)
                return
            # An older fixture instance stops serving once discovery points at its replacement.
            if json.loads(record.read_text(encoding="utf-8"))["port"] != descriptor["port"]:
                self.send_error(503)
                return
            if self.headers.get("Authorization") != "Bearer " + descriptor["token"]:
                self.send_error(401)
                return
            if self.path not in (Endpoints.HEALTH, Endpoints.PROJECT_INFO, Endpoints.COMPILATION_STATUS, Endpoints.PLAY_STATUS):
                self.send_error(404)
                return
            public = {key: value for key, value in descriptor.items() if key != "token"}
            if self.path == Endpoints.PROJECT_INFO:
                public = {"protocol": 2, "instance_id": descriptor["instance_id"],
                          "project_path": str(project.resolve()), "project_name": project.name,
                          "unity_version": "6000.0.1f1", "source": "editor"}
            if self.path == Endpoints.COMPILATION_STATUS:
                public = {"instance_id": descriptor["instance_id"], "is_compiling": False, "is_updating": False,
                    "compilation_revision": "fixture-compilation", "has_compilation_result": True,
                    "errors": [], "errors_truncated": False, "refresh_id": "fixture-refresh", "refresh_pending": False}
            if self.path == Endpoints.PLAY_STATUS:
                public = {"instance_id": descriptor["instance_id"], "is_playing": True, "is_changing": False,
                    "is_paused": False, "session_id": "fixture-play", "frame_count": 10, "test_running": False, "test_instances": []}
            body = json.dumps(public).encode()
            self.send_response(200)
            self.send_header("X-Unity-Instance", descriptor["instance_id"])
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    descriptor["port"] = server.server_port
    record = project / "Library/UnityMcp/bridge.json"
    record.parent.mkdir(parents=True, exist_ok=True)
    record.write_text(json.dumps({"port": server.server_port, "token": descriptor["token"]}), encoding="utf-8")
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield
    finally:
        server.shutdown()
        server.server_close()
        thread.join()
        record.unlink(missing_ok=True)


def make_project(project: Path):
    for directory in ("Assets", "Packages", "ProjectSettings"):
        (project / directory).mkdir()
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.0.1f1\n", encoding="utf-8")
    (project / "Packages/manifest.json").write_text('{"dependencies":{}}', encoding="utf-8")


async def check(project: Path, mock_failures: bool = False):
    project = project.resolve()
    parameters = StdioServerParameters(command=sys.executable, args=["-m", "unity_mcp.server"])
    async with stdio_client(parameters) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = await session.list_tools()
            assert {"connect_project", "unity_project_info", "create_ui_prefab"} <= {tool.name for tool in tools.tools}
            assert (await session.call_tool("unity_project_info", {})).isError
            connected = await session.call_tool("connect_project", {"project_path": str(project)})
            assert not connected.isError, connected
            assert connected.content[0].text == "Connected to Unity Editor."
            initial_info = await session.call_tool("unity_project_info", {})
            assert not initial_info.isError, initial_info
            original_instance = json.loads(initial_info.content[0].text)["instance_id"]
            assert (await session.call_tool("connect_project", {"project_path": "relative/path"})).isError
            with tempfile.TemporaryDirectory(prefix="unity mcp alternate ") as temporary:
                alternate = Path(temporary)
                make_project(alternate)
                # Valid project with no running bridge must not replace the existing connection.
                assert (await session.call_tool("connect_project", {"project_path": str(alternate)})).isError
                with fixture_editor(alternate):
                    switched = await session.call_tool("connect_project", {"project_path": str(alternate)})
                    assert not switched.isError, switched
                restored = await session.call_tool("connect_project", {"project_path": str(project)})
                assert not restored.isError, restored
            async with stdio_client(parameters) as (other_read, other_write):
                async with ClientSession(other_read, other_write) as other:
                    await other.initialize()
                    assert (await other.call_tool("unity_project_info", {})).isError
            result = await session.call_tool("unity_project_info", {})
            assert not result.isError, result
            data = json.loads(result.content[0].text)
            assert Path(data["project_path"]) == project
            assert data["source"] == "editor"
            assert data["instance_id"] == original_instance
            assert data["project_name"] == project.name
            assert isinstance(data["unity_version"], str) and data["unity_version"]
            assert "dependencies" not in data
            if mock_failures:
                play = await session.call_tool("get_play_status", {"wait_seconds": 1, "wait_for": "playing"})
                assert not play.isError, play
                assert json.loads(play.content[0].text)["is_playing"] is True
                runtime = await session.call_tool("run_ui_test", {"test_instance_id": "owned", "steps": [
                    {"action": "assert", "property": "active_self", "expected": "true"}, {"action": "screenshot", "width": 1, "height": 1}]})
                assert not runtime.isError, runtime
                assert json.loads(runtime.content[0].text)["passed"] is True
                assert runtime.content[1].type == "image"
                for name, args in [("enter_play_mode", {}), ("enter_play_mode", {"mode": "current"}), ("exit_play_mode", {}), ("inspect_runtime_ui", {}),
                                   ("mount_test_prefab", {"prefab_path": "Assets/Test.prefab"}),
                                   ("mount_test_prefab", {"prefab_path": "Assets/Test.prefab", "parent_path": "Canvas"}),
                                   ("clear_test_instances", {}),
                                   ("create_tmp_font", {"source_font_path": "Assets/Font.ttf", "asset_path": "Assets/Font.asset", "characters": "ABC"}),
                                   ("update_tmp_font_characters", {"asset_path": "Assets/Font.asset", "characters": "XYZ", "mode": "replace"})]:
                    result = await session.call_tool(name, args)
                    assert not result.isError, result
                    assert json.loads(result.content[0].text)["ok"] is False
                status = await session.call_tool("get_compilation_status", {"wait_seconds": 1})
                assert not status.isError, status
                assert json.loads(status.content[0].text)["has_compilation_result"] is True
                refresh = await session.call_tool("refresh_assets", {})
                assert not refresh.isError, refresh
                assert json.loads(refresh.content[0].text)["refresh_requested"] is True
                for tool, arguments in [
                    ("create_ui_prefab", {"output_path": "Assets/New.prefab", "spec": {"name": "Canvas", "children": [
                        {"name": "Button", "kind": "prefab", "prefab_path": "Assets/Button.prefab"}]}}),
                    ("preview_ui_prefab", {"asset_path": "Assets/Test.prefab"}),
                    ("batch_edit_ui_prefab", {"asset_path": "Assets/Test.prefab", "expected_version": "a" * 64,
                        "operations": [{"action": "duplicate", "index_path": "0/0", "parent_path": "0"},
                            {"action": "instantiate_prefab", "prefab_path": "Assets/Button.prefab", "parent_path": "0"},
                            {"action": "add_script", "index_path": "0", "script_path": "Assets/DialogView.cs"},
                            {"action": "set_reference", "index_path": "0", "component_type": "DialogView",
                                "field_path": "button", "target_path": "0/0", "target_component": "UnityEngine.UI.Button"}]}),
                ]:
                    failed = await session.call_tool(tool, arguments)
                    assert not failed.isError, failed
                    body = json.loads(failed.content[0].text)
                    assert body["ok"] is False and body["error"] == "Fixture business failure", body
                # Simulate a domain reload: discovery now names a new instance/token/port.
                for status_tool in ("get_compilation_status", "get_play_status"):
                    with fixture_editor(project, transient_status_failure=True):
                        reloaded = await session.call_tool(status_tool, {"wait_seconds": 5})
                        assert not reloaded.isError, reloaded
                        assert json.loads(reloaded.content[0].text)["instance_id"] != original_instance
                        repeated = await session.call_tool(status_tool, {"wait_seconds": 1})
                        assert not repeated.isError, repeated
                        assert json.loads(repeated.content[0].text)["instance_id"] == json.loads(reloaded.content[0].text)["instance_id"]
            print("PASS: MCP tools, HTTP connection, switching, failed connection recovery, process isolation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, help="Real project with running Editor bridge; omit for an HTTP stub")
    args = parser.parse_args()
    if args.project:
        asyncio.run(asyncio.wait_for(check(args.project), timeout=45))
    else:
        with tempfile.TemporaryDirectory(prefix="unity mcp check ") as temporary:
            project = Path(temporary)
            make_project(project)
            with fixture_editor(project):
                asyncio.run(asyncio.wait_for(check(project, mock_failures=True), timeout=45))


if __name__ == "__main__":
    main()
