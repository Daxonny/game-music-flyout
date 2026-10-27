# Regenerates src/GameMusicFlyout/Assets/app.ico (no dependencies):
#   python3 tools/make_icon.py src/GameMusicFlyout/Assets/app.ico
import struct, sys

BG = (0x4A, 0x9E, 0xFF)
FG = (0xFF, 0xFF, 0xFF)
SS = 4  # supersampling per axis

def inside_rounded(x, y, r):
    # unit square [0,1], corner radius r
    cx = min(max(x, r), 1 - r); cy = min(max(y, r), 1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

# Equalizer bars: (x0, x1, y_top) in unit coords; bars sit on a common baseline, rounded tops/bottoms.
BARS = [(0.20, 0.33, 0.48), (0.435, 0.565, 0.24), (0.67, 0.80, 0.38)]
BASE = 0.78

def in_bar(x, y):
    for x0, x1, top in BARS:
        w = x1 - x0; r = w / 2
        if x0 <= x <= x1 and top + r <= y <= BASE - r:
            return True
        cx = (x0 + x1) / 2
        for cy in (top + r, BASE - r):
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                return True
    return False

def render(n):
    px = []
    for row in range(n):
        line = []
        for col in range(n):
            a = fg = 0
            for sy in range(SS):
                for sx in range(SS):
                    x = (col + (sx + 0.5) / SS) / n; y = (row + (sy + 0.5) / SS) / n
                    if inside_rounded(x, y, 0.22):
                        a += 1
                        if in_bar(x, y):
                            fg += 1
            t = SS * SS
            alpha = a / t
            f = fg / a if a else 0
            rgb = tuple(round(BG[i] * (1 - f) + FG[i] * f) for i in range(3))
            line.append((*rgb, round(alpha * 255)))
        px.append(line)
    return px

def bmp_entry(n):
    px = render(n)
    header = struct.pack('<IiiHHIIiiII', 40, n, n * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    data = bytearray()
    for row in reversed(px):  # bottom-up
        for r, g, b, a in row:
            data += bytes((b, g, r, a))
    mask_row = ((n + 31) // 32) * 4
    data += bytes(mask_row * n)  # AND mask unused with 32-bit alpha
    return header + bytes(data)

sizes = [16, 20, 24, 32, 40, 48, 64]
entries = [bmp_entry(n) for n in sizes]
out = bytearray(struct.pack('<HHH', 0, 1, len(sizes)))
offset = 6 + 16 * len(sizes)
for n, e in zip(sizes, entries):
    out += struct.pack('<BBBBHHII', n % 256, n % 256, 0, 0, 1, 32, len(e), offset)
    offset += len(e)
for e in entries:
    out += e
open(sys.argv[1], 'wb').write(out)
print(len(out), 'bytes')
