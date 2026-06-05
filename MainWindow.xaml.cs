using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace WindowsPomodoro;

public partial class MainWindow : Window
{
    private const int TimerMinutes = 30;
    private const int DefaultBreakSeconds = 10;

    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;
    private TimeSpan _timeRemaining;
    private bool _isRunning;
    private TimerPhase _phase = TimerPhase.Pomodoro;

    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "WindowsPomodoro";
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsPomodoro");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly Brush PomodoroBackground = Brushes.White;
    private static readonly Brush BreakBackground = new SolidColorBrush(Color.FromRgb(232, 245, 233));

    public MainWindow()
    {
        InitializeComponent();
        _settings = LoadSettings();
        BreakSecondsBox.Text = _settings.BreakSeconds.ToString(CultureInfo.InvariantCulture);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        ResetTimer();
        AutoStartCheckBox.IsChecked = IsAutoStartEnabled();
        StartPauseThumbButton.ImageSource = CreateGlyphIcon("\uE768");
        ResetThumbButton.ImageSource = CreateGlyphIcon("\uE72C");
    }

    private static ImageSource CreateGlyphIcon(string glyph)
    {
        const int size = 16;
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            var formattedText = new FormattedText(
                glyph,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12,
                Brushes.White,
                1.0);
            formattedText.TextAlignment = TextAlignment.Center;
            dc.DrawText(formattedText, new Point(size / 2.0, (size - formattedText.Height) / 2.0));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private void StartPauseThumbButton_Click(object? sender, EventArgs e) => TogglePlayPause();
    private void ResetThumbButton_Click(object? sender, EventArgs e) => DoReset();

    private void TogglePlayPause()
    {
        StopFlashTaskbar();
        if (_isRunning)
        {
            _timer.Stop();
            _isRunning = false;
            SetStartButtonState();
        }
        else
        {
            _timer.Start();
            _isRunning = true;
            SetPauseButtonState();
        }
        UpdateTaskbarOverlay();
    }

    private void DoReset()
    {
        StopFlashTaskbar();
        _timer.Stop();
        _isRunning = false;
        SetStartButtonState();
        ResetTimer();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timeRemaining -= TimeSpan.FromSeconds(1);

        if (_timeRemaining <= TimeSpan.Zero)
        {
            _timeRemaining = TimeSpan.Zero;
            UpdateDisplay();
            _timer.Stop();
            _isRunning = false;
            SetStartButtonState();
            OnTimerComplete();
            return;
        }

        UpdateDisplay();
    }

    private void OnTimerComplete()
    {
        var completedPhase = _phase;
        SystemSounds.Beep.Play();
        FlashTaskbar();
        ShowToastNotification(completedPhase);

        if (completedPhase == TimerPhase.Pomodoro)
            SetBreakReady();
        else
            SetPomodoroReady();
    }

    private static void ShowToastNotification(TimerPhase completedPhase)
    {
        var toast = new ToastContentBuilder();
        if (completedPhase == TimerPhase.Pomodoro)
        {
            toast
                .AddText("Pomodoro Complete!")
                .AddText($"Time for a break. Your {TimerMinutes}-minute session has ended.");
        }
        else
        {
            toast
                .AddText("Break Complete!")
                .AddText("Ready for the next pomodoro.");
        }

        toast.Show();
    }

    private void ResetTimer()
    {
        SetPomodoroReady();
    }

    private void SetPomodoroReady()
    {
        _timeRemaining = TimeSpan.FromMinutes(TimerMinutes);
        _phase = TimerPhase.Pomodoro;
        UpdateDisplay();
    }

    private void SetBreakReady()
    {
        _timeRemaining = TimeSpan.FromSeconds(_settings.BreakSeconds);
        _phase = TimerPhase.Break;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var displayTime = FormatTimeRemaining(_timeRemaining);
        TimerDisplay.Text = displayTime;
        Title = $"{displayTime} - {GetPhaseTitle()}";
        Background = _phase == TimerPhase.Break ? BreakBackground : PomodoroBackground;
        UpdateTaskbarOverlay();
    }

    private string GetPhaseTitle() => _phase == TimerPhase.Break ? "Break" : "Pomodoro";

    private static string FormatTimeRemaining(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    private void SetStartButtonState()
    {
        StartPauseButton.Content = "Start";
        StartPauseThumbButton.Description = "Start";
        StartPauseThumbButton.ImageSource = CreateGlyphIcon("\uE768");
    }

    private void SetPauseButtonState()
    {
        StartPauseButton.Content = "Pause";
        StartPauseThumbButton.Description = "Pause";
        StartPauseThumbButton.ImageSource = CreateGlyphIcon("\uE769");
    }

    private void UpdateTaskbarOverlay()
    {
        if (TaskbarItemInfo == null) return;

        if (!_isRunning)
        {
            TaskbarItemInfo.Overlay = null;
            return;
        }

        var text = _timeRemaining.TotalMinutes >= 1
            ? Math.Ceiling(_timeRemaining.TotalMinutes).ToString(CultureInfo.InvariantCulture)
            : Math.Ceiling(_timeRemaining.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        const int size = 20;
        var dpi = VisualTreeHelper.GetDpi(this);
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            var overlayBrush = _phase == TimerPhase.Break ? Brushes.DodgerBlue : Brushes.OrangeRed;
            dc.DrawEllipse(overlayBrush, null, new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);

            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                text.Length >= 3 ? 9 : text.Length == 2 ? 11 : 14,
                Brushes.White,
                dpi.PixelsPerDip);

            formattedText.TextAlignment = TextAlignment.Center;
            dc.DrawText(formattedText, new Point(size / 2.0, (size - formattedText.Height) / 2.0));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();

        TaskbarItemInfo.Overlay = bitmap;
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void ResetButton_Click(object sender, RoutedEventArgs e) => DoReset();

    private void SetCustomMinutes_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CustomMinutesBox.Text, out var minutes) && minutes > 0)
        {
            _timer.Stop();
            _isRunning = false;
            _timeRemaining = TimeSpan.FromMinutes(minutes);
            _phase = TimerPhase.Pomodoro;
            UpdateDisplay();
            SetStartButtonState();
            CustomMinutesBox.Clear();
        }
    }

