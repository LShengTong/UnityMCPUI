"""Validated, deliberately small uGUI creation schema."""
from typing import Literal
from pydantic import BaseModel, ConfigDict, Field, model_validator


AnchorPreset = Literal[
    "top_left", "top_center", "top_right", "center_left", "center", "center_right",
    "bottom_left", "bottom_center", "bottom_right",
    "stretch_horizontal_top", "stretch_horizontal_center", "stretch_horizontal_bottom",
    "stretch_vertical_left", "stretch_vertical_center", "stretch_vertical_right", "stretch",
]


class UiOffset(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    x: float = Field(ge=-16384, le=16384)
    y: float = Field(ge=-16384, le=16384)


class UiNode(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    name: str = Field(min_length=1, max_length=80, pattern=r"^[^/\\\x00-\x1f]+$")
    kind: Literal["panel", "image", "text", "button"] = "panel"
    width: float = Field(default=320, gt=0, le=16384)
    height: float = Field(default=80, gt=0, le=16384)
    x: float = Field(default=0, ge=-16384, le=16384)
    y: float = Field(default=0, ge=-16384, le=16384)
    anchor: AnchorPreset = Field(default="center", description="Choose a fixed corner/edge or stretch preset to match layout intent. Parent LayoutGroup may drive anchors.")
    offset_min: UiOffset | None = Field(default=None, description="Explicit left/bottom offset in parent units; overrides calculated geometry.")
    offset_max: UiOffset | None = Field(default=None, description="Explicit right/top offset; negative x/y mean inward margins on stretched axes.")
    color: str = Field(default="#FFFFFF", pattern=r"^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")
    text: str = Field(default="", max_length=4096)
    text_color: str = Field(default="#202020", pattern=r"^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")
    font_size: float = Field(default=24, gt=0, le=512)
    font_path: str = ""
    sprite_path: str = ""
    layout: Literal["none", "vertical", "horizontal"] = "none"
    spacing: float = Field(default=12, ge=0, le=4096)
    padding: int = Field(default=16, ge=0, le=4096)
    children: list["UiNode"] = Field(default_factory=list, max_length=128)


class UiPrefabSpec(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)
    name: str = Field(min_length=1, max_length=80, pattern=r"^[^/\\\x00-\x1f]+$")
    reference_width: float = Field(default=1920, gt=0, le=16384)
    reference_height: float = Field(default=1080, gt=0, le=16384)
    children: list[UiNode] = Field(min_length=1, max_length=128)

    @model_validator(mode="after")
    def bounded_tree(self):
        count = 0
        def visit(nodes, depth):
            nonlocal count
            if depth > 12:
                raise ValueError("UI nesting cannot exceed 12 levels")
            names = [node.name for node in nodes]
            if len(names) != len(set(names)):
                raise ValueError("Sibling names must be unique")
            for node in nodes:
                count += 1
                if count > 128:
                    raise ValueError("UI cannot exceed 128 nodes")
                if node.kind in ("text", "button") and (node.children or node.layout != "none"):
                    raise ValueError("Text and button nodes cannot contain custom children/layout")
                if node.kind == "text" and not node.text:
                    raise ValueError("Text nodes require nonempty text")
                visit(node.children, depth + 1)
        visit(self.children, 1)
        return self


class UiPropertyChange(BaseModel):
    """One property on an existing node. Values use strings; vectors use JSON objects."""
    model_config = ConfigDict(extra="forbid")
    index_path: str = Field(pattern=r"^0(?:/(?:0|[1-9][0-9]*)){0,64}$")
    property: Literal[
        "name", "active", "text", "font_size", "text_color", "font_path", "sprite_path",
        "color", "anchored_position", "size_delta", "anchor", "anchor_min", "anchor_max", "pivot", "offset_min", "offset_max",
        "spacing", "padding", "interactable", "image_type",
        "preferred_width", "preferred_height", "min_width", "min_height", "flexible_width", "flexible_height",
        "horizontal_fit", "vertical_fit", "grid_cell_size", "grid_spacing", "scroll_horizontal", "scroll_vertical",
    ]
    value: str = Field(max_length=4096)


class UiNodeAddition(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["add"]
    parent_path: str = Field(pattern=r"^0(?:/(?:0|[1-9][0-9]*)){0,63}$")
    node: UiNode
    sibling_index: int = Field(default=-1, ge=-1, le=2000)

    @model_validator(mode="after")
    def validate_subtree(self):
        UiPrefabSpec(name="Validation", children=[self.node])
        return self


class UiNodeDeletion(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["delete"]
    index_path: str = Field(pattern=r"^0/(?:0|[1-9][0-9]*)(?:/(?:0|[1-9][0-9]*)){0,63}$")


class UiNodeMove(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["move"]
    index_path: str = Field(pattern=r"^0/(?:0|[1-9][0-9]*)(?:/(?:0|[1-9][0-9]*)){0,63}$")
    parent_path: str = Field(pattern=r"^0(?:/(?:0|[1-9][0-9]*)){0,64}$")
    sibling_index: int = Field(default=-1, ge=-1, le=2000)
    world_position_stays: bool = False


class UiPropertyModification(UiPropertyChange):
    action: Literal["modify"]


class UiComponentAddition(BaseModel):
    model_config = ConfigDict(extra="forbid")
    action: Literal["add_component"]
    index_path: str = Field(pattern=r"^0(?:/(?:0|[1-9][0-9]*)){0,64}$")
    component: Literal[
        "Image", "Button", "CanvasGroup", "LayoutElement", "ContentSizeFitter",
        "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup", "RectMask2D", "ScrollRect",
    ]
    viewport_path: str = ""
    content_path: str = ""
    horizontal: bool = False
    vertical: bool = True

    @model_validator(mode="after")
    def scroll_references(self):
        import re
        pattern = r"^0(?:/(?:0|[1-9][0-9]*)){0,64}$"
        if self.component == "ScrollRect":
            if not re.fullmatch(pattern, self.viewport_path) or not re.fullmatch(pattern, self.content_path):
                raise ValueError("ScrollRect requires existing viewport_path and content_path")
            if not self.horizontal and not self.vertical:
                raise ValueError("ScrollRect must enable horizontal or vertical scrolling")
        elif self.viewport_path or self.content_path:
            raise ValueError("viewport_path/content_path are only for ScrollRect")
        return self
