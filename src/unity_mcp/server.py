"""Session-based Unity MCP server with a local Unity Editor HTTP connection."""

import argparse
import asyncio
import time
import httpx
import json
from contextlib import asynccontextmanager
from pathlib import Path
from typing import AsyncIterator, Annotated, Any, Literal

from mcp.server.fastmcp import Context, FastMCP, Image
from mcp.types import ToolAnnotations, TextContent, Tool as MCPTool
from pydantic import Field
from .generated.requests import UiPrefabSpec, UiNodeAddition, UiNodeDeletion, UiNodeMove, UiPropertyModification, UiComponentAddition, UiNodeDuplication, UiPrefabInstantiation, UiScriptAddition, UiReferenceAssignment
from .bridge import EditorConnection
from .image_utils import read_image, encode_base64, decode_base64, decode_png
from .generated.requests import UiAssetSearch, UiAssetInspection
from .generated.requests import UiTestStep
from .generated.routes import Endpoints


def without_schema_titles(schema: dict) -> dict:
    """Remove presentation titles while preserving property names and validation keywords."""
    result = {key: value for key, value in schema.items() if key != "title"}
    for key in ("properties", "$defs", "definitions", "patternProperties", "dependentSchemas"):
        if isinstance(result.get(key), dict):
            result[key] = {name: without_schema_titles(value) if isinstance(value, dict) else value
                           for name, value in result[key].items()}
    for key in ("items", "additionalProperties", "not", "if", "then", "else", "contains",
                "propertyNames", "unevaluatedProperties"):
        if isinstance(result.get(key), dict):
            result[key] = without_schema_titles(result[key])
    for key in ("allOf", "anyOf", "oneOf", "prefixItems"):
        if isinstance(result.get(key), list):
            result[key] = [without_schema_titles(value) if isinstance(value, dict) else value
                           for value in result[key]]
    return result


class CompactSchemaFastMCP(FastMCP):
    async def list_tools(self) -> list[MCPTool]:
        tools = await super().list_tools()
        return [tool.model_copy(update={"inputSchema": without_schema_titles(tool.inputSchema)})
                for tool in tools]


def require_editor(ctx: Context) -> EditorConnection:
    editor = ctx.request_context.lifespan_context["editor"]
    if editor is None:
        raise ValueError("No Editor connection. Call connect_project first.")
    return editor


