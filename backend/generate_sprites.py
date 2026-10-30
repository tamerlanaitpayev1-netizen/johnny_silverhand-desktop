"""
Генератор стартовых киберпанк-спрайтов Джонни Сильверхенда (Этап 5) и иконки трея (Этап 7).
Создаёт 5 прозрачных PNG-спрайтов в папке assets/sprites/:
  - johnny_idle.png   (стоит со скрещенными руками и дымящейся сигаретой, очки-авиаторы)
  - johnny_talk.png   (разговаривает и жестикулирует серебряной бионической рукой)
  - johnny_drag.png   (недовольная поза при перетаскивании курсором мыши)
  - johnny_guitar.png (играет рифф на электрогитаре Samurai)
  - johnny_glitch.png (сбой биочипа Relic с сине-жёлтым хроматическим сдвигом и полосами помех)
  - relic_tray.ico    (иконка биочипа Relic для системного трея Windows)
"""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT_DIR = Path(__file__).resolve().parent.parent
SPRITES_DIR = ROOT_DIR / "assets" / "sprites"
SPRITES_DIR.mkdir(parents=True, exist_ok=True)

W, H = 220, 260


def _draw_base_johnny(draw: ImageDraw.ImageDraw, mode: str) -> None:
    # Цветовая палитра энграммы Cyberpunk 2077
    cyan = (0, 240, 255, 220)
    cyan_glow = (0, 240, 255, 65)
    yellow = (252, 238, 9, 235)
    red_lens = (235, 45, 75, 230)
    skin = (210, 175, 150, 235)
    hair = (22, 26, 34, 245)
    vest = (32, 36, 44, 245)
    pants = (25, 28, 36, 245)
    silver = (200, 215, 230, 250)
    silver_dark = (120, 138, 155, 250)

    # Голографический ореол-подложка
    draw.rounded_rectangle((28, 18, W - 28, H - 12), radius=14, fill=(8, 18, 28, 110), outline=cyan, width=2)

    # Голографические линии сканирования
    for y in range(24, H - 16, 8):
        draw.line([(32, y), (W - 32, y)], fill=(0, 240, 255, 28), width=1)

    dy = -6 if mode == "drag" else 0

    # Ноги и тяжёлые рокерские ботинки
    draw.rectangle((82, 182 + dy, 103, 232 + dy), fill=pants, outline=cyan, width=1)
    draw.rectangle((117, 182 + dy, 138, 232 + dy), fill=pants, outline=cyan, width=1)
    draw.rectangle((78, 226 + dy, 105, 242 + dy), fill=(18, 20, 25, 255), outline=silver_dark, width=1)
    draw.rectangle((115, 226 + dy, 142, 242 + dy), fill=(18, 20, 25, 255), outline=silver_dark, width=1)

    # Ремень
    draw.rectangle((76, 172 + dy, 144, 183 + dy), fill=(45, 35, 30, 250), outline=silver, width=1)
    draw.rectangle((103, 172 + dy, 117, 183 + dy), fill=silver, outline=yellow, width=1)

    # Бронежилет Samurai
    draw.polygon([(72, 102 + dy), (148, 102 + dy), (142, 172 + dy), (78, 172 + dy)], fill=vest, outline=cyan)
    # Воротник и наплечники
    draw.polygon([(66, 96 + dy), (92, 104 + dy), (74, 116 + dy)], fill=(45, 50, 62, 250), outline=cyan)
    draw.polygon([(154, 96 + dy), (128, 104 + dy), (146, 116 + dy)], fill=(45, 50, 62, 250), outline=cyan)

    # Армейские жетоны на груди
    draw.line([(98, 102 + dy), (106, 128 + dy)], fill=silver, width=2)
    draw.line([(122, 102 + dy), (114, 128 + dy)], fill=silver, width=2)
    draw.rectangle((103, 126 + dy, 117, 138 + dy), fill=silver, outline=silver_dark, width=1)

    # Голова и борода
    draw.rounded_rectangle((86, 42 + dy, 134, 96 + dy), radius=10, fill=skin, outline=cyan, width=1)
    # Эспаньолка и щетина рокера
    draw.polygon([(88, 78 + dy), (132, 78 + dy), (124, 96 + dy), (96, 96 + dy)], fill=hair)
    draw.rectangle((100, 80 + dy, 120, 85 + dy), fill=skin)
    if mode == "talk":
        draw.rectangle((102, 81 + dy, 118, 88 + dy), fill=(30, 10, 15, 255), outline=cyan, width=1)
    elif mode == "drag":
        draw.line([(100, 85 + dy), (120, 82 + dy)], fill=yellow, width=2)

    # Длинные тёмные волосы Джонни
    draw.polygon([(78, 40 + dy), (142, 40 + dy), (148, 104 + dy), (133, 104 + dy), (131, 54 + dy), (89, 54 + dy), (87, 104 + dy), (72, 104 + dy)], fill=hair, outline=cyan)

    # Культовые очки-авиаторы с красными линзами
    draw.rectangle((89, 58 + dy, 108, 71 + dy), fill=red_lens, outline=silver, width=2)
    draw.rectangle((112, 58 + dy, 131, 71 + dy), fill=red_lens, outline=silver, width=2)
    draw.line([(108, 62 + dy), (112, 62 + dy)], fill=silver, width=2)

    # Позы рук в зависимости от состояния
    if mode == "idle":
        # Правая обычная рука + сигарета с голубым дымом энграммы
        draw.polygon([(56, 105), (74, 105), (84, 150), (66, 150)], fill=skin, outline=cyan)
        # Левая серебряная бионическая рука (скрещена)
        draw.polygon([(146, 105), (164, 105), (150, 152), (110, 148), (110, 134), (138, 136)], fill=silver, outline=cyan)
        draw.line([(148, 118), (160, 120)], fill=silver_dark, width=2)
        draw.line([(144, 130), (156, 132)], fill=silver_dark, width=2)
        # Сигарета и дымок
        draw.line([(116, 84), (136, 88)], fill=(245, 245, 245, 255), width=3)
        draw.ellipse((134, 85, 140, 91), fill=(255, 90, 20, 255))
        draw.arc((134, 52, 156, 84), start=200, end=40, fill=cyan, width=2)

    elif mode == "talk":
        # Правая рука на поясе
        draw.polygon([(58, 106), (74, 106), (78, 158), (62, 158)], fill=skin, outline=cyan)
        # Серебряная рука активно жестикулирует вверх
        draw.polygon([(146, 104), (164, 100), (182, 74), (168, 66), (148, 92)], fill=silver, outline=yellow)
        draw.line([(154, 94), (170, 82)], fill=cyan, width=2)
        # Звуковые волны вокруг головы
        draw.arc((142, 46, 172, 78), start=-50, end=50, fill=yellow, width=2)
        draw.arc((150, 40, 186, 84), start=-50, end=50, fill=cyan, width=2)

    elif mode == "drag":
        # Руки разведены в стороны (недоволен тем, что его тащат курсором)
        draw.polygon([(40, 82), (54, 76), (76, 106), (62, 114)], fill=skin, outline=yellow)
        draw.polygon([(146, 106), (160, 114), (182, 82), (168, 74)], fill=silver, outline=yellow)
        draw.text((70, 20), "ЭЙ, ЧУМБА!", fill=yellow)

    elif mode == "guitar":
        # Руки держат электрогитару Samurai
        draw.polygon([(56, 108), (74, 108), (94, 148), (78, 154)], fill=skin, outline=cyan)
        draw.polygon([(146, 106), (164, 112), (144, 148), (128, 142)], fill=silver, outline=cyan)
        # Корпус гитары и гриф
        draw.polygon([(64, 134), (126, 124), (138, 166), (74, 178)], fill=(195, 25, 45, 245), outline=silver, width=2)
        draw.line([(98, 146), (186, 104)], fill=silver, width=6)
        draw.line([(98, 146), (186, 104)], fill=cyan, width=2)
        # Неоновые молнии риффа
        draw.line([(44, 95), (58, 108), (48, 114), (62, 126)], fill=yellow, width=2)
        draw.line([(168, 62), (182, 74), (172, 80), (188, 92)], fill=cyan, width=2)

    elif mode == "glitch":
        # Базовая поза со смещением бионической руки
        draw.polygon([(56, 105), (74, 105), (84, 150), (66, 150)], fill=skin, outline=yellow)
        draw.polygon([(146, 105), (166, 105), (156, 154), (138, 150)], fill=silver, outline=yellow)

    # Подпись статуса внизу голограммы
    label_map = {
        "idle": "RELIC // IDLE",
        "talk": "RELIC // VOICE SYNC",
        "drag": "RELIC // MOVING",
        "guitar": "SAMURAI // RIFF",
        "glitch": "⚠ RELIC MALFUNCTION",
    }
    badge_color = yellow if mode in ("glitch", "drag", "guitar") else cyan
    draw.text((54, H - 28), label_map.get(mode, "RELIC"), fill=badge_color)


