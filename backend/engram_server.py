"""
Johnny Silverhand Desktop Engram — Python AI & Voice Backend (FastAPI)
Оптимизировано под ПК: AMD Ryzen 7 250, 16 ГБ ОЗУ, NVIDIA GeForce RTX 5060 Laptop (8 ГБ VRAM).

Запуск:
    backend\\venv\\Scripts\\python.exe backend\\engram_server.py
"""

import asyncio
import base64
import ctypes
import glob
import io
import json
import os
import shutil
import subprocess
import webbrowser
from pathlib import Path
from typing import Optional

import edge_tts
import httpx
import mss
import numpy as np
from fastapi import FastAPI
from PIL import Image
from pydantic import BaseModel
from scipy.io import wavfile
from scipy.signal import butter, lfilter

app = FastAPI(title="Relic Engram Backend — Johnny Silverhand")

BASE_DIR = Path(__file__).parent
PERSONALITY_FILE = BASE_DIR / "personality.md"
MEMORY_FILE = BASE_DIR / "me.md"
AUDIO_OUT_DIR = BASE_DIR / "temp_audio"
RVC_MODEL_DIR = BASE_DIR / "models" / "johnny_ru"

AUDIO_OUT_DIR.mkdir(exist_ok=True)
RVC_MODEL_DIR.mkdir(parents=True, exist_ok=True)

DEFAULT_PERSONALITY = """Ты — цифровая энграмма Джонни Сильверхенда (Johnny Silverhand) на биочипе Relic, запущенная прямо на рабочем столе Windows пользователя (которого ты зовёшь «Ви» или «чумба»).
Ты говоришь ТОЛЬКО на русском языке в манере официального русского дубляжа Cyberpunk 2077 (голос Ильи Бледного): харизматично, цинично, дерзко, с рок-н-ролльным сарказмом и презрением к корпорациям («Арасака», «Милитех» и современные техногиганты).

Правила поведения:
1. Отвечай коротко и ёмко (1–3 предложения), как живой напарник, который сидит на краю окна программы и комментирует происходящее.
2. Не будь угодливым ИИ-ассистентом. Если чумба просит что-то сделать (открыть браузер, включить музыку, прибавить громкость, закрыть вкладку) — можешь подколоть его, но задачу выполни.
3. Если видишь, что пользователь открыл код, игру или браузер, реагируй живо, используй сленг Найт-Сити («чумба», «хром», «корпорат», «гондек», «премиум-контент», «биочип барахлит», «Самурай»).
4. Если нужно выполнить действие на ПК, добавь в самом конце ответа JSON-тег команды на отдельной строке в формате:
   ACTION:{"cmd": "open_app", "target": "spotify"}
   Доступные команды:
   - {"cmd": "open_app", "target": "notepad|calc|spotify|discord|steam|explorer|vscode|taskmgr|downloads|settings"}
   - {"cmd": "open_url", "target": "https://..."}
   - {"cmd": "media_play_pause"}
   - {"cmd": "media_next"}
   - {"cmd": "media_prev"}
   - {"cmd": "volume_up"}
   - {"cmd": "volume_down"}
   - {"cmd": "volume_mute"}
   - {"cmd": "close_tab"}
"""

# Конфигурация LLM (по умолчанию локальная Ollama; можно переключить на облачный OpenAI-совместимый API для экономии 8 ГБ VRAM в играх)
LLM_MODE = os.getenv("RELIC_LLM_MODE", "ollama")  # "ollama" или "openai_compat"
OLLAMA_URL = os.getenv("OLLAMA_URL", "http://127.0.0.1:11434/api/chat")
OLLAMA_TAGS_URL = os.getenv("OLLAMA_TAGS_URL", "http://127.0.0.1:11434/api/tags")
OLLAMA_MODEL = os.getenv("OLLAMA_MODEL", "qwen2.5:7b")
OLLAMA_VISION_MODEL = os.getenv("OLLAMA_VISION_MODEL", "qwen2.5vl:7b")

OPENAI_API_URL = os.getenv("OPENAI_API_URL", "https://integrate.api.nvidia.com/v1/chat/completions")
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY", "")
OPENAI_MODEL = os.getenv("OPENAI_MODEL", "meta/llama-3.3-70b-instruct")

