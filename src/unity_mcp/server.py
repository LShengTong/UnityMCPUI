"""Session-based Unity MCP server with a local Unity Editor HTTP connection."""

import argparse
import asyncio
import time
import httpx
import base64
import json
from contextlib import asynccontextmanager
from pathlib import Path
from typing import AsyncIterator, Annotated, Any, Literal

from mcp.server.fastmcp import Context, FastMCP, Image
from mcp.types import ToolAnnotations, TextContent
from pydantic import Field
from .generated.requests import UiPrefabSpec, UiNodeAddition, UiNodeDeletion, UiNodeMove, UiPropertyModification, UiComponentAddition, UiNodeDuplication, UiPrefabInstantiation, UiScriptAddition, UiReferenceAssignment
from .bridge import EditorConnection
from .asset_schema import UiAssetSearch, UiAssetInspection
from .generated.requests import UiTestStep
from .generated.routes import Endpoints


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

    mcp = FastMCP(
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
        return await require_editor(ctx)._request_ui_query(Endpoints.CREATE_TMP_FONT,
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
        return await require_editor(ctx)._request_ui_query(Endpoints.UPDATE_TMP_FONT_CHARACTERS,
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
        return await require_editor(ctx).refresh_assets()

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
                result = await editor.compilation_status()
            except RuntimeError:
                try:
                    replacement = await EditorConnection.connect(editor.project)
                    try:
                        result = await replacement.compilation_status()
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
                result = await editor.live_status(Endpoints.PLAY_STATUS)
            except RuntimeError:
                try:
                    replacement = await EditorConnection.connect(editor.project)
                    try:
                        result = await replacement.live_status(Endpoints.PLAY_STATUS)
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
        return await require_editor(ctx)._request_ui_query(Endpoints.ENTER_PLAY_MODE,
            {"mode": mode, "reference_width": reference_width, "reference_height": reference_height})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, idempotentHint=True, openWorldHint=False))
    async def exit_play_mode(ctx: Context) -> dict:
        """Exit Play, discarding runtime scene state. Does not save user scenes.

        Isolated mode restores the original scene setup/start-scene setting and deletes its temporary
        scene. Confirm completion with get_play_status(wait_for='stopped'); inspect recovery errors.

        This ends the running game's session, including state outside tool-created instances.
        """
        return await require_editor(ctx)._request_ui_query(Endpoints.EXIT_PLAY_MODE, {})

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
        return await require_editor(ctx)._request_ui_query(Endpoints.INSPECT_RUNTIME_UI,
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
        return await require_editor(ctx)._request_ui_query(Endpoints.MOUNT_TEST_PREFAB,
            {"prefab_path": prefab_path, "parent_path": parent_path, "scene_name": scene_name})

    @mcp.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False))
    async def run_ui_test(test_instance_id: str, steps: list[UiTestStep], ctx: Context) -> Any:
        """Run up to 32 Play-mode UI steps, stopping at first failure; total execution limit 15 seconds.

        Steps: invoke_button (onClick callback, NOT pointer/raycast input), set_input_text (TMP_InputField,
        optional notify), assert, wait_condition (timeout_seconds <=10), screenshot (at most one).
        Paths select owned-root descendants via '0/1' indexes or relative NAME paths ('Panel/Label').
        Conditions use property and STRING expected: active_self/active_in_hierarchy/interactable/
        reference_is_set use 'true'/'false', text and reference_path use exact strings. Reference checks
        require component_type and field_path. No arbitrary method calls or C# execution.
        Returns per-step expected/actual/error/frame_count and passed/failed_step_index. Screenshots are
        actual Game-view captures (1..2048 per side), not prefab previews, and require advancing frames.
        screenshot accepts optional width/height together (both 0/omitted keeps current size).
        It requests Screen.SetResolution, confirms dimensions across advancing frames, updates layout,
        captures exact PNG dimensions, then restores original dimensions/mode (up to 2 extra seconds).
        This does not restore the original Free Aspect/fixed-resolution dropdown preset. Failure to
        switch or confirm restoration is a test failure. For multiple sizes, call again on the same
        test instance; at most one screenshot per call and the game continues running between calls.
        Success/failed assertions are structured JSON; screenshot steps additionally return PNG images.
        interaction_mode='callback' does not verify pointer hit testing, occlusion or keyboard navigation.
        Runtime game scripts may have effects outside the instance; this is not a gameplay sandbox.
        """
        result = await require_editor(ctx).run_ui_test(test_instance_id, [step.model_dump() for step in steps])
        images = []
        for step in result.get("steps", []):
            encoded = step.pop("png_base64", "")
            if encoded:
                images.append(Image(data=base64.b64decode(encoded), format="png").to_image_content())
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
        return await require_editor(ctx)._request_ui_query(Endpoints.CLEAR_TEST_INSTANCES, {"test_instance_id": test_instance_id})

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def create_ui_prefab(output_path: str, spec: UiPrefabSpec, ctx: Context) -> dict:
        """Create a NEW uGUI prefab with a RectTransform root for the scene's existing Canvas.

        Convert the user's natural-language UI request into spec. Supports panel/image/text/button,
        nine fixed anchor positions, horizontal/vertical/full stretch, and horizontal/vertical/grid layouts. All text uses TextMeshPro:
        specify a project font_path (including CJK glyphs when needed) or configure TMP default font.
        output_path must be Assets/.../*.prefab and must not exist. No arbitrary C# or event logic.
        Root stretches to its parent with zero offsets. No Canvas, CanvasScaler, GraphicRaycaster
        or EventSystem is added; use the destination scene's UI infrastructure.
        kind=prefab with prefab_path reuses an existing UI prefab, preserving its nested prefab link.
        For these nodes only explicitly supplied anchor/width/height/x/y/offsets override source
        layout; width/height set actual rect dimensions, x/y set anchoredPosition components.
        Source visuals/components are preserved; children/layout overrides are unsupported.
        Ordinary text/button nodes support text_alignment, word_wrap, auto_size, font_size_min/max,
        text_overflow and line_spacing (buttons apply text settings to their generated Label).
        Images support image_type, preserve_aspect, fill_method/origin/amount/clockwise and
        raycast_target. Linear layouts support child_alignment, child_control_width/height,
        child_force_expand_width/height and child_scale_width/height. layout=grid supports
        grid_cell_size, grid_spacing (x/y objects), grid_constraint/count and grid_start_corner/axis.
        Unity Editor must be open with the bridge package installed. Does not alter the active scene.
        Layout parents use child width/height as preferred sizes. Stretch nodes fill their parent
        with x/y as symmetric inset. On horizontal stretch, x is inset, y is position, height is fixed;
        on vertical stretch, y is inset, x is position, width is fixed. Optional offset_min/offset_max
        override preset geometry using Unity RectTransform edge-offset semantics. Buttons include their own label. No EventSystem is embedded:
        the destination scene must have exactly one EventSystem with the appropriate input module.
        """
        editor = require_editor(ctx)
        return await editor.create_prefab(output_path, spec.model_dump(exclude_unset=True))

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
        png = await require_editor(ctx).preview_ui_prefab(asset_path, width, height, background)
        if isinstance(png, dict):
            return png
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
        return await require_editor(ctx).import_ui_image(
            source_path, asset_path, border, pixels_per_unit, filter_mode)

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=True, idempotentHint=False, openWorldHint=False,
    ))
    async def batch_edit_ui_prefab(asset_path: str, expected_version: str, ctx: Context,
                                   operations: list[Annotated[
                                       UiPropertyModification | UiNodeAddition | UiNodeDeletion | UiNodeMove | UiComponentAddition | UiNodeDuplication | UiPrefabInstantiation | UiScriptAddition | UiReferenceAssignment,
                                       Field(discriminator="action")]]) -> dict:
        """Combine modify/add/delete/move/add_component/duplicate/instantiate_prefab and save once.

        Use inspect_ui_assets item's prefab.version. modify has index_path, property and string value.
        add has parent_path, node and optional sibling_index (-1 append). delete has index_path.
        move has index_path, parent_path, sibling_index and world_position_stays (default false). All paths identify nodes BEFORE this batch, resolved
        before any operation, so earlier deletions/reordering do not shift later targets. New nodes
        cannot be referenced within the batch. Using a previously deleted target fails the batch.
        Max 128 operations. Saves once after success, returning backup_id for the pre-edit state; pre-save failure discards the isolated edits.
        modify property "anchor" uses the same preset names as creation, setting anchorMin/anchorMax
        and pivot while keeping sizeDelta/anchoredPosition values (not screen bounds). Follow it with
        offset_min/offset_max for stretch margins; vectors use JSON strings. Parent layouts may drive
        anchors/size and override manual settings.
        Text properties: text_alignment (TopLeft/Top/TopRight/Left/Center/Right/BottomLeft/Bottom/
        BottomRight/Justified/Flush), word_wrap, auto_size, font_size_min/max (1..512),
        text_overflow (Overflow/Ellipsis/Masking/Truncate/Page), line_spacing (-512..512).
        Image properties: preserve_aspect, fill_method (Horizontal/Vertical/Radial90/Radial180/
        Radial360), fill_origin (0..1 for Horizontal/Vertical, otherwise 0..3), fill_amount (0..1),
        fill_clockwise and raycast_target. Fill settings display with image_type=Filled.
        Layout properties: child_alignment (Unity TextAnchor names), child_control_width/height,
        child_force_expand_width/height, child_scale_width/height; Grid supports grid_constraint
        (Flexible/FixedColumnCount/FixedRowCount), grid_constraint_count (1..2000), grid_start_corner
        and grid_start_axis. Boolean values are lowercase true/false strings. Set font_size_min/max
        in an order that keeps min<=max at every step. Properties require the relevant component.
        add_component adds a whitelisted component to index_path, rejecting duplicates and layout
        conflicts. ScrollRect requires existing viewport_path/content_path and a masked viewport;
        no hierarchy is generated. Other components use defaults; follow with modify for settings.
        duplicate copies a non-root index_path subtree to parent_path; instantiate_prefab inserts
        an existing UI prefab_path under parent_path, preserving its nested prefab link.
        Both accept optional name (otherwise choose a unique sibling name) and sibling_index.
        Source layout is preserved. Cannot duplicate nested prefab internals or insert inside
        nested instances; circular prefab dependencies are rejected. created_nodes returns
        operation_index and final index_path for surviving added/copied/instantiated roots.
        add_script attaches a compiled runtime MonoBehaviour from script_path (Assets/...cs).
        set_reference assigns a serialized object-reference field_path on component_type (full C#
        type name) at index_path. target_path selects a node; empty target_component means its
        GameObject, otherwise provide the exact component full type name. Ambiguous components
        require component_index/target_component_index (zero-based among exact type matches).
        clear=true assigns null. Supports single fields and nested serializable fields, not arrays.
        add_script then set_reference can share one batch; nodes must already exist before the batch.
        Unity business failures return a dict with ok=false, error, phase, failed_operation_index
        (zero-based; -1 when not tied to an operation), failed_action, executed_operations,
        saved and backup_id. Check ok before treating the result as success; executed operations
        do not imply a saved prefab. Transport/validation failures still raise tool errors. Re-inspect on
        success; after transport failure inspect before retrying because saving may have completed.
        """
        return await require_editor(ctx).batch_edit_ui_prefab(
            asset_path, expected_version, [operation.model_dump(exclude_unset=True) for operation in operations])

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
        return await require_editor(ctx).find_ui_assets([query.model_dump(mode="json") for query in queries])

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
        return await require_editor(ctx).inspect_ui_assets([asset.model_dump(mode="json") for asset in assets])

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=True, destructiveHint=False, idempotentHint=True, openWorldHint=False,
    ))
    async def list_ui_backups(asset_path: str, ctx: Context, limit: int = 20, offset: int = 0) -> dict:
        """List project-local pre-edit snapshots of this prefab, newest first, with pagination.

        Each backup_id addresses the state BEFORE its associated edit/restore. Includes timestamp,
        summary, original GUID and before/after versions. Empty after_version can mean saving or
        metadata completion did not finish. Stored under Library/UnityMcp/Backups, lost if cleaned.
        """
        return await require_editor(ctx).list_ui_backups(asset_path, limit, offset)

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
        return await require_editor(ctx).restore_ui_prefab(backup_id, expected_version)

    return mcp


def main() -> None:
    parser = argparse.ArgumentParser(description="Unity MCP stdio server")
    parser.parse_args()
    create_server().run(transport="stdio")


if __name__ == "__main__":
    main()
