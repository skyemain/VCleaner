#!/usr/bin/env python3
"""Generates the CleanSlate application icon (multi-size PNG-in-ICO plus a 256px PNG) without third-party packages."""
import math
import struct
import sys
import zlib
from pathlib import Path

SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4  # supersampling per axis


def rounded_rect_alpha(x, y, w, h, r):
    cx = min(max(x, r), w - r)
    cy = min(max(y, r), h - r)
    return 1.0 if (x - cx) ** 2 + (y - cy) ** 2 <= r * r else 0.0


def sparkle_inside(x, y, cx, cy, radius, pinch):
    # Four-pointed star (astroid-like): |dx|^p + |dy|^p <= r^p with p < 1.
    dx = abs(x - cx) / radius
    dy = abs(y - cy) / radius
    return (dx ** pinch + dy ** pinch) <= 1.0


def render(size):
    pixels = bytearray(size * size * 4)
    for py in range(size):
        for px in range(size):
            r_acc = g_acc = b_acc = a_acc = 0.0
            for sy in range(SS):
                for sx in range(SS):
                    x = (px + (sx + 0.5) / SS) / size
                    y = (py + (sy + 0.5) / SS) / size
                    a = rounded_rect_alpha(x, y, 1.0, 1.0, 0.22)
                    if a == 0.0:
                        continue
                    t = (x + y) / 2.0
                    r = 0x00 + (0x2B - 0x00) * t
                    g = 0x78 + (0x57 - 0x78) * t * 0.0 + (0x9A - 0x78) * t * 0.6
                    b = 0xD4 + (0xE8 - 0xD4) * t
                    # Large sparkle and a small one (a "clean slate" shine).
                    if sparkle_inside(x, y, 0.47, 0.52, 0.34, 0.62):
                        r, g, b = 255, 255, 255
                    elif sparkle_inside(x, y, 0.76, 0.26, 0.13, 0.62):
                        r, g, b = 255, 255, 255
                    r_acc += r * a
                    g_acc += g * a
                    b_acc += b * a
                    a_acc += a
            n = SS * SS
            o = (py * size + px) * 4
            if a_acc > 0:
                pixels[o] = int(r_acc / a_acc)
                pixels[o + 1] = int(g_acc / a_acc)
                pixels[o + 2] = int(b_acc / a_acc)
                pixels[o + 3] = int(255 * a_acc / n)
    return bytes(pixels)


def png(size, rgba):
    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "../src/CleanSlate.App/Assets")
    out.mkdir(parents=True, exist_ok=True)
    images = [(s, png(s, render(s))) for s in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    blobs = b""
    for size, data in images:
        entries += struct.pack("<BBBBHHII", 0 if size >= 256 else size, 0 if size >= 256 else size, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    (out / "CleanSlate.ico").write_bytes(header + entries + blobs)
    (out / "CleanSlate.png").write_bytes(images[-1][1])
    print(f"wrote {out / 'CleanSlate.ico'}")


if __name__ == "__main__":
    main()