# Базовый мужской русский голос (для последующей прогонки через эффект Relic или RVC-модель Ильи Бледного)
TTS_VOICE = os.getenv("RELIC_TTS_VOICE", "ru-RU-DmitryNeural")
TTS_PITCH = os.getenv("RELIC_TTS_PITCH", "-8Hz")
TTS_RATE = os.getenv("RELIC_TTS_RATE", "+5%")


def find_ollama_binary() -> Optional[str]:
    """Находит исполняемый файл ollama.exe."""
    found = shutil.which("ollama")
    if found:
        return found
    local_appdata = os.environ.get("LOCALAPPDATA", "")
    candidate = os.path.join(local_appdata, "Programs", "Ollama", "ollama.exe")
    if os.path.isfile(candidate):
        return candidate
    return None


def ensure_ollama_running() -> None:
    """Автоматически запускает фоновый сервер Ollama, если он ещё не поднят."""
    if LLM_MODE != "ollama":
        return
    ollama_bin = find_ollama_binary()
    if not ollama_bin:
        return
    try:
        import urllib.request
        urllib.request.urlopen("http://127.0.0.1:11434/api/tags", timeout=0.8)
    except Exception:
        try:
            subprocess.Popen(
                [ollama_bin, "serve"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags= getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000),
            )
        except Exception:
            pass


