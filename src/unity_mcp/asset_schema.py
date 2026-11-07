"""Unified, batch read-only UI resource queries."""
from enum import Enum
from pydantic import BaseModel, ConfigDict, Field, field_validator


class UiAssetType(str, Enum):
    UI_PREFAB = "ui_prefab"
    SPRITE = "sprite"
    TMP_FONT = "tmp_font"


class UiAssetSearch(BaseModel):
    model_config = ConfigDict(extra="forbid")
    asset_type: UiAssetType
    search: str = Field(default="", max_length=200)
    folder: str = "Assets"
    limit: int = Field(default=50, ge=1, le=100)
    offset: int = Field(default=0, ge=0, le=1000000)


class UiAssetInspection(BaseModel):
    model_config = ConfigDict(extra="forbid")
    asset_path: str
    asset_type: UiAssetType
    local_id: str = Field(default="", pattern=r"^(?:-?[0-9]+)?$")
    text: str = Field(default="", max_length=1024)
    search_fallbacks: bool = True
    max_nodes: int = Field(default=200, ge=1, le=1000)


    @field_validator("text")
    @classmethod
    def utf16_text_limit(cls, value: str) -> str:
        if len(value.encode("utf-16-le")) > 2048:
            raise ValueError("text must be at most 1024 UTF-16 code units")
        return value
