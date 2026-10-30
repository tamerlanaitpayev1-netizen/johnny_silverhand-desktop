"""
Этап 11: Генератор горизонтальных покадровых PNG-лент (Sprite Strips, 8 кадров по 240x329 = 1920x329) в папке assets/strips/:
  - strip_beam_in.png       (материализация Джонни из лазерных лучей биочипа Relic при запуске)
  - strip_malorian_spin.png (фирменная прокрутка и перезарядка пистолета Malorian Arms 3516 с огненной вспышкой)
  - strip_sleep.png         (режим сна / ожидания с плавающими голограммами Zzz // RELIC STANDBY)
  - strip_walk.png          (8-кадровая походка вдоль верхней рамки окна для режима прогулки Wandering)
"""

import math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance

ROOT_DIR = Path(__file__).resolve().parent.parent
RIG_DIR = ROOT_DIR / "assets" / "rig"
STRIPS_DIR = ROOT_DIR / "assets" / "strips"
STRIPS_DIR.mkdir(parents=True, exist_ok=True)

FW, FH = 240, 329
FRAMES = 8


def _load_rig_part(name: str) -> Image.Image:
    p = RIG_DIR / name
    return Image.open(p).convert("RGBA").resize((FW, FH), Image.Resampling.LANCZOS)


def _assemble_pose(
    head_ang: float = 0.0,
    arm_r_ang: float = 0.0,
    arm_l_ang: float = 0.0,
    leg_l_ang: float = 0.0,
    leg_r_ang: float = 0.0,
    body_dy: int = 0,
) -> Image.Image:
    hair = _load_rig_part("rig_hair_back.png")
    leg_l = _load_rig_part("rig_leg_left.png")
    leg_r = _load_rig_part("rig_leg_right.png")
    lower = _load_rig_part("rig_lower.png")
    torso = _load_rig_part("rig_torso.png")
    arm_r = _load_rig_part("rig_arm_right.png")
    arm_l = _load_rig_part("rig_arm_left.png")
    head = _load_rig_part("rig_head.png")
    glint = _load_rig_part("rig_glasses_glint.png")
    jaw = _load_rig_part("rig_jaw.png")

    canvas = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))

    ll_rot = leg_l.rotate(leg_l_ang, center=(int(FW * 0.42), int(FH * 0.78)), resample=Image.Resampling.BICUBIC)
    lr_rot = leg_r.rotate(leg_r_ang, center=(int(FW * 0.60), int(FH * 0.78)), resample=Image.Resampling.BICUBIC)
    ar_rot = arm_r.rotate(arm_r_ang, center=(int(FW * 0.316), int(FH * 0.408)), resample=Image.Resampling.BICUBIC)
    al_rot = arm_l.rotate(arm_l_ang, center=(int(FW * 0.753), int(FH * 0.408)), resample=Image.Resampling.BICUBIC)
    hd_comb = Image.alpha_composite(Image.alpha_composite(head, glint), jaw)
    hd_rot = hd_comb.rotate(head_ang, center=(int(FW * 0.525), int(FH * 0.255)), resample=Image.Resampling.BICUBIC)
    hr_rot = hair.rotate(-head_ang * 0.7, center=(int(FW * 0.525), int(FH * 0.18)), resample=Image.Resampling.BICUBIC)

    upper = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
    for part in (hr_rot, torso, al_rot, hd_rot, ar_rot):
        upper = Image.alpha_composite(upper, part)

    canvas = Image.alpha_composite(canvas, ll_rot)
    canvas = Image.alpha_composite(canvas, lr_rot)
    canvas = Image.alpha_composite(canvas, lower)

    shifted_upper = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
    shifted_upper.paste(upper, (0, body_dy), upper)
    canvas = Image.alpha_composite(canvas, shifted_upper)
    return canvas


def build_beam_in_strip() -> None:
    strip = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    base_pose = _assemble_pose()
    arr_base = np.array(base_pose, dtype=np.uint8)

    for i in range(FRAMES):
        progress = (i + 1) / FRAMES
        frame = Image.new("RGBA", (FW, FH), (0, 0, 0, 0))
        d = ImageDraw.Draw(frame)

        # Вертикальные лазерные лучи телепортации Relic (#00F0FF и #FF2A55)
        beam_w = int((1.0 - progress * 0.75) * 70) + 6
        cx = FW // 2
        if i < FRAMES - 1:
            d.rectangle((cx - beam_w, 0, cx + beam_w, FH), fill=(0, 240, 255, int((1.0 - progress) * 165)))
            d.line([(cx, 0), (cx, FH)], fill=(252, 238, 9, int((1.0 - progress) * 240)), width=3)

        # Постепенное проявление силуэта сверху вниз со сдвигом полос
        reveal_h = int(FH * progress)
        f_arr = np.zeros_like(arr_base)
        f_arr[:reveal_h] = arr_base[:reveal_h]
        if i < FRAMES - 1:
            # Голубая вспышка на границе сканирования
            y_scan = min(reveal_h - 1, FH - 1)
            f_arr[max(0, y_scan - 4) : y_scan + 1, :, :3] = [0, 240, 255]

        char_layer = Image.fromarray(f_arr, mode="RGBA")
        frame = Image.alpha_composite(frame, char_layer)
        strip.paste(frame, (i * FW, 0))

    strip.save(STRIPS_DIR / "strip_beam_in.png", format="PNG")
    print("[OK] Saved strip_beam_in.png")


