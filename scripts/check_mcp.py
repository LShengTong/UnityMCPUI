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


@contextmanager
def fixture_editor(project: Path):
    """Editor-info HTTP stub: does not validate Unity compilation or prefab creation."""
    descriptor = {"protocol": 2, "project_path": str(project.resolve()),
                  "instance_id": uuid.uuid4().hex, "token": uuid.uuid4().hex}

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.headers.get("Authorization") != "Bearer " + descriptor["token"]:
                self.send_error(401)
                return
            if self.path not in ("/health", "/project/info"):
                self.send_error(404)
                return
            public = {key: value for key, value in descriptor.items() if key != "token"}
            if self.path == "/project/info":
                public = {"protocol": 2, "instance_id": descriptor["instance_id"],
                          "project_path": str(project.resolve()), "project_name": project.name,
                          "unity_version": "6000.0.1f1", "source": "editor"}
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


async def check(project: Path):
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
                asyncio.run(asyncio.wait_for(check(project), timeout=45))


if __name__ == "__main__":
    main()