def create_server() -> FastMCP:
    @asynccontextmanager
    async def lifespan(server: FastMCP) -> AsyncIterator[dict[str, EditorConnection | None]]:
        # One state per stdio connection/process; never persist it globally or on disk.
        state: dict[str, EditorConnection | None] = {"editor": None}
        try:
            yield state
        finally:
            editor = state["editor"]
            if editor is not None:
                await editor.aclose()

    mcp = CompactSchemaFastMCP(
        "Unity MCP Local",
        lifespan=lifespan,
        instructions=(
            "Unity business failures return structured results with ok=false; check ok before treating a result as success. "
            "Connection, transport and validation failures are tool errors. "
            "Before using project tools, call connect_project with the absolute Unity project "
            "root from the user's current workspace context. Do not guess a path when unclear. "
            "Reuse the selected project in this session; reconnect when the target changes. "
            "For UI requests, translate the user description into a UiPrefabSpec and call create_ui_prefab. Use find_ui_assets and inspect_ui_assets with asset_type=ui_prefab to inspect existing UI conventions and output paths first. Editor bridge must be installed for creation."
        ),
    )

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def set_game_view_resolution(ctx: Context,
            width: Annotated[int, Field(ge=0, le=2048)] = 0,
            height: Annotated[int, Field(ge=0, le=2048)] = 0,
            preset_index: Annotated[int, Field(ge=-1)] = -1, preset_group: str = "") -> dict:
        """Set Game view resolution in Edit or Play, then wait for stable dimensions.

        Supply width/height together (10..2048) to reuse or create a fixed preset, or preset_index
        from get_game_view_resolution with its preset_group. Indices belong to the current platform
        group. Created custom presets remain in Unity preferences. The first set records the original
        selection across subsequent sets and assembly reloads. Restore explicitly after testing;
        leaving Play also restores it. Uses version-sensitive Editor Game view APIs.
        """
        return await require_editor(ctx).request(Endpoints.GAME_VIEW_RESOLUTION, {
            "action": "set", "width": width, "height": height,
            "preset_index": preset_index, "preset_group": preset_group})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False))
    async def get_game_view_resolution(ctx: Context) -> dict:
        """Query an open Game view's actual dimensions, selected index, platform group and presets.

        Also reports can_restore and is_playing. Presets include fixed sizes and aspect ratios,
        including Free Aspect. Query before using a preset index. No Game view window is created.
        """
        return await require_editor(ctx).request(Endpoints.GAME_VIEW_RESOLUTION, {"action": "query"})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def restore_game_view_resolution(ctx: Context) -> dict:
        """Restore the original Game view preset recorded by the first set, including Free Aspect.

        Waits for stable dimensions in Edit/Play. On failure query actual dimensions and can_restore.
        Missing windows, removed presets or changed platform groups are reported as failures.
        """
        return await require_editor(ctx).request(Endpoints.GAME_VIEW_RESOLUTION, {"action": "restore"})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def create_ui_test_scene(scene_path: str, ctx: Context, prefab_path: str = "",
                                   reference_width: Annotated[float, Field(ge=1, le=16384)] = 1920,
                                   reference_height: Annotated[float, Field(ge=1, le=16384)] = 1080) -> dict:
        """Create, save and open an additive Edit-mode UI test scene at a new Assets/*.unity path.

        The output folder must exist. Creates an Overlay Canvas with CanvasScaler/GraphicRaycaster,
        a stretched Content container, EventSystem with the project's input module, and background
        camera. Optionally instantiate an existing UI prefab under Content, retaining prefab linkage.
        The prefab root must be RectTransform without Canvas. Returns scene_path, parent_path,
        prefab_instance_path and saved. The scene stays open and becomes active for further editing.
        Exit Play first. Output is persistent and is never overwritten. After a timeout check the
        output asset before retrying. Existing open scenes retain their unsaved changes.
        """
        return await require_editor(ctx).request(Endpoints.CREATE_UI_TEST_SCENE, {
            "scene_path": scene_path, "prefab_path": prefab_path,
            "reference_width": reference_width, "reference_height": reference_height})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def create_tmp_font(source_font_path: str, asset_path: str, ctx: Context,
                              characters: Annotated[str, Field(max_length=4096)] = "",
                              population_mode: Literal["static", "dynamic"] = "dynamic",
                              sampling_point_size: Annotated[int, Field(ge=16, le=128)] = 48,
                              atlas_size: Literal[512, 1024, 2048] = 1024,
                              padding: Annotated[int, Field(ge=1, le=16)] = 5) -> dict:
        """Create a TMP font asset from an imported Assets TTF/OTF/TTC font; never overwrites.

        Output must be an .asset in an existing Assets folder. Include Font Data and TMP resources
        must be available. Default dynamic, single SDFAA atlas, clear dynamic data on build enabled.
        Static mode requires characters. Newlines/tabs are ignored; Unicode scalars are deduplicated.
        Missing glyphs (source coverage or full atlas) are reported via all_characters_present and
        missing_codepoints even when ok=true. Assign the returned asset_path using font_path on UI
        creation or modification. This creates a font asset, not a new font design or OS font import.
        """
        return await require_editor(ctx).request(Endpoints.CREATE_TMP_FONT,
            {"source_font_path": source_font_path, "asset_path": asset_path, "characters": characters,
             "population_mode": population_mode, "sampling_point_size": sampling_point_size,
             "atlas_size": atlas_size, "padding": padding})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False))
    async def update_tmp_font_characters(asset_path: str, characters: Annotated[str, Field(min_length=1, max_length=4096)],
                                          ctx: Context, mode: Literal["append", "replace"] = "append") -> dict:
        """Append or replace a TMP font's baked characters, retaining its asset GUID and population mode.

        Replace removes existing baked glyphs and affects all users of this shared font. Dynamic fonts
        may generate characters again later. Requires a saved, self-contained single-atlas font up to
        2048 pixels with an available source font; Dynamic OS is unsupported. Newlines/tabs are ignored.
        Missing glyphs are reported even when ok=true; partial character coverage is saved. Does not
        change UI text content or assign fonts to nodes. Exceptions restore the saved asset bytes.
        """
        return await require_editor(ctx).request(Endpoints.UPDATE_TMP_FONT_CHARACTERS,
            {"asset_path": asset_path, "characters": characters, "mode": mode})

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def connect_project(project_path: str, ctx: Context) -> str:
        """Connect to the running Unity Editor for this project before calling project tools.

        Pass the absolute project root from the conversation workspace. Verifies the live
        Editor HTTP identity without reading project metadata. Install the bridge first.
        Call again to switch or reconnect.
        A failed selection leaves the previous project selected.
        """
        project = Path(project_path).expanduser()
        if not project.is_absolute():
            raise ValueError("project_path must be an absolute Unity project root")
        project = project.resolve()
        editor = await EditorConnection.connect(project)
        state = ctx.request_context.lifespan_context
        previous = state["editor"]
        state["editor"] = editor
        if previous is not None:
            await previous.aclose()
        return "Connected to Unity Editor."

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def unity_project_info(ctx: Context) -> dict:
        """Get project path, name and running Unity version directly from the connected Editor.

        Call connect_project first. Does not read project files or query package dependencies.
        """
        editor = require_editor(ctx)
        return await editor.project_info()

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def refresh_assets(ctx: Context) -> dict:
        """Run AssetDatabase.Refresh on Unity's main thread after writing files with normal file editing tools.

        Success means Refresh returned, not successful compilation. Poll get_compilation_status.
        Assembly reload may interrupt this response; query status before retrying.
        """
        return await require_editor(ctx).request(Endpoints.REFRESH_ASSETS, {})

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def get_compilation_status(ctx: Context, wait_seconds: Annotated[float, Field(ge=0, le=60)] = 0) -> dict:
        """Read live compiling/updating state and up to 200 compiler errors (file/line/column).

        Optional wait_seconds waits for refresh/compilation to settle. busy=false alone does not
        prove compilation succeeded: inspect has_compilation_result, compilation_revision and errors.
        has_compilation_result=false means no completed compilation was observed (or one is running).
        Errors describe the latest observed compilation and persist through domain reload.
        Automatically reconnects the same selected project while polling after reload; timeout is
        a tool error. No automatic retries of prefab edits or refresh requests.
        """
        deadline = time.monotonic() + wait_seconds
        while True:
            editor = require_editor(ctx)
            try:
                result = await editor.request(Endpoints.COMPILATION_STATUS)
            except RuntimeError:
                try:
                    replacement = await EditorConnection.connect(editor.project)
                    try:
                        result = await replacement.request(Endpoints.COMPILATION_STATUS)
                    except BaseException:
                        await replacement.aclose()
                        raise
                except (RuntimeError, httpx.HTTPError, OSError, ValueError) as error:
                    if time.monotonic() >= deadline:
                        raise RuntimeError(f"Could not reconnect to Unity project {editor.project} within "
                            f"{wait_seconds:g}s while reading compilation status. Last error: {error}") from error
                    await asyncio.sleep(.25)
                    continue
                ctx.request_context.lifespan_context["editor"] = replacement
                await editor.aclose()
            if not (result["is_compiling"] or result["is_updating"] or result["refresh_pending"]) or wait_seconds == 0:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError("Timed out waiting for Unity refresh/compilation; query status again")
            await asyncio.sleep(.25)

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False))
    async def get_play_status(ctx: Context, wait_seconds: Annotated[float, Field(ge=0, le=60)] = 0,
                              wait_for: Literal["settled", "playing", "stopped"] = "settled") -> dict:
        """Get Play mode/transition/pause state, test session ID, frame count and owned instances.

        Optional wait waits for settled/playing/stopped state, reconnecting the SAME selected project
        after domain reload. Status has no ok field. Frame count must advance for runtime validation.
        Enter/exit acknowledgements do not prove transition completion: query this tool afterwards.
        """
        deadline = time.monotonic() + wait_seconds
        while True:
            editor = require_editor(ctx)
            try:
                result = await editor.request(Endpoints.PLAY_STATUS)
            except RuntimeError:
                try:
                    replacement = await EditorConnection.connect(editor.project)
                    try:
                        result = await replacement.request(Endpoints.PLAY_STATUS)
                    except BaseException:
                        await replacement.aclose()
                        raise
                except (RuntimeError, httpx.HTTPError, OSError, ValueError):
                    if time.monotonic() >= deadline:
                        raise
                    await asyncio.sleep(.25)
                    continue
                ctx.request_context.lifespan_context["editor"] = replacement
                await editor.aclose()
            recovery_error = result.get("scene_restore_error") or result.get("test_scene_error")
            if recovery_error and wait_seconds > 0:
                raise RuntimeError(f"Play test scene error: {recovery_error}")
            ready = not result["is_changing"] and not result.get("scene_restore_pending", False) and (wait_for == "settled" or result["is_playing"] == (wait_for == "playing"))
            if ready or wait_seconds == 0:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError("Timed out waiting for Play mode transition; query status before retrying")
            await asyncio.sleep(.25)

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=True, openWorldHint=False))
    async def enter_play_mode(ctx: Context, mode: Literal["isolated", "current"] = "isolated",
                              reference_width: Annotated[float, Field(gt=0, le=16384)] = 1920,
                              reference_height: Annotated[float, Field(gt=0, le=16384)] = 1080) -> dict:
        """Enter Play; default isolated creates a temporary UI test scene instead of running open scenes.

        Isolated mode requires saved, clean scenes and closed Prefab Mode. It preserves the original
        scene setup and Play start-scene override, builds a Canvas/Content, EventSystem/input module
        and background camera, and restores/cleans on exit. reference_width/height set the temporary
        CanvasScaler only. Current mode uses existing scene/start-scene configuration unchanged.
        Global RuntimeInitializeOnLoad/project startup scripts still run: this is not process isolation.

        Returns transition acknowledgement. Follow with get_play_status(wait_for='playing').
        Reload may interrupt delivery; query status before retrying, never blindly resend mutations.
        """
        return await require_editor(ctx).request(Endpoints.ENTER_PLAY_MODE,
            {"mode": mode, "reference_width": reference_width, "reference_height": reference_height})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, idempotentHint=True, openWorldHint=False))
    async def exit_play_mode(ctx: Context) -> dict:
        """Exit Play, discarding runtime scene state. Does not save user scenes.

        Isolated mode restores the original scene setup/start-scene setting and deletes its temporary
        scene. Confirm completion with get_play_status(wait_for='stopped'); inspect recovery errors.

        This ends the running game's session, including state outside tool-created instances.
        """
        return await require_editor(ctx).request(Endpoints.EXIT_PLAY_MODE, {})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False))
    async def inspect_runtime_ui(ctx: Context, test_instance_id: str = "", scene_name: str = "", path: str = "",
                                 max_nodes: Annotated[int, Field(ge=1, le=500)] = 200) -> dict:
        """Inspect Play-mode scene hierarchy, UI properties and serialized references (including inactive nodes).

        Without instance ID path is a root/child NAME path, with optional scene name or scene asset path.
        Empty path enumerates loaded scene roots. Duplicate names are rejected for selection.
        With test_instance_id path is relative: '0' is its root, '0/1' a sibling-index path, or use
        a relative NAME path like 'Panel/Label'. Returned node.path is relative to the selected root,
        scene_path includes the scene prefix for diagnostics. A scene_path is not a selector argument.
        Requires settled, unpaused Play. Nodes are bounded/truncated, not asset prefab snapshots.
        """
        return await require_editor(ctx).request(Endpoints.INSPECT_RUNTIME_UI,
            {"test_instance_id": test_instance_id, "scene_name": scene_name, "path": path, "max_nodes": max_nodes})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def mount_test_prefab(prefab_path: str, ctx: Context, parent_path: str = "", scene_name: str = "") -> dict:
        """Instantiate a UI prefab during Play; isolated mode defaults to its own Canvas/Content.

        In current mode parent_path is required. It is a root/child NAME path; scene_name disambiguates loaded scenes. Requires exactly
        one active EventSystem with enabled input module. Creates no Canvas or EventSystem; rejects a
        prefab with Canvas on its root. Returns owned test_instance_id; at most 16 instances per session.
        Instantiation executes the prefab's game scripts. Scene is not saved. Root gets an ownership name;
        scripts must preserve it for the tool to resolve/clean the instance across domain reload.
        """
        return await require_editor(ctx).request(Endpoints.MOUNT_TEST_PREFAB,
            {"prefab_path": prefab_path, "parent_path": parent_path, "scene_name": scene_name})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def run_ui_test(test_instance_id: str, steps: list[UiTestStep], ctx: Context) -> Any:
        """Run up to 32 UI steps on an owned Play-mode test instance; stop at first failure (15s total).

        Pointer steps use normal UI input and raycasting; invoke_button calls onClick directly.
        Paths are relative names or 0/1 indices. Assertion expected values are strings.
        Mouse buttons persist within this call and are cleaned up afterward. Legacy pointer input
        requires focus. wait_condition allows up to 10s. One screenshot per call; fixed dimensions
        must be paired (10..2048), or both 0 for current size. Screenshot restores the prior preset.
        Check ok, passed and failed_step_index. Game scripts continue running between calls.
        Detailed steps and examples are in the unity-ui skill references.
        """
        result = await require_editor(ctx).run_ui_test(test_instance_id, [step.model_dump() for step in steps])
        images = []
        for step in result.get("steps", []):
            encoded = step.pop("png_base64", "")
            if encoded:
                images.append(Image(data=decode_base64(encoded), format="png").to_image_content())
                step["screenshot_attached"] = True
        if not images:
            return result
        return [TextContent(type="text", text=json.dumps(result, ensure_ascii=False)), *images]

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False))
    async def clear_test_instances(ctx: Context, test_instance_id: str = "") -> dict:
        """Destroy tool-owned test instances only; empty ID cleans all owned roots in this Play session.

        Does not delete Canvas/EventSystem or save scenes. Destruction happens at frame end; game
        callbacks may run. Renamed or missing roots are not destroyed without verified ownership.
        """
        return await require_editor(ctx).request(Endpoints.CLEAR_TEST_INSTANCES, {"test_instance_id": test_instance_id})

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def create_ui_prefab(output_path: str, spec: UiPrefabSpec, ctx: Context) -> dict:
        """Create a new uGUI prefab under Assets with a RectTransform root for a scene Canvas.

        Output must not exist. Supports TMP text and nested prefabs via kind=prefab; explicitly
        supplied layout fields override reused prefab layout. Choose font_path with required glyphs.
        Root stretches to its parent. Buttons need script-driven callbacks. Check ok before use.
        Detailed layout semantics and examples are in the unity-ui skill references.
        """
        editor = require_editor(ctx)
        return await editor.request(Endpoints.CREATE_UI_PREFAB, {
            "output_path": output_path, "spec": spec.model_dump(exclude_unset=True),
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def preview_ui_prefab(asset_path: str, ctx: Context, width: int = 1280,
                                 height: int = 720, background: str = "#202020") -> Any:
        """Render a saved uGUI prefab and return a PNG image for visual inspection.

        Dimensions are 64..2048 pixels. Background is an opaque HTML color. Uses an isolated
        preview scene and screen-space camera; a Canvas is supplied for Canvas-less subprefabs.
        Existing CanvasScaler settings are reused. World-space root Canvas is unsupported.
        Does not save assets or intentionally alter the active scene. Preview instantiation can
        run ExecuteAlways/OnValidate scripts; runtime data and animations are not simulated.
        Requires a Unity Editor with graphics support; image display depends on the MCP client.
        Success returns a PNG image; Unity business failure returns the full ok=false JSON response.
        """
        result = await require_editor(ctx).request(Endpoints.PREVIEW_UI_PREFAB, {
            "asset_path": asset_path, "width": width, "height": height, "background": background,
        })
        if not result.get("ok"):
            return result
        if (result["width"], result["height"]) != (width, height):
            raise RuntimeError("Preview image dimensions or identity mismatch")
        try:
            png = decode_png(result["png_base64"], width, height)
        except ValueError as exc:
            raise RuntimeError(f"Invalid preview image: {exc}") from exc
        return Image(data=png, format="png")

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def import_ui_image(source_path: str, asset_path: str, ctx: Context,
                              border: list[int] | None = None, pixels_per_unit: float = 100,
                              filter_mode: str = "bilinear") -> dict:
        """Import a local PNG/JPEG as a new single Sprite asset in the connected Unity project.

        source_path is an absolute path on the Python server machine. asset_path must be a new
        Assets/... image path with matching .png/.jpg/.jpeg extension. Never overwrites existing
        assets. Max 8 MiB, 8192 per side and 16 megapixels. border is [left,bottom,right,top] in
        source pixels (default zeros). filter_mode is bilinear or point (pixel art).
        Returns path, GUID, size and import settings. Use the path as sprite_path on UI Images;
        set image_type to Sliced for nine-slice rendering. No network image download is performed.
        On timeout inspect the destination before retrying; importing may have completed.
        """
        editor = require_editor(ctx)
        data, _, _, _ = await asyncio.to_thread(read_image, source_path)
        return await editor.request(Endpoints.IMPORT_UI_IMAGE, {
            "asset_path": asset_path, "image_base64": encode_base64(data),
            "border": [0, 0, 0, 0] if border is None else border,
            "pixels_per_unit": pixels_per_unit, "filter_mode": filter_mode,
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False,
    ))
    async def batch_edit_ui_prefab(asset_path: str, expected_version: str, ctx: Context,
                                   operations: list[Annotated[
                                       UiPropertyModification | UiNodeAddition | UiNodeDeletion | UiNodeMove | UiComponentAddition | UiNodeDuplication | UiPrefabInstantiation | UiScriptAddition | UiReferenceAssignment,
                                       Field(discriminator="action")]]) -> dict:
        """Batch property, node, component, script and reference edits to a UI prefab; save once.

        Use expected_version from the latest inspect. Up to 128 operations. Existing node paths
        resolve before any edit; newly created nodes can be addressed in a later request.
        modify.value is a string. Scripts must be compiled runtime MonoBehaviours.
        Pre-save failure discards edits. Check ok, saved, phase and failed_operation_index before
        retrying. Success returns backup_id and final created_nodes paths.
        Detailed property values and operation semantics are in the unity-ui skill references.
        """
        return await require_editor(ctx).request(Endpoints.BATCH_EDIT_UI_PREFAB, {
            "asset_path": asset_path, "expected_version": expected_version,
            "operations": [operation.model_dump(exclude_unset=True) for operation in operations],
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def find_ui_assets(queries: list[UiAssetSearch], ctx: Context) -> dict:
        """Batch search for UI prefabs, Sprites and TMP fonts under Assets (1..16 queries).

        Each query has asset_type (ui_prefab/sprite/tmp_font), literal name search, folder, limit and offset.
        Results preserve query order with per-item ok/error, total and next_offset (-1 at end).
        Sprite sheets return each Sprite separately with a string local_id for inspection.
        Query does not import, save or generate assets. Packages are excluded.
        """
        return await require_editor(ctx).request(Endpoints.FIND_UI_ASSETS, {
            "queries": [query.model_dump(mode="json") for query in queries],
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def inspect_ui_assets(assets: list[UiAssetInspection], ctx: Context) -> dict:
        """Batch inspect 1..32 UI prefab/Sprite/TMP font assets with per-item results/errors.

        Specify asset_path, asset_type and optional local_id from search (required for ambiguous
        sub-assets). UI prefabs return prefab.version, prefab.nodes (hierarchy, components,
        RectTransform/UI properties), prefab.total and prefab.truncated; max_nodes=1..1000.
        Use prefab.version for editing/restoring. ui_prefab excludes prefabs without RectTransform.
        Sprite details include rect size, texture, border (left,bottom,right,top), PPU.
        Font details include atlas mode, direct fallback references and optional text coverage.
        text is plain text <=1024 chars; newline/tab are ignored. search_fallbacks includes TMP's
        configured fallback chain/global settings. Checks existing glyphs only, NEVER dynamically
        adds characters. Missing glyphs do not prove a dynamic source font cannot generate them.
        """
        return await require_editor(ctx).request(Endpoints.INSPECT_UI_ASSETS, {
            "assets": [asset.model_dump(mode="json") for asset in assets],
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def list_ui_backups(asset_path: str, ctx: Context, limit: int = 20, offset: int = 0) -> dict:
        """List project-local pre-edit snapshots of this prefab, newest first, with pagination.

        Each backup_id addresses the state BEFORE its associated edit/restore. Includes timestamp,
        summary, original GUID and before/after versions. Empty after_version can mean saving or
        metadata completion did not finish. Stored under Library/UnityMcp/Backups, lost if cleaned.
        """
        return await require_editor(ctx).request(Endpoints.LIST_UI_BACKUPS, {
            "asset_path": asset_path, "limit": limit, "offset": offset,
        })

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False,
    ))
    async def restore_ui_prefab(backup_id: str, expected_version: str, ctx: Context) -> dict:
        """Restore a backup into its original prefab path, preserving the current .meta and GUID.

        Use list_ui_backups/edit result to choose backup_id and inspect_ui_assets with ui_prefab for current prefab.version.
        Close Prefab Mode for that asset. Refuses version/GUID conflicts and missing original assets.
        Creates another backup of the current state before restoring; returns its backup_id, new
        version and restored_from. Only restores prefab bytes, not fonts/images/nested dependencies.
        On transport failure inspect before retrying; restore may have completed. No restore-as-copy.
        """
        return await require_editor(ctx).request(Endpoints.RESTORE_UI_PREFAB, {
            "backup_id": backup_id, "expected_version": expected_version,
        })

    return mcp


def main() -> None:
    parser = argparse.ArgumentParser(description="Unity MCP stdio server")
    parser.parse_args()
    create_server().run(transport="stdio")


if __name__ == "__main__":
    main()
