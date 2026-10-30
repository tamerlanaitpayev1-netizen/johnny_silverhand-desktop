// MainWindow.xaml.cs — C# (.NET WPF) Десктопный компаньон Джонни Сильверхенд (Игровая 3D-голограмма Relic)
// Поддерживает 60 FPS анимацию дыхания и голографического шлейфа, аудио-эквалайзер речи,
// привязку к окнам Windows (user32.dll), быструю панель вызова и глобальные хоткеи Alt+J / Ctrl+Alt+H.

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
        // --- Win32 API (user32.dll) для отслеживания открытых окон и глобальных хоткеев ---
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
        private const int HOTKEY_ID_QUICK_CHAT = 9078;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint VK_H = 0x48;
        private const uint VK_J = 0x4A;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _windowTrackerTimer;
        private readonly DispatcherTimer _glitchTimer;
        private readonly DispatcherTimer _animTimer;
        private readonly DispatcherTimer _checkInTimer;
        private readonly Random _rng = new Random();

        private readonly Dictionary<string, BitmapImage> _sprites = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private readonly string _projectRoot;
        private Process? _backendProcess;
        private Forms.NotifyIcon? _trayIcon;

        private bool _isDragging = false;
        private bool _isSpeaking = false;
        private bool _snapToActiveWindow = true;
        private bool _pseudoendotrizineMode = true; // Разрешить Джонни управлять ПК
        private bool _proactiveCheckInEnabled = true;
        private int _glitchTicksRemaining = 0;
        private double _animPhase = 0.0;
        private string _currentSpriteState = "idle";
        private string _postGlitchSpriteState = "idle";

        public MainWindow()
        {
            InitializeComponent();

            _projectRoot = ResolveProjectRoot();
            LoadSprites();
            InitializeTrayIcon();

            // Стартовая позиция — правый нижний угол над панелью задач (всегда в пределах экрана)
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 8;

            // Когда аудиозапись реплики заканчивается — возвращаем Джонни в состояние idle
            _player.MediaEnded += (_, _) =>
            {
                _isSpeaking = false;
                VoiceEqPanel.Visibility = Visibility.Collapsed;
                _postGlitchSpriteState = "idle";
                SetSpriteState("idle");
            };

            // Таймер плавной 30 FPS анимации живого дыхания, луча сканирования и эквалайзера
            _animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            // Таймер отслеживания активного окна (чтобы Джонни мог сидеть около активного окна, не уходя за край экрана)
            _windowTrackerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _windowTrackerTimer.Tick += OnWindowTrackerTick;
            _windowTrackerTimer.Start();

            // Таймер визуального глитча биочипа Relic (сдвиг и сине-жёлтое свечение)
            _glitchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _glitchTimer.Tick += OnGlitchTick;

            // Таймер фоновых реакций «Check in on me» (раз в 12 минут комментирует активное окно/игру)
            _checkInTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(12)
            };
            _checkInTimer.Tick += OnProactiveCheckInTick;
            _checkInTimer.Start();

            SourceInitialized += OnWindowSourceInitialized;
            Closed += OnWindowClosed;

            // Автозапуск Python-сервера и приветственная фраза
            Loaded += async (_, _) =>
            {
                await EnsurePythonBackendRunningAsync();
                TriggerRelicGlitch(18, "talk");
                await AskJohnnyAsync("Ты только что прогрузился на рабочий стол Ви. Поприветствуй его в своём стиле в 1 предложении.");
            };
        }

        private static string ResolveProjectRoot()
        {
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

        public void LoadSprites()
        {
            _sprites.Clear();
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
                        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        _sprites[state] = bmp;
                    }
                    catch
                    {
                        // Игнорируем ошибку чтения отдельного файла
                    }
                }
            }

            if (_sprites.Count > 0)
            {
                FallbackCard.Visibility = Visibility.Collapsed;
                JohnnySpriteImage.Visibility = Visibility.Visible;
                JohnnyGhostImage.Visibility = Visibility.Visible;
                SetSpriteState(_currentSpriteState);
            }
            else
            {
                JohnnySpriteImage.Visibility = Visibility.Collapsed;
                JohnnyGhostImage.Visibility = Visibility.Collapsed;
                FallbackCard.Visibility = Visibility.Visible;
            }
        }

        public void SetSpriteState(string state)
        {
            _currentSpriteState = state;
            if (_sprites.TryGetValue(state, out var bmp))
            {
                JohnnySpriteImage.Source = bmp;
                JohnnyGhostImage.Source = bmp;
            }
            else if (_sprites.TryGetValue("idle", out var idleBmp))
            {
                JohnnySpriteImage.Source = idleBmp;
                JohnnyGhostImage.Source = idleBmp;
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            _animPhase += 0.11;

            // 1. Бегущий голографический луч сканирования по высоте 3D-рендера
            double scanY = ((Math.Sin(_animPhase * 0.7) + 1.0) * 0.5) * 265.0 + 10.0;
            ScanlineTranslate.Y = scanY;

            // 2. Живое дыхание и микродвижение 3D-персонажа
            if (_glitchTicksRemaining <= 0)
            {
                if (_isSpeaking)
                {
                    // Во время речи Джонни слегка покачивается в такт голосу, а эквалайзер пульсирует
                    AvatarScale.ScaleY = 1.0 + Math.Sin(_animPhase * 2.6) * 0.016;
                    AvatarScale.ScaleX = 1.0 + Math.Cos(_animPhase * 1.9) * 0.010;
                    GhostTranslate.X = Math.Sin(_animPhase * 3.5) * 5.0;
                    GhostTranslate.Y = Math.Cos(_animPhase * 2.5) * 2.0;

                    EqBar1.Height = _rng.Next(4, 15);
                    EqBar2.Height = _rng.Next(6, 16);
                    EqBar3.Height = _rng.Next(4, 14);
                    EqBar4.Height = _rng.Next(5, 16);

                    // Живое чередование кадров talk <-> idle при длинной реплике для эффекта артикуляции
                    if (((int)(_animPhase * 2.2)) % 5 == 0 && _currentSpriteState != "guitar")
                        SetSpriteState("idle");
                    else if (_currentSpriteState != "guitar")
                        SetSpriteState("talk");
                }
                else
                {
                    // Спокойное дыхание в режиме ожидания (idle / guitar)
                    AvatarScale.ScaleY = 1.0 + Math.Sin(_animPhase * 0.85) * 0.011;
                    AvatarScale.ScaleX = 1.0 + Math.Sin(_animPhase * 0.85) * 0.005;
                    GhostTranslate.X = -3.0 + Math.Sin(_animPhase * 0.6) * 2.0;
                    GhostTranslate.Y = 0;
                }
            }
        }

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

                for (int i = 0; i < 16; i++)
                {
                    await Task.Delay(500);
                    if (await IsBackendHealthyAsync())
                        break;
                }
            }
            catch
            {
                // Ошибка автозапуска отобразится при запросе в чат
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

        private void InitializeTrayIcon()
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Johnny Silverhand — Relic Engram (Alt+J чат / Ctrl+Alt+H скрыть)",
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
            trayMenu.Items.Add("💬 Написать Джонни (Alt+J)", null, (_, _) => Dispatcher.Invoke(OpenChatBox));
            trayMenu.Items.Add("Показать / Скрыть Джонни (Ctrl+Alt+H)", null, (_, _) => Dispatcher.Invoke(ToggleOverlayVisibility));
            trayMenu.Items.Add("👁 Прокомментировать экран (Vision)", null, async (_, _) =>
                await Dispatcher.InvokeAsync(() => AskJohnnyAsync("Посмотри на мой экран и прокомментируй, чем я сейчас занимаюсь.", watchScreen: true)));
            trayMenu.Items.Add("💊 Переключить Псевдоэндотризин (Управление ПК)", null, (_, _) =>
                Dispatcher.Invoke(() => MenuToggleControl_Click(this, new RoutedEventArgs())));
            trayMenu.Items.Add("📝 Открыть характер (personality.md)", null, (_, _) =>
                Dispatcher.Invoke(() => MenuOpenPersonality_Click(this, new RoutedEventArgs())));
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("❌ Отключить биочип (Выход)", null, (_, _) =>
                Dispatcher.Invoke(() => Application.Current.Shutdown()));

            _trayIcon.ContextMenuStrip = trayMenu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(OpenChatBox);
        }

        private void OnWindowSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource? source = HwndSource.FromHwnd(handle);
            source?.AddHook(WndProc);
            // Глобальные хоткеи: Ctrl+Alt+H (скрыть/показать) и Alt+J (вызвать чат с Джонни)
            RegisterHotKey(handle, HOTKEY_ID_TOGGLE_HIDE, MOD_CONTROL | MOD_ALT, VK_H);
            RegisterHotKey(handle, HOTKEY_ID_QUICK_CHAT, MOD_ALT, VK_J);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_ID_TOGGLE_HIDE)
                {
                    ToggleOverlayVisibility();
                    handled = true;
                }
                else if (id == HOTKEY_ID_QUICK_CHAT)
                {
                    OpenChatBox();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void OpenChatBox()
        {
            if (Visibility != Visibility.Visible)
                Visibility = Visibility.Visible;

            InputPanel.Visibility = Visibility.Visible;
            Activate();
            PromptTextBox.Focus();
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
                TriggerRelicGlitch(12, "idle");
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HOTKEY_ID_TOGGLE_HIDE);
                UnregisterHotKey(handle, HOTKEY_ID_QUICK_CHAT);
            }

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
                    // Игнорируем при закрытии
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

            var workArea = SystemParameters.WorkArea;
            if (GetWindowRect(fg, out RECT rect))
            {
                double targetLeft;
                double targetTop;

                // Если окно не развёрнуто на весь экран — Джонни опирается на правый верхний угол окна
                if (rect.Top > 120 && rect.Right > 350 && rect.Right < workArea.Right - 20)
                {
                    targetLeft = rect.Right - Width - 20;
                    targetTop = rect.Top - 110;
                }
                else
                {
                    // Если активное окно развёрнуто на весь экран — Джонни дежурит в правом нижнем углу экрана
                    targetLeft = workArea.Right - Width - 20;
                    targetTop = workArea.Bottom - Height - 8;
                }

                // Жёстко удерживаем Джонни в пределах видимой области экрана
                targetLeft = Math.Clamp(targetLeft, workArea.Left + 8, workArea.Right - Width - 8);
                targetTop = Math.Clamp(targetTop, workArea.Top + 8, workArea.Bottom - Height - 8);

                Left += (targetLeft - Left) * 0.22;
                Top += (targetTop - Top) * 0.22;
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
                GhostTranslate.X = -3;
                GhostTranslate.Y = 0;
                AvatarContainer.Opacity = 1.0;
                RelicStatusBanner.Visibility = Visibility.Collapsed;
                JohnnyDropShadow.Color = Color.FromRgb(0, 240, 255);
                SetSpriteState(_postGlitchSpriteState);
                return;
            }

            _glitchTicksRemaining--;
            AvatarTranslate.X = _rng.Next(-10, 11);
            AvatarTranslate.Y = _rng.Next(-4, 5);
            GhostTranslate.X = _rng.Next(-18, 19);
            GhostTranslate.Y = _rng.Next(-6, 7);
            AvatarContainer.Opacity = _rng.NextDouble() * 0.35 + 0.65;
            JohnnyDropShadow.Color = (_glitchTicksRemaining % 2 == 0)
                ? Color.FromRgb(0, 240, 255)
                : Color.FromRgb(252, 238, 9);
        }

        private void Avatar_MouseEnter(object sender, MouseEventArgs e)
        {
            QuickActionBar.Opacity = 1.0;
            JohnnyDropShadow.BlurRadius = 28;
        }

        private void Avatar_MouseLeave(object sender, MouseEventArgs e)
        {
            QuickActionBar.Opacity = 0.85;
            JohnnyDropShadow.BlurRadius = 20;
        }

        private void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                OpenChatBox();
                return;
            }

            _isDragging = true;
            SetSpriteState("drag");

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
                SetSpriteState(_isSpeaking ? "talk" : "idle");
            }
        }

        private void QuickChat_Click(object sender, RoutedEventArgs e)
        {
            if (InputPanel.Visibility == Visibility.Visible)
                InputPanel.Visibility = Visibility.Collapsed;
            else
                OpenChatBox();
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
            SpeechText.Text = "⚡ [СИНХРОНИЗАЦИЯ НЕЙРОСЕТИ С БИОЧИПОМ RELIC...]";
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
                    _isSpeaking = true;
                    VoiceEqPanel.Visibility = Visibility.Visible;
                    _player.Open(new Uri(audioPath, UriKind.Absolute));
                    _player.Play();
                }
            }
            catch (Exception)
            {
                _isSpeaking = false;
                VoiceEqPanel.Visibility = Visibility.Collapsed;
                TriggerRelicGlitch(12, "idle");
                SpeechText.Text = "Ви, твой Python-сервер энграммы молчит. Перезапусти Start-Johnny.bat, чумба!";
            }
        }

        private async void MenuWatchScreen_Click(object sender, RoutedEventArgs e)
        {
            await AskJohnnyAsync("Посмотри на мой экран и прокомментируй, чем я сейчас занимаюсь.", watchScreen: true);
        }

        private void MenuPlayGuitar_Click(object sender, RoutedEventArgs e)
        {
            TriggerRelicGlitch(10, "guitar");
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
