using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace JohnnyOverlay
{
    public partial class MainframeWindow : Window
    {
        private readonly MainWindow _ownerOverlay;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string _projectRoot;

        public MainframeWindow(MainWindow ownerOverlay, string projectRoot)
        {
            InitializeComponent();
            _ownerOverlay = ownerOverlay;
            _projectRoot = projectRoot;
        }

        private async void BtnCheckHealth_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var resp = await _http.GetStringAsync("http://127.0.0.1:8077/health");
                HealthResultText.Text = $"Ядро активно: {resp}";
                StatusBadge.Text = "[ONLINE]";
            }
            catch (Exception ex)
            {
                HealthResultText.Text = $"Ошибка связи с сервером: {ex.Message}";
                StatusBadge.Text = "[OFFLINE]";
            }
        }

        private void BtnOpenRvcFolder_Click(object sender, RoutedEventArgs e)
        {
            string rvcDir = Path.Combine(_projectRoot, "backend", "models", "johnny_ru");
            Directory.CreateDirectory(rvcDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{rvcDir}\"") { UseShellExecute = true });
        }

        private async void BtnVoiceListen_Click(object sender, RoutedEventArgs e)
        {
            await _ownerOverlay.TriggerVoiceListenAsync();
        }

        private void SliderScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_ownerOverlay != null)
                _ownerOverlay.SetCharacterBaseScale(e.NewValue);
        }

        private void SliderFps_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_ownerOverlay != null)
                _ownerOverlay.SetAnimationFps((int)e.NewValue);
        }

        private void BtnStripBeam_Click(object sender, RoutedEventArgs e) =>
            _ownerOverlay.PlaySpriteStrip("strip_beam_in.png", loops: 1);

        private void BtnStripMalorian_Click(object sender, RoutedEventArgs e) =>
            _ownerOverlay.PlaySpriteStrip("strip_malorian_spin.png", loops: 2);

        private void BtnStripSleep_Click(object sender, RoutedEventArgs e) =>
            _ownerOverlay.PlaySpriteStrip("strip_sleep.png", loops: 3);

        private void BtnStripWalk_Click(object sender, RoutedEventArgs e) =>
            _ownerOverlay.PlaySpriteStrip("strip_walk.png", loops: 2);

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var payload = new
                {
                    llm_mode = RadioCloud.IsChecked == true ? "openai_compat" : "ollama",
                    api_key = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password,
                    voice_enabled = ChkVoiceEnabled.IsChecked == true,
                    dsp_strength = SliderDspStrength.Value
                };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await _http.PostAsync("http://127.0.0.1:8077/settings", content);
            }
            catch
            {
            }
            Close();
        }
    }
}
