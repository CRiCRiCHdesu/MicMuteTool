using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace MicMuteTool.Views;

public partial class TrayMenuWindow : Window, INotifyPropertyChanged
{
    private readonly Action _openSettings;
    private readonly Action _openLegacySettings;
    private readonly Action _toggleOsd;
    private readonly Action _toggleSound;
    private bool _isInvokingAction;
    private readonly Action _exit;
    private readonly bool _showLegacySettings;

    public TrayMenuWindow(TrayMenuState state, TrayMenuActions actions)
    {
        InitializeComponent();
        DataContext = this;

        _openSettings = actions.OpenSettings;
        _openLegacySettings = actions.OpenLegacySettings;
        _toggleOsd = actions.ToggleOsd;
        _toggleSound = actions.ToggleSound;
        _exit = actions.Exit;
        _showLegacySettings = state.ShowLegacySettings;

        IsMuted = state.IsMuted;
        IsOsdEnabled = state.IsOsdEnabled;
        IsSoundEnabled = state.IsSoundEnabled;
        ApplyTheme();
        LoadSvgIcons();
        LegacyButton.Visibility = _showLegacySettings ? Visibility.Visible : Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Brush SurfaceBrush { get; private set; } = Brushes.White;
    public Brush TextBrush { get; private set; } = Brushes.Black;
    public Brush SecondaryTextBrush { get; private set; } = Brushes.Gray;
    public Brush IconBrush { get; private set; } = Brushes.DimGray;
    public Brush OutlineBrush { get; private set; } = Brushes.LightGray;
    public Brush HoverBrush { get; private set; } = Brushes.WhiteSmoke;
    public Brush PressedBrush { get; private set; } = Brushes.Gainsboro;
    public Brush HighlightBrush { get; private set; } = Brushes.WhiteSmoke;
    public Brush AccentBrush { get; private set; } = Brushes.MediumPurple;
    public Brush MutedBrush { get; private set; } = Brushes.Gray;

    public bool IsMuted { get; }
    public bool IsOsdEnabled { get; }
    public bool IsSoundEnabled { get; }
    public string StatusText => IsMuted ? "麦克风已静音" : "麦克风已开启";
    public string OsdStatusText => IsOsdEnabled ? "开" : "关";
    public string SoundStatusText => IsSoundEnabled ? "开" : "关";

    public void ShowAt(Point screenPoint)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screenPoint.X - Width;
        Top = screenPoint.Y - 4;
        Show();
        UpdateLayout();

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y));
        var area = screen.Bounds;
        const double edgePadding = 4;
        const double pointerGap = 4;

        Left = screenPoint.X - ActualWidth + 24;
        Top = screenPoint.Y - ActualHeight - pointerGap;

        if (Left + ActualWidth > area.Right - edgePadding)
        {
            Left = area.Right - ActualWidth - edgePadding;
        }
        if (Left < area.Left + edgePadding)
        {
            Left = area.Left + edgePadding;
        }
        if (Top < area.Top + edgePadding)
        {
            Top = screenPoint.Y + pointerGap;
        }
        if (Top + ActualHeight > area.Bottom - edgePadding)
        {
            Top = area.Bottom - ActualHeight - edgePadding;
        }

        Activate();
    }

    private void ApplyTheme()
    {
        var accent = GetWindowsAccentColor();
        var light = IsLightTheme();
        var accentHsl = ToHsl(accent);
        var neutral = accentHsl with { S = Math.Clamp(accentHsl.S - 0.34, 0.06, 0.24) };

        AccentBrush = new SolidColorBrush(accent);
        SurfaceBrush = new SolidColorBrush(light ? FromHsl(neutral with { L = 0.985 }) : FromHsl(neutral with { L = 0.115 }));
        TextBrush = new SolidColorBrush(light ? Color.FromRgb(32, 30, 36) : Color.FromRgb(238, 232, 242));
        SecondaryTextBrush = new SolidColorBrush(light ? Color.FromRgb(92, 86, 98) : Color.FromRgb(202, 196, 208));
        IconBrush = new SolidColorBrush(light ? Color.FromRgb(83, 79, 88) : Color.FromRgb(218, 212, 225));
        OutlineBrush = new SolidColorBrush(light ? Color.FromArgb(120, 218, 211, 225) : Color.FromArgb(120, 74, 69, 80));
        HoverBrush = new SolidColorBrush(light ? Color.FromRgb(238, 233, 241) : Color.FromRgb(42, 39, 47));
        PressedBrush = new SolidColorBrush(light ? Color.FromRgb(226, 220, 234) : Color.FromRgb(54, 50, 60));
        HighlightBrush = new SolidColorBrush(light ? Mix(accent, Colors.White, 0.84) : Mix(accent, Colors.Black, 0.58));
        MutedBrush = new SolidColorBrush(IsMuted ? Color.FromRgb(186, 26, 26) : Color.FromRgb(20, 108, 46));

        OnPropertyChanged(string.Empty);
    }

    private void LoadSvgIcons()
    {
        SettingsIcon.Data = LoadSvgPath("1.svg");
        OsdIcon.Data = LoadSvgPath(IsOsdEnabled ? "on.svg" : "off.svg");
        SoundIcon.Data = LoadSvgPath(IsSoundEnabled ? "on.svg" : "off.svg");
    }

    private static Geometry? LoadSvgPath(string fileName)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }
        if (!File.Exists(path))
        {
            return Geometry.Empty;
        }

        var svg = File.ReadAllText(path);
        var match = Regex.Match(svg, "<path[^>]*\\sd=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? Geometry.Parse(match.Groups[1].Value) : Geometry.Empty;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_openSettings);
    private void LegacySettings_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_openLegacySettings);
    private void OsdToggle_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_toggleOsd);
    private void SoundToggle_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_toggleSound);
    private void Exit_Click(object sender, RoutedEventArgs e) => InvokeAndClose(_exit);
    private void InvokeAndClose(Action action)
    {
        if (_isInvokingAction)
        {
            return;
        }

        _isInvokingAction = true;
        Hide();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                action();
            }
            finally
            {
                Close();
            }
        }));
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isInvokingAction)
        {
            Close();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 0), CultureInfo.InvariantCulture) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color GetWindowsAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            var value = Convert.ToInt32(key?.GetValue("ColorizationColor", unchecked((int)0xFF6750A4)), CultureInfo.InvariantCulture);
            return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
        }
        catch
        {
            return Color.FromRgb(103, 80, 164);
        }
    }

    private static Color Mix(Color color, Color with, double amount)
    {
        return Color.FromRgb(
            (byte)Math.Round(color.R * (1 - amount) + with.R * amount),
            (byte)Math.Round(color.G * (1 - amount) + with.G * amount),
            (byte)Math.Round(color.B * (1 - amount) + with.B * amount));
    }

    private static HslColor ToHsl(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var h = 0d;
        var s = 0d;
        var l = (max + min) / 2d;

        if (Math.Abs(max - min) > double.Epsilon)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2d - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }

        return new HslColor(h, s, l);
    }

    private static Color FromHsl(HslColor hsl)
    {
        double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1d / 6d) return p + (q - p) * 6 * t;
            if (t < 1d / 2d) return q;
            if (t < 2d / 3d) return p + (q - p) * (2d / 3d - t) * 6;
            return p;
        }

        double r;
        double g;
        double b;
        if (hsl.S <= double.Epsilon)
        {
            r = g = b = hsl.L;
        }
        else
        {
            var q = hsl.L < 0.5 ? hsl.L * (1 + hsl.S) : hsl.L + hsl.S - hsl.L * hsl.S;
            var p = 2 * hsl.L - q;
            r = HueToRgb(p, q, hsl.H + 1d / 3d);
            g = HueToRgb(p, q, hsl.H);
            b = HueToRgb(p, q, hsl.H - 1d / 3d);
        }

        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private readonly record struct HslColor(double H, double S, double L);
}

public sealed record TrayMenuState(bool IsMuted, bool IsOsdEnabled, bool IsSoundEnabled, bool ShowLegacySettings);

public sealed record TrayMenuActions(Action OpenSettings, Action OpenLegacySettings, Action ToggleOsd, Action ToggleSound, Action Exit);
