# Johnny Silverhand Desktop Engram v2.0 (C# .NET WPF + Python FastAPI)

Полноценный десктоп-компаньон **Джонни Сильверхенда** в духе [capri-desktop](https://github.com/lachydotmcg/capri-desktop), спроектированный и настроенный под ноутбук **AMD Ryzen 7 250, 16 ГБ DDR5, NVIDIA GeForce RTX 5060 Laptop 8 ГБ VRAM, Windows 11**.

## Сводная таблица этапов (Этапы 1–14)

### Базовая платформа v1.0 (Этапы 1–7)

| Этап | Статус | Реализация в проекте |
| :--- | :--- | :--- |
| **Этап 1. Окружение** | ✅ Готово | Python 3.12 (`backend/venv`), .NET SDK (`RollForward=LatestMajor`), FFmpeg и Ollama (`qwen2.5:7b`) |
| **Этап 2. Мозг и характер** | ✅ Готово | [engram_server.py](backend/engram_server.py), [personality.md](backend/personality.md), [me.md](backend/me.md) на `127.0.0.1:8077` |
| **Этап 3. Оверлей и физика окон** | ✅ Готово | Прозрачный киберпанк-оверлей [MainWindow.xaml](frontend/MainWindow.xaml) + привязка к окнам через `user32.dll` в [MainWindow.xaml.cs](frontend/MainWindow.xaml.cs) |
| **Этап 4. Русский голос + DSP + RVC** | ✅ Готово | Русский синтез `edge-tts` + фильтр биочипа `apply_relic_engram_dsp` + автоподхват RVC v2 моделей Ильи Бледного из `backend/models/johnny_ru/` |
| **Этап 5. Спрайты и 3D-рендеры** | ✅ Готово | Игровые 3D-рендеры REDengine 4 в `assets/raw_renders/` и `assets/sprites/` |
| **Этап 6. Зрение и управление ПК** | ✅ Готово | Скриншоты через `mss` для Vision-моделей (`qwen2.5vl:7b`), таймер реакций, команды `ACTION:` (громкость, медиа, вкладки, YouTube, Spotify, Блокнот) |
| **Этап 7. Трей и лаунчер `.exe`** | ✅ Готово | Иконка в системном трее (`relic_tray.ico`), автозапуск Python из C# и релизная сборка в `release/JohnnyOverlay.exe` |

### Апгрейд v2.0 «Живой компаньон» (Этапы 8–14)

| Этап | Статус | Реализация в проекте |
| :--- | :--- | :--- |
| **Этап 8. 10-слойный риг + Edgerunners Cel-Shading** | ✅ Готово | [build_skeletal_rig.py](backend/build_skeletal_rig.py) — раздельные кости рук, ног, торса, головы, челюсти, задних прядей волос, блика авиаторов и гитары в `assets/rig/` |
| **Этап 9. Боковой кибер-терминал (Capri Layout)** | ✅ Готово | Двухколоночная верстка `540x360`: слева терминал `JOHNNY // NIGHT-CITY` со строкой `> you:` и пилюлями `[✎ TYPE]`, `[👁 SCREEN]`, `[🎸 RIFF]`, справа — персонаж со свешенными ногами |
| **Этап 10. Физика перетаскивания, гравитации и поглаживаний** | ✅ Готово | Инерционный маятник при перетаскивании за шкирку, бросок с гравитацией (`2100 px/s²`), `Squash & Stretch` при приземлении, реакция `Head Pats` и векторные `Glitch Tendrils` |
| **Этап 11. Покадровые Sprite Strips (8 кадров)** | ✅ Готово | [build_sprite_strips.py](backend/backend/build_sprite_strips.py) + `PlaySpriteStrip()` в `MainWindow.xaml.cs`: `strip_beam_in.png`, `strip_malorian_spin.png`, `strip_sleep.png`, `strip_walk.png` в `assets/strips/` |
| **Этап 12. Аудио-конверт Lip-Sync и голосовой ввод** | ✅ Готово | Расчёт `audio_envelope` (RMS-амплитуда каждые 30 мс) для челюсти + эндпоинт `/listen` (`Ctrl + Alt + V`) |
| **Этап 13. Кибер-курсор «Серебряная рука»** | ✅ Готово | Прозрачное Click-Through окно [JohnnyCursorWindow.xaml](frontend/JohnnyCursorWindow.xaml), летящее по кривым Безье при выполнении `ACTION:` |
| **Этап 14. Окно настроек Relic Mainframe + Smoke-тест** | ✅ Готово | [MainframeWindow.xaml](frontend/MainframeWindow.xaml) (вкладки `BRAIN`, `VOICE`, `HABITS`) + автотест `JohnnyOverlay.exe --smoke` ([smoke_report.txt](smoke_report.txt)) |

## Быстрый запуск в один клик

Запусти файл **[Start-Johnny.bat](Start-Johnny.bat)** или напрямую **[release/JohnnyOverlay.exe](release/JohnnyOverlay.exe)**.
Клиент C# автоматически поднимет фоновый Python-сервер `backend/engram_server.py` и выведет Джонни с голографической анимацией появления (`strip_beam_in.png`).

Для проверки всех слоёв рига, спрайт-лент и связи с сервером выполни:
```powershell
.\release\JohnnyOverlay.exe --smoke
```

## Управление и горячие клавиши

* **Двойной клик левой кнопкой мыши по Джонни (или пилюля `[✎ TYPE]`)** — открыть/скрыть строку ввода `> you:` в боковом терминале (`Enter` — отправить, `Esc` — свернуть).
* **Пилюля `[👁 SCREEN]`** — отправить скриншот экрана Джонни на разбор.
* **Пилюля `[🎸 RIFF]`** — переключить режим игры на электрогитаре Samurai.
* **Быстрое движение мыши над макушкой Джонни** — `Head Pats` (Джонни ворчит и отпускает саркастическую реплику).
* **Перетаскивание мышкой и бросок** — схвати Джонни за куртку (включается маятниковая физика ragdoll и дрыганье ногами) и отпусти в воздухе — он упадёт с ускорением свободного падения и спружинит о край окна или панель задач. Зажми **Shift** при перетаскивании, чтобы отключить автопривязку к окнам.
* **Правый клик по Джонни (или иконка Relic в системном трее)**:
  * `⚙ Открыть Relic Mainframe (Настройки)` — окно с вкладками `BRAIN`, `VOICE`, `HABITS`.
  * `🔫 Трюк с Malorian Arms 3516` — проигрывает 8-кадровую анимацию вращения пистолета.
  * `👁 Прокомментировать экран (Vision)` / `🎸 Сыграть рифф Samurai`.
* **Горячие клавиши**:
  * `Alt + J` — открыть строку чата с Джонни.
  * `Ctrl + Alt + V` — голосовой ввод с микрофона (`/listen`).
  * `Ctrl + Alt + H` — мгновенно скрыть/показать оверлей.
