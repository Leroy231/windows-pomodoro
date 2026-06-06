using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly int[] DefaultFrequentPomodoroMinutes = [20, 45, 60];

    private readonly DispatcherTimer _timer;
    private readonly AppSettings _settings;
    private readonly ImageSource _defaultIcon;
    private readonly ImageSource _sittingIcon;
    private readonly IntPtr _defaultSmallIconHandle;
    private readonly IntPtr _defaultLargeIconHandle;
    private readonly IntPtr _sittingSmallIconHandle;
    private readonly IntPtr _sittingLargeIconHandle;
    private TimeSpan _timeRemaining;
    private TimeSpan _currentPomodoroDuration = TimeSpan.FromMinutes(TimerMinutes);
    private bool _isRunning;
    private TimerPhase _phase = TimerPhase.Pomodoro;
    private DeskMode _currentDeskMode = DeskMode.Sitting;
    private int _currentPomodoroMinutes = TimerMinutes;
    private bool _hasStartedCurrentPomodoro;
    private bool _sittingStartReminderShown;
    private bool _sittingMidpointReminderShown;
    private bool _isUpdatingDeskModeControls;

    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "WindowsPomodoro";
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsPomodoro");
    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    private static readonly JsonSerializerOptions SettingsJsonSerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly Brush PomodoroBackground = Brushes.White;
    private static readonly Brush BreakBackground = new SolidColorBrush(Color.FromRgb(232, 245, 233));

    public MainWindow()
    {
        InitializeComponent();
        _settings = LoadSettings();
        _defaultIcon = Icon;
        _sittingIcon = CreateEmojiIcon("🚲");
        _defaultSmallIconHandle = CreateIconHandle(_defaultIcon, GetSystemMetric(SM_CXSMICON, 16));
        _defaultLargeIconHandle = CreateIconHandle(_defaultIcon, GetSystemMetric(SM_CXICON, 32));
        _sittingSmallIconHandle = CreateIconHandle(_sittingIcon, GetSystemMetric(SM_CXSMICON, 16));
        _sittingLargeIconHandle = CreateIconHandle(_sittingIcon, GetSystemMetric(SM_CXICON, 32));
        SourceInitialized += (_, _) => ApplyWindowAndTaskbarIcon();
        Closed += (_, _) => DestroyIconHandles();
        _currentDeskMode = _settings.NextDeskMode;
        BreakSecondsBox.Text = _settings.BreakSeconds.ToString(CultureInfo.InvariantCulture);
        UpdateDeskModeControls();
        RenderFrequentTimes();
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

    private static IntPtr CreateIconHandle(ImageSource imageSource, int size)
    {
        var bitmap = RenderIconBitmap(imageSource, size);
        var stride = size * 4;
        var pixels = new byte[stride * size];
        bitmap.CopyPixels(pixels, stride, 0);

        var colorBitmap = CreateBitmap(size, size, 1, 32, pixels);
        var maskStride = ((size + 15) / 16) * 2;
        var maskBitmap = CreateBitmap(size, size, 1, 1, new byte[maskStride * size]);

        try
        {
            var iconInfo = new ICONINFO
            {
                fIcon = true,
                hbmColor = colorBitmap,
                hbmMask = maskBitmap
            };
            return CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            if (colorBitmap != IntPtr.Zero)
                DeleteObject(colorBitmap);

            if (maskBitmap != IntPtr.Zero)
                DeleteObject(maskBitmap);
        }
    }

    private static BitmapSource RenderIconBitmap(ImageSource imageSource, int size)
    {
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            dc.DrawImage(imageSource, new Rect(0, 0, size, size));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private static int GetSystemMetric(int index, int fallback)
    {
        var value = GetSystemMetrics(index);
        return value > 0 ? value : fallback;
    }

    private static ImageSource CreateEmojiIcon(string emoji)
    {
        const int size = 64;
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(20, 120, 80)), null, new Point(size / 2.0, size / 2.0), 30, 30);
            dc.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(size / 2.0, size / 2.0), 28, 28);

            var formattedText = new FormattedText(
                emoji,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Emoji"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                38,
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
            StartCurrentTimer();
        }
        UpdateTaskbarOverlay();
    }

    private void StartCurrentTimer()
    {
        _timer.Start();
        _isRunning = true;
        SetPauseButtonState();

        if (_phase == TimerPhase.Pomodoro && !_hasStartedCurrentPomodoro)
        {
            _hasStartedCurrentPomodoro = true;
            MaybeShowSittingStartReminder();
        }
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

        MaybeShowSittingMidpointReminder();
        UpdateDisplay();
    }

    private void OnTimerComplete()
    {
        var completedPhase = _phase;
        SystemSounds.Beep.Play();
        FlashTaskbar();
        ShowToastNotification(completedPhase, _currentPomodoroMinutes);

        if (completedPhase == TimerPhase.Pomodoro)
        {
            AdvanceDeskModeAfterPomodoro();
            SetBreakReady();
        }
        else
        {
            SetPomodoroReady();
        }
    }

    private static void ShowToastNotification(TimerPhase completedPhase, int completedPomodoroMinutes)
    {
        var toast = new ToastContentBuilder();
        if (completedPhase == TimerPhase.Pomodoro)
        {
            toast
                .AddText("Pomodoro Complete!")
                .AddText($"Time for a break. Your {completedPomodoroMinutes}-minute session has ended.");
        }
        else
        {
            toast
                .AddText("Break Complete!")
                .AddText("Ready for the next pomodoro.");
        }

        toast.Show();
    }

    private static void ShowEllipticalReminderToast(bool isMidpoint)
    {
        var toast = new ToastContentBuilder();
        if (isMidpoint)
        {
            toast
                .AddText("Elliptical reminder")
                .AddText("You're halfway through this sitting session. Time to pedal a bit.");
        }
        else
        {
            toast
                .AddText("Sitting session started")
                .AddText("Use your under-desk elliptical while you sit.");
        }

        toast.Show();
    }

    private void ResetTimer()
    {
        SetPomodoroReady();
    }

    private void SetPomodoroReady()
    {
        SetPomodoroMinutes(TimerMinutes, false);
    }

    private void SetPomodoroMinutes(int minutes, bool startImmediately)
    {
        _timer.Stop();
        _isRunning = false;
        _currentPomodoroMinutes = minutes;
        _currentPomodoroDuration = TimeSpan.FromMinutes(minutes);
        _timeRemaining = _currentPomodoroDuration;
        _phase = TimerPhase.Pomodoro;
        ResetPomodoroReminderState();
        UpdateDisplay();
        SetStartButtonState();

        if (!startImmediately)
            return;

        StartCurrentTimer();
        UpdateTaskbarOverlay();
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

    private string GetPhaseTitle() =>
        _phase == TimerPhase.Break ? "Break" : $"Pomodoro ({_currentDeskMode})";

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
        if (int.TryParse(CustomMinutesBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            IsValidPomodoroMinutes(minutes))
        {
            StopFlashTaskbar();
            SetPomodoroMinutes(minutes, false);
            CustomMinutesBox.Clear();
        }
    }

    private void FrequentMinutesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int minutes })
        {
            StopFlashTaskbar();
            SetPomodoroMinutes(minutes, true);
        }
    }

    private void DeskModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingDeskModeControls)
            return;

        if (sender == SittingModeRadioButton)
            SetDeskMode(DeskMode.Sitting);
        else if (sender == StandingModeRadioButton)
            SetDeskMode(DeskMode.Standing);
    }

    private void SetDeskMode(DeskMode deskMode)
    {
        if (_currentDeskMode == deskMode)
            return;

        _currentDeskMode = deskMode;
        _settings.NextDeskMode = deskMode;
        ResetPomodoroReminderState();
        UpdateDeskModeControls();
        UpdateDisplay();
        TrySaveSettings();

        if (_phase == TimerPhase.Pomodoro && _isRunning)
        {
            _hasStartedCurrentPomodoro = true;
            MaybeShowSittingStartReminder();
        }
    }

    private void AdvanceDeskModeAfterPomodoro()
    {
        _settings.LastCompletedDeskMode = _currentDeskMode;
        _currentDeskMode = GetNextDeskMode(_currentDeskMode);
        _settings.NextDeskMode = _currentDeskMode;
        UpdateDeskModeControls();
        TrySaveSettings();
    }

    private static DeskMode GetNextDeskMode(DeskMode deskMode) =>
        deskMode == DeskMode.Sitting ? DeskMode.Standing : DeskMode.Sitting;

    private void UpdateDeskModeControls()
    {
        _isUpdatingDeskModeControls = true;
        SittingModeRadioButton.IsChecked = _currentDeskMode == DeskMode.Sitting;
        StandingModeRadioButton.IsChecked = _currentDeskMode == DeskMode.Standing;
        _isUpdatingDeskModeControls = false;
        ApplyWindowAndTaskbarIcon();
    }

    private void ApplyWindowAndTaskbarIcon()
    {
        Icon = _currentDeskMode == DeskMode.Sitting ? _sittingIcon : _defaultIcon;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var smallIcon = _currentDeskMode == DeskMode.Sitting ? _sittingSmallIconHandle : _defaultSmallIconHandle;
        var largeIcon = _currentDeskMode == DeskMode.Sitting ? _sittingLargeIconHandle : _defaultLargeIconHandle;

        if (smallIcon != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, ICON_SMALL, smallIcon);
            SendMessage(hwnd, WM_SETICON, ICON_SMALL2, smallIcon);
            SetClassLongPtr(hwnd, GCLP_HICONSM, smallIcon);
        }

        if (largeIcon != IntPtr.Zero)
        {
            SendMessage(hwnd, WM_SETICON, ICON_BIG, largeIcon);
            SetClassLongPtr(hwnd, GCLP_HICON, largeIcon);
        }

        RefreshTaskbarOverlay();
    }

    private void RefreshTaskbarOverlay()
    {
        if (TaskbarItemInfo == null)
            return;

        TaskbarItemInfo.Overlay = null;
        UpdateTaskbarOverlay();
    }

    private void DestroyIconHandles()
    {
        DestroyIconHandle(_defaultSmallIconHandle);
        DestroyIconHandle(_defaultLargeIconHandle);
        DestroyIconHandle(_sittingSmallIconHandle);
        DestroyIconHandle(_sittingLargeIconHandle);
    }

    private static void DestroyIconHandle(IntPtr iconHandle)
    {
        if (iconHandle != IntPtr.Zero)
            DestroyIcon(iconHandle);
    }

    private void ResetPomodoroReminderState()
    {
        _hasStartedCurrentPomodoro = false;
        _sittingStartReminderShown = false;
        _sittingMidpointReminderShown = false;
    }

    private void MaybeShowSittingStartReminder()
    {
        if (_phase != TimerPhase.Pomodoro ||
            _currentDeskMode != DeskMode.Sitting ||
            _sittingStartReminderShown)
        {
            return;
        }

        _sittingStartReminderShown = true;
        ShowEllipticalReminderToast(false);
    }

    private void MaybeShowSittingMidpointReminder()
    {
        if (_phase != TimerPhase.Pomodoro ||
            _currentDeskMode != DeskMode.Sitting ||
            !_hasStartedCurrentPomodoro ||
            _sittingMidpointReminderShown ||
            _timeRemaining > TimeSpan.FromTicks(_currentPomodoroDuration.Ticks / 2))
        {
            return;
        }

        _sittingMidpointReminderShown = true;
        ShowEllipticalReminderToast(true);
    }

    private void ManageFrequentTimesButton_Click(object sender, RoutedEventArgs e)
    {
        FrequentTimesError.Text = string.Empty;
        var isOpening = FrequentTimesEditor.Visibility != Visibility.Visible;
        FrequentTimesEditor.Visibility = isOpening ? Visibility.Visible : Visibility.Collapsed;
        ManageFrequentTimesButton.Content = isOpening ? "-" : "+";
    }

    private void AddFrequentMinutes_Click(object sender, RoutedEventArgs e)
    {
        FrequentTimesError.Text = string.Empty;

        if (!int.TryParse(FrequentMinutesBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !IsValidPomodoroMinutes(minutes))
        {
            FrequentTimesError.Text = "Enter 1-999 minutes.";
            return;
        }

        if (_settings.FrequentPomodoroMinutes.Contains(minutes))
        {
            FrequentTimesError.Text = "That time is already listed.";
            return;
        }

        _settings.FrequentPomodoroMinutes.Add(minutes);
        if (!TrySaveFrequentTimes())
            return;

        FrequentMinutesBox.Clear();
        RenderFrequentTimes();
    }

    private void RemoveFrequentMinutes_Click(object sender, RoutedEventArgs e)
    {
        FrequentTimesError.Text = string.Empty;

        if (sender is not Button { Tag: int minutes })
            return;

        _settings.FrequentPomodoroMinutes.Remove(minutes);
        if (!TrySaveFrequentTimes())
            return;

        RenderFrequentTimes();
    }

    private void RenderFrequentTimes()
    {
        FrequentTimesPanel.Children.Clear();

        foreach (var minutes in _settings.FrequentPomodoroMinutes)
        {
            var button = new Button
            {
                Content = FormatMinutesLabel(minutes),
                Tag = minutes,
                MinWidth = 44,
                Height = 28,
                Margin = new Thickness(4, 5, 0, 5),
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 12,
                ToolTip = $"Start {FormatMinutesLabel(minutes)} pomodoro"
            };
            button.Click += FrequentMinutesButton_Click;
            FrequentTimesPanel.Children.Add(button);
        }

        RenderFrequentTimesList();
    }

    private void RenderFrequentTimesList()
    {
        FrequentTimesListPanel.Children.Clear();

        if (_settings.FrequentPomodoroMinutes.Count == 0)
        {
            FrequentTimesListPanel.Children.Add(new TextBlock
            {
                Text = "No frequent times",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return;
        }

        foreach (var minutes in _settings.FrequentPomodoroMinutes)
        {
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 2, 4, 2)
            };
            item.Children.Add(new TextBlock
            {
                Text = FormatMinutesLabel(minutes),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });

            var removeButton = new Button
            {
                Content = "x",
                Tag = minutes,
                Width = 20,
                Height = 20,
                FontSize = 10,
                Foreground = Brushes.Gray,
                Padding = new Thickness(0),
                ToolTip = $"Remove {FormatMinutesLabel(minutes)}"
            };
            removeButton.Click += RemoveFrequentMinutes_Click;
            item.Children.Add(removeButton);

            FrequentTimesListPanel.Children.Add(item);
        }
    }

    private bool TrySaveFrequentTimes()
    {
        try
        {
            SaveSettings(_settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FrequentTimesError.Text = "Could not save setting.";
            return false;
        }
    }

    private static bool IsValidPomodoroMinutes(int minutes) => minutes is >= 1 and <= 999;

    private static string FormatMinutesLabel(int minutes)
    {
        if (minutes < 60)
            return $"{minutes}m";

        if (minutes % 60 == 0)
            return $"{minutes / 60}h";

        return $"{minutes / 60}h {minutes % 60}m";
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
        _currentPomodoroDuration = TimeSpan.FromSeconds(5);
        _timeRemaining = TimeSpan.FromSeconds(5);
        ResetPomodoroReminderState();
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
        public List<int> FrequentPomodoroMinutes { get; set; } = [..DefaultFrequentPomodoroMinutes];
        public DeskMode? LastCompletedDeskMode { get; set; }
        public DeskMode NextDeskMode { get; set; } = DeskMode.Sitting;
    }

    private enum TimerPhase
    {
        Pomodoro,
        Break
    }

    private enum DeskMode
    {
        Sitting,
        Standing
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsFilePath),
                SettingsJsonSerializerOptions);
            return NormalizeSettings(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    private static AppSettings NormalizeSettings(AppSettings? settings)
    {
        if (settings == null)
            return new AppSettings();

        if (settings.BreakSeconds <= 0)
            settings.BreakSeconds = DefaultBreakSeconds;

        var frequentPomodoroMinutes = settings.FrequentPomodoroMinutes ?? [..DefaultFrequentPomodoroMinutes];
        settings.FrequentPomodoroMinutes = frequentPomodoroMinutes
            .Where(IsValidPomodoroMinutes)
            .Distinct()
            .ToList();

        if (settings.LastCompletedDeskMode.HasValue &&
            !Enum.IsDefined(settings.LastCompletedDeskMode.Value))
        {
            settings.LastCompletedDeskMode = null;
        }

        if (!Enum.IsDefined(settings.NextDeskMode))
            settings.NextDeskMode = DeskMode.Sitting;

        return settings;
    }

    private bool TrySaveSettings()
    {
        try
        {
            SaveSettings(_settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, SettingsJsonSerializerOptions));
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
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL2 = 2;
    private const int SM_CXICON = 11;
    private const int SM_CXSMICON = 49;
    private const int GCLP_HICON = -14;
    private const int GCLP_HICONSM = -34;

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, byte[] lpBits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

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
