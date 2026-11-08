"""Generated resource-query fields with UTF-16 business validation."""
from enum import Enum
from typing import get_args
from pydantic import field_validator
from .generated.requests import UiAssetTypeValue, UiAssetSearch, UiAssetInspection as GeneratedUiAssetInspection

UiAssetType = Enum("UiAssetType", {name.upper(): name for name in get_args(UiAssetTypeValue)}, type=str)


class UiAssetInspection(GeneratedUiAssetInspection):
    @field_validator("text")
    @classmethod
    def utf16_text_limit(cls, value: str) -> str:
        if len(value.encode("utf-16-le")) > 2048:
            raise ValueError("text must be at most 1024 UTF-16 code units")
        return value
