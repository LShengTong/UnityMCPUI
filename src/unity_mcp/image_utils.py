"""Local image loading and bounded Base64/PNG handling for Unity image transfers."""
import base64
import binascii
import struct
from pathlib import Path

MAX_IMAGE_BYTES = 8 * 1024 * 1024
MAX_SCREENSHOT_BYTES = 12 * 1024 * 1024


def encode_base64(data: bytes) -> str:
    return base64.b64encode(data).decode("ascii")


def decode_base64(encoded: str, max_bytes: int = MAX_SCREENSHOT_BYTES) -> bytes:
    if not isinstance(encoded, str) or len(encoded) > 4 * ((max_bytes + 2) // 3):
        raise ValueError("Invalid image encoding or size")
    try:
        data = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error) as error:
        raise ValueError("Invalid image Base64 encoding") from error
    if not data or len(data) > max_bytes:
        raise ValueError("Invalid image size")
    return data


def decode_png(encoded: str, width: int, height: int) -> bytes:
    """Check PNG header, end marker and declared size; this is not a full PNG decode."""
    data = decode_base64(encoded)
    if (len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n"
            or data[12:16] != b"IHDR" or data[-8:] != b"IEND\xaeB`\x82"):
        raise ValueError("Invalid PNG header or end marker")
    if (not 1 <= width <= 2048 or not 1 <= height <= 2048
            or struct.unpack(">II", data[16:24]) != (width, height)):
        raise ValueError("PNG dimensions do not match declared size or exceed 2048 pixels")
    return data


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


