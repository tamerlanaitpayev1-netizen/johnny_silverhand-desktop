"""
Генератор 10-слойного скелетного 2.5D-рига Джонни Сильверхенда в стиле Capri (Edgerunners / capri-desktop).
Превращает 3D-рендер Cyberpunk 2077 в контрастный аниме/киберпанк-риг (Cel-Shaded + неоновый контур Relic)
и разделяет модель (600x822) на независимые конечности для 60 FPS физики:
  - rig_hair_back.png     (задние пряди длинных волос — развеваются с инерцией при движении и ветре)
  - rig_leg_left.png      (левая нога в ботинке — свешивается с рамки окна и качается как у Капри)
  - rig_leg_right.png     (правая нога в ботинке — качается в противофазе и болтается при перетаскивании)
  - rig_lower.png         (таз и ремень — точка опоры на верхней границе активного окна Windows)
  - rig_torso.png         (торс в бронежилете Samurai, шея и плечи)
  - rig_arm_right.png     (правая рука с дымящейся сигаретой — пивот (190, 335))
  - rig_arm_left.png      (левая серебряная бионическая рука — пивот (452, 335))
  - rig_head.png          (голова и очки-авиаторы — пивот (315, 210))
  - rig_glasses_glint.png (неоновый блик авиаторов и моргание/свечение глаз энграммы)
  - rig_jaw.png           (нижняя челюсть и борода для слогового Lip-Sync)
  - rig_guitar.png        (электрогитара Deluze Orphean из Cyberpunk 2077)
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT_DIR = Path(__file__).resolve().parent.parent
RAW_DIR = ROOT_DIR / "assets" / "raw_renders"
RIG_DIR = ROOT_DIR / "assets" / "rig"
RIG_DIR.mkdir(parents=True, exist_ok=True)

W, H = 600, 822


def apply_edgerunners_cel_shading(src: Image.Image) -> Image.Image:
    """
    Придаёт 3D-рендеру выразительный контрастный вид в духе Cyberpunk: Edgerunners / Capri:
    усиливает контурный свет (cyan/crimson rim light), подчёркивает тени и делает силуэт чётким на любом фоне.
    """
    rgba = src.convert("RGBA")
    r, g, b, a = rgba.split()
    rgb = Image.merge("RGB", (r, g, b))

    # Повышаем локальный контраст и насыщенность (аниме-стилизация)
    rgb = ImageEnhance.Contrast(rgb).enhance(1.22)
    rgb = ImageEnhance.Color(rgb).enhance(1.18)
    rgb = ImageEnhance.Sharpness(rgb).enhance(1.35)

    # Лёгкая постеризация полутонов в сочетании с оригиналом (эффект Cel-Shading без потери деталей лица)
    arr_orig = np.array(rgb, dtype=np.float32)
    quantized = np.floor(arr_orig / 24.0) * 24.0 + 12.0
    blended = np.clip(arr_orig * 0.72 + quantized * 0.28, 0, 255).astype(np.uint8)

    # Добавляем тонкий неоновый контур Relic (#00F0FF слева, #FF2A55 справа, как в ролике с Капри)
    alpha_np = np.array(a, dtype=np.float32)
    edge_outer = np.array(a.filter(ImageFilter.MaxFilter(5)), dtype=np.float32)
    rim_mask = np.clip((edge_outer - alpha_np * 0.75) / 255.0, 0.0, 1.0)

    h, w = alpha_np.shape
    x_grad = np.linspace(0.0, 1.0, w, dtype=np.float32)[None, :]
    # Слева — голубой неон Relic (0, 240, 255), справа — малиново-жёлтый акцент Edgerunners (255, 45, 95)
    rim_r = (1.0 - x_grad) * 0.0 + x_grad * 255.0
    rim_g = (1.0 - x_grad) * 240.0 + x_grad * 65.0
    rim_b = (1.0 - x_grad) * 255.0 + x_grad * 110.0

    for c_idx, rim_c in enumerate((rim_r, rim_g, rim_b)):
        blended[:, :, c_idx] = np.clip(
            blended[:, :, c_idx] * (1.0 - rim_mask * 0.55) + rim_c * (rim_mask * 0.55),
            0, 255
        ).astype(np.uint8)

    out = Image.fromarray(blended, mode="RGB").convert("RGBA")
    out.putalpha(a)
    return out


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


def create_hair_back_layer(src: Image.Image) -> Image.Image:
    """Выделяет боковые и задние пряди длинных волос Джонни для независимого покачивания на ветру."""
    hair_poly = [
        (195, 35), (440, 35), (452, 238),
        (415, 252), (370, 220), (255, 220),
        (210, 252), (185, 235)
    ]
    mask = polygon_mask((W, H), hair_poly, blur=3.0)
    layer = extract_layer(src, mask)
    # Слегка затемняем задний слой волос для глубины 2.5D параллакса
    r, g, b, a = layer.split()
    rgb = ImageEnhance.Brightness(Image.merge("RGB", (r, g, b))).enhance(0.82)
    out = rgb.convert("RGBA")
    out.putalpha(a)
    return out


def create_glasses_glint_layer() -> Image.Image:
    """Создаёт слой неонового блика на красных очках-авиаторах и свечения глаз энграммы (для моргания/реакций)."""
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Левая и правая линзы авиаторов Джонни на холсте 600x822
    # Косые белые/голубые блики в стиле аниме Edgerunners
    d.polygon([(262, 106), (276, 106), (260, 134), (248, 134)], fill=(0, 240, 255, 195))
    d.polygon([(334, 105), (348, 105), (332, 133), (320, 133)], fill=(0, 240, 255, 195))

    # Тонкие жёлтые кибер-дужки свечения поверх оправы
    d.line([(244, 102), (300, 104)], fill=(252, 238, 9, 210), width=2)
    d.line([(314, 104), (368, 102)], fill=(252, 238, 9, 210), width=2)

    return img.filter(ImageFilter.GaussianBlur(0.8))


def build_rig() -> None:
    raw_path = RAW_DIR / "db_2077.png"
    if not raw_path.exists():
        raise FileNotFoundError(f"Не найден исходный рендер: {raw_path}")

    raw_src = Image.open(raw_path).convert("RGBA").resize((W, H), Image.Resampling.LANCZOS)
    src = apply_edgerunners_cel_shading(raw_src)

    # 1. Правая рука с чипом/сигаретой (плечевой сустав: пивот x=190, y=335)
    arm_right_poly = [
        (0, 240), (208, 295), (200, 355), (186, 430),
        (182, 520), (170, 595), (0, 595)
    ]
    rig_arm_right = extract_layer(src, polygon_mask((W, H), arm_right_poly, blur=2.2))

    # 2. Левая серебряная бионическая рука (пивот кибер-плеча: x=452, y=335)
    arm_left_poly = [
        (438, 285), (600, 285), (600, 822),
        (465, 822), (456, 455), (446, 375)
    ]
    rig_arm_left = extract_layer(src, polygon_mask((W, H), arm_left_poly, blur=1.8))

    # 3. Задние пряди волос (развеваются независимо от головы)
    rig_hair_back = create_hair_back_layer(src)

    # 4. Голова, очки-авиаторы и основные волосы (пивот шеи: x=315, y=210)
    head_poly = [
        (190, 0), (450, 0), (450, 224),
        (368, 232), (315, 228), (252, 230), (190, 222)
    ]
    rig_head = extract_layer(src, polygon_mask((W, H), head_poly, blur=2.5))

    # 5. Блик очков-авиаторов (моргание и кибер-свечение)
    rig_glasses_glint = create_glasses_glint_layer()

    # 6. Нижняя челюсть / борода для Lip-Sync (открывает рот во время речи)
    jaw_poly = [
        (278, 166), (334, 164), (346, 184),
        (342, 208), (308, 214), (278, 206), (270, 184)
    ]
    rig_jaw = extract_layer(src, polygon_mask((W, H), jaw_poly, blur=1.8))

    # Мягкая тень полости рта на слое головы (видна когда rig_jaw опускается вниз)
    mouth_shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ms_draw = ImageDraw.Draw(mouth_shadow)
    ms_draw.ellipse((286, 164, 324, 172), fill=(26, 12, 15, 220))
    mouth_shadow = mouth_shadow.filter(ImageFilter.GaussianBlur(1.2))
    rig_head = Image.alpha_composite(rig_head, mouth_shadow)

    # 7. Пояс / таз (точка посадки на верхний край окна: y=585..665)
    pelvis_poly = [
        (165, 585), (450, 585), (455, 668), (165, 668)
    ]
    rig_lower = extract_layer(src, polygon_mask((W, H), pelvis_poly, blur=2.0))

    # 8. Левая и правая ноги отдельно (пивот на уровне бедра/колена y=650 — качаются над заголовком окна!)
    leg_left_poly = [
        (150, 645), (312, 645), (312, 822), (150, 822)
    ]
    leg_right_poly = [
        (308, 645), (470, 645), (470, 822), (308, 822)
    ]
    rig_leg_left = extract_layer(src, polygon_mask((W, H), leg_left_poly, blur=2.0))
    rig_leg_right = extract_layer(src, polygon_mask((W, H), leg_right_poly, blur=2.0))

    # 9. Торс (бронежилет Samurai, шея с запасом до y=180 и плечи)
    torso_poly = [
        (225, 180), (395, 180), (452, 285), (456, 425),
        (448, 620), (176, 620), (186, 495), (186, 365), (172, 290)
    ]
    rig_torso = extract_layer(src, polygon_mask((W, H), torso_poly, blur=2.2))

    # 10. Электрогитара Deluze Orphean
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
        "rig_hair_back.png": rig_hair_back,
        "rig_leg_left.png": rig_leg_left,
        "rig_leg_right.png": rig_leg_right,
        "rig_lower.png": rig_lower,
        "rig_torso.png": rig_torso,
        "rig_arm_right.png": rig_arm_right,
        "rig_arm_left.png": rig_arm_left,
        "rig_head.png": rig_head,
        "rig_glasses_glint.png": rig_glasses_glint,
        "rig_jaw.png": rig_jaw,
        "rig_guitar.png": rig_guitar,
    }
    for filename, img in layers.items():
        img.save(RIG_DIR / filename, format="PNG")
        print(f"[OK] Saved Capri-style rig layer: {RIG_DIR / filename}")


if __name__ == "__main__":
    build_rig()
