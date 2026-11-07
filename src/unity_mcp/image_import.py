"""Bounded PNG/JPEG header inspection before sending images to Unity."""
import struct
from pathlib import Path

MAX_IMAGE_BYTES = 8 * 1024 * 1024


def image_dimensions(data: bytes) -> tuple[str, int, int]:
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) >= 33 and data[12:16] == b"IHDR":
        width, height = struct.unpack(">II", data[16:24])
        kind = "png"
    elif data.startswith(b"\xff\xd8"):
        width = height = 0
        position = 2
        while position < len(data):
            if data[position] != 0xff:
                raise ValueError("Invalid JPEG marker")
            while position < len(data) and data[position] == 0xff:
                position += 1
            if position >= len(data):
                break
            marker = data[position]
            position += 1
            if marker in (0xd9, 0xda):
                break
            if marker == 0x01 or 0xd0 <= marker <= 0xd8:
                continue
            if position + 2 > len(data):
                break
            length = int.from_bytes(data[position:position + 2], "big")
            if length < 2 or position + length > len(data):
                break
            if marker in (0xc0, 0xc1, 0xc2):
                if length < 8:
                    break
                height, width = struct.unpack(">HH", data[position + 3:position + 7])
                kind = "jpeg"
                break
            position += length
        else:
            raise ValueError("JPEG dimensions not found")
        if width == 0 or height == 0:
            raise ValueError("Unsupported or invalid JPEG")
    else:
        raise ValueError("Only PNG and JPEG images are supported")
    if not 1 <= width <= 8192 or not 1 <= height <= 8192 or width * height > 16 * 1024 * 1024:
        raise ValueError("Image must be <=8192 per side and <=16 megapixels")
    return kind, width, height


def read_image(source_path: str) -> tuple[bytes, str, int, int]:
    source = Path(source_path).expanduser()
    if not source.is_absolute():
        raise ValueError("source_path must be an absolute local file path")
    with source.open("rb") as stream:
        data = stream.read(MAX_IMAGE_BYTES + 1)
    if not data or len(data) > MAX_IMAGE_BYTES:
        raise ValueError("Image must be nonempty and at most 8 MiB")
    kind, width, height = image_dimensions(data)
    return data, kind, width, height
