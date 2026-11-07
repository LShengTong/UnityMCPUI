"""Session-based Unity MCP server with a local Unity Editor HTTP connection."""

import argparse
from contextlib import asynccontextmanager
from pathlib import Path
from typing import AsyncIterator, Annotated

from mcp.server.fastmcp import Context, FastMCP, Image
from mcp.types import ToolAnnotations
from pydantic import Field
from .ui_schema import UiPrefabSpec, UiNodeAddition, UiNodeDeletion, UiNodeMove, UiPropertyModification, UiComponentAddition
from .bridge import EditorConnection
from .asset_schema import UiAssetSearch, UiAssetInspection


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
            "Before using project tools, call connect_project with the absolute Unity project "
            "root from the user's current workspace context. Do not guess a path when unclear. "
            "Reuse the selected project in this session; reconnect when the target changes. "
            "For UI requests, translate the user description into a UiPrefabSpec and call create_ui_prefab. Use find_ui_assets and inspect_ui_assets with asset_type=ui_prefab to inspect existing UI conventions and output paths first. Editor bridge must be installed for creation."
        ),
    )

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
    async def create_ui_prefab(output_path: str, spec: UiPrefabSpec, ctx: Context) -> dict:
        """Create a NEW uGUI Canvas prefab from a structured UI description in the selected project.

        Convert the user's natural-language UI request into spec. Supports panel/image/text/button,
        nine fixed anchor positions, horizontal/vertical/full stretch, and horizontal/vertical layouts. All text uses TextMeshPro:
        specify a project font_path (including CJK glyphs when needed) or configure TMP default font.
        output_path must be Assets/.../*.prefab and must not exist. No arbitrary C# or event logic.
        Unity Editor must be open with the bridge package installed. Does not alter the active scene.
        Layout parents use child width/height as preferred sizes. Stretch nodes fill their parent
        with x/y as symmetric inset. On horizontal stretch, x is inset, y is position, height is fixed;
        on vertical stretch, y is inset, x is position, width is fixed. Optional offset_min/offset_max
        override preset geometry using Unity RectTransform edge-offset semantics. Buttons include their own label. No EventSystem is embedded:
        the destination scene must have exactly one EventSystem with the appropriate input module.
        """
        editor = require_editor(ctx)
        return await editor.create_prefab(output_path, spec.model_dump())

    @mcp.tool(annotations=ToolAnnotations(
        readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False,
    ))
    async def preview_ui_prefab(asset_path: str, ctx: Context, width: int = 1280,
                                 height: int = 720, background: str = "#202020") -> Image:
        """Render a saved uGUI prefab and return a PNG image for visual inspection.

        Dimensions are 64..2048 pixels. Background is an opaque HTML color. Uses an isolated
        preview scene and screen-space camera; a Canvas is supplied for Canvas-less subprefabs.
        Existing CanvasScaler settings are reused. World-space root Canvas is unsupported.
        Does not save assets or intentionally alter the active scene. Preview instantiation can
        run ExecuteAlways/OnValidate scripts; runtime data and animations are not simulated.
        Requires a Unity Editor with graphics support; image display depends on the MCP client.
        """
        png = await require_editor(ctx).preview_ui_prefab(asset_path, width, height, background)
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
                                       UiPropertyModification | UiNodeAddition | UiNodeDeletion | UiNodeMove | UiComponentAddition,
                                       Field(discriminator="action")]]) -> dict:
        """Combine modify/add/delete/move/add_component in one ordered prefab edit and save.

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
        add_component adds a whitelisted component to index_path, rejecting duplicates and layout
        conflicts. ScrollRect requires existing viewport_path/content_path and a masked viewport;
        no hierarchy is generated. Other components use defaults; follow with modify for settings.
        Errors identify zero-based operation index/action and phase when applicable. Re-inspect on
        success; after transport failure inspect before retrying because saving may have completed.
        """
        return await require_editor(ctx).batch_edit_ui_prefab(
            asset_path, expected_version, [operation.model_dump() for operation in operations])

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
