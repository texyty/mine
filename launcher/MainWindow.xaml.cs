using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MyCustomLauncher;

public partial class MainWindow : Window {
    private readonly LauncherConfig config;
    private readonly ApiClient api;
    private readonly LauncherPreferences preferences;
    private readonly string gameDir;
    private readonly DispatcherTimer healthTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? launchCancellation;
    private Process? gameProcess;
    private string? sessionToken;
    private string? username;

    public MainWindow() {
        InitializeComponent();
        preferences = LauncherPreferences.Load();
        UsernameBox.Text = preferences.Username;
        SelectRam(preferences.RamGb);
        RamSlider.Value = preferences.RamGb;
        try {
            config = LauncherConfig.Load();
            api = new ApiClient(config.ApiBaseUrl);
            gameDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".mycustomclient");
            GamePathBox.Text = gameDir;
            Loaded += RestoreSessionAsync;
            healthTimer.Tick += async (_, _) => await UpdateServerStatusAsync();
            healthTimer.Start();
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "Ошибка конфигурации", MessageBoxButton.OK, MessageBoxImage.Error);
            Close(); config = null!; api = null!; gameDir = "";
        }
    }

    private async void RestoreSessionAsync(object sender, RoutedEventArgs e) {
        await UpdateServerStatusAsync();
        var saved = SecureTokenStore.Load();
        if (saved is null) return;
        LoginStatus.Text = "Проверяем сохранённую сессию…";
        if (await api.ValidateAsync(saved)) {
            sessionToken = saved; username = ReadJwtUsername(saved) ?? "Игрок"; ShowLauncher();
        } else { SecureTokenStore.Clear(); LoginStatus.Text = "Сессия истекла. Войдите снова."; }
    }

    private async void Login_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text) || string.IsNullOrEmpty(PasswordBox.Password)) { LoginStatus.Text = "Введите логин и пароль."; return; }
        SetLoginBusy(true);
        try {
            var result = await api.LoginAsync(UsernameBox.Text.Trim(), PasswordBox.Password, HwidService.GetHwid(config.HwidApplicationSalt));
            CompleteLogin(result.SessionToken, result.Username);
        } catch (Exception ex) when (ex is LauncherApiException or InvalidOperationException) { LoginStatus.Text = ex.Message; }
        finally { SetLoginBusy(false); }
    }

    private async void WebLogin_Click(object sender, RoutedEventArgs e) {
        SetLoginBusy(true); LoginStatus.Text = "Создаём защищённый запрос для сайта…";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try {
            var request = await api.StartWebAuthAsync(HwidService.GetHwid(config.HwidApplicationSalt), timeout.Token);
            var address = $"{config.WebBaseUrl.TrimEnd('/')}?launcher_auth={Uri.EscapeDataString(request.RequestId)}";
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });
            LoginStatus.Text = "Войдите на сайте. Лаунчер ждёт подтверждение…";
            while (!timeout.IsCancellationRequested) {
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                var result = await api.PollWebAuthAsync(request.RequestId, timeout.Token);
                if (result.Status == "approved" && result.SessionToken is not null && result.Username is not null) { CompleteLogin(result.SessionToken, result.Username); return; }
                if (result.Status is "denied" or "expired") throw new LauncherApiException(result.Detail ?? "Вход через сайт не был подтверждён.");
            }
        } catch (OperationCanceledException) { LoginStatus.Text = "Время ожидания входа через сайт истекло."; }
        catch (LauncherApiException ex) { LoginStatus.Text = ex.Message; }
        finally { SetLoginBusy(false); }
    }

    private void CompleteLogin(string token, string login) {
        sessionToken = token; username = login; preferences.Username = login; preferences.Save();
        if (RememberBox.IsChecked == true) SecureTokenStore.Save(token); else SecureTokenStore.Clear();
        PasswordBox.Clear(); ShowLauncher();
    }

    private async void Play_Click(object sender, RoutedEventArgs e) {
        if (sessionToken is null || username is null || gameProcess is { HasExited: false }) return;
        PlayButton.IsEnabled = false; CancelButton.Visibility = Visibility.Visible; launchCancellation = new CancellationTokenSource();
        try {
            LaunchStatus.Text = "Проверяем сессию…";
            if (!await api.ValidateAsync(sessionToken, launchCancellation.Token)) { SecureTokenStore.Clear(); throw new LauncherApiException("Сессия истекла. Войдите повторно."); }
            var service = new ContentService(config.ContentBaseUrl, gameDir);
            var progress = new Progress<ContentProgress>(v => { Progress.Value = v.Percent; LaunchStatus.Text = v.Message; });
            var manifest = await service.SynchronizeAsync(config.ManifestPath, progress, launchCancellation.Token);
            var ram = int.Parse(((ComboBoxItem)RamBox.SelectedItem).Tag.ToString()!);
            preferences.RamGb = ram; preferences.Save();
            LaunchStatus.Text = "Запускаем игру…";
            gameProcess = GameLauncher.Start(JavaResolver.Resolve(config.JavaPath), gameDir, manifest, username, sessionToken, ram);
            gameProcess.EnableRaisingEvents = true;
            gameProcess.Exited += (_, _) => Dispatcher.Invoke(() => { LaunchStatus.Text = "Игра завершена"; PlayButton.IsEnabled = true; gameProcess?.Dispose(); gameProcess = null; });
            LaunchStatus.Text = $"Игра запущена (PID {gameProcess.Id})";
        } catch (OperationCanceledException) { LaunchStatus.Text = "Обновление отменено"; PlayButton.IsEnabled = true; }
        catch (Exception ex) when (ex is LauncherApiException or HttpRequestException or IOException or InvalidDataException or InvalidOperationException) { AppLog.Error("Game launch failed", ex); LaunchStatus.Text = ex.Message; PlayButton.IsEnabled = true; }
        finally { CancelButton.Visibility = Visibility.Collapsed; launchCancellation?.Dispose(); launchCancellation = null; }
    }

    private void ShowLauncher() { LoginPanel.Visibility = Visibility.Collapsed; LauncherPanel.Visibility = Visibility.Visible; GreetingText.Text = $"Привет, {username}"; ProfileUsername.Text = $"Логин: {username}"; ProfileAccess.Text = "Статус: сессия активна"; ProfileHwid.Text = $"HWID: {HwidService.GetHwid(config.HwidApplicationSalt)}"; ShowPage(HomePage); }
    private void ShowPage(Grid page) { HomePage.Visibility = page == HomePage ? Visibility.Visible : Visibility.Collapsed; ProfilePage.Visibility = page == ProfilePage ? Visibility.Visible : Visibility.Collapsed; SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed; page.Opacity = 0; ((Storyboard)FindResource("Reveal")).Begin(page); }
    private void Home_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void Profile_Click(object sender, RoutedEventArgs e) => ShowPage(ProfilePage);
    private void Settings_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void SelectClient_Click(object sender, RoutedEventArgs e) { GreetingText.Text = $"{((Button)sender).Tag} готов"; LaunchStatus.Text = "Клиент выбран. Можно запускать."; }
    private void Logout_Click(object sender, RoutedEventArgs e) { SecureTokenStore.Clear(); sessionToken = null; username = null; LauncherPanel.Visibility = Visibility.Collapsed; LoginPanel.Visibility = Visibility.Visible; LoginStatus.Text = ""; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => launchCancellation?.Cancel();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void OpenGameFolder_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(gameDir); Process.Start(new ProcessStartInfo(gameDir) { UseShellExecute = true }); }
    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (RamValueText is null || RamBox is null) return; var value = (int)Math.Round(e.NewValue); RamValueText.Text = $"{value} GB"; SelectRam(value); }
    private void SetLoginBusy(bool busy) { LoginButton.IsEnabled = !busy; WebLoginButton.IsEnabled = !busy; if (busy && string.IsNullOrEmpty(LoginStatus.Text)) LoginStatus.Text = "Авторизация…"; }
    private async Task UpdateServerStatusAsync() { var healthy = await api.IsHealthyAsync(); ServerStatus.Text = healthy ? "Сервер доступен" : "Сервер недоступен"; ServerDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(healthy ? "#53DF9E" : "#FF7186")); }
    private void SelectRam(int ram) { if (RamBox is null) return; foreach (ComboBoxItem item in RamBox.Items) if (item.Tag?.ToString() == ram.ToString()) { RamBox.SelectedItem = item; break; } }
    private static string? ReadJwtUsername(string token) { try { var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '='); using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload)); return document.RootElement.GetProperty("username").GetString(); } catch { return null; } }
}