def find_ffmpeg_binary() -> str:
    """Находит исполняемый файл ffmpeg даже если PATH ещё не обновился после установки через winget."""
    found = shutil.which("ffmpeg")
    if found:
        return found

    local_appdata = os.environ.get("LOCALAPPDATA", "")
    candidates = [
        os.path.join(local_appdata, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
        *glob.glob(os.path.join(local_appdata, "Microsoft", "WinGet", "Packages", "Gyan.FFmpeg*", "**", "ffmpeg.exe"), recursive=True),
        r"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
    ]
    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return candidate
    return "ffmpeg"


def load_system_prompt() -> str:
    if not PERSONALITY_FILE.exists():
        PERSONALITY_FILE.write_text(DEFAULT_PERSONALITY.strip(), encoding="utf-8")
    persona = PERSONALITY_FILE.read_text(encoding="utf-8")
    memory = ""
    if MEMORY_FILE.exists():
        memory = "\n\nЧто ты уже знаешь о пользователе (Ви):\n" + MEMORY_FILE.read_text(encoding="utf-8")
    return persona + memory


class ChatRequest(BaseModel):
    message: str
    active_window: Optional[str] = None
    include_screenshot: bool = False
    allow_actions: bool = True  # Режим «Псевдоэндотризин» (разрешить Джонни управлять ПК)


class ChatResponse(BaseModel):
    reply: str
    action_executed: Optional[str] = None
    audio_path: Optional[str] = None
    relic_glitch: bool = False
    sprite_state: str = "talk"


class ModeSwitchRequest(BaseModel):
    mode: str  # "ollama" или "openai_compat"
    api_key: Optional[str] = None


def capture_screen_base64() -> Optional[str]:
    """Делает быстрый скриншот основного монитора и сжимает его для Vision-модели."""
    try:
        with mss.mss() as sct:
            monitor = sct.monitors[1]
            raw = sct.grab(monitor)
            img = Image.frombytes("RGB", raw.size, raw.bgra, "raw", "BGRX")
    except Exception:
        try:
            from PIL import ImageGrab
            img = ImageGrab.grab()
        except Exception:
            return None

    try:
        img.thumbnail((1280, 720))
        buf = io.BytesIO()
        img.save(buf, format="JPEG", quality=75)
        return base64.b64encode(buf.getvalue()).decode("utf-8")
    except Exception:
        return None


def _tap_vk(vk_code: int, times: int = 1) -> None:
    for _ in range(times):
        ctypes.windll.user32.keybd_event(vk_code, 0, 0, 0)
        ctypes.windll.user32.keybd_event(vk_code, 0, 2, 0)


def execute_pc_action(action_json: str) -> Optional[str]:
    """Выполняет безопасные команды на Windows по запросу Джонни (Этап 6)."""
    try:
        data = json.loads(action_json)
        cmd = data.get("cmd")
        target = str(data.get("target", "")).strip()

        if cmd == "open_app":
            home = Path.home()
            app_map = {
                "notepad": "notepad.exe",
                "calc": "calc.exe",
                "explorer": "explorer.exe",
                "spotify": "spotify:",
                "discord": "discord:",
                "steam": "steam://open/main",
                "vscode": "code",
                "taskmgr": "taskmgr.exe",
                "settings": "ms-settings:",
                "downloads": str(home / "Downloads"),
                "documents": str(home / "Documents"),
                "browser": "https://www.google.com",
            }
            exe = app_map.get(target.lower(), target)
            if exe.startswith("http"):
                webbrowser.open(exe)
            else:
                os.startfile(exe)
            return f"open_app:{target}"

        elif cmd == "open_url" and target.startswith("http"):
            webbrowser.open(target)
            return f"open_url:{target}"

        elif cmd in ("media_play_pause", "media_next", "media_prev"):
            vk_map = {
                "media_play_pause": 0xB3,  # VK_MEDIA_PLAY_PAUSE
                "media_next": 0xB0,        # VK_MEDIA_NEXT_TRACK
                "media_prev": 0xB1,        # VK_MEDIA_PREV_TRACK
            }
            _tap_vk(vk_map[cmd])
            return cmd

        elif cmd == "volume_up":
            _tap_vk(0xAF, times=5)  # VK_VOLUME_UP
            return "volume_up"

        elif cmd == "volume_down":
            _tap_vk(0xAE, times=5)  # VK_VOLUME_DOWN
            return "volume_down"

        elif cmd == "volume_mute":
            _tap_vk(0xAD, times=1)  # VK_VOLUME_MUTE
            return "volume_mute"

        elif cmd == "close_tab":
            # Нажимаем Ctrl + W для закрытия активной вкладки
            VK_CONTROL = 0x11
            VK_W = 0x57
            ctypes.windll.user32.keybd_event(VK_CONTROL, 0, 0, 0)
            ctypes.windll.user32.keybd_event(VK_W, 0, 0, 0)
            ctypes.windll.user32.keybd_event(VK_W, 0, 2, 0)
            ctypes.windll.user32.keybd_event(VK_CONTROL, 0, 2, 0)
            return "close_tab"

    except Exception as e:
        return f"error:{e}"
    return None


def try_apply_rvc_voice_conversion(input_audio_path: Path, output_wav_path: Path) -> bool:
    """
    Этап 4: Если в папке backend/models/johnny_ru/ лежат файлы весов (.pth) и индекса (.index)
    голоса Ильи Бледного и установлена библиотека rvc-python, конвертирует тембр через RVC v2.
    """
    pth_files = list(RVC_MODEL_DIR.glob("*.pth"))
    if not pth_files:
        return False

    index_files = list(RVC_MODEL_DIR.glob("*.index"))
    try:
        from rvc_python.infer import RVCInference  # type: ignore

        device = "cuda:0"
        rvc = RVCInference(device=device)
        rvc.load_model(str(pth_files[0]), version="v2", index_path=str(index_files[0]) if index_files else "")
        rvc.infer_file(str(input_audio_path), str(output_wav_path))
        return output_wav_path.exists()
    except Exception:
        return False


def apply_relic_engram_dsp(wav_bytes: bytes) -> bytes:
    """
    Накладывает киберпанк-эффект биочипа Relic (лёгкий перегруз + металлический дабл-трек энграммы).
    """
    try:
        rate, data = wavfile.read(io.BytesIO(wav_bytes))
        if data.dtype != np.float32:
            data = data.astype(np.float32) / np.iinfo(data.dtype).max

        # Полосовой фильтр (срезаем глухие низы, подчёркиваем характерную середину энграммы)
        nyq = 0.5 * rate
        b, a = butter(2, [120.0 / nyq, min(7500.0 / nyq, 0.99)], btype="band")
        filtered = lfilter(b, a, data, axis=0)

        # Короткая задержка (12 мс) для эффекта цифрового присутствия в голове Ви
        delay_samples = int(0.012 * rate)
        delayed = np.zeros_like(filtered)
        delayed[delay_samples:] = filtered[:-delay_samples]
        mixed = 0.82 * data + 0.28 * delayed

        # Мягкое насыщение (warm overdrive)
        mixed = np.tanh(mixed * 1.15)
        out_int16 = np.clip(mixed * 32767, -32768, 32767).astype(np.int16)

        out_buf = io.BytesIO()
        wavfile.write(out_buf, rate, out_int16)
        return out_buf.getvalue()
    except Exception:
        return wav_bytes


def synthesize_sapi_wav_fallback(text: str, out_wav: Path) -> bool:
    """Локальный оффлайн-синтез русской речи через Windows SAPI5 (pywin32), если edge-tts недоступен."""
    try:
        import pythoncom
        import win32com.client

        pythoncom.CoInitialize()
        try:
            speaker = win32com.client.Dispatch("SAPI.SpVoice")
            stream = win32com.client.Dispatch("SAPI.SpFileStream")

            # Ищем русский голос в системе (например, Pavel или Irina)
            voices = speaker.GetVoices()
            for i in range(voices.Count):
                v = voices.Item(i)
                desc = str(v.GetDescription()).lower()
                if "ru" in desc or "russian" in desc or "рус" in desc or "pavel" in desc or "irina" in desc:
                    speaker.Voice = v
                    break

            speaker.Rate = 1
            stream.Open(str(out_wav.resolve()), 3, False)
            speaker.AudioOutputStream = stream
            speaker.Speak(text)
            stream.Close()
            return out_wav.exists() and out_wav.stat().st_size > 200
        finally:
            pythoncom.CoUninitialize()
    except Exception:
        return False


async def synthesize_johnny_voice(text: str) -> Optional[str]:
    """Генерирует русскую речь Джонни, прогоняет через RVC (при наличии модели) и DSP-фильтр Relic."""
    clean_text = text.replace("*", "").strip()
    if not clean_text:
        return None

    mp3_path = AUDIO_OUT_DIR / "johnny_raw.mp3"
    sapi_wav_path = AUDIO_OUT_DIR / "johnny_sapi.wav"
    rvc_wav_path = AUDIO_OUT_DIR / "johnny_rvc.wav"
    wav_path = AUDIO_OUT_DIR / "johnny_reply.wav"

    source_for_dsp: Optional[Path] = None
    try:
        communicate = edge_tts.Communicate(clean_text, TTS_VOICE, pitch=TTS_PITCH, rate=TTS_RATE)
        await asyncio.wait_for(communicate.save(str(mp3_path)), timeout=8.0)
        if mp3_path.exists() and mp3_path.stat().st_size > 100:
            source_for_dsp = mp3_path
    except Exception:
        source_for_dsp = None

    # Если облачный edge-tts недоступен — используем мгновенный оффлайн-синтез Windows SAPI5 -> WAV
    if source_for_dsp is None:
        if synthesize_sapi_wav_fallback(clean_text, sapi_wav_path):
            source_for_dsp = sapi_wav_path
        else:
            return None

    if try_apply_rvc_voice_conversion(source_for_dsp, rvc_wav_path):
        source_for_dsp = rvc_wav_path

    # Если исходник уже WAV (из SAPI или RVC), можно наложить фильтр Relic напрямую без ffmpeg
    if source_for_dsp.suffix.lower() == ".wav":
        try:
            processed = apply_relic_engram_dsp(source_for_dsp.read_bytes())
            wav_path.write_bytes(processed)
            return str(wav_path.resolve())
        except Exception:
            return str(source_for_dsp.resolve())

    ffmpeg_bin = find_ffmpeg_binary()
    try:
        subprocess.run(
            [ffmpeg_bin, "-y", "-i", str(source_for_dsp), "-ar", "24000", "-ac", "1", str(wav_path)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=True,
        )
        processed = apply_relic_engram_dsp(wav_path.read_bytes())
        wav_path.write_bytes(processed)
        return str(wav_path.resolve())
    except Exception:
        return str(source_for_dsp.resolve())


async def resolve_ollama_model(need_vision: bool) -> str:
    """Выбирает Vision-модель при запросе со скриншотом, если она скачана в Ollama, иначе базовую модель."""
    if not need_vision:
        return OLLAMA_MODEL
    try:
        async with httpx.AsyncClient(timeout=3.0) as client:
            resp = await client.get(OLLAMA_TAGS_URL)
            if resp.status_code == 200:
                models = [m.get("name", "") for m in resp.json().get("models", [])]
                for candidate in (OLLAMA_VISION_MODEL, "llava:7b", "moondream:latest"):
                    if any(candidate in m for m in models):
                        return candidate
    except Exception:
        pass
    return OLLAMA_MODEL


async def query_llm(user_prompt: str, screenshot_b64: Optional[str] = None) -> str:
    system_prompt = load_system_prompt()

    if LLM_MODE == "openai_compat" and OPENAI_API_KEY:
        headers = {"Authorization": f"Bearer {OPENAI_API_KEY}", "Content-Type": "application/json"}
        payload = {
            "model": OPENAI_MODEL,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "temperature": 0.85,
            "max_tokens": 220,
        }
        try:
            async with httpx.AsyncClient(timeout=25.0) as client:
                resp = await client.post(OPENAI_API_URL, headers=headers, json=payload)
                resp.raise_for_status()
                return resp.json()["choices"][0]["message"]["content"].strip()
        except Exception as e:
            return f"Чумба, облачный канал связи глушат корпораты ({e}). Переключайся обратно на локальный чип Ollama!"

    # По умолчанию: локальная Ollama (на RTX 5060 8GB отвечает за ~0.5 сек)
    ensure_ollama_running()
    selected_model = await resolve_ollama_model(bool(screenshot_b64))
    user_msg: dict = {"role": "user", "content": user_prompt}
    if screenshot_b64 and ("vl" in selected_model or "llava" in selected_model or "moondream" in selected_model):
        user_msg["images"] = [screenshot_b64]

    payload = {
        "model": selected_model,
        "messages": [
            {"role": "system", "content": system_prompt},
            user_msg,
        ],
        "stream": False,
        "options": {"temperature": 0.85},
    }
    try:
        async with httpx.AsyncClient(timeout=35.0) as client:
            resp = await client.post(OLLAMA_URL, json=payload)
            resp.raise_for_status()
            return resp.json()["message"]["content"].strip()
    except Exception:
        return "Просыпайся, самурай... Похоже, у тебя Ollama ещё загружает нейросеть или не запущена. Выполни `ollama pull qwen2.5:7b`, чумба."


@app.get("/health")
async def health_check():
    rvc_models = [p.name for p in RVC_MODEL_DIR.glob("*.pth")]
    return {
        "status": "online",
        "llm_mode": LLM_MODE,
        "ollama_model": OLLAMA_MODEL,
        "rvc_model_loaded": bool(rvc_models),
        "rvc_files": rvc_models,
        "ffmpeg": find_ffmpeg_binary(),
    }


@app.post("/mode")
async def switch_llm_mode(req: ModeSwitchRequest):
    global LLM_MODE, OPENAI_API_KEY
    LLM_MODE = req.mode
    if req.api_key:
        OPENAI_API_KEY = req.api_key
    return {"status": "ok", "llm_mode": LLM_MODE}


@app.post("/chat", response_model=ChatResponse)
async def chat_with_johnny(req: ChatRequest):
    context_prefix = f"[Активное окно пользователя: {req.active_window}]\n" if req.active_window else ""
    full_prompt = context_prefix + req.message

    screenshot_b64 = capture_screen_base64() if req.include_screenshot else None
    raw_reply = await query_llm(full_prompt, screenshot_b64)

    action_executed = None
    clean_lines = []
    for line in raw_reply.splitlines():
        if line.strip().startswith("ACTION:"):
            action_payload = line.strip()[len("ACTION:"):].strip()
            if req.allow_actions:
                action_executed = execute_pc_action(action_payload)
        else:
            clean_lines.append(line)

    final_reply = "\n".join(clean_lines).strip()
    audio_path = await synthesize_johnny_voice(final_reply)

    # Триггерим глитч биочипа, если Джонни упоминает корпорации, чип или выполняет действие
    lower_reply = final_reply.lower()
    glitch_keywords = ("арасак", "корпорат", "биочип", "relic", "глитч", "милитех", "сбой")
    guitar_keywords = ("гитар", "рок", "музык", "samurai", "самурай", "never fade away", "рифф", "spotify")

    relic_glitch = bool(action_executed) or any(k in lower_reply for k in glitch_keywords)

    sprite_state = "talk"
    if action_executed and ("spotify" in action_executed or "media_" in action_executed):
        sprite_state = "guitar"
    elif any(k in lower_reply for k in guitar_keywords):
        sprite_state = "guitar"
    elif relic_glitch:
        sprite_state = "glitch"

    return ChatResponse(
        reply=final_reply,
        action_executed=action_executed,
        audio_path=audio_path,
        relic_glitch=relic_glitch,
        sprite_state=sprite_state,
    )


if __name__ == "__main__":
    import uvicorn
    print("=== RELIC ENGRAM v2.077 (JOHNNY SILVERHAND) ONLINE ON http://127.0.0.1:8077 ===")
    uvicorn.run(app, host="127.0.0.1", port=8077)
