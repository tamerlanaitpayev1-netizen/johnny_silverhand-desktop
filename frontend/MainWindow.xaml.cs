// MainWindow.xaml.cs — C# (.NET WPF) 60 FPS Движок в стиле Capri (YouTube Shorts sEAqdBnvUCU / capri-desktop)
// Что нового по сравнению с прошлой версией:
// 1. Посадка на край окна со свешенными ногами (Perch Pose): таз Джонни сидит прямо на верхней границе окна (rect.Top),
//    а его ноги (rig_leg_left / rig_leg_right) свисают на заголовок окна и качаются в 60 FPS!
// 2. Неблокирующая физика перетаскивания (Ragdoll Pendulum & Flailing): когда ты берёшь Джонни мышкой, он висит на курсоре,
//    раскачивается как маятник от скорости мыши и отчаянно дрыгает руками и ногами.
// 3. Гравитация и бросок (Toss & Squash-and-Stretch Bounce): если отпустить его в воздухе, он падает с ускорением
//    на ближайшую рамку окна или панель задач и пружинит при приземлении со вспышкой глитчей.
// 4. Щупальца глитчей (Glitch Tendrils), физика развевающихся волос, блик авиаторов и реакция на поглаживание головы (Head Pats).

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
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

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

        private sealed class GlitchShard
        {
            public Polygon Visual = null!;
            public double Life;
            public double MaxLife;
            public double Vx;
            public double Vy;
            public double OffsetX;
            public double OffsetY;
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
        private readonly List<GlitchShard> _glitchShards = new List<GlitchShard>();
        private readonly string _projectRoot;
        private Process? _backendProcess;
        private Forms.NotifyIcon? _trayIcon;

        // Состояния физики и интерактивности в стиле Capri
        private bool _isDragging = false;
        private bool _isFalling = false;
        private bool _isSpeaking = false;
        private bool _snapToActiveWindow = true;
        private bool _pseudoendotrizineMode = true;
        private bool _proactiveCheckInEnabled = true;
        private int _glitchTicksRemaining = 0;

        // Координаты и скорости для маятника при перетаскивании и гравитации при броске
        private Point _dragAnchorOffset;
        private double _lastMouseScreenX;
        private double _lastMouseScreenY;
        private double _dragVx = 0.0;
        private double _dragVy = 0.0;
        private double _fallVx = 0.0;
        private double _fallVy = 0.0;
        private double _pendulumAngle = 0.0;
        private double _pendulumAngularVel = 0.0;
        private double _landingSquashTimer = 0.0;

        // Детектор поглаживания головы курсором (Head Pats как у Капри)
        private double _lastHoverX = 0.0;
        private int _headPatDirectionChanges = 0;
        private int _lastHeadPatSign = 0;
        private double _headPatCooldown = 0.0;

        // Переменные скелетной физики (60 FPS)
        private double _time = 0.0;
        private double _currentHeadAngle = 0.0;
        private double _currentHeadX = 0.0;
        private double _currentHairAngle = 0.0;
        private double _currentArmRightAngle = 0.0;
        private double _currentArmLeftAngle = 0.0;
        private double _currentLegLeftAngle = 0.0;
        private double _currentLegRightAngle = 0.0;
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
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height + 55; // Ноги свешиваются за край

            _player.MediaEnded += (_, _) =>
            {
                _isSpeaking = false;
                VoiceEqPanel.Visibility = Visibility.Collapsed;
                _postGlitchSpriteState = "idle";
                SetSpriteState("idle");
            };

            // Главный цикл 60 FPS (16 мс): маятник, гравитация, болтание ногами, волосы, Lip-Sync, дым и щупальца глитчей
            _animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += OnSkeletalAnimationTick;
            _animTimer.Start();

            // Таймер привязки к верхнему краю активного окна Windows
            _windowTrackerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
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
                SpawnGlitchTendrils(18);
                TriggerRelicGlitch(16, "talk");
                await AskJohnnyAsync("Ты только что телепортировался через биочип Relic на край окна Ви. Поприветствуй его в 1 коротком дерзком предложении.");
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
            RigHairBackImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_hair_back.png"));
            RigLegLeftImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_leg_left.png"));
            RigLegRightImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_leg_right.png"));
            RigLowerImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_lower.png"));
            RigTorsoImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_torso.png"));
            RigArmRightImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_arm_right.png"));
            RigArmLeftImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_arm_left.png"));
            RigHeadImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_head.png"));
            RigGlassesGlintImage.Source = LoadBitmap(Path.Combine(rigDir, "rig_glasses_glint.png"));
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

            // В режиме drag теперь работает полноценный живой скелетный риг (чтобы он дрыгал ногами и руками в 60 FPS как Капри!)
            if (state == "glitch")
            {
                if (_specialSprites.TryGetValue("glitch", out var bmp))
                {
                    SpecialStateImage.Source = bmp;
                    SpecialStateImage.Visibility = Visibility.Visible;
                    RigRootGrid.Opacity = 0.25;
                }
                return;
            }

            SpecialStateImage.Visibility = Visibility.Collapsed;
            RigRootGrid.Opacity = 1.0;

            if (state == "guitar")
            {
                RigGuitarImage.Visibility = Visibility.Visible;
                _guitarModeTimer = 8.5;
            }
            else
            {
                RigGuitarImage.Visibility = Visibility.Collapsed;
            }
        }

        // --- Спавнер багрово-голубых щупалец и осколков глитчей (Glitch Tendrils как в видео у Капри) ---
        public void SpawnGlitchTendrils(int count = 12)
        {
            Color[] palette =
            {
                Color.FromArgb(220, 255, 42, 85),   // Багровый Blackwall / Edgerunners
                Color.FromArgb(220, 0, 240, 255),   // Голубой Relic
                Color.FromArgb(220, 252, 238, 9)    // Жёлтый Cyberpunk
            };

            for (int i = 0; i < count; i++)
            {
                double cx = 120.0 + (_rng.NextDouble() - 0.5) * 90.0;
                double cy = 160.0 + (_rng.NextDouble() - 0.5) * 160.0;
                double angle = _rng.NextDouble() * Math.PI * 2.0;
                double len = 24.0 + _rng.NextDouble() * 48.0;
                double thick = 3.0 + _rng.NextDouble() * 6.0;

                var poly = new Polygon
                {
                    Fill = new SolidColorBrush(palette[_rng.Next(palette.Length)]),
                    Points = new PointCollection
                    {
                        new Point(cx, cy - thick),
                        new Point(cx + Math.Cos(angle) * len, cy + Math.Sin(angle) * len * 0.45),
                        new Point(cx, cy + thick),
                        new Point(cx - Math.Cos(angle) * (len * 0.35), cy)
                    },
                    IsHitTestVisible = false
                };

                GlitchTendrilsCanvas.Children.Add(poly);
                _glitchShards.Add(new GlitchShard
                {
                    Visual = poly,
                    Life = 0.0,
                    MaxLife = 0.28 + _rng.NextDouble() * 0.35,
                    Vx = Math.Cos(angle) * (45.0 + _rng.NextDouble() * 80.0),
                    Vy = Math.Sin(angle) * (25.0 + _rng.NextDouble() * 50.0),
                    OffsetX = 0.0,
                    OffsetY = 0.0
                });
            }
        }

        private void UpdateGlitchTendrils(double dt)
        {
            // Случайные микро-глитчи в простое (как у Капри на видео)
            if (_rng.NextDouble() < 0.025 && _glitchShards.Count < 6)
            {
                SpawnGlitchTendrils(2);
            }

            for (int i = _glitchShards.Count - 1; i >= 0; i--)
            {
                var s = _glitchShards[i];
                s.Life += dt;
                if (s.Life >= s.MaxLife)
                {
                    GlitchTendrilsCanvas.Children.Remove(s.Visual);
                    _glitchShards.RemoveAt(i);
                    continue;
                }

                s.OffsetX += s.Vx * dt;
                s.OffsetY += s.Vy * dt;
                s.Visual.RenderTransform = new TranslateTransform(s.OffsetX, s.OffsetY);
                s.Visual.Opacity = 1.0 - (s.Life / s.MaxLife);
            }
        }

        // --- Главный цикл 60 FPS процедурной скелетной анимации и физики ---
        private void OnSkeletalAnimationTick(object? sender, EventArgs e)
        {
            const double dt = 0.016;
            _time += dt;

            if (_headPatCooldown > 0)
                _headPatCooldown -= dt;

            if (_guitarModeTimer > 0)
            {
                _guitarModeTimer -= dt;
                if (_guitarModeTimer <= 0 && _currentSpriteState == "guitar")
                    SetSpriteState(_isSpeaking ? "talk" : "idle");
            }

            // 0. Физика падения с гравитацией после броска мышкой (Toss & Gravity Drop)
            if (_isFalling && !_isDragging)
            {
                _fallVy += 2100.0 * dt; // Ускорение свободного падения
                Left += _fallVx * dt;
                Top += _fallVy * dt;
                _fallVx *= 0.96;

                double floorTop = GetTargetPerchPosition(out double targetLeft);
                var workArea = SystemParameters.WorkArea;
                Left = Math.Clamp(Left, workArea.Left - 180, workArea.Right - Width + 20);

                if (Top >= floorTop)
                {
                    Top = floorTop;
                    _isFalling = false;
                    _fallVx = 0.0;
                    _fallVy = 0.0;
                    // Эффект пружинящего приземления (Squash & Stretch) + вспышка щупалец Relic
                    _landingSquashTimer = 0.26;
                    SpawnGlitchTendrils(10);
                    TriggerRelicGlitch(6, _isSpeaking ? "talk" : "idle");
                }
            }

            // Пружинящее сжатие/растяжение при приземлении на окно
            if (_landingSquashTimer > 0)
            {
                _landingSquashTimer -= dt;
                double s = Math.Sin((_landingSquashTimer / 0.26) * Math.PI);
                AvatarScale.ScaleX = 1.0 + s * 0.16;
                AvatarScale.ScaleY = 1.0 - s * 0.14;
            }
            else
            {
                AvatarScale.ScaleX += (1.0 - AvatarScale.ScaleX) * 0.25;
                AvatarScale.ScaleY += (1.0 - AvatarScale.ScaleY) * 0.25;
            }

            // 1. Физика маятника всего тела при перетаскивании за шкирку (Pendulum Ragdoll)
            double targetPendulum = _isDragging
                ? Math.Clamp(-_dragVx * 0.045, -34.0, 34.0)
                : (_isFalling ? Math.Clamp(_fallVx * 0.025, -18.0, 18.0) : 0.0);

            double springForce = (targetPendulum - _pendulumAngle) * 140.0;
            _pendulumAngularVel = (_pendulumAngularVel + springForce * dt) * 0.82;
            _pendulumAngle += _pendulumAngularVel * dt;
            AvatarPendulumRotate.Angle = _pendulumAngle;

            // Затухание скорости мыши, если курсор остановился во время удержания
            _dragVx *= 0.80;
            _dragVy *= 0.80;

            // 2. Бегущий голографический луч сканирования
            ScanlineTranslate.Y = ((Math.Sin(_time * 1.5) + 1.0) * 0.5) * 305.0 + 10.0;

            // 3. Живое дыхание торса и покачивание в такт музыке / речи
            double breath = Math.Sin(_time * 2.3);
            TorsoScale.ScaleY = 1.0 + breath * 0.013;
            TorsoScale.ScaleX = 1.0 + breath * 0.009;
            TorsoTranslate.Y = -breath * 1.8;
            TorsoRotate.Angle = _isDragging
                ? Math.Sin(_time * 24.0) * 4.5
                : Math.Sin(_time * 1.15) * 1.2;

            // 4. Независимое качание свешенных ног над заголовком окна (Perch Leg Swing & Flailing)
            double targetLegL;
            double targetLegR;
            if (_isDragging || _isFalling)
            {
                // Отчаянно болтает ногами в воздухе, когда ты тащишь его курсором (как Капри!)
                targetLegL = Math.Sin(_time * 26.0) * 22.0 + Math.Clamp(-_dragVx * 0.03, -15.0, 15.0);
                targetLegR = -Math.Sin(_time * 26.0) * 22.0 + Math.Clamp(-_dragVx * 0.03, -15.0, 15.0);
                LegLeftTranslate.Y = Math.Abs(Math.Sin(_time * 26.0)) * -5.0;
                LegRightTranslate.Y = Math.Abs(Math.Cos(_time * 26.0)) * -5.0;
            }
            else if (_currentSpriteState == "guitar")
            {
                // Отбивает ритм ботинками по заголовку окна под рифф Samurai
                targetLegL = Math.Sin(_time * 14.0) * 9.5;
                targetLegR = Math.Cos(_time * 14.0) * 9.5;
                LegLeftTranslate.Y = Math.Abs(Math.Sin(_time * 14.0)) * -3.0;
                LegRightTranslate.Y = 0.0;
            }
            else
            {
                // Спокойно болтает свешенными с окна ногами в 60 FPS
                targetLegL = Math.Sin(_time * 2.6) * 5.2;
                targetLegR = Math.Sin(_time * 2.6 + Math.PI * 0.85) * 5.2;
                LegLeftTranslate.Y = Math.Sin(_time * 2.6) * 1.5;
                LegRightTranslate.Y = -Math.Sin(_time * 2.6) * 1.5;
            }
            _currentLegLeftAngle += (targetLegL - _currentLegLeftAngle) * 0.18;
            _currentLegRightAngle += (targetLegR - _currentLegRightAngle) * 0.18;
            LegLeftRotate.Angle = _currentLegLeftAngle;
            LegRightRotate.Angle = _currentLegRightAngle;

            // 5. Отслеживание курсора мыши головой + развевание волос + блик очков-авиаторов
            double targetHeadAngle = 0.0;
            double targetHeadX = 0.0;
            if (GetCursorPos(out POINT mousePt))
            {
                double headCenterX = Left + Width - 125;
                double headCenterY = Top + 95;
                double dx = (mousePt.X - headCenterX) / 380.0;
                double dy = (mousePt.Y - headCenterY) / 380.0;
                targetHeadAngle = Math.Clamp(dx * 6.5, -7.5, 7.5);
                targetHeadX = Math.Clamp(dx * 3.0, -3.5, 3.5);
                HeadTranslate.Y = Math.Clamp(dy * 2.2, -2.8, 2.8);
            }

            if (_isDragging)
            {
                targetHeadAngle = Math.Sin(_time * 22.0) * 8.0;
            }
            else if (_isSpeaking)
            {
                targetHeadAngle += Math.Sin(_time * 9.5) * 3.2 + Math.Cos(_time * 6.2) * 1.8;
            }
            else if (_currentSpriteState == "guitar")
            {
                targetHeadAngle = Math.Sin(_time * 14.0) * 6.5;
                HeadTranslate.Y = Math.Abs(Math.Sin(_time * 14.0)) * 4.0;
            }

            _currentHeadAngle += (targetHeadAngle - _currentHeadAngle) * 0.15;
            _currentHeadX += (targetHeadX - _currentHeadX) * 0.15;
            HeadRotate.Angle = _currentHeadAngle;
            HeadTranslate.X = _currentHeadX;

            // Физика длинных волос Джонни (вторичное колебание с запаздыванием от головы и ветра)
            double targetHair = -_currentHeadAngle * 0.85 + Math.Sin(_time * 3.1) * 3.2 - _pendulumAngle * 0.45;
            _currentHairAngle += (targetHair - _currentHairAngle) * 0.10;
            HairRotate.Angle = _currentHairAngle;
            HairSkew.AngleX = _currentHairAngle * 0.65;

            // Периодическое «моргание» / пробегающий неоновый блик по линзам авиаторов (каждые ~4.2 сек)
            double blinkPhase = _time % 4.2;
            if (blinkPhase < 0.14)
            {
                RigGlassesGlintImage.Opacity = 0.10; // Прищур / погасание блика
                GlintTranslate.X = 2.5;
            }
            else if (blinkPhase < 0.35)
            {
                RigGlassesGlintImage.Opacity = 1.0;  // Яркая кибер-вспышка на очках
                GlintTranslate.X = (blinkPhase - 0.14) * 12.0;
            }
            else
            {
                RigGlassesGlintImage.Opacity = 0.78 + Math.Sin(_time * 3.0) * 0.12;
                GlintTranslate.X = _currentHeadX * -0.5;
            }

            // 6. Реалистичный Lip-Sync (движение нижней челюсти и бороды при разговоре)
            double targetJawY = 0.0;
            if (_isSpeaking)
            {
                double syllable = Math.Sin(_time * 18.0) * Math.Cos(_time * 11.0) + Math.Sin(_time * 27.0) * 0.5;
                targetJawY = Math.Clamp(Math.Abs(syllable) * 3.6, 0.4, 4.4);

                EqBar1.Height = 4 + Math.Abs(Math.Sin(_time * 19.0)) * 11;
                EqBar2.Height = 5 + Math.Abs(Math.Cos(_time * 23.0)) * 11;
                EqBar3.Height = 4 + Math.Abs(Math.Sin(_time * 15.0)) * 10;
                EqBar4.Height = 5 + Math.Abs(Math.Cos(_time * 27.0)) * 11;
            }
            _currentJawOpen += (targetJawY - _currentJawOpen) * 0.35;
            JawTranslate.Y = _currentJawOpen;

            // 7. Правая и левая руки (курение, барахтанье при перетаскивании, жестикуляция, гитара)
            double targetArmRight = Math.Sin(_time * 1.7) * 2.2;
            double targetArmLeft = -Math.Sin(_time * 1.7) * 1.8;
            bool isTakingDrag = false;

            if (_isDragging || _isFalling)
            {
                // Размахивает обеими руками в воздухе при перетаскивании (Flailing как у Капри)
                targetArmRight = -28.0 + Math.Sin(_time * 28.0) * 18.0;
                targetArmLeft = 24.0 + Math.Cos(_time * 28.0) * 18.0;
            }
            else if (_currentSpriteState == "guitar")
            {
                targetArmRight = -16.0 + Math.Sin(_time * 28.0) * 8.0;
                targetArmLeft = -6.0 + Math.Cos(_time * 14.0) * 4.0;
                GuitarRotate.Angle = Math.Sin(_time * 14.0) * 3.5;
            }
            else if (_isSpeaking)
            {
                targetArmRight = -6.0 + Math.Sin(_time * 7.5) * 5.2 + Math.Cos(_time * 4.2) * 2.8;
                targetArmLeft = -4.0 + Math.Sin(_time * 6.0 + 1.2) * 4.2;
            }
            else
            {
                double cycle = _time % 9.5;
                if (cycle > 6.2 && cycle < 8.6)
                {
                    targetArmRight = -11.5;
                    isTakingDrag = true;
                }
            }

            _currentArmRightAngle += (targetArmRight - _currentArmRightAngle) * 0.14;
            _currentArmLeftAngle += (targetArmLeft - _currentArmLeftAngle) * 0.14;
            ArmRightRotate.Angle = _currentArmRightAngle;
            ArmLeftRotate.Angle = _currentArmLeftAngle;

            CigaretteEmber.Opacity = isTakingDrag ? 0.96 : (0.55 + Math.Sin(_time * 4.5) * 0.25);

            // 8. Частицы дыма и щупальца глитчей (60 FPS)
            UpdateSmokeParticles(dt, isTakingDrag);
            UpdateGlitchTendrils(dt);
        }

        private void UpdateSmokeParticles(double dt, bool intenseSmoke)
        {
            double spawnChance = intenseSmoke ? 0.42 : 0.18;
            if (_currentSpriteState != "guitar" && !_isDragging && _rng.NextDouble() < spawnChance && _smokeParticles.Count < 22)
            {
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

        // --- Вычисление координаты посадки на верхний край окна со свешенными ногами (Perch Ledge) ---
        private double GetTargetPerchPosition(out double targetLeft)
        {
            var workArea = SystemParameters.WorkArea;
            targetLeft = workArea.Right - Width - 16;
            // Смещение +68 px вниз позволяет ногам Джонни (ниже пояса) свисать прямо на панель задач или заголовок окна!
            double defaultFloorTop = workArea.Bottom - Height + 68;

            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && GetActiveWindowTitle() != "Рабочий стол Windows")
            {
                if (GetWindowRect(fg, out RECT rect))
                {
                    // Если активное окно не развёрнуто на весь экран, сажаем Джонни прямо на его верхнюю рамку (rect.Top)
                    if (rect.Top > 160 && rect.Right > 420 && rect.Right <= workArea.Right - 10)
                    {
                        targetLeft = Math.Clamp(rect.Right - Width - 24, workArea.Left - 180, workArea.Right - Width - 8);
                        // Таз Джонни (y = Height - 82) ложится ровно на линию rect.Top, а ботинки свисают на заголовок окна!
                        return Math.Clamp(rect.Top - Height + 82, workArea.Top + 4, defaultFloorTop);
                    }
                }
            }
            return defaultFloorTop;
        }

        private void OnWindowTrackerTick(object? sender, EventArgs e)
        {
            if (_isDragging || _isFalling || !_snapToActiveWindow || Visibility != Visibility.Visible) return;

            double targetTop = GetTargetPerchPosition(out double targetLeft);
            Left += (targetLeft - Left) * 0.20;
            Top += (targetTop - Top) * 0.20;
        }

        // --- Неблокирующее перетаскивание мышкой с физикой маятника, броском и поглаживанием головы ---
        private void Avatar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                OpenChatBox();
                return;
            }

            _isDragging = true;
            _isFalling = false;
            _dragAnchorOffset = e.GetPosition(this);

            if (GetCursorPos(out POINT pt))
            {
                _lastMouseScreenX = pt.X;
                _lastMouseScreenY = pt.Y;
            }

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                _snapToActiveWindow = false;
                MenuSnapItem.Header = "📌 Посадка на край активного окна: ВЫКЛ";
            }

            SpawnGlitchTendrils(8);
            SpeechText.Text = "Эй-эй, чумба, полегче с курсором! Куда ты меня тащишь?!";
            AvatarContainer.CaptureMouse();
        }

        private void Avatar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                if (GetCursorPos(out POINT pt))
                {
                    double dx = pt.X - _lastMouseScreenX;
                    double dy = pt.Y - _lastMouseScreenY;
                    _dragVx = (_dragVx * 0.45) + (dx / 0.016) * 0.55;
                    _dragVy = (_dragVy * 0.45) + (dy / 0.016) * 0.55;

                    Left = pt.X - _dragAnchorOffset.X;
                    Top = pt.Y - _dragAnchorOffset.Y;

                    _lastMouseScreenX = pt.X;
                    _lastMouseScreenY = pt.Y;
                }
                return;
            }

            // Пасхалка Head Pats (поглаживание курсором по голове Джонни, как в capri-desktop)
            Point localPos = e.GetPosition(AvatarContainer);
            if (localPos.Y > 15 && localPos.Y < 105 && _headPatCooldown <= 0)
            {
                double deltaX = localPos.X - _lastHoverX;
                if (Math.Abs(deltaX) > 6.0)
                {
                    int sign = Math.Sign(deltaX);
                    if (sign != _lastHeadPatSign && _lastHeadPatSign != 0)
                    {
                        _headPatDirectionChanges++;
                        if (_headPatDirectionChanges >= 6)
                        {
                            _headPatDirectionChanges = 0;
                            _headPatCooldown = 6.0;
                            SpawnGlitchTendrils(10);
                            TriggerRelicGlitch(10, "idle");
                            SpeechText.Text = "Ви, руки убрал от моих авиаторов! Я тебе легенда Найт-Сити, а не ручной тамагочи.";
                        }
                    }
                    _lastHeadPatSign = sign;
                    _lastHoverX = localPos.X;
                }
            }
        }

        private void Avatar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            AvatarContainer.ReleaseMouseCapture();

            // Если отпустили в воздухе выше точки опоры — включаем гравитацию и инерцию броска (Toss)
            double perchTop = GetTargetPerchPosition(out _);
            if (_snapToActiveWindow && Top < perchTop - 12)
            {
                _isFalling = true;
                _fallVx = Math.Clamp(_dragVx * 0.35, -650.0, 650.0);
                _fallVy = Math.Clamp(_dragVy * 0.25, -400.0, 250.0);
            }
            else
            {
                _landingSquashTimer = 0.20;
                SpawnGlitchTendrils(8);
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
            trayMenu.Items.Add("✎ Написать Джонни (Alt+J)", null, (_, _) => Dispatcher.Invoke(OpenChatBox));
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
                SpawnGlitchTendrils(14);
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

        private async void OnProactiveCheckInTick(object? sender, EventArgs e)
        {
            if (!_proactiveCheckInEnabled || Visibility != Visibility.Visible) return;
            string activeTitle = GetActiveWindowTitle();
            SpawnGlitchTendrils(12);
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
            AvatarTranslate.X = _rng.Next(-11, 12);
            AvatarTranslate.Y = _rng.Next(-4, 5);
            AvatarContainer.Opacity = _rng.NextDouble() * 0.35 + 0.65;
            JohnnyDropShadow.Color = (_glitchTicksRemaining % 2 == 0)
                ? Color.FromRgb(0, 240, 255)
                : Color.FromRgb(255, 42, 85);
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
                    SpawnGlitchTendrils(14);
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
                SpawnGlitchTendrils(10);
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
            SpawnGlitchTendrils(12);
            TriggerRelicGlitch(10, "guitar");
            SpeechText.Text = "🎸 Врубай Samurai — Never Fade Away на полную катушку, чумба!";
        }

        private void MenuToggleSnap_Click(object sender, RoutedEventArgs e)
        {
            _snapToActiveWindow = !_snapToActiveWindow;
            MenuSnapItem.Header = _snapToActiveWindow
                ? "📌 Посадка на край активного окна: ВКЛ"
                : "📌 Посадка на край активного окна: ВЫКЛ";
        }

        private void MenuToggleControl_Click(object sender, RoutedEventArgs e)
        {
            _pseudoendotrizineMode = !_pseudoendotrizineMode;
            MenuControlItem.Header = _pseudoendotrizineMode
                ? "💊 Псевдоэндотризин (Управление ПК): ВКЛ"
                : "💊 Псевдоэндотризин (Управление ПК): ВЫКЛ";
            SpawnGlitchTendrils(10);
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
