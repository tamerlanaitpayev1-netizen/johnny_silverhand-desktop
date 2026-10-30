using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace JohnnyOverlay
{
    public partial class JohnnyCursorWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public JohnnyCursorWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
            };
        }

        public async Task FlyAlongBezierAsync(Point start, Point end, string actionLabel = "RELIC // ACT")
        {
            CursorActionLabel.Text = actionLabel;
            ClickPulseRing.Opacity = 0.0;
            Left = start.X;
            Top = start.Y;
            Opacity = 1.0;
            Show();

            // Контрольная точка кривой Безье (дуга вверх/вбок как живое движение руки)
            Point control = new Point(
                (start.X + end.X) * 0.5 + (end.Y - start.Y) * 0.25,
                Math.Min(start.Y, end.Y) - 90.0
            );

            const int steps = 38;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                // Smoothstep easing
                double s = t * t * (3.0 - 2.0 * t);
                double u = 1.0 - s;

                double bx = u * u * start.X + 2.0 * u * s * control.X + s * s * end.X;
                double by = u * u * start.Y + 2.0 * u * s * control.Y + s * s * end.Y;

                Left = bx;
                Top = by;
                await Task.Delay(15);
            }

            // Эффект клика кибер-рукой в целевой точке
            for (int k = 0; k < 10; k++)
            {
                ClickPulseRing.Opacity = 1.0 - k * 0.1;
                await Task.Delay(25);
            }

            Hide();
        }
    }
}
