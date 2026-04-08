using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WindowsPomodoro;

public partial class MainWindow : Window
{
    private const int TimerMinutes = 30;

    private readonly DispatcherTimer _timer;
    private TimeSpan _timeRemaining;
    private bool _isRunning;

    public MainWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        ResetTimer();
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
        ResetTimer();
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
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            _timer.Stop();
            _isRunning = false;
            StartPauseButton.Content = "Start";
        }
        else
        {
            _timer.Start();
            _isRunning = true;
            StartPauseButton.Content = "Pause";
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _isRunning = false;
        StartPauseButton.Content = "Start";
        ResetTimer();
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