def generate_all_sprites() -> None:
    modes = ("idle", "talk", "drag", "guitar", "glitch")
    for mode in modes:
        img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        draw = ImageDraw.Draw(img)
        _draw_base_johnny(draw, mode)

        if mode == "glitch":
            # Нарезаем горизонтальные полосы со сдвигом и жёлто-голубыми помехами Relic
            glitched = img.copy()
            slices = [(45, 62, -10), (88, 104, 12), (136, 156, -8), (185, 202, 10)]
            for y1, y2, dx in slices:
                strip = img.crop((0, y1, W, y2))
                glitched.paste((0, 0, 0, 0), (0, y1, W, y2))
                glitched.paste(strip, (dx, y1))
            g_draw = ImageDraw.Draw(glitched)
            for y in (52, 96, 144, 192):
                g_draw.line([(18, y), (W - 18, y)], fill=(252, 238, 9, 220), width=2)
                g_draw.line([(24, y + 3), (W - 24, y + 3)], fill=(0, 240, 255, 220), width=2)
            img = glitched

        out_path = SPRITES_DIR / f"johnny_{mode}.png"
        img.save(out_path, format="PNG")

    # Генерируем иконку биочипа Relic (.ico) для системного трея Windows
    ico_img = Image.new("RGBA", (64, 64), (13, 17, 23, 255))
    ico_draw = ImageDraw.Draw(ico_img)
    ico_draw.rounded_rectangle((4, 4, 60, 60), radius=10, fill=(13, 17, 23, 255), outline=(0, 240, 255, 255), width=3)
    ico_draw.polygon([(32, 12), (50, 32), (32, 52), (14, 32)], fill=(235, 45, 75, 240), outline=(252, 238, 9, 255))
    ico_draw.line([(32, 8), (32, 56)], fill=(0, 240, 255, 255), width=2)
    ico_draw.line([(8, 32), (56, 32)], fill=(0, 240, 255, 255), width=2)
    ico_img.save(SPRITES_DIR / "relic_tray.ico", format="ICO", sizes=[(64, 64), (32, 32), (16, 16)])


if __name__ == "__main__":
    generate_all_sprites()
    print(f"Sprites generated in: {SPRITES_DIR}")

