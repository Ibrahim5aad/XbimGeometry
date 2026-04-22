namespace Xbim.Geometry.Viewer;

public enum ViewerTheme { Light, Dark }

public sealed class ViewerThemeService
{
    private ViewerTheme _theme = ViewerTheme.Dark;

    public event Action? OnThemeChanged;

    public ViewerTheme Theme
    {
        get => _theme;
        set
        {
            if (_theme == value) return;
            _theme = value;
            OnThemeChanged?.Invoke();
        }
    }

    // ── Accent colours (configurable per theme) ──

    public string LightAccentColor { get; set; } = "#0066cc";
    public string DarkAccentColor { get; set; } = "#4da3ff";
    public string AccentColor => _theme == ViewerTheme.Light ? LightAccentColor : DarkAccentColor;

    // ── Background colours ──

    public string LightBackgroundColor { get; set; } = "#e8e8e8";
    public string DarkBackgroundColor { get; set; } = "#0d1117";
    public string BackgroundColor => _theme == ViewerTheme.Light ? LightBackgroundColor : DarkBackgroundColor;

    // ── Convenience ──

    public string ThemeClass => _theme == ViewerTheme.Dark ? "xbim-theme-dark" : "xbim-theme-light";
    public bool IsDark => _theme == ViewerTheme.Dark;

    public void Toggle() =>
        Theme = _theme == ViewerTheme.Dark ? ViewerTheme.Light : ViewerTheme.Dark;

    public void SetAccentColors(string? light = null, string? dark = null)
    {
        if (light is not null) LightAccentColor = light;
        if (dark is not null) DarkAccentColor = dark;
        OnThemeChanged?.Invoke();
    }

    public void SetBackgroundColors(string? light = null, string? dark = null)
    {
        if (light is not null) LightBackgroundColor = light;
        if (dark is not null) DarkBackgroundColor = dark;
        OnThemeChanged?.Invoke();
    }

    /// <summary>
    /// Resolves the full set of CSS variables for the current theme and accent.
    /// </summary>
    internal Dictionary<string, string> GetCssVariables()
    {
        var accent = AccentColor;
        var accentHover = _theme == ViewerTheme.Light
            ? Darken(accent, 0.15)
            : Lighten(accent, 0.15);
        var accentBg = WithAlpha(accent, _theme == ViewerTheme.Light ? 0.1 : 0.2);

        if (_theme == ViewerTheme.Light)
        {
            return new Dictionary<string, string>
            {
                ["--xbim-bg"]           = "#ffffff",
                ["--xbim-bg-secondary"] = "#f6f8fa",
                ["--xbim-bg-hover"]     = "#f3f4f6",
                ["--xbim-text"]         = "#24292f",
                ["--xbim-text-muted"]   = "#6e7781",
                ["--xbim-panel-bg"]     = "rgba(246, 248, 250, 0.95)",
                ["--xbim-panel-border"] = "#d0d7de",
                ["--xbim-accent"]       = accent,
                ["--xbim-accent-hover"] = accentHover,
                ["--xbim-accent-bg"]    = accentBg,
                ["--xbim-btn-bg"]       = "#ffffff",
                ["--xbim-btn-border"]   = "#d0d7de",
                ["--xbim-btn-text"]     = "#24292f",
                ["--xbim-btn-hover-bg"] = "#f3f4f6",
                ["--xbim-active-bg"]    = accentBg,
                ["--xbim-error"]        = "#dc3545",
                ["--xbim-shadow"]       = "0 4px 12px rgba(0,0,0,0.08)",
            };
        }
        else
        {
            return new Dictionary<string, string>
            {
                ["--xbim-bg"]           = "#0d1117",
                ["--xbim-bg-secondary"] = "#161b22",
                ["--xbim-bg-hover"]     = "#21262d",
                ["--xbim-text"]         = "#c9d1d9",
                ["--xbim-text-muted"]   = "#6e7681",
                ["--xbim-panel-bg"]     = "rgba(22, 27, 34, 0.95)",
                ["--xbim-panel-border"] = "#444c56",
                ["--xbim-accent"]       = accent,
                ["--xbim-accent-hover"] = accentHover,
                ["--xbim-accent-bg"]    = accentBg,
                ["--xbim-btn-bg"]       = "#21262d",
                ["--xbim-btn-border"]   = "#444c56",
                ["--xbim-btn-text"]     = "#c9d1d9",
                ["--xbim-btn-hover-bg"] = "#30363d",
                ["--xbim-active-bg"]    = accentBg,
                ["--xbim-error"]        = "#f44336",
                ["--xbim-shadow"]       = "0 4px 12px rgba(0,0,0,0.4)",
            };
        }
    }

    internal static string Lighten(string hex, double amount)
    {
        var (r, g, b) = ParseHex(hex);
        r = Math.Min(255, (int)(r + (255 - r) * amount));
        g = Math.Min(255, (int)(g + (255 - g) * amount));
        b = Math.Min(255, (int)(b + (255 - b) * amount));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    internal static string Darken(string hex, double amount)
    {
        var (r, g, b) = ParseHex(hex);
        r = Math.Max(0, (int)(r * (1 - amount)));
        g = Math.Max(0, (int)(g * (1 - amount)));
        b = Math.Max(0, (int)(b * (1 - amount)));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    internal static string WithAlpha(string hex, double alpha)
    {
        var (r, g, b) = ParseHex(hex);
        return $"rgba({r}, {g}, {b}, {alpha:F2})";
    }

    private static (int R, int G, int B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return (0, 0, 0);
        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }
}
