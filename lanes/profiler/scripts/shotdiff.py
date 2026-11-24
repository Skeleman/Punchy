#!/usr/bin/env python3
"""Compares two screenshots: mean absolute difference (0-255, over RGB) and the share of pixels off by more than 8 and
by more than 24 levels in any channel, plus the box round the changed pixels. usage: shotdiff.py <a.png> <b.png> [label]"""
import sys
from PIL import Image, ImageChops

a = Image.open(sys.argv[1]).convert('RGB')
b = Image.open(sys.argv[2]).convert('RGB')
if a.size != b.size:
    print(f"{sys.argv[3] if len(sys.argv) > 3 else ''}: sizes differ {a.size} vs {b.size}")
    sys.exit(0)
d = ImageChops.difference(a, b)
px = list(d.getdata())
n = len(px)
total = sum(r + g + bl for r, g, bl in px)
over8 = sum(1 for p in px if max(p) > 8)
over24 = sum(1 for p in px if max(p) > 24)
box = d.point(lambda v: 255 if v > 24 else 0).getbbox()
print(f"{sys.argv[3] if len(sys.argv) > 3 else ''}: mean {total / (3 * n):.3f}, >8 {100 * over8 / n:.2f}%, >24 {100 * over24 / n:.2f}%, box {box}")
