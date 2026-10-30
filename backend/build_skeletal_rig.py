"""
Генератор бесшовного скелетного 2.5D-рига Джонни Сильверхенда (Live2D / Spine стиль) из 3D-рендера Cyberpunk 2077.
Разбивает рендер (600x822) на независимые анатомические слои без внутренних швов:
  - rig_lower.png     (пояс и кожаные штаны)
  - rig_torso.png     (торс в бронежилете Samurai, шея и плечевые суставы)
  - rig_arm_right.png (правая рука с дымящимся биочипом/сигаретой — пивот (186, 395))
  - rig_arm_left.png  (левая серебряная бионическая рука — пивот (452, 335))
  - rig_head.png      (голова в красных очках-авиаторах и волосы — пивот (315, 210))
  - rig_jaw.png       (нижняя челюсть и борода для Lip-Sync — двигается синхронно с голосом)
  - rig_guitar.png    (электрогитара Deluze Orphean из Cyberpunk 2077)
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT_DIR = Path(__file__).resolve().parent.parent
RAW_DIR = ROOT_DIR / "assets" / "raw_renders"
RIG_DIR = ROOT_DIR / "assets" / "rig"
RIG_DIR.mkdir(parents=True, exist_ok=True)

W, H = 600, 822


def polygon_mask(size: tuple[int, int], points: list[tuple[int, int]], blur: float = 2.0) -> Image.Image:
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.polygon(points, fill=255)
    if blur > 0:
        m = m.filter(ImageFilter.GaussianBlur(blur))
    return m


def extract_layer(src: Image.Image, mask: Image.Image) -> Image.Image:
    src_a = np.array(src.split()[3], dtype=np.float32)
    msk_a = np.array(mask, dtype=np.float32) / 255.0
    combined_a = np.clip(src_a * msk_a, 0, 255).astype(np.uint8)
    rgba = src.copy()
    rgba.putalpha(Image.fromarray(combined_a, mode="L"))
    return rgba


def build_rig() -> None:
    src = Image.open(RAW_DIR / "db_2077.png").convert("RGBA").resize((W, H), Image.Resampling.LANCZOS)

    # 1. Правая рука с чипом/сигаретой (плечевой сустав: пивот x=190, y=335)
    arm_right_poly = [
        (0, 240), (208, 295), (200, 355), (186, 430),
        (182, 520), (170, 595), (0, 595)
    ]
    mask_arm_r = polygon_mask((W, H), arm_right_poly, blur=2.2)
    rig_arm_right = extract_layer(src, mask_arm_r)

    # 2. Левая серебряная бионическая рука (пивот кибер-плеча: x=452, y=335)
    arm_left_poly = [
        (438, 285), (600, 285), (600, 822),
        (465, 822), (456, 455), (446, 375)
    ]
    mask_arm_l = polygon_mask((W, H), arm_left_poly, blur=1.8)
    rig_arm_left = extract_layer(src, mask_arm_l)

    # 3. Голова, очки-авиаторы и длинные волосы (пивот шеи: x=315, y=210)
    head_poly = [
        (190, 0), (450, 0), (450, 224),
        (368, 232), (315, 228), (252, 230), (190, 222)
    ]
    mask_head = polygon_mask((W, H), head_poly, blur=2.5)
    rig_head = extract_layer(src, mask_head)

    # 4. Нижняя челюсть / борода для Lip-Sync (открывает рот во время речи)
    jaw_poly = [
        (278, 166), (334, 164), (346, 184),
        (342, 208), (308, 214), (278, 206), (270, 184)
    ]
    mask_jaw = polygon_mask((W, H), jaw_poly, blur=1.8)
    rig_jaw = extract_layer(src, mask_jaw)

    # Мягкая тень полости рта на слое головы (видна только когда rig_jaw опускается вниз)
    mouth_shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ms_draw = ImageDraw.Draw(mouth_shadow)
    ms_draw.ellipse((286, 164, 324, 172), fill=(26, 12, 15, 220))
    mouth_shadow = mouth_shadow.filter(ImageFilter.GaussianBlur(1.2))
    rig_head = Image.alpha_composite(rig_head, mouth_shadow)

    # 5. Нижняя часть тела (ремень и кожаные штаны: с запасом вверх до y=595 под дыхание торса)
    lower_poly = [
        (150, 595), (455, 595), (465, 822), (150, 822)
    ]
    mask_lower = polygon_mask((W, H), lower_poly, blur=2.0)
    rig_lower = extract_layer(src, mask_lower)

    # 6. Торс (бронежилет Samurai, шея с запасом до y=180 и плечи без дублирования рук)
    torso_poly = [
        (225, 180), (395, 180), (452, 285), (456, 425),
        (448, 635), (176, 635), (186, 495), (186, 365), (172, 290)
    ]
    mask_torso = polygon_mask((W, H), torso_poly, blur=2.2)
    rig_torso = extract_layer(src, mask_torso)

    # 7. Электрогитара Deluze Orphean
    guitar_src = Image.open(RAW_DIR / "Silverhand_guitar_render.png").convert("RGBA")
    gw, gh = guitar_src.size
    front_g = guitar_src.crop((gw // 2, 0, gw, gh))
    gbbox = front_g.getbbox()
    if gbbox:
        front_g = front_g.crop(gbbox)
    front_g.thumbnail((310, 540), Image.Resampling.LANCZOS)
    rot_g = front_g.rotate(-52, resample=Image.Resampling.BICUBIC, expand=True)

    rig_guitar = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gx = (W - rot_g.width) // 2 + 18
    gy = H - rot_g.height - 85
    rig_guitar.paste(rot_g, (gx, gy), rot_g)

    layers = {
        "rig_lower.png": rig_lower,
        "rig_torso.png": rig_torso,
        "rig_arm_right.png": rig_arm_right,
        "rig_arm_left.png": rig_arm_left,
        "rig_head.png": rig_head,
        "rig_jaw.png": rig_jaw,
        "rig_guitar.png": rig_guitar,
    }
    for filename, img in layers.items():
        img.save(RIG_DIR / filename, format="PNG")
        print(f"[OK] Saved seamless rig layer: {RIG_DIR / filename}")


if __name__ == "__main__":
    build_rig()
