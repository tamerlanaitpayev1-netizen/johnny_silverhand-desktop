"""
Сборка 5 игровых 3D-состояний Джонни Сильверхенда из оригинальных рендеров Cyberpunk 2077 (REDengine 4).
Создаёт в assets/sprites/:
  - johnny_idle.png   (оригинальный 3D-рендер Джонни в очках-авиаторах с дымящимся чипом/сигаретой)
  - johnny_talk.png   (оригинальный 3D-рендер Джонни со снятыми очками в руке во время диалога)
  - johnny_drag.png   (крупный план 3D-рендера Джонни с суровым взглядом при перетаскивании)
  - johnny_guitar.png (3D-рендер Джонни с его культовой электрогитарой Deluze Orphean из Cyberpunk 2077)
  - johnny_glitch.png (эффект сбоя биочипа Relic: RGB-хроматическая аберрация + сдвиг полос #00F0FF / #FCEE09)
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

ROOT_DIR = Path(__file__).resolve().parent.parent
RAW_DIR = ROOT_DIR / "assets" / "raw_renders"
SPRITES_DIR = ROOT_DIR / "assets" / "sprites"
SPRITES_DIR.mkdir(parents=True, exist_ok=True)

TARGET_SIZE = (480, 600)


def fit_and_grade_render(img: Image.Image, add_relic_rim: bool = True) -> Image.Image:
    """Кадрирует прозрачный 3D-рендер из Cyberpunk 2077, масштабирует в высоком разрешении и добавляет контур Relic."""
    rgba = img.convert("RGBA")
    bbox = rgba.getbbox()
    if bbox:
        rgba = rgba.crop(bbox)

    rgba.thumbnail((TARGET_SIZE[0] - 28, TARGET_SIZE[1] - 20), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", TARGET_SIZE, (0, 0, 0, 0))
    ox = (TARGET_SIZE[0] - rgba.width) // 2
    oy = TARGET_SIZE[1] - rgba.height - 4
    canvas.paste(rgba, (ox, oy), rgba)

    if not add_relic_rim:
        return canvas

    # Создаём мягкое голубое свечение энграммы Relic (#00F0FF) вокруг контура игровой модели
    alpha = canvas.split()[3]
    dilated = alpha.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(4.0))
    glow = Image.new("RGBA", TARGET_SIZE, (0, 240, 255, 0))
    glow.putalpha(ImageEnhance.Brightness(dilated).enhance(0.55))

    combined = Image.alpha_composite(glow, canvas)

    # Едва заметные голографические линии сканирования Relic поверх модели
    arr = np.array(combined, dtype=np.float32)
    for y in range(0, TARGET_SIZE[1], 4):
        mask = arr[y, :, 3] > 25
        arr[y, mask, 0] = np.clip(arr[y, mask, 0] * 0.94, 0, 255)
        arr[y, mask, 1] = np.clip(arr[y, mask, 1] * 0.98 + 5, 0, 255)
        arr[y, mask, 2] = np.clip(arr[y, mask, 2] * 1.00 + 9, 0, 255)

    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


def build_guitar_pose(johnny_img: Image.Image, guitar_render: Image.Image) -> Image.Image:
    """Совмещает 3D-рендер Джонни с рендером его игровой электрогитары Deluze Orphean."""
    base = fit_and_grade_render(johnny_img, add_relic_rim=True)

    # Вырезаем правую (лицевую) половину рендера гитары Silverhand_guitar_render.png
    g_rgba = guitar_render.convert("RGBA")
    gw, gh = g_rgba.size
    front_guitar = g_rgba.crop((gw // 2, 0, gw, gh))
    gbbox = front_guitar.getbbox()
    if gbbox:
        front_guitar = front_guitar.crop(gbbox)

    # Масштабируем и поворачиваем электрогитару в рок-н-ролльный наклон на груди Джонни
    front_guitar.thumbnail((250, 440), Image.Resampling.LANCZOS)
    rotated_guitar = front_guitar.rotate(-52, resample=Image.Resampling.BICUBIC, expand=True)

    # Добавляем неоновый жёлто-голубой контур вокруг гитары
    g_alpha = rotated_guitar.split()[3]
    g_glow_mask = g_alpha.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(3.0))
    g_glow = Image.new("RGBA", rotated_guitar.size, (252, 238, 9, 0))
    g_glow.putalpha(ImageEnhance.Brightness(g_glow_mask).enhance(0.75))
    guitar_layered = Image.alpha_composite(g_glow, rotated_guitar)

    out = base.copy()
    gx = (TARGET_SIZE[0] - guitar_layered.width) // 2 + 12
    gy = TARGET_SIZE[1] - guitar_layered.height - 25
    out.paste(guitar_layered, (gx, gy), guitar_layered)
    return out


def create_relic_glitch_variant(base_sprite: Image.Image) -> Image.Image:
    """Аутентичный сбой биочипа Relic из Cyberpunk 2077: RGB-сдвиг и горизонтальные разрывы голограммы."""
    arr = np.array(base_sprite, dtype=np.uint8)
    h, w, _ = arr.shape
    out = arr.copy()

    # Хроматическая аберрация (сдвиг красного и голубого каналов)
    shift = 9
    out[:, shift:, 0] = arr[:, :-shift, 0]
    out[:, :-shift, 1] = arr[:, shift:, 1]
    out[:, :-shift, 2] = arr[:, shift:, 2]

    # Сдвиги полос (Relic Malfunction)
    slices = [
        (int(h * 0.12), int(h * 0.19), -18),
        (int(h * 0.31), int(h * 0.39), 22),
        (int(h * 0.52), int(h * 0.60), -16),
        (int(h * 0.72), int(h * 0.79), 19),
    ]
    for y1, y2, dx in slices:
        out[y1:y2] = np.roll(out[y1:y2], dx, axis=1)
        mask = out[y1, :, 3] > 30
        out[y1 : min(y1 + 3, h), mask, :3] = [252, 238, 9]
        mask2 = out[y2 - 1, :, 3] > 30
        out[max(y2 - 3, 0) : y2, mask2, :3] = [0, 240, 255]

    return Image.fromarray(out, mode="RGBA")


def build_all_game_sprites() -> None:
    idle_raw = Image.open(RAW_DIR / "db_2077.png")
    talk_raw = Image.open(RAW_DIR / "cosplay_01.png")
    drag_raw = Image.open(RAW_DIR / "Silverhand_render.png")
    guitar_raw = Image.open(RAW_DIR / "Silverhand_guitar_render.png")

    idle_sprite = fit_and_grade_render(idle_raw)
    talk_sprite = fit_and_grade_render(talk_raw)
    drag_sprite = fit_and_grade_render(drag_raw)
    guitar_sprite = build_guitar_pose(idle_raw, guitar_raw)
    glitch_sprite = create_relic_glitch_variant(idle_sprite)

    sprites = {
        "idle": idle_sprite,
        "talk": talk_sprite,
        "drag": drag_sprite,
        "guitar": guitar_sprite,
        "glitch": glitch_sprite,
    }

    for name, sp in sprites.items():
        out_path = SPRITES_DIR / f"johnny_{name}.png"
        sp.save(out_path, format="PNG")
        print(f"[OK] Saved authentic game sprite: {out_path} ({sp.size})")


if __name__ == "__main__":
    build_all_game_sprites()
