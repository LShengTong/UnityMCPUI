# Detailed tool semantics

Read only the section relevant to the tool being used. Parameter types and constraints are provided by the tool schema.

## create_ui_prefab

Create a NEW uGUI prefab with a RectTransform root for the scene's existing Canvas.

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

## batch_edit_ui_prefab

Combine modify/add/delete/move/add_component/duplicate/instantiate_prefab and save once.

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

## run_ui_test

Run up to 32 Play-mode UI steps, stopping at first failure; total execution limit 15 seconds.

Steps: pointer_move (target_path center + pixel offsets x/y, or absolute Game-view pixels from bottom-left;
frames=1..60 for dragging), pointer_down/up (button=left/right/middle), pointer_scroll
(scroll_x/y wheel ticks at current pointer). Buttons persist within one test and are cleaned up.
Pointer steps use the active UI input module and normal raycasting. Input System virtual mouse
supports background Play; legacy StandaloneInputModule requires focus. Custom mouse bindings
must accept the virtual Mouse. invoke_button (direct onClick callback), set_input_text (TMP_InputField,
optional notify), assert, wait_condition (timeout_seconds <=10), screenshot (at most one).
Paths select owned-root descendants via '0/1' indexes or relative NAME paths ('Panel/Label').
Conditions use property and STRING expected: active_self/active_in_hierarchy/interactable/
reference_is_set use 'true'/'false', text and reference_path use exact strings. Reference checks
require component_type and field_path. No arbitrary method calls or C# execution.
Returns per-step expected/actual/error/frame_count and passed/failed_step_index. Screenshots are
actual Game-view captures (1..2048 per side), not prefab previews, and require advancing frames.
screenshot accepts optional fixed width/height together (10..2048; both 0/omitted keeps current size).
It selects a Game view preset, confirms dimensions across advancing frames, updates layout,
captures exact PNG dimensions, then restores the original preset including Free Aspect (up to 2 extra seconds).
Failure to
switch or confirm restoration is a test failure. For multiple sizes, call again on the same
test instance; at most one screenshot per call and the game continues running between calls.
Success/failed assertions are structured JSON; screenshot steps additionally return PNG images.
interaction_mode='callback' does not verify pointer hit testing, occlusion or keyboard navigation.
Runtime game scripts may have effects outside the instance; this is not a gameplay sandbox.

