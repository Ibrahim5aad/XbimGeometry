namespace Xbim.Geometry.Viewer;

/// <summary>
/// Built-in SVG icons for toolbar buttons.
/// Each constant is a complete inline SVG element sized to 16x16.
/// </summary>
public static class ViewerIcons
{
    private const string S = "width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";

    public static readonly string Home = $"<svg {S}><path d=\"M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z\"/><polyline points=\"9 22 9 12 15 12 15 22\"/></svg>";

    public static readonly string Perspective = $"<svg {S}><path d=\"M3 20L9 4h6l6 16\"/><line x1=\"6\" y1=\"12\" x2=\"18\" y2=\"12\"/></svg>";

    public static readonly string Orthographic = $"<svg {S}><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\" rx=\"1\"/><line x1=\"3\" y1=\"12\" x2=\"21\" y2=\"12\"/><line x1=\"12\" y1=\"3\" x2=\"12\" y2=\"21\"/></svg>";

    public static readonly string Wireframe = $"<svg {S}><path d=\"M12 2L2 7l10 5 10-5-10-5z\"/><path d=\"M2 17l10 5 10-5\"/><path d=\"M2 12l10 5 10-5\"/></svg>";

    public static readonly string Grid = $"<svg {S}><rect x=\"3\" y=\"3\" width=\"18\" height=\"18\"/><line x1=\"3\" y1=\"9\" x2=\"21\" y2=\"9\"/><line x1=\"3\" y1=\"15\" x2=\"21\" y2=\"15\"/><line x1=\"9\" y1=\"3\" x2=\"9\" y2=\"21\"/><line x1=\"15\" y1=\"3\" x2=\"15\" y2=\"21\"/></svg>";

    public static readonly string ZoomIn = $"<svg {S}><circle cx=\"11\" cy=\"11\" r=\"8\"/><line x1=\"21\" y1=\"21\" x2=\"16.65\" y2=\"16.65\"/><line x1=\"11\" y1=\"8\" x2=\"11\" y2=\"14\"/><line x1=\"8\" y1=\"11\" x2=\"14\" y2=\"11\"/></svg>";

    public static readonly string ZoomOut = $"<svg {S}><circle cx=\"11\" cy=\"11\" r=\"8\"/><line x1=\"21\" y1=\"21\" x2=\"16.65\" y2=\"16.65\"/><line x1=\"8\" y1=\"11\" x2=\"14\" y2=\"11\"/></svg>";

    public static readonly string Eye = $"<svg {S}><path d=\"M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/></svg>";

    public static readonly string EyeOff = $"<svg {S}><path d=\"M17.94 17.94A10.07 10.07 0 0112 20c-7 0-11-8-11-8a18.45 18.45 0 015.06-5.94M9.9 4.24A9.12 9.12 0 0112 4c7 0 11 8 11 8a18.5 18.5 0 01-2.16 3.19m-6.72-1.07a3 3 0 11-4.24-4.24\"/><line x1=\"1\" y1=\"1\" x2=\"23\" y2=\"23\"/></svg>";

    public static readonly string Crosshair = $"<svg {S}><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"22\" y1=\"12\" x2=\"18\" y2=\"12\"/><line x1=\"6\" y1=\"12\" x2=\"2\" y2=\"12\"/><line x1=\"12\" y1=\"6\" x2=\"12\" y2=\"2\"/><line x1=\"12\" y1=\"22\" x2=\"12\" y2=\"18\"/></svg>";

    public static readonly string RotateRight = $"<svg {S}><polyline points=\"23 4 23 10 17 10\"/><path d=\"M20.49 15a9 9 0 11-2.12-9.36L23 10\"/></svg>";

    public static readonly string Layers = $"<svg {S}><polygon points=\"12 2 2 7 12 12 22 7 12 2\"/><polyline points=\"2 17 12 22 22 17\"/><polyline points=\"2 12 12 17 22 12\"/></svg>";

    public static readonly string Scissors = $"<svg {S}><line x1=\"5\" y1=\"4\" x2=\"19\" y2=\"20\"/><line x1=\"19\" y1=\"4\" x2=\"5\" y2=\"20\"/><line x1=\"12\" y1=\"12\" x2=\"12\" y2=\"12\"/></svg>";

    public static readonly string Maximize = $"<svg {S}><polyline points=\"15 3 21 3 21 9\"/><polyline points=\"9 21 3 21 3 15\"/><line x1=\"21\" y1=\"3\" x2=\"14\" y2=\"10\"/><line x1=\"3\" y1=\"21\" x2=\"10\" y2=\"14\"/></svg>";

    public static readonly string Camera = $"<svg {S}><path d=\"M23 19a2 2 0 01-2 2H3a2 2 0 01-2-2V8a2 2 0 012-2h4l2-3h6l2 3h4a2 2 0 012 2z\"/><circle cx=\"12\" cy=\"13\" r=\"4\"/></svg>";

    public static readonly string Sun = $"<svg {S}><circle cx=\"12\" cy=\"12\" r=\"5\"/><line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"3\"/><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"23\"/><line x1=\"4.22\" y1=\"4.22\" x2=\"5.64\" y2=\"5.64\"/><line x1=\"18.36\" y1=\"18.36\" x2=\"19.78\" y2=\"19.78\"/><line x1=\"1\" y1=\"12\" x2=\"3\" y2=\"12\"/><line x1=\"21\" y1=\"12\" x2=\"23\" y2=\"12\"/><line x1=\"4.22\" y1=\"19.78\" x2=\"5.64\" y2=\"18.36\"/><line x1=\"18.36\" y1=\"5.64\" x2=\"19.78\" y2=\"4.22\"/></svg>";

    public static readonly string Moon = $"<svg {S}><path d=\"M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z\"/></svg>";
}