    private void SaveBreakSeconds_Click(object sender, RoutedEventArgs e)
    {
        BreakSecondsError.Text = string.Empty;

        if (!int.TryParse(BreakSecondsBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            BreakSecondsError.Text = "Enter positive seconds.";
            return;
        }

        _settings.BreakSeconds = seconds;
        BreakSecondsBox.Text = seconds.ToString(CultureInfo.InvariantCulture);

        try
        {
            SaveSettings(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BreakSecondsError.Text = "Could not save setting.";
            return;
        }

        if (_phase == TimerPhase.Break && !_isRunning)
            SetBreakReady();
    }

    private void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        _timeRemaining = TimeSpan.FromSeconds(5);
        UpdateDisplay();
        if (!_isRunning) TogglePlayPause();
    }

    #region Auto-Start

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
        return key?.GetValue(RegistryValueName) != null;
    }

    private void AutoStartCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var exePath = Environment.ProcessPath;
        if (exePath == null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
        if (key == null) return;

        if (AutoStartCheckBox.IsChecked == true)
            key.SetValue(RegistryValueName, $"\"{exePath}\"");
        else
            key.DeleteValue(RegistryValueName, false);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    #endregion

    #region Settings

    private sealed class AppSettings
    {
        public int BreakSeconds { get; set; } = DefaultBreakSeconds;
    }

    private enum TimerPhase
    {
        Pomodoro,
        Break
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath));
            return settings?.BreakSeconds > 0 ? settings : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    private static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    #endregion

    #region Taskbar Flash

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_ALL = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void FlashTaskbar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
    }

    private void StopFlashTaskbar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_STOP,
            uCount = 0,
            dwTimeout = 0
        };
        FlashWindowEx(ref info);
    }

    #endregion
}
