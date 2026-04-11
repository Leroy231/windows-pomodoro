using System.Globalization;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace WindowsPomodoro;

public partial class MainWindow : Window
{
    private const int TimerMinutes = 30;

    private readonly DispatcherTimer _timer;
    private TimeSpan _timeRemaining;
    private bool _isRunning;

    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "WindowsPomodoro";

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        ResetTimer();
        AutoStartCheckBox.IsChecked = IsAutoStartEnabled();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timeRemaining -= TimeSpan.FromSeconds(1);
        UpdateDisplay();

        if (_timeRemaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            _isRunning = false;
            StartPauseButton.Content = "Start";
            OnTimerComplete();
        }
    }

    private void OnTimerComplete()
    {
        SystemSounds.Beep.Play();
        FlashTaskbar();
        ShowToastNotification();
        ResetTimer();
    }

    private static void ShowToastNotification()
    {
        new ToastContentBuilder()
            .AddText("Pomodoro Complete!")
            .AddText("Time for a break. Your 30-minute session has ended.")
            .Show();
    }

    private void ResetTimer()
    {
        _timeRemaining = TimeSpan.FromMinutes(TimerMinutes);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        TimerDisplay.Text = _timeRemaining.ToString(@"mm\:ss");
        Title = $"{_timeRemaining:mm\\:ss} - Pomodoro";
        UpdateTaskbarOverlay();
    }

    private void UpdateTaskbarOverlay()
    {
        if (TaskbarItemInfo == null) return;

        if (!_isRunning)
        {
            TaskbarItemInfo.Overlay = null;
            return;
        }

        var minutes = (int)Math.Ceiling(_timeRemaining.TotalMinutes);
        var text = minutes.ToString();

        const int size = 20;
        var dpi = VisualTreeHelper.GetDpi(this);
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            dc.DrawEllipse(Brushes.OrangeRed, null, new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);

            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                minutes >= 10 ? 11 : 14,
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

    private void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _timer.Stop();
            _isRunning = false;
            StartPauseButton.Content = "Start";
            UpdateTaskbarOverlay();
        }
        else
        {
            _timer.Start();
            _isRunning = true;
            StartPauseButton.Content = "Pause";
            UpdateTaskbarOverlay();
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _isRunning = false;
        StartPauseButton.Content = "Start";
        ResetTimer();
    }

    private void SetCustomMinutes_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(CustomMinutesBox.Text, out var minutes) && minutes > 0)
        {
            _timer.Stop();
            _isRunning = false;
            _timeRemaining = TimeSpan.FromMinutes(minutes);
            UpdateDisplay();
            StartPauseButton.Content = "Start";
            CustomMinutesBox.Clear();
        }
    }

    private void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        _timeRemaining = TimeSpan.FromSeconds(5);
        UpdateDisplay();
        if (!_isRunning)
        {
            _timer.Start();
            _isRunning = true;
            StartPauseButton.Content = "Pause";
        }
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

    #endregion
}
