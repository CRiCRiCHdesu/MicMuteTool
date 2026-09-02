using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MicMuteTool.Models;

namespace MicMuteTool.Views;

public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _initialized;
    private OsdSettings _settings = new();
    private bool _draggingEnabled;
    private bool _isPreviewMode;
    private bool _lastMutedState;

    public event Action<double, double>? PositionChanged;

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => BeginFadeOut();

        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public void ApplySettings(OsdSettings settings)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplySettings(settings));
            return;
        }

        var previousSettings = _settings.Clone();
        _settings = settings.Clone();

        var previousWidth = GetEffectiveSize(previousSettings.Width, ActualWidth, Width);
        var previousHeight = GetEffectiveSize(previousSettings.Height, ActualHeight, Height);
        var widthChanged = !AreClose(previousWidth, _settings.Width);
        var heightChanged = !AreClose(previousHeight, _settings.Height);
        var fallbackLeft = _initialized ? Left : double.NaN;
        var fallbackTop = _initialized ? Top : double.NaN;
        var previousLeft = !double.IsNaN(previousSettings.PositionX) ? previousSettings.PositionX : fallbackLeft;
        var previousTop = !double.IsNaN(previousSettings.PositionY) ? previousSettings.PositionY : fallbackTop;
        var hasKnownPosition = !double.IsNaN(previousLeft) && !double.IsNaN(previousTop);

        Width = _settings.Width;
        Height = _settings.Height;
        StatusText.FontSize = _settings.FontSize;
        RootBorder.Opacity = _settings.BackgroundOpacity;
        UpdateDragState();
        UpdateHideTimerInterval();
        UpdateVisualState(_lastMutedState);

        if (hasKnownPosition && (widthChanged || heightChanged))
        {
            var centerX = previousLeft + previousWidth / 2;
            var centerY = previousTop + previousHeight / 2;
            var newLeft = centerX - _settings.Width / 2;
            var newTop = centerY - _settings.Height / 2;

            _settings.PositionX = newLeft;
            _settings.PositionY = newTop;
            Left = newLeft;
            Top = newTop;
            PositionChanged?.Invoke(newLeft, newTop);
        }

        if (!double.IsNaN(_settings.PositionX) && !double.IsNaN(_settings.PositionY))
        {
            Left = _settings.PositionX;
            Top = _settings.PositionY;
        }
    }

    public void SetLockState(bool isLocked)
    {
        _settings.IsPositionLocked = isLocked;
        UpdateDragState();
    }

    public void ShowStatus(bool muted)
    {
        _isPreviewMode = false;
        DisplayStatus(muted, autoHide: true);
    }

    public void ShowPreview(bool muted)
    {
        _isPreviewMode = true;
        DisplayStatus(muted, autoHide: false);
    }

    public void HidePreview()
    {
        if (!_isPreviewMode)
        {
            return;
        }

        _isPreviewMode = false;
        Hide();
    }

    private void BeginFadeOut()
    {
        if (_isPreviewMode)
        {
            return;
        }

        _hideTimer.Stop();

        ApplyFadeOut();
    }

    private void DisplayStatus(bool muted, bool autoHide)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => DisplayStatus(muted, autoHide));
            return;
        }

        EnsureInitialized();
        _lastMutedState = muted;
        UpdateVisualState(muted);
        UpdateLayout();
        PositionToConfiguredLocation();

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        ApplyFadeIn();

        if (autoHide)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }
        else
        {
            _hideTimer.Stop();
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Opacity = 0;
        Show();
        Hide();
        _initialized = true;
    }

    private void UpdateVisualState(bool muted)
    {
        var colorCode = muted ? _settings.MicMutedColor : _settings.MicOnColor;
        var defaultColor = muted ? "#FFE74C3C" : "#FF2ECC71";
        var activeColor = TryParseColor(colorCode, defaultColor);
        var opacityByte = (byte)Math.Clamp(_settings.BackgroundOpacity * 255, 0, 255);
        var background = System.Windows.Media.Color.FromArgb(opacityByte, activeColor.R, activeColor.G, activeColor.B);
        var contentOpacity = Math.Clamp(_settings.ContentOpacity, 0.05, 1.0);
        var dotVisible = _settings.ShowStatusDot;

        StatusText.Text = muted ? _settings.MicMutedText : _settings.MicOnText;
        StatusText.Opacity = contentOpacity;
        StatusText.Margin = dotVisible ? new Thickness(12, 0, 0, 0) : new Thickness(0);
        StatusDot.Fill = new SolidColorBrush(activeColor);
        StatusDot.Visibility = dotVisible ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Opacity = contentOpacity;
        RootBorder.Background = new SolidColorBrush(background);
    }

    private void ApplyFadeIn()
    {
        var duration = Math.Clamp(_settings.FadeInDurationMs, 0, 2000);
        if (!_settings.EnableFadeIn || duration <= 0)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            return;
        }

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    private void ApplyFadeOut()
    {
        var duration = Math.Clamp(_settings.FadeOutDurationMs, 0, 2000);
        if (!_settings.EnableFadeOut || duration <= 0)
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void UpdateHideTimerInterval()
    {
        var duration = Math.Max(200, _settings.DisplayDurationMs);
        _hideTimer.Interval = TimeSpan.FromMilliseconds(duration);
    }

    private void PositionToConfiguredLocation()
    {
        if (!double.IsNaN(_settings.PositionX) && !double.IsNaN(_settings.PositionY))
        {
            Left = _settings.PositionX;
            Top = _settings.PositionY;
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        var top = workArea.Top + workArea.Height - ActualHeight - 80;

        Left = double.IsNaN(left) ? workArea.Left : left;
        Top = Math.Max(workArea.Top + 20, top);
        _settings.PositionX = Left;
        _settings.PositionY = Top;
        PositionChanged?.Invoke(Left, Top);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        PositionToConfiguredLocation();
    }

    private void UpdateDragState()
    {
        _draggingEnabled = !_settings.IsPositionLocked;
        RootBorder.IsHitTestVisible = _draggingEnabled;
        Cursor = _draggingEnabled ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Arrow;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsPositionLocked)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // ignore drag exceptions
        }

        _settings.PositionX = Left;
        _settings.PositionY = Top;
        PositionChanged?.Invoke(Left, Top);
    }

    private static System.Windows.Media.Color TryParseColor(string? colorCode, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(colorCode) ? fallback : colorCode;
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(candidate)!;
            return color;
        }
        catch
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback)!;
        }
    }

    private static double GetEffectiveSize(double configuredSize, double actualSize, double fallback)
    {
        if (configuredSize > 0)
        {
            return configuredSize;
        }

        if (actualSize > 0)
        {
            return actualSize;
        }

        return fallback;
    }

    private static bool AreClose(double a, double b)
    {
        return Math.Abs(a - b) < 0.1;
    }
}

