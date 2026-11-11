"""HTTP connection to one project-local Unity Editor instance."""
import json
import time
import uuid
from dataclasses import dataclass
from pathlib import Path

import httpx
from pydantic import ValidationError
from .image_utils import decode_png
from .generated.requests import REQUEST_MODELS, prepare_ui_node as _prepare_ui_node
from .generated.responses import RESPONSE_MODELS
from .generated.routes import Endpoints, HTTP_METHODS

def validate_response(endpoint: str, payload: object) -> dict:
    """Validate the wire shape without coercing types or changing returned data."""
    try:
        RESPONSE_MODELS[endpoint].model_validate(payload)
    except ValidationError as exc:
        errors = exc.errors(include_input=False)
        fields = "; ".join(
            (".".join(map(str, error["loc"])) or "response") + ": " + error["msg"][:200]
            for error in errors[:4]
        )
        raise RuntimeError(f"Invalid Unity response for {endpoint}: {fields}") from exc
    return payload

@dataclass
class EditorConnection:
    project: Path
    instance_id: str
    client: httpx.AsyncClient

    async def project_info(self) -> dict:
        result = await self.request(Endpoints.PROJECT_INFO)
        if result["protocol"] != 2 or Path(result["project_path"]).resolve() != self.project:
            raise RuntimeError("Unity Editor project/protocol mismatch")
        return result

    async def run_ui_test(self, test_instance_id: str, steps: list[dict]) -> dict:
        result = await self.request(Endpoints.RUN_UI_TEST, {"test_instance_id": test_instance_id, "steps": steps})
        if result.get("ok") and (result.get("test_instance_id") != test_instance_id or not result.get("passed")
                                  or len(result.get("steps", [])) != len(steps)):
            raise RuntimeError("Runtime test result mismatch")
        for index, step in enumerate(result.get("steps", [])):
            if index >= len(steps) or step["index"] != index or step["action"] != steps[index]["action"]:
                raise RuntimeError("Runtime test step identity mismatch")
            encoded = step.get("png_base64")
            if encoded:
                if step["action"] != "screenshot":
                    raise RuntimeError("Non-screenshot step returned a screenshot")
                try:
                    decode_png(encoded, step["width"], step["height"])
                except ValueError as exc:
                    raise RuntimeError(f"Invalid runtime screenshot: {exc}") from exc
                if steps[index].get("width", 0) and (step["width"], step["height"]) != (steps[index]["width"], steps[index]["height"]):
                    raise RuntimeError("Runtime screenshot does not match requested resolution")
        return result

    @classmethod
    async def connect(cls, project: Path) -> "EditorConnection":
        discovery = project / "Library/UnityMcp/bridge.json"
        try:
            status = json.loads(discovery.read_text(encoding="utf-8-sig"))
            port = status["port"]
            if type(port) is not int or not 1 <= port <= 65535:
                raise ValueError("Invalid bridge port")
            token = status["token"]
            if not isinstance(token, str) or len(token) < 32:
                raise ValueError("Invalid bridge token")
        except FileNotFoundError as exc:
            raise RuntimeError(f"Unity bridge discovery file is missing for project {project}. "
                "The Editor may be closed or reloading assemblies, the selected project may differ, "
                "or the bridge may not have started. Check the target project and Unity Console; "
                "this does not establish that the package is missing.") from exc
        except OSError as exc:
            raise RuntimeError(f"Cannot read Unity bridge discovery file for project {project}: {type(exc).__name__}") from exc
        except (ValueError, KeyError, TypeError) as exc:
            raise RuntimeError(f"Invalid Unity bridge discovery data for project {project}; "
                "wait for bridge startup to complete and query again.") from exc
        client = httpx.AsyncClient(
            base_url=f"http://127.0.0.1:{port}",
            headers={"Authorization": f"Bearer {token}"},
            timeout=5, trust_env=False, follow_redirects=False,
        )
        try:
            response = await client.request(HTTP_METHODS[Endpoints.PROJECT_INFO], Endpoints.PROJECT_INFO)
            response.raise_for_status()
            info = response.json()
            if not isinstance(info, dict):
                raise RuntimeError("Invalid Unity handshake response")
            validate_response(Endpoints.PROJECT_INFO, info)
            instance_id = info.get("instance_id")
            if not isinstance(instance_id, str) or not instance_id or response.headers.get("X-Unity-Instance") != instance_id:
                raise RuntimeError("Unity handshake instance identity mismatch")
            if info.get("protocol") != 2 or Path(info.get("project_path", "")).resolve() != project:
                raise RuntimeError("Unity Editor project/protocol mismatch")
            if not isinstance(info.get("unity_version"), str) or not info["unity_version"]:
                raise RuntimeError("Unity Editor did not provide its running version")
        except BaseException:
            await client.aclose()
            raise
        return cls(project, instance_id, client)

    async def aclose(self) -> None:
        await self.client.aclose()

    async def request(self, endpoint: str, payload: dict | None = None, *, timeout: float = 20) -> dict:
        """Shared schema validation, Unity encoding, HTTP transport and response validation.

        Never retry mutations: transport failure does not establish that execution failed.
        GET endpoints have no request envelope. POST models come from generated route bindings.
        """
        method = HTTP_METHODS[endpoint]
        request_id = None
        kwargs = {"timeout": 5 if method == "GET" else timeout + 2}
        if method == "GET":
            if payload:
                raise ValueError("GET endpoints do not accept a request body")
        else:
            if not 0 < timeout <= 60:
                raise ValueError("Request timeout must be >0 and <=60 seconds")
            request_id = uuid.uuid4().hex
            model = REQUEST_MODELS[endpoint].model_validate({
                **(payload or {}), "id": request_id, "deadline": time.time() + timeout})
            wire = model.model_dump()
            explicit = model.model_dump(exclude_unset=True)
            if endpoint == Endpoints.CREATE_UI_PREFAB:
                wire["spec"]["children"] = [_prepare_ui_node(node, explicit["spec"]["children"][i])
                    for i, node in enumerate(wire["spec"]["children"])]
            elif endpoint == Endpoints.BATCH_EDIT_UI_PREFAB:
                for i, operation in enumerate(wire["operations"]):
                    if operation["action"] == "add":
                        operation["node"] = _prepare_ui_node(operation["node"], explicit["operations"][i]["node"])
            encoded = json.dumps(wire, ensure_ascii=False).encode("utf-8")
            limit = 12 * 1024 * 1024 if endpoint == Endpoints.IMPORT_UI_IMAGE else 256 * 1024
            if len(encoded) > limit:
                raise ValueError("Unity request exceeds size limit")
            kwargs.update(content=encoded, headers={"Content-Type": "application/json"})
        try:
            response = await self.client.request(method, endpoint, **kwargs)
            self.check_instance(response)
            response.raise_for_status()
            result = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise RuntimeError(f"Unity request failed for {method} {endpoint}; reconnect if Editor reloaded. "
                "Before retrying a write, inspect its target: execution may have completed.") from exc
        if not isinstance(result, dict) or result.get("instance_id") != self.instance_id or (
                request_id is not None and result.get("id") != request_id):
            raise RuntimeError("Unity response identity mismatch")
        validate_response(endpoint, result)
        if request_id is not None and result.get("ok") and "asset_path" in result:
            target = wire.get("asset_path", wire.get("output_path"))
            if target is not None and result["asset_path"] != target:
                raise RuntimeError("Unity response target asset mismatch")
        return result

    def check_instance(self, response: httpx.Response) -> None:
        if response.headers.get("X-Unity-Instance") != self.instance_id:
            raise RuntimeError("Unity Editor instance changed; call connect_project again")
