// MainWindow.xaml.cs — C# (.NET WPF) Живой 60 FPS скелетный 2.5D-риг Джонни Сильверхенда (Live2D / Spine стиль)
// Включает:
// - Отслеживание курсора мыши головой Джонни (GetCursorPos)
// - Реалистичный Lip-Sync (движение нижней челюсти и бороды синхронно с русской речью)
// - Анимацию курения с поднимающейся рукой, тлеющим угольком и частицами дыма (60 FPS)
// - Независимую жестикуляцию серебряной бионической рукой и игру на электрогитаре Samurai

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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Forms = System.Windows.Forms;

namespace JohnnyOverlay
{
    public partial class MainWindow : Window
    {
        // --- Win32 API (user32.dll) ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

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

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private sealed class SmokeParticle
        {
            public Ellipse Visual = null!;
            public double X;
            public double Y;
            public double Vx;
            public double Vy;
            public double Size;
            public double Life;
            public double MaxLife;
        }

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _windowTrackerTimer;
        private readonly DispatcherTimer _glitchTimer;
        private readonly DispatcherTimer _animTimer;
        private readonly DispatcherTimer _checkInTimer;
        private readonly Random _rng = new Random();

        private readonly Dictionary<string, BitmapImage> _specialSprites = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SmokeParticle> _smokeParticles = new List<SmokeParticle>();
        private readonly string _projectRoot;
        private Process? _backendProcess;
        private Forms.NotifyIcon? _trayIcon;

        private bool _isDragging = false;
        private bool _isSpeaking = false;
        private bool _snapToActiveWindow = true;
        private bool _pseudoendotrizineMode = true;
        private bool _proactiveCheckInEnabled = true;
        private int _glitchTicksRemaining = 0;

        // Переменные скелетной физики (60 FPS)
        private double _time = 0.0;
        private double _currentHeadAngle = 0.0;
        private double _currentHeadX = 0.0;
        private double _currentArmRightAngle = 0.0;
        private double _currentArmLeftAngle = 0.0;
        private double _currentJawOpen = 0.0;
        private double _guitarModeTimer = 0.0;

        private string _currentSpriteState = "idle";
        private string _postGlitchSpriteState = "idle";

        public MainWindow()
        {
            InitializeComponent();

            _projectRoot = ResolveProjectRoot();
            LoadRigAndSprites();
            InitializeTrayIcon();

            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 8;

            _player.MediaEnded += (_, _) =>
            {
                _isSpeaking = false;
                VoiceEqPanel.Visibility = Visibility.Collapsed;
                _postGlitchSpriteState = "idle";
                SetSpriteState("idle");
            };

            // Таймер 60 FPS (16 мс) для живой скелетной анимации, слежения за мышью, Lip-Sync и дыма
            _animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += OnSkeletalAnimationTick;
            _animTimer.Start();

            // Таймер отслеживания активного окна Windows
            _windowTrackerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _windowTrackerTimer.Tick += OnWindowTrackerTick;
            _windowTrackerTimer.Start();

            // Таймер визуального глитча биочипа Relic
            _glitchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _glitchTimer.Tick += OnGlitchTick;

            // Таймер фоновых реакций «Check in on me» (раз в 12 минут)
            _checkInTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(12)
            };
            _checkInTimer.Tick += OnProactiveCheckInTick;
            _checkInTimer.Start();

            SourceInitialized += OnWindowSourceInitialized;
            Closed += OnWindowClosed;

