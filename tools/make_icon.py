#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成 NetSeep 程序/托盘图标（多尺寸 .ico）与 README 用图标 PNG。

只在需要重新生成图标时运行，构建 NetSeep.exe 本身不需要 Python。
    python tools/make_icon.py
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")

BG = (32, 34, 38, 255)
BORDER = (126, 128, 136, 255)
UP = (90, 214, 140, 255)
DOWN = (86, 170, 245, 255)


def arrow(d, x, y, w, h, up, color):
    cx = x + w / 2.0
    head = h * 0.62
    stem = max(1.0, w * 0.26)
    if up:
        d.polygon([(cx, y), (x, y + head), (x + w, y + head)], fill=color)
        d.rectangle([cx - stem / 2, y + head * 0.75, cx + stem / 2, y + h], fill=color)
    else:
        d.polygon([(cx, y + h), (x, y + h - head), (x + w, y + h - head)], fill=color)
        d.rectangle([cx - stem / 2, y, cx + stem / 2, y + h - head * 0.75], fill=color)


def render(size, ss=8):
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = max(1.0, s * 0.06)
    d.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 0.22,
                        fill=BG, outline=BORDER, width=max(1, int(s * 0.035)))
    aw, ah = s * 0.26, s * 0.42
    arrow(d, s * 0.19, s * 0.29, aw, ah, True, UP)
    arrow(d, s * 0.55, s * 0.29, aw, ah, False, DOWN)
    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    big = render(256)
    ico = os.path.join(ASSETS, "netseep.ico")
    big.save(ico, format="ICO",
             sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
    big.save(os.path.join(ASSETS, "netseep-256.png"), format="PNG")
    render(32).save(os.path.join(ASSETS, "netseep-32.png"), format="PNG")
    print("已生成:", ico)


if __name__ == "__main__":
    main()
