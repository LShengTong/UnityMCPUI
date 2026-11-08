"""HTTP connection to one project-local Unity Editor instance."""
import asyncio
import math
import base64
import binascii
import struct
import json
import time
import uuid
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

import httpx
from .image_import import read_image
from .generated.requests import CreateUiPrefabRequest, REQUEST_MODELS, prepare_ui_node as _prepare_ui_node
from .ui_schema import (
    UiPrefabSpec, UiNodeAddition, UiNodeDeletion, UiNodeMove,
    UiPropertyModification, UiComponentAddition,
)
from .asset_schema import UiAssetInspection


def asset_path(project: Path, value: str, suffix: str | None = None) -> str:
    if "\\" in value or ":" in value:
        raise ValueError("Use an Assets/... path with forward slashes")
    parts = PurePosixPath(value).parts
    if len(parts) < 2 or parts[0] != "Assets" or any(p in (".", "..", "") for p in value.split("/")):
        raise ValueError("Asset path must stay inside Assets/")
    if suffix and not value.endswith(suffix):
        raise ValueError(f"Asset path must end with {suffix}")
    if not (project / value).resolve().is_relative_to((project / "Assets").resolve()):
        raise ValueError("Asset path escapes Assets/")
    return value


@dataclass
class EditorConnection:
    project: Path
    instance_id: str
    client: httpx.AsyncClient

    @classmethod
    async def connect(cls, project: Path) -> "EditorConnection":
        try:
            status = json.loads((project / "Library/UnityMcp/bridge.json").read_text(encoding="utf-8-sig"))
            port = status["port"]
            if type(port) is not int or not 1 <= port <= 65535:
                raise ValueError("Invalid bridge port")
            token = status["token"]
            if not isinstance(token, str) or len(token) < 32:
                raise ValueError("Invalid bridge token")
        except (OSError, ValueError, KeyError, TypeError) as exc:
            raise RuntimeError("Unity bridge unavailable. Install unity-package in this project, open Editor and wait for compilation.") from exc
        client = httpx.AsyncClient(
            base_url=f"http://127.0.0.1:{port}",
            headers={"Authorization": f"Bearer {token}"},
            timeout=5, trust_env=False, follow_redirects=False,
        )
        try:
            response = await client.get("/project/info")
            response.raise_for_status()
            info = response.json()
            if not isinstance(info, dict):
                raise RuntimeError("Invalid Unity handshake response")
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

    async def project_info(self) -> dict:
        try:
            response = await self.client.get("/project/info")
            self.check_instance(response)
            response.raise_for_status()
            info = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise RuntimeError("Cannot read project info from Unity Editor; reconnect if Editor reloaded.") from exc
        if not isinstance(info, dict) or info.get("instance_id") != self.instance_id:
            raise RuntimeError("Project info identity mismatch; call connect_project again")
        return info

    def check_instance(self, response: httpx.Response) -> None:
        if response.headers.get("X-Unity-Instance") != self.instance_id:
            raise RuntimeError("Unity Editor instance changed; call connect_project again")

    async def aclose(self) -> None:
        await self.client.aclose()

    async def create_prefab(self, output_path: str, spec: dict, timeout: float = 20) -> dict:
        asset_path(self.project, output_path, ".prefab")
        if (self.project / output_path).exists() or (self.project / (output_path + ".meta")).exists():
            raise ValueError("Output already exists; choose a new prefab path")
        spec = UiPrefabSpec.model_validate(spec).model_dump()
        def validate(nodes):
            for node in nodes:
                for key in ("font_path", "sprite_path"):
                    if node[key]:
                        asset_path(self.project, node[key])
                validate(node["children"])
        validate(spec["children"])
        request_id = uuid.uuid4().hex
        request = CreateUiPrefabRequest(
            id=request_id, deadline=time.time() + timeout,
            output_path=output_path, spec=spec,
        )
        payload = request.model_dump()
        payload["spec"]["children"] = [_prepare_ui_node(node) for node in payload["spec"]["children"]]
        encoded = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        if len(encoded) > 256 * 1024:
            raise ValueError("UI request exceeds 256 KiB")
        try:
            response = await self.client.post(
                "/ui/prefabs", content=encoded,
                headers={"Content-Type": "application/json"}, timeout=timeout + 2,
            )
            self.check_instance(response)
            response.raise_for_status()
            result = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise RuntimeError("Unity request failed. Check the output asset before retrying; an in-flight operation may have completed. Reconnect if Editor reloaded.") from exc
        if not isinstance(result, dict) or result.get("id") != request_id or result.get("instance_id") != self.instance_id:
            raise RuntimeError("Unity response identity mismatch")
        if not result.get("ok"):
            raise RuntimeError(result.get("error", "Unity prefab creation failed"))
        if result.get("asset_path") != output_path or not (self.project / output_path).is_file():
            raise RuntimeError("Unity reported success but output asset could not be verified")
        return result

    async def _request_ui_query(self, endpoint: str, payload: dict) -> dict:
        request_id = uuid.uuid4().hex
        model = REQUEST_MODELS[endpoint]
        wire = model.model_validate({**payload, "id": request_id, "deadline": time.time() + 20}).model_dump()
        if endpoint == "/ui/prefabs/batch":
            for operation in wire["operations"]:
                if operation["action"] == "add":
                    operation["node"] = _prepare_ui_node(operation["node"])
        limit = 12 * 1024 * 1024 if endpoint == "/ui/images/import" else 256 * 1024
        if len(json.dumps(wire, ensure_ascii=False).encode("utf-8")) > limit:
            raise ValueError("UI request exceeds size limit")
        try:
            response = await self.client.post(endpoint, json=wire, timeout=22)
            self.check_instance(response)
            response.raise_for_status()
            result = response.json()
        except (httpx.HTTPError, ValueError) as exc:
            raise RuntimeError("Unity UI query failed; reconnect if Editor reloaded.") from exc
        if not isinstance(result, dict) or result.get("id") != request_id or result.get("instance_id") != self.instance_id:
            raise RuntimeError("Unity query response identity mismatch")
        if not result.get("ok"):
            message = result.get("error") or "Unity UI query failed"
            backup_id = result.get("backup_id")
            if backup_id and backup_id not in message:
                message += f"; backup_id={backup_id}"
            raise RuntimeError(message)
        return result

    async def preview_ui_prefab(self, prefab_path: str, width: int = 1280, height: int = 720,
                                background: str = "#202020") -> bytes:
        asset_path(self.project, prefab_path, ".prefab")
        if not 64 <= width <= 2048 or not 64 <= height <= 2048:
            raise ValueError("Image dimensions must be 64..2048")
        result = await self._request_ui_query("/ui/prefabs/preview", {
            "asset_path": prefab_path, "width": width, "height": height, "background": background,
        })
        encoded = result.get("png_base64", "")
        if not isinstance(encoded, str) or len(encoded) > 16 * 1024 * 1024:
            raise RuntimeError("Invalid preview image size")
        try:
            png = base64.b64decode(encoded, validate=True)
        except (ValueError, binascii.Error) as exc:
            raise RuntimeError("Invalid preview image encoding") from exc
        if (result.get("asset_path") != prefab_path or len(png) < 24
                or png[:8] != b"\x89PNG\r\n\x1a\n" or png[12:16] != b"IHDR"
                or struct.unpack(">II", png[16:24]) != (width, height)
                or (result.get("width"), result.get("height")) != (width, height)):
            raise RuntimeError("Preview image dimensions or identity mismatch")
        return png

    async def import_ui_image(self, source_path: str, output_path: str,
                              border: list[int] | None = None, pixels_per_unit: float = 100,
                              filter_mode: str = "bilinear") -> dict:
        asset_path(self.project, output_path)
        border = [0, 0, 0, 0] if border is None else border
        if len(border) != 4 or any(type(n) is not int or n < 0 for n in border):
            raise ValueError("border must be four nonnegative integers: left, bottom, right, top")
        if not math.isfinite(pixels_per_unit) or not 0 < pixels_per_unit <= 10000:
            raise ValueError("pixels_per_unit must be >0 and <=10000")
        if filter_mode not in ("point", "bilinear"):
            raise ValueError("filter_mode must be point or bilinear")
        if (self.project / output_path).exists() or (self.project / (output_path + ".meta")).exists():
            raise ValueError("Output already exists; choose a new image asset path")
        data, kind, width, height = await asyncio.to_thread(read_image, source_path)
        extension = Path(output_path).suffix.lower()
        if extension not in ((".png",) if kind == "png" else (".jpg", ".jpeg")):
            raise ValueError("Output extension must match PNG/JPEG image contents")
        if border[0] + border[2] > width or border[1] + border[3] > height:
            raise ValueError("Border exceeds source image dimensions")
        try:
            result = await self._request_ui_query("/ui/images/import", {
                "asset_path": output_path, "image_base64": base64.b64encode(data).decode("ascii"),
                "border": border, "pixels_per_unit": pixels_per_unit, "filter_mode": filter_mode,
            })
        except RuntimeError as exc:
            raise RuntimeError(f"{exc} Check the target asset before retrying; import may have completed.") from exc
        if result.get("asset_path") != output_path or not result.get("guid"):
            raise RuntimeError("Imported image response mismatch")
        return result

    async def batch_edit_ui_prefab(self, prefab_path: str, expected_version: str,
                                    operations: list[dict]) -> dict:
        asset_path(self.project, prefab_path, ".prefab")
        if len(expected_version) != 64 or any(c not in "0123456789abcdef" for c in expected_version):
            raise ValueError("Use the version returned by inspect_ui_assets (ui_prefab: prefab.version)")
        if not 1 <= len(operations) <= 128:
            raise ValueError("Provide 1..128 batch operations")
        operation_types = {
            "add": UiNodeAddition, "delete": UiNodeDeletion, "move": UiNodeMove,
            "modify": UiPropertyModification, "add_component": UiComponentAddition,
        }
        prepared = []
        for operation in operations:
            operation_type = operation_types.get(operation.get("action"))
            if operation_type is None:
                raise ValueError("Unknown operation action")
            prepared.append(operation_type.model_validate(operation).model_dump())
        payload = {"asset_path": prefab_path, "expected_version": expected_version, "operations": prepared}
        if len(json.dumps(payload).encode("utf-8")) > 250 * 1024:
            raise ValueError("Batch request exceeds size limit")
        try:
            return await self._request_ui_query("/ui/prefabs/batch", payload)
        except RuntimeError as exc:
            raise RuntimeError(f"{exc} Inspect the prefab before retrying; saving may have completed.") from exc

    async def find_ui_assets(self, queries: list[dict]) -> dict:
        if not 1 <= len(queries) <= 16:
            raise ValueError("Provide 1..16 search queries")
        payload = {"queries": queries}
        if len(json.dumps(payload).encode("utf-8")) > 250 * 1024:
            raise ValueError("Asset search request exceeds size limit")
        return await self._request_ui_query("/ui/assets/find", payload)

    async def inspect_ui_assets(self, assets: list[dict]) -> dict:
        if not 1 <= len(assets) <= 32:
            raise ValueError("Provide 1..32 assets")
        payload = {"assets": [UiAssetInspection.model_validate(asset).model_dump() for asset in assets]}
        if len(json.dumps(payload).encode("utf-8")) > 250 * 1024:
            raise ValueError("Asset inspection request exceeds size limit")
        return await self._request_ui_query("/ui/assets/inspect", payload)

    async def list_ui_backups(self, prefab_path: str, limit: int = 20, offset: int = 0) -> dict:
        asset_path(self.project, prefab_path, ".prefab")
        if not 1 <= limit <= 100 or offset < 0:
            raise ValueError("limit must be 1..100 and offset >= 0")
        return await self._request_ui_query("/ui/backups/list", {
            "asset_path": prefab_path, "limit": limit, "offset": offset,
        })

    async def restore_ui_prefab(self, backup_id: str, expected_version: str) -> dict:
        if len(backup_id) != 32 or any(c not in "0123456789abcdef" for c in backup_id):
            raise ValueError("Use a backup_id returned by editing or list_ui_backups")
        if len(expected_version) != 64 or any(c not in "0123456789abcdef" for c in expected_version):
            raise ValueError("Use the current version returned by inspect_ui_assets (ui_prefab: prefab.version)")
        try:
            return await self._request_ui_query("/ui/backups/restore", {
                "backup_id": backup_id, "expected_version": expected_version,
            })
        except RuntimeError as exc:
            raise RuntimeError(f"{exc} Inspect the prefab and backup list before retrying; restoring may have completed.") from exc
