"""
Обработчик 3D-рендеров Джонни Сильверхенда (REDengine 4 / Cyberpunk 2077 стиль).
Превращает рендеры из assets/raw_renders/ в чистые прозрачные PNG-спрайты с альфа-каналом,
неоновым контурным свечением биочипа Relic (#00F0FF) и хроматическим RGB-глитчем.
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

ROOT_DIR = Path(__file__).resolve().parent.parent
RAW_DIR = ROOT_DIR / "assets" / "raw_renders"
SPRITES_DIR = ROOT_DIR / "assets" / "sprites"
SPRITES_DIR.mkdir(parents=True, exist_ok=True)

TARGET_SIZE = (420, 520)


def extract_alpha_from_black_bg(img: Image.Image) -> Image.Image:
    """Удаляет чёрный студийный фон (#000000) с мягким сглаживанием краёв и сохранением полутеней."""
    rgba = img.convert("RGBA")
    arr = np.array(rgba, dtype=np.float32)
    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]

    # Яркость пикселя (максимум по каналам + взвешенная яркость)
    lum = np.maximum(np.maximum(r, g), b)

    # Порог отсечения фона с плавным переходом (soft matte между яркостью 10 и 38)
    alpha = np.clip((lum - 10.0) / 28.0, 0.0, 1.0)

    # Заполняем внутреннюю область силуэта с помощью морфологического закрытия по маске
    mask_img = Image.fromarray((alpha * 255).astype(np.uint8), mode="L")
    # Убираем мелкий шум на фоне и сглаживаем границу
    mask_closed = mask_img.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5))
    mask_smooth = mask_closed.filter(ImageFilter.GaussianBlur(radius=1.2))

    # Внутри уверенного силуэта сохраняем 100% непрозрачность даже для тёмных волос и жилета
    from scipy.ndimage import binary_fill_holes
    binary_core = np.array(mask_closed) > 42
    filled_core = binary_fill_holes(binary_core)

    final_alpha = np.where(filled_core, np.maximum(np.array(mask_smooth), 245), np.array(mask_smooth))
    arr[:, :, 3] = final_alpha

    # Убираем тёмный ореол по самому краю (de-fringe)
    edge_zone = (final_alpha > 15) & (final_alpha < 230)
    arr[edge_zone, 0] = np.clip(arr[edge_zone, 0] * 0.8, 0, 255)
    arr[edge_zone, 1] = np.clip(arr[edge_zone, 1] * 1.1 + 25, 0, 255)  # лёгкий голубой оттенок Relic на краях
    arr[edge_zone, 2] = np.clip(arr[edge_zone, 2] * 1.2 + 40, 0, 255)

    return Image.fromarray(arr.astype(np.uint8), mode="RGBA")


def apply_relic_hologram_grade(img: Image.Image, add_scanlines: bool = True) -> Image.Image:
    """Добавляет фирменный киберпанк-грейдинг энграммы Relic (контраст, лёгкие голографические линии и контурное свечение)."""
    # Кадрируем по полезной области (bbox) и масштабируем под размер оверлея
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)

    img.thumbnail((TARGET_SIZE[0] - 24, TARGET_SIZE[1] - 24), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", TARGET_SIZE, (0, 0, 0, 0))
    ox = (TARGET_SIZE[0] - img.width) // 2
    oy = TARGET_SIZE[1] - img.height - 6
    canvas.paste(img, (ox, oy), img)

    # Создаём неоновый голубой контур биочипа Relic (#00F0FF) вокруг силуэта
    alpha = canvas.split()[3]
    dilated = alpha.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(3.5))
    glow_layer = Image.new("RGBA", TARGET_SIZE, (0, 240, 255, 0))
    glow_alpha = ImageEnhance.Brightness(dilated).enhance(0.65)
    glow_layer.putalpha(glow_alpha)

    combined = Image.alpha_composite(glow_layer, canvas)

    if add_scanlines:
        # Тонкие полупрозрачные линии сканирования Relic только поверх непрозрачных пикселей персонажа
        arr = np.array(combined, dtype=np.float32)
        for y in range(0, TARGET_SIZE[1], 4):
            mask = arr[y, :, 3] > 20
            arr[y, mask, 0] = np.clip(arr[y, mask, 0] * 0.90, 0, 255)
            arr[y, mask, 1] = np.clip(arr[y, mask, 1] * 0.96 + 8, 0, 255)
            arr[y, mask, 2] = np.clip(arr[y, mask, 2] * 0.98 + 12, 0, 255)
        combined = Image.fromarray(arr.astype(np.uint8), mode="RGBA")

    return combined


def create_relic_glitch_variant(base_sprite: Image.Image) -> Image.Image:
    """Создаёт аутентичный эффект сбоя биочипа Relic (RGB-сдвиг каналов + нарезка полос + жёлто-голубые артефакты)."""
    arr = np.array(base_sprite, dtype=np.uint8)
    h, w, _ = arr.shape
    out = arr.copy()

    # Хроматическая аберрация (сдвиг красного канала влево, голубого вправо)
    shift = 7
    out[:, shift:, 0] = arr[:, :-shift, 0]
    out[:, :-shift, 1] = arr[:, shift:, 1]
    out[:, :-shift, 2] = arr[:, shift:, 2]

    # Горизонтальные сдвиги полос (slice displacement)
    slices = [
        (int(h * 0.14), int(h * 0.21), -16),
        (int(h * 0.34), int(h * 0.42), 18),
        (int(h * 0.56), int(h * 0.63), -14),
        (int(h * 0.75), int(h * 0.82), 15),
    ]
    for y1, y2, dx in slices:
        out[y1:y2] = np.roll(out[y1:y2], dx, axis=1)
        # Подсвечиваем края сбоя жёлтым (#FCEE09) и голубым (#00F0FF)
        mask = out[y1, :, 3] > 30
        out[y1 : min(y1 + 3, h), mask, :3] = [252, 238, 9]
        mask2 = out[y2 - 1, :, 3] > 30
        out[max(y2 - 3, 0) : y2, mask2, :3] = [0, 240, 255]

    return Image.fromarray(out, mode="RGBA")


def process_all() -> None:
    mapping = {
        "idle": RAW_DIR / "raw_idle.png",
        "talk": RAW_DIR / "raw_talk.png",
        "guitar": RAW_DIR / "raw_guitar.png",
        "drag": RAW_DIR / "raw_drag.png",
    }

    processed_sprites = {}
    for state, raw_path in mapping.items():
        if not raw_path.exists():
            print(f"[WARN] Missing raw render: {raw_path}")
            continue
        raw_img = Image.open(raw_path)
        cutout = extract_alpha_from_black_bg(raw_img)
        graded = apply_relic_hologram_grade(cutout, add_scanlines=True)
        out_path = SPRITES_DIR / f"johnny_{state}.png"
        graded.save(out_path, format="PNG")
        processed_sprites[state] = graded
        print(f"[OK] Saved {out_path}")

    # Генерируем johnny_glitch.png на основе idle или talk
    base_for_glitch = processed_sprites.get("talk") or processed_sprites.get("idle")
    if base_for_glitch is not None:
        glitch_img = create_relic_glitch_variant(base_for_glitch)
        glitch_path = SPRITES_DIR / "johnny_glitch.png"
        glitch_img.save(glitch_path, format="PNG")
        print(f"[OK] Saved {glitch_path}")


if __name__ == "__main__":
    process_all()