def build_malorian_spin_strip() -> None:
    strip = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for i in range(FRAMES):
        angle = i * (360.0 / FRAMES)
        arm_r_ang = -14.0 + math.sin(i * math.pi / 4.0) * 10.0
        head_ang = math.sin(i * math.pi / 4.0) * 3.5
        frame = _assemble_pose(head_ang=head_ang, arm_r_ang=arm_r_ang)

        # Рисуем вращающийся пистолет Malorian Arms 3516 с огненной дульной вспышкой у правой руки
        gun_canvas = Image.new("RGBA", (90, 90), (0, 0, 0, 0))
        gd = ImageDraw.Draw(gun_canvas)
        # Массивный серебристо-чёрный ствол Malorian 3516
        gd.rectangle((22, 34, 76, 48), fill=(195, 208, 222, 255), outline=(0, 240, 255, 255), width=2)
        gd.rectangle((28, 44, 68, 52), fill=(235, 45, 75, 255))
        gd.polygon([(22, 46), (36, 46), (28, 72), (16, 70)], fill=(45, 32, 28, 255), outline=(252, 238, 9, 240), width=1)
        # Кольцо прокрутки на указательном пальце
        gd.ellipse((36, 48, 48, 60), outline=(0, 240, 255, 255), width=2)

        rot_gun = gun_canvas.rotate(angle, resample=Image.Resampling.BICUBIC)
        frame.paste(rot_gun, (2, 82), rot_gun)

        # На кадрах 3 и 7 — яркая огненная вспышка выстрела / выброса гильзы
        if i in (2, 6):
            fd = ImageDraw.Draw(frame)
            fd.polygon([(8, 95), (28, 80), (22, 102), (40, 96), (16, 114)], fill=(252, 238, 9, 235), outline=(255, 42, 85, 255))

        strip.paste(frame, (i * FW, 0))

    strip.save(STRIPS_DIR / "strip_malorian_spin.png", format="PNG")
    print("[OK] Saved strip_malorian_spin.png")


def build_sleep_strip() -> None:
    strip = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for i in range(FRAMES):
        phase = (i / FRAMES) * math.pi * 2.0
        # Голова опущена на грудь, медленное сонное дыхание
        frame = _assemble_pose(
            head_ang=8.5 + math.sin(phase) * 2.0,
            arm_r_ang=4.0,
            arm_l_ang=2.0,
            leg_l_ang=math.sin(phase) * 2.0,
            leg_r_ang=-math.sin(phase) * 2.0,
            body_dy=int(math.sin(phase) * 2),
        )
        d = ImageDraw.Draw(frame)
        # Всплывающие неоновые символы Zzz
        zy = int(68 - (i * 6) % 42)
        zx = int(158 + (i * 4) % 24)
        d.text((zx, zy), "Zzz", fill=(0, 240, 255, 220))
        d.text((zx + 14, zy - 12), "z", fill=(252, 238, 9, 200))
        strip.paste(frame, (i * FW, 0))

    strip.save(STRIPS_DIR / "strip_sleep.png", format="PNG")
    print("[OK] Saved strip_sleep.png")


def build_walk_strip() -> None:
    strip = Image.new("RGBA", (FW * FRAMES, FH), (0, 0, 0, 0))
    for i in range(FRAMES):
        phase = (i / FRAMES) * math.pi * 2.0
        swing = math.sin(phase)
        frame = _assemble_pose(
            head_ang=swing * 2.5,
            arm_r_ang=-swing * 12.0,
            arm_l_ang=swing * 12.0,
            leg_l_ang=swing * 18.0,
            leg_r_ang=-swing * 18.0,
            body_dy=int(abs(swing) * -4),
        )
        strip.paste(frame, (i * FW, 0))

    strip.save(STRIPS_DIR / "strip_walk.png", format="PNG")
    print("[OK] Saved strip_walk.png")


if __name__ == "__main__":
    build_beam_in_strip()
    build_malorian_spin_strip()
    build_sleep_strip()
    build_walk_strip()
