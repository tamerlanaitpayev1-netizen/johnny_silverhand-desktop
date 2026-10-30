using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace JohnnyOverlay
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
            {
                int exitCode = await RunSmokeTestsAsync();
                Shutdown(exitCode);
            }
        }

        private static async Task<int> RunSmokeTestsAsync()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            string root = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "assets")))
                {
                    root = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }

            var log = new System.Collections.Generic.List<string>
            {
                "=== RELIC ENGRAM v2.0 AUTOMATED SMOKE TEST ==="
            };
            bool allPass = true;

            // 1. Проверка 11 слоёв 2.5D-рига (Этап 8)
            string[] rigFiles =
            {
                "rig_hair_back.png", "rig_leg_left.png", "rig_leg_right.png", "rig_lower.png",
                "rig_torso.png", "rig_arm_right.png", "rig_arm_left.png", "rig_head.png",
                "rig_glasses_glint.png", "rig_jaw.png", "rig_guitar.png"
            };
            foreach (string rf in rigFiles)
            {
                bool exists = File.Exists(Path.Combine(root, "assets", "rig", rf));
                log.Add($"[{(exists ? "PASS" : "FAIL")}] Rig Layer: {rf}");
                if (!exists) allPass = false;
            }

            // 2. Проверка 4 покадровых лент Sprite Strips (Этап 11)
            string[] stripFiles = { "strip_beam_in.png", "strip_malorian_spin.png", "strip_sleep.png", "strip_walk.png" };
            foreach (string sf in stripFiles)
            {
                bool exists = File.Exists(Path.Combine(root, "assets", "strips", sf));
                log.Add($"[{(exists ? "PASS" : "FAIL")}] Sprite Strip: {sf}");
                if (!exists) allPass = false;
            }

            // 3. Проверка связи с Python-сервером (/health)
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string health = await http.GetStringAsync("http://127.0.0.1:8077/health");
                log.Add($"[PASS] Backend /health: {health}");
            }
            catch (Exception ex)
            {
                log.Add($"[WARN] Backend /health offline during standalone check: {ex.Message}");
            }

            log.Add($"=== RESULT: {(allPass ? "ALL ASSETS & RIG CHECKS PASSED" : "SOME CHECKS FAILED")} ===");
            File.WriteAllLines(Path.Combine(root, "smoke_report.txt"), log);
            return allPass ? 0 : 1;
        }
    }
}
