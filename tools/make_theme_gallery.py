#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把 NetSeep.exe --themes 生成的单主题预览图拼成一张主题画廊（assets/themes.png）。

用法：
    NetSeep.exe --themes obj\\themes 2
    python tools/make_theme_gallery.py

只在更新 README 插图时用得到，构建程序本身不需要 Python。
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "obj", "themes")
OUT = os.path.join(ROOT, "assets", "themes.png")

# 与 src/Theme.cs 中的 Id / Name 保持一致（system 跟随系统，不单独出图）
THEMES = [
    ("dark", "深色（默认）"),
    ("light", "浅色"),
    ("midnight", "午夜蓝"),
    ("graphite", "石墨灰"),
    ("contrast", "高对比"),
    ("glass", "极简透明"),
]

COLS = 3
PAD = 18
GAP_X = 18
GAP_Y = 14
LABEL_H = 30
BG = (250, 250, 251, 255)
LABEL = (60, 62, 68, 255)

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\msyhl.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
    r"C:\Windows\Fonts\simsun.ttc",
]


def load_font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except Exception:
                continue
    return ImageFont.load_default()


def main():
    missing = [t for t, _ in THEMES if not os.path.exists(os.path.join(SRC, t + ".png"))]
    if missing:
        print("缺少预览图：%s\n请先运行：NetSeep.exe --themes obj\\themes 2" % ", ".join(missing))
        return 1

    panels = [(Image.open(os.path.join(SRC, t + ".png")).convert("RGBA"), name) for t, name in THEMES]
    cw = max(p.width for p, _ in panels)
    ch = max(p.height for p, _ in panels)
    rows = (len(panels) + COLS - 1) // COLS

    W = PAD * 2 + COLS * cw + (COLS - 1) * GAP_X
    H = PAD * 2 + rows * (ch + LABEL_H) + (rows - 1) * GAP_Y
    canvas = Image.new("RGBA", (W, H), BG)
    draw = ImageDraw.Draw(canvas)
    font = load_font(15)

    for idx, (panel, name) in enumerate(panels):
        r, c = divmod(idx, COLS)
        x = PAD + c * (cw + GAP_X) + (cw - panel.width) // 2
        y = PAD + r * (ch + LABEL_H + GAP_Y)
        canvas.alpha_composite(panel, (x, y))
        tw = draw.textlength(name, font=font)
        draw.text((PAD + c * (cw + GAP_X) + (cw - tw) / 2, y + ch + 6), name, font=font, fill=LABEL)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    canvas.convert("RGB").save(OUT, "PNG")
    print("已生成主题画廊:", OUT, canvas.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