            Loaded += async (_, _) =>
            {
                await EnsurePythonBackendRunningAsync();
                TriggerRelicGlitch(16, "talk");
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

        private static BitmapImage? LoadBitmap(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public void LoadRigAndSprites()
        {
            string rigDir = Path.Combine(_projectRoot, "assets", "rig");
            RigLowerImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_lower.png"));
            RigTorsoImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_torso.png"));
            RigArmRightImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_arm_right.png"));
            RigArmLeftImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_arm_left.png"));
            RigHeadImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_head.png"));
            RigJawImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_jaw.png"));
            RigGuitarImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_guitar.png"));

            string spritesDir = Path.Combine(_projectRoot, "assets", "sprites");
            foreach (string state in new[] { "drag", "glitch", "idle" })
            {
                var bmp = LoadBitmap(Path.Combine(spritesDir, $"johnny_{state}.png"));
                if (bmp != null)
                    _specialSprites[state] = bmp;
            }

            SetSpriteState("idle");
        }

        public void SetSpriteState(string state)
        {
            _currentSpriteState = state;

            if (state == "glitch" || state == "drag")
            {
                if (_specialSprites.TryGetValue(state, out var bmp))
                {
                    SpecialStateImage.Source = bmp;
                    SpecialStateImage.Visibility = Visibility.Visible;
                    RigRootGrid.Opacity = 0.15;
                }
                return;
            }

            SpecialStateImage.Visibility = Visibility.Collapsed;
            RigRootGrid.Opacity = 1.0;

            if (state == "guitar")
            {
                RigGuitarImage.Visibility = Visibility.Visible;
                _guitarModeTimer = 8.0; // Играет рифф 8 секунд
            }
            else
            {
                RigGuitarImage.Visibility = Visibility.Collapsed;
            }
        }

        // --- Главный цикл 60 FPS процедурной скелетной анимации ---
        private void OnSkeletalAnimationTick(object? sender, EventArgs e)
        {
            const double dt = 0.016;
            _time += dt;

            if (_guitarModeTimer > 0)
            {
                _guitarModeTimer -= dt;
                if (_guitarModeTimer <= 0 && _currentSpriteState == "guitar")
                    SetSpriteState(_isSpeaking ? "talk" : "idle");
            }

            // 1. Бегущий голографический луч сканирования по высоте модели
            ScanlineTranslate.Y = ((Math.Sin(_time * 1.4) + 1.0) * 0.5) * 305.0 + 10.0;

            // 2. Живое глубокое дыхание грудной клетки и плеч
            double breath = Math.Sin(_time * 2.1);
            TorsoScale.ScaleY = 1.0 + breath * 0.012;
            TorsoScale.ScaleX = 1.0 + breath * 0.008;
            TorsoTranslate.Y = -breath * 1.6;
            TorsoRotate.Angle = Math.Sin(_time * 1.05) * 0.8;

            // 3. Отслеживание курсора мыши головой Джонни + кивание во время речи/гитары
            double targetHeadAngle = 0.0;
            double targetHeadX = 0.0;
            if (GetCursorPos(out POINT mousePt))
            {
                double headCenterX = Left + Width - 130;
                double headCenterY = Top + 140;
                double dx = (mousePt.X - headCenterX) / 420.0;
                double dy = (mousePt.Y - headCenterY) / 420.0;
                targetHeadAngle = Math.Clamp(dx * 5.5, -6.5, 6.5);
                targetHeadX = Math.Clamp(dx * 2.5, -3.0, 3.0);
                HeadTranslate.Y = Math.Clamp(dy * 2.0, -2.5, 2.5);
            }

            if (_isSpeaking)
            {
                // Энергичные наклоны головы в такт словам
                targetHeadAngle += Math.Sin(_time * 9.5) * 2.8 + Math.Cos(_time * 6.2) * 1.5;
            }
            else if (_currentSpriteState == "guitar")
            {
                // Качает головой в рок-н-ролльный бит Samurai
                targetHeadAngle = Math.Sin(_time * 14.0) * 5.5;
                HeadTranslate.Y = Math.Abs(Math.Sin(_time * 14.0)) * 3.5;
            }

            _currentHeadAngle += (targetHeadAngle - _currentHeadAngle) * 0.14;
            _currentHeadX += (targetHeadX - _currentHeadX) * 0.14;
            HeadRotate.Angle = _currentHeadAngle;
            HeadTranslate.X = _currentHeadX;

            // 4. Реалистичный Lip-Sync (движение нижней челюсти и бороды при разговоре)
            double targetJawY = 0.0;
            if (_isSpeaking)
            {
                // Многочастотная огибающая русской речи (слоговая артикуляция)
                double syllable = Math.Sin(_time * 18.0) * Math.Cos(_time * 11.0) + Math.Sin(_time * 27.0) * 0.5;
                targetJawY = Math.Clamp(Math.Abs(syllable) * 3.4, 0.4, 4.2);

                // Обновляем столбики эквалайзера в HUD
                EqBar1.Height = 4 + Math.Abs(Math.Sin(_time * 19.0)) * 11;
                EqBar2.Height = 5 + Math.Abs(Math.Cos(_time * 23.0)) * 11;
                EqBar3.Height = 4 + Math.Abs(Math.Sin(_time * 15.0)) * 10;
                EqBar4.Height = 5 + Math.Abs(Math.Cos(_time * 27.0)) * 11;
            }
            _currentJawOpen += (targetJawY - _currentJawOpen) * 0.35;
            JawTranslate.Y = _currentJawOpen;

            // 5. Анимация правой руки (курение в idle, жестикуляция в talk, бой по струнам в guitar)
            double targetArmRight = Math.Sin(_time * 1.7) * 1.8;
            bool isTakingDrag = false;

            if (_currentSpriteState == "guitar")
            {
                // Быстрый гитарный чес правой рукой по струнам Deluze Orphean
                targetArmRight = -16.0 + Math.Sin(_time * 28.0) * 7.5;
                GuitarRotate.Angle = Math.Sin(_time * 14.0) * 3.2;
            }
            else if (_isSpeaking)
            {
                // Выразительная жестикуляция рукой во время реплики
                targetArmRight = -5.5 + Math.Sin(_time * 7.5) * 4.8 + Math.Cos(_time * 4.2) * 2.5;
            }
            else
            {
                // Периодическая затяжка сигаретой каждые ~9 секунд в режиме ожидания
                double cycle = _time % 9.5;
                if (cycle > 6.2 && cycle < 8.6)
                {
                    targetArmRight = -11.5; // Поднимает руку с чипом/сигаретой ближе к лицу
                    isTakingDrag = true;
                }
            }

            _currentArmRightAngle += (targetArmRight - _currentArmRightAngle) * 0.12;
            ArmRightRotate.Angle = _currentArmRightAngle;

            // Пульсация тлеющего уголька сигареты / биочипа
            CigaretteEmber.Opacity = isTakingDrag
                ? 0.95
                : (0.55 + Math.Sin(_time * 4.5) * 0.25);

            // 6. Анимация левой серебряной бионической руки
            double targetArmLeft = -Math.Sin(_time * 1.7) * 1.5;
            if (_currentSpriteState == "guitar")
            {
                targetArmLeft = -6.0 + Math.Cos(_time * 14.0) * 3.5;
            }
            else if (_isSpeaking)
            {
                targetArmLeft = -3.5 + Math.Sin(_time * 6.0 + 1.2) * 3.8;
            }
            _currentArmLeftAngle += (targetArmLeft - _currentArmLeftAngle) * 0.12;
            ArmLeftRotate.Angle = _currentArmLeftAngle;

            // 7. Обновление частиц дыма сигареты (60 FPS)
            UpdateSmokeParticles(dt, isTakingDrag);
        }

        private void UpdateSmokeParticles(double dt, bool intenseSmoke)
        {
            // Спавним новую частицу дыма из кончика сигареты
            double spawnChance = intenseSmoke ? 0.42 : 0.18;
            if (_currentSpriteState != "guitar" && _rng.NextDouble() < spawnChance && _smokeParticles.Count < 22)
            {
                // Смещаем точку спавна дыма с учётом угла поворота правой руки
                double rad = _currentArmRightAngle * Math.PI / 180.0;
                double spawnX = 18.0 + Math.Sin(rad) * 12.0;
                double spawnY = 118.0 + Math.Sin(rad) * 28.0;

                var ellipse = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(Color.FromArgb(140, 160, 245, 255)),
                    IsHitTestVisible = false
                };
                SmokeCanvas.Children.Add(ellipse);

                _smokeParticles.Add(new SmokeParticle
                {
                    Visual = ellipse,
                    X = spawnX + (_rng.NextDouble() - 0.5) * 4.0,
                    Y = spawnY,
                    Vx = (_rng.NextDouble() - 0.35) * 8.0,
                    Vy = -18.0 - _rng.NextDouble() * 12.0,
                    Size = 5.0 + _rng.NextDouble() * 3.0,
                    Life = 0.0,
                    MaxLife = 1.6 + _rng.NextDouble() * 0.9
                });
            }

            for (int i = _smokeParticles.Count - 1; i >= 0; i--)
            {
                var p = _smokeParticles[i];
                p.Life += dt;
                if (p.Life >= p.MaxLife)
                {
                    SmokeCanvas.Children.Remove(p.Visual);
                    _smokeParticles.RemoveAt(i);
                    continue;
                }

                double progress = p.Life / p.MaxLife;
                p.X += (p.Vx + Math.Sin(_time * 3.0 + i) * 6.0) * dt;
                p.Y += p.Vy * dt;
                p.Size += 8.5 * dt;

                p.Visual.Width = p.Size;
                p.Visual.Height = p.Size;
                p.Visual.Opacity = (1.0 - progress) * 0.45;
                Canvas.SetLeft(p.Visual, p.X - p.Size * 0.5);
                Canvas.SetTop(p.Visual, p.Y - p.Size * 0.5);
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

                if (rect.Top > 120 && rect.Right > 350 && rect.Right < workArea.Right - 20)
                {
                    targetLeft = rect.Right - Width - 20;
                    targetTop = rect.Top - 110;
                }
                else
                {
                    targetLeft = workArea.Right - Width - 20;
                    targetTop = workArea.Bottom - Height - 8;
                }

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
                AvatarContainer.Opacity = 1.0;
                RelicStatusBanner.Visibility = Visibility.Collapsed;
                JohnnyDropShadow.Color = Color.FromRgb(0, 240, 255);
                SetSpriteState(_postGlitchSpriteState);
                return;
            }

            _glitchTicksRemaining--;
            AvatarTranslate.X = _rng.Next(-10, 11);
            AvatarTranslate.Y = _rng.Next(-4, 5);
            AvatarContainer.Opacity = _rng.NextDouble() * 0.35 + 0.65;
            JohnnyDropShadow.Color = (_glitchTicksRemaining % 2 == 0)
                ? Color.FromRgb(0, 240, 255)
                : Color.FromRgb(252, 238, 9);
        }

        private void Avatar_MouseEnter(object sender, MouseEventArgs e)
        {
            QuickActionBar.Opacity = 1.0;
            JohnnyDropShadow.BlurRadius = 26;
        }

        private void Avatar_MouseLeave(object sender, MouseEventArgs e)
        {
            QuickActionBar.Opacity = 0.88;
            JohnnyDropShadow.BlurRadius = 18;
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
                string targetState = spriteState == "guitar" ? "guitar" : (hasAudio ? "talk" : "idle");

                if (glitch)
                {
                    TriggerRelicGlitch(14, targetState);
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
