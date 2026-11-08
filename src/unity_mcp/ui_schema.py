"""Generated request fields with hand-written UI business validation."""
from pydantic import model_validator
from .generated.requests import (
    AnchorPreset, UiOffset, UiNode, UiPropertyChange, UiNodeDeletion, UiNodeMove,
    UiPropertyModification, UiPrefabSpec as GeneratedUiPrefabSpec,
    UiNodeAddition as GeneratedUiNodeAddition, UiComponentAddition as GeneratedUiComponentAddition,
)


class UiPrefabSpec(GeneratedUiPrefabSpec):
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


class UiNodeAddition(GeneratedUiNodeAddition):
    @model_validator(mode="after")
    def validate_subtree(self):
        UiPrefabSpec(name="Validation", children=[self.node])
        return self


class UiComponentAddition(GeneratedUiComponentAddition):
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
