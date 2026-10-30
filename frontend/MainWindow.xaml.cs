// MainWindow.xaml.cs — C# (.NET WPF) Десктопный компаньон Джонни Сильверхенд (Этапы 3, 5, 6, 7)
// Отрисовывает прозрачный оверлей поверх окон Windows, привязывается к заголовкам активных окон (через user32.dll),
// переключает 5 состояний спрайтов, запускает фоновый Python-сервер, поддерживает системный трей и хоткей Ctrl+Alt+H.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace JohnnyOverlay
{
    public partial class MainWindow : Window
    {
        // --- Win32 API (user32.dll) для отслеживания открытых окон и глобального хоткея Ctrl+Alt+H ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID_TOGGLE_HIDE = 9077;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_H = 0x48;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _windowTrackerTimer;
        private readonly DispatcherTimer _glitchTimer;
        private readonly DispatcherTimer _checkInTimer;
        private readonly Random _rng = new Random();

        private readonly Dictionary<string, BitmapImage> _sprites = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private readonly string _projectRoot;
        private Process? _backendProcess;
        private Forms.NotifyIcon? _trayIcon;

        private bool _isDragging = false;
        private bool _snapToActiveWindow = true;
        private bool _pseudoendotrizineMode = true; // Разрешить Джонни управлять ПК
        private bool _proactiveCheckInEnabled = true;
        private int _glitchTicksRemaining = 0;
        private string _currentSpriteState = "idle";
        private string _postGlitchSpriteState = "idle";

        public MainWindow()
        {
            InitializeComponent();

            _projectRoot = ResolveProjectRoot();
            LoadSprites();
            InitializeTrayIcon();

            // Стартовая позиция — правый нижний угол над панелью задач
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 40;
            Top = workArea.Bottom - Height;

            // Когда аудиозапись реплики заканчивается — возвращаем Джонни в состояние idle
            _player.MediaEnded += (_, _) =>
            {
                _postGlitchSpriteState = "idle";
                SetSpriteState("idle");
            };

            // Таймер отслеживания активного окна (чтобы Джонни мог сидеть на заголовке окна)
            _windowTrackerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _windowTrackerTimer.Tick += OnWindowTrackerTick;
            _windowTrackerTimer.Start();

            // Таймер визуального глитча биочипа Relic (сдвиг и сине-жёлтое свечение)
            _glitchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _glitchTimer.Tick += OnGlitchTick;

            // Таймер фоновых реакций «Check in on me» (Этап 6 — раз в 12 минут комментирует активное окно/игру)
            _checkInTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(12)
            };
            _checkInTimer.Tick += OnProactiveCheckInTick;
            _checkInTimer.Start();

            SourceInitialized += OnWindowSourceInitialized;
            Closed += OnWindowClosed;

            // Автозапуск Python-сервера (Этап 7) и приветственная фраза
            Loaded += async (_, _) =>
            {
                await EnsurePythonBackendRunningAsync();
                TriggerRelicGlitch(18, "talk");
                await AskJohnnyAsync("Ты только что прогрузился на рабочий стол Ви. Поприветствуй его в своём стиле в 1 предложении.");
            };
        }

        private static string ResolveProjectRoot()
        {
            // Ищем корень проекта, где лежат папки backend и assets
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "backend")) ||
                    Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        // --- Этап 5: Загрузка и переключение 5 состояний спрайтов ---
        private void LoadSprites()
        {
            string spritesDir = Path.Combine(_projectRoot, "assets", "sprites");
            string[] states = { "idle", "talk", "drag", "guitar", "glitch" };

            foreach (string state in states)
            {
                string filePath = Path.Combine(spritesDir, $"johnny_{state}.png");
                if (File.Exists(filePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        _sprites[state] = bmp;
                    }
                    catch
                    {
                        // Игнорируем повреждённый файл и используем fallback
                    }
                }
            }

            if (_sprites.Count > 0)
            {
                FallbackCard.Visibility = Visibility.Collapsed;
                JohnnySpriteImage.Visibility = Visibility.Visible;
                SetSpriteState("idle");
            }
            else
            {
                JohnnySpriteImage.Visibility = Visibility.Collapsed;
                FallbackCard.Visibility = Visibility.Visible;
            }
        }

        public void SetSpriteState(string state)
        {
            _currentSpriteState = state;
            if (_sprites.TryGetValue(state, out var bmp))
            {
                JohnnySpriteImage.Source = bmp;
            }
            else if (_sprites.TryGetValue("idle", out var idleBmp))
            {
                JohnnySpriteImage.Source = idleBmp;
            }
        }

        // --- Этап 7: Автозапуск фонового Python-сервера из C# ---
        private async Task EnsurePythonBackendRunningAsync()
        {
            if (await IsBackendHealthyAsync())
                return;

            string serverScript = Path.Combine(_projectRoot, "backend", "engram_server.py");
            if (!File.Exists(serverScript))
                return;

            string venvPython = Path.Combine(_projectRoot, "backend", "venv", "Scripts", "python.exe");
            string pythonExe = File.Exists(venvPython) ? venvPython : "python";

            try
            {
                SpeechText.Text = "⚡ [ЗАПУСК ЯДРА ЭНГРАММЫ RELIC В ФОНЕ...]";
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{serverScript}\"",
                    WorkingDirectory = Path.Combine(_projectRoot, "backend"),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                _backendProcess = Process.Start(psi);

                // Ждём до 8 секунд, пока FastAPI поднимется на 127.0.0.1:8077
                for (int i = 0; i < 16; i++)
                {
                    await Task.Delay(500);
                    if (await IsBackendHealthyAsync())
                        break;
                }
            }
            catch
            {
                // Если не удалось запустить автоматически, пользователь увидит подсказку в облаке чата
            }
        }

        private async Task<bool> IsBackendHealthyAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(900));
                var resp = await _http.GetAsync("http://127.0.0.1:8077/health", cts.Token);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // --- Этап 7: Системный трей Windows (NotifyIcon) и глобальный хоткей Ctrl+Alt+H ---
        private void InitializeTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Johnny Silverhand — Relic Engram (Ctrl+Alt+H)",
                Visible = true
            };

            string icoPath = Path.Combine(_projectRoot, "assets", "sprites", "relic_tray.ico");
            if (File.Exists(icoPath))
            {
                try
                {
                    _trayIcon.Icon = new System.Drawing.Icon(icoPath);
                }
                catch
                {
                    _trayIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("Показать / Скрыть Джонни (Ctrl+Alt+H)", null, (_, _) => Dispatcher.Invoke(ToggleOverlayVisibility));
            trayMenu.Items.Add("Прокомментировать экран (Vision)", null, async (_, _) =>
                await Dispatcher.InvokeAsync(() => AskJohnnyAsync("Посмотри на мой экран и прокомментируй, чем я сейчас занимаюсь.", watchScreen: true)));
            trayMenu.Items.Add("Переключить Псевдоэндотризин (Управление ПК)", null, (_, _) =>
                Dispatcher.Invoke(() => MenuToggleControl_Click(this, new RoutedEventArgs())));
            trayMenu.Items.Add("Открыть характер (personality.md)", null, (_, _) =>
                Dispatcher.Invoke(() => MenuOpenPersonality_Click(this, new RoutedEventArgs())));
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("Отключить биочип (Выход)", null, (_, _) =>
                Dispatcher.Invoke(() => Application.Current.Shutdown()));

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleOverlayVisibility);
        }

        private void OnWindowSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
            RegisterHotKey(handle, HOTKEY_ID_TOGGLE_HIDE, MOD_CONTROL | MOD_ALT, VK_H);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_TOGGLE_HIDE)
            {
                ToggleOverlayVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleOverlayVisibility()
        {
            if (Visibility == Visibility.Visible)
            {
                Visibility = Visibility.Hidden;
            }
            else
            {
                Visibility = Visibility.Visible;
                Activate();
                TriggerRelicGlitch(10, "idle");
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
                UnregisterHotKey(handle, HOTKEY_ID_TOGGLE_HIDE);

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                try
                {
                    _backendProcess.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Игнорируем при завершении
                }
            }
        }

        private string GetActiveWindowTitle()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "Рабочий стол Windows";
            var sb = new StringBuilder(256);
            if (GetWindowText(handle, sb, 256) > 0)
            {
                string title = sb.ToString();
                if (!title.Contains("JohnnyOverlay"))
                    return title;
            }
            return "Рабочий стол Windows";
        }

        private void OnWindowTrackerTick(object? sender, EventArgs e)
        {
            if (_isDragging || !_snapToActiveWindow || Visibility != Visibility.Visible) return;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            string title = GetActiveWindowTitle();
            if (title == "Рабочий стол Windows") return;

            if (GetWindowRect(fg, out RECT rect))
            {
                // Если окно не развёрнуто на весь экран, Джонни садится на правый край его верхней рамки
                if (rect.Top > 80 && rect.Right > 300 && rect.Right < SystemParameters.VirtualScreenWidth)
                {
                    double targetLeft = rect.Right - Width - 30;
                    double targetTop = rect.Top - 135; // Опирается на заголовок окна
                    Left += (targetLeft - Left) * 0.25;
                    Top += (targetTop - Top) * 0.25;
                }
            }
        }

        private async void OnProactiveCheckInTick(object? sender, EventArgs e)
        {
            if (!_proactiveCheckInEnabled || Visibility != Visibility.Visible) return;
            string activeTitle = GetActiveWindowTitle();
            TriggerRelicGlitch(14, "talk");
            await AskJohnnyAsync($"Ты внезапно проявился через биочип Relic. Прокомментируй в 1-2 дерзких предложениях то, чем сейчас занят Ви в окне «{activeTitle}».");
        }

        public void TriggerRelicGlitch(int ticks = 14, string afterGlitchState = "idle")
        {
            _glitchTicksRemaining = ticks;
            _postGlitchSpriteState = afterGlitchState;
            RelicStatusBanner.Visibility = Visibility.Visible;
            SetSpriteState("glitch");
            _glitchTimer.Start();
        }

        private void OnGlitchTick(object? sender, EventArgs e)
        {
            if (_glitchTicksRemaining <= 0)
            {
                _glitchTimer.Stop();
                AvatarTranslate.X = 0;
                AvatarTranslate.Y = 0;
                AvatarContainer.Opacity = 1.0;
                RelicStatusBanner.Visibility = Visibility.Collapsed;
                JohnnyDropShadow.Color = Color.FromRgb(0, 240, 255); // Чистый голубой цвет энграммы
                SetSpriteState(_postGlitchSpriteState);
                return;
            }

            _glitchTicksRemaining--;
            // Эффект сбоя биочипа: резкие горизонтальные смещения и чередование голубого (#00F0FF) и жёлтого (#FCEE09)
            AvatarTranslate.X = _rng.Next(-9, 10);
            AvatarTranslate.Y = _rng.Next(-3, 4);
            AvatarContainer.Opacity = _rng.NextDouble() * 0.35 + 0.65;
            JohnnyDropShadow.Color = (_glitchTicksRemaining % 2 == 0)
                ? Color.FromRgb(0, 240, 255)
                : Color.FromRgb(252, 238, 9);
        }

        private void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Двойной клик по Джонни — открыть/скрыть поле ввода
                InputPanel.Visibility = InputPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                if (InputPanel.Visibility == Visibility.Visible)
                    PromptTextBox.Focus();
                return;
            }

            _isDragging = true;
            SetSpriteState("drag");

            // Зажатый Shift отключает авто-привязку к окнам и оставляет Джонни ровно там, куда его поставили
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                _snapToActiveWindow = false;
                MenuSnapItem.Header = "📌 Привязка к активным окнам: ВЫКЛ";
            }

            try
            {
                DragMove();
            }
            finally
            {
                _isDragging = false;
                SetSpriteState("idle");
            }
        }

        private async void PromptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(PromptTextBox.Text))
            {
                string userText = PromptTextBox.Text.Trim();
                PromptTextBox.Clear();
                InputPanel.Visibility = Visibility.Collapsed;
                await AskJohnnyAsync(userText);
            }
            else if (e.Key == Key.Escape)
            {
                InputPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task AskJohnnyAsync(string message, bool watchScreen = false)
        {
            SpeechBubble.Visibility = Visibility.Visible;
            SpeechText.Text = "⚡ [СБОЙ БИОЧИПА... СИНХРОНИЗАЦИЯ ЭНГРАММЫ]";
            SetSpriteState("talk");

            var payload = new
            {
                message = message,
                active_window = GetActiveWindowTitle(),
                include_screenshot = watchScreen,
                allow_actions = _pseudoendotrizineMode
            };

            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"
                );
                var response = await _http.PostAsync("http://127.0.0.1:8077/chat", content);
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;

                string reply = root.GetProperty("reply").GetString() ?? "";
                bool glitch = root.GetProperty("relic_glitch").GetBoolean();
                string spriteState = root.TryGetProperty("sprite_state", out var sp) && sp.ValueKind == JsonValueKind.String
                    ? (sp.GetString() ?? "talk")
                    : "talk";
                string? audioPath = root.TryGetProperty("audio_path", out var ap) && ap.ValueKind == JsonValueKind.String
                    ? ap.GetString()
                    : null;

                SpeechText.Text = reply;

                bool hasAudio = !string.IsNullOrEmpty(audioPath) && File.Exists(audioPath);
                string targetState = hasAudio ? (spriteState == "guitar" ? "guitar" : "talk") : (spriteState == "guitar" ? "guitar" : "idle");

                if (glitch)
                {
                    TriggerRelicGlitch(16, targetState);
                }
                else
                {
                    SetSpriteState(targetState);
                }

                if (hasAudio && audioPath != null)
                {
                    _player.Open(new Uri(audioPath, UriKind.Absolute));
                    _player.Play();
                }
            }
            catch (Exception)
            {
                TriggerRelicGlitch(12, "idle");
                SpeechText.Text = "Ви, твой Python-сервер энграммы молчит. Запусти `python engram_server.py`, чумба!";
            }
        }

        private async void MenuWatchScreen_Click(object sender, RoutedEventArgs e)
        {
            await AskJohnnyAsync("Посмотри на мой экран и прокомментируй, чем я сейчас занимаюсь.", watchScreen: true);
        }

        private void MenuPlayGuitar_Click(object sender, RoutedEventArgs e)
        {
            SetSpriteState("guitar");
            SpeechText.Text = "🎸 Врубай Samurai — Never Fade Away на полную катушку, чумба!";
        }

        private void MenuToggleSnap_Click(object sender, RoutedEventArgs e)
        {
            _snapToActiveWindow = !_snapToActiveWindow;
            MenuSnapItem.Header = _snapToActiveWindow
                ? "📌 Привязка к активным окнам: ВКЛ"
                : "📌 Привязка к активным окнам: ВЫКЛ";
        }

        private void MenuToggleControl_Click(object sender, RoutedEventArgs e)
        {
            _pseudoendotrizineMode = !_pseudoendotrizineMode;
            MenuControlItem.Header = _pseudoendotrizineMode
                ? "💊 Псевдоэндотризин (Управление ПК): ВКЛ"
                : "💊 Псевдоэндотризин (Управление ПК): ВЫКЛ";
            TriggerRelicGlitch(10, "idle");
        }

        private void MenuToggleCheckIn_Click(object sender, RoutedEventArgs e)
        {
            _proactiveCheckInEnabled = !_proactiveCheckInEnabled;
            MenuCheckInItem.Header = _proactiveCheckInEnabled
                ? "⏱ Фоновые реакции (раз в 12 мин): ВКЛ"
                : "⏱ Фоновые реакции (раз в 12 мин): ВЫКЛ";
        }

        private void MenuOpenPersonality_Click(object sender, RoutedEventArgs e)
        {
            string file = Path.Combine(_projectRoot, "backend", "personality.md");
            if (File.Exists(file))
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
        }

        private void MenuOpenMemory_Click(object sender, RoutedEventArgs e)
        {
            string file = Path.Combine(_projectRoot, "backend", "me.md");
            if (File.Exists(file))
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
        }

        private void MenuHideToTray_Click(object sender, RoutedEventArgs e)
        {
            ToggleOverlayVisibility();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
