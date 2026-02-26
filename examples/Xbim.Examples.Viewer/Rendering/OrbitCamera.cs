using System.Numerics;

namespace Xbim.Examples.Viewer.Rendering;

/// <summary>
/// An orbit camera that rotates around a target point with adjustable distance,
/// azimuth, and elevation. Supports panning, zooming, and automatic framing
/// of a bounding box.
/// </summary>
internal sealed class OrbitCamera
{
    private const float MinElevation = -MathF.PI / 2f + 0.01f;
    private const float MaxElevation = MathF.PI / 2f - 0.01f;
    private const float MinDistance = 0.01f;

    /// <summary>
    /// The point the camera orbits around (world space).
    /// </summary>
    public Vector3 Target { get; set; }

    /// <summary>
    /// Distance from the camera to the target.
    /// </summary>
    public float Distance { get; set; } = 10f;

    /// <summary>
    /// Horizontal rotation angle in radians (around the Y axis).
    /// </summary>
    public float Azimuth { get; set; } = MathF.PI / 4f;

    /// <summary>
    /// Vertical rotation angle in radians (above/below the XZ plane).
    /// </summary>
    public float Elevation { get; set; } = MathF.PI / 6f;

    /// <summary>
    /// Vertical field of view in radians.
    /// </summary>
    public float FovY { get; set; } = MathF.PI / 4f; // 45 degrees

    /// <summary>
    /// Viewport aspect ratio (width / height).
    /// </summary>
    public float AspectRatio { get; set; } = 16f / 9f;

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public float NearClip { get; set; } = 0.1f;

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public float FarClip { get; set; } = 10000f;

    /// <summary>
    /// Computes the camera's world-space position from orbit parameters.
    /// </summary>
    public Vector3 GetEyePosition()
    {
        float cosElev = MathF.Cos(Elevation);
        float sinElev = MathF.Sin(Elevation);
        float cosAz = MathF.Cos(Azimuth);
        float sinAz = MathF.Sin(Azimuth);

        var offset = new Vector3(
            sinAz * cosElev,
            sinElev,
            cosAz * cosElev
        ) * Distance;

        return Target + offset;
    }

    /// <summary>
    /// Builds a look-at view matrix from the current orbit state.
    /// </summary>
    public Matrix4x4 GetViewMatrix()
    {
        var eye = GetEyePosition();
        return Matrix4x4.CreateLookAt(eye, Target, Vector3.UnitY);
    }

    /// <summary>
    /// Builds a perspective projection matrix from current FOV, aspect, and clip planes.
    /// </summary>
    public Matrix4x4 GetProjectionMatrix()
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(FovY, AspectRatio, NearClip, FarClip);
    }

    /// <summary>
    /// Rotates the camera around the target by the given screen-space deltas.
    /// Positive dx rotates right, positive dy rotates up.
    /// </summary>
    /// <param name="dx">Horizontal pixel delta (scaled to rotation speed by caller).</param>
    /// <param name="dy">Vertical pixel delta (scaled to rotation speed by caller).</param>
    public void Orbit(float dx, float dy)
    {
        Azimuth -= dx;
        Elevation += dy;
        Elevation = Math.Clamp(Elevation, MinElevation, MaxElevation);
    }

    /// <summary>
    /// Moves the target point in the camera's local XY plane (screen-space panning).
    /// Positive dx moves right, positive dy moves up from the camera's perspective.
    /// </summary>
    /// <param name="dx">Horizontal pixel delta (scaled to pan speed by caller).</param>
    /// <param name="dy">Vertical pixel delta (scaled to pan speed by caller).</param>
    public void Pan(float dx, float dy)
    {
        // Camera local axes from the view matrix
        var view = GetViewMatrix();
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);

        // Scale pan by distance so it feels proportional at any zoom level
        float panScale = Distance * 0.002f;
        Target += right * (-dx * panScale) + up * (dy * panScale);
    }

    /// <summary>
    /// Adjusts the camera distance by a scroll delta.
    /// Positive values zoom in (reduce distance), negative values zoom out.
    /// </summary>
    /// <param name="delta">Scroll wheel delta (typically +/- 1 per notch).</param>
    public void Zoom(float delta)
    {
        float factor = 1f - delta * 0.1f;
        Distance *= factor;
        if (Distance < MinDistance)
            Distance = MinDistance;
    }

    /// <summary>
    /// Adjusts the camera to frame the given axis-aligned bounding box.
    /// Centers the target, sets distance so the entire box is visible,
    /// and adjusts clip planes proportionally.
    /// </summary>
    public void FitToScene(Vector3 min, Vector3 max)
    {
        Target = (min + max) * 0.5f;

        var size = max - min;
        float diagonal = size.Length();

        if (diagonal < 1e-6f)
        {
            Distance = 10f;
            NearClip = 0.1f;
            FarClip = 10000f;
            return;
        }

        // Place the camera far enough to see the bounding sphere
        float halfFov = FovY * 0.5f;
        float radius = diagonal * 0.5f;
        Distance = radius / MathF.Sin(halfFov);

        // Set clip planes with margin
        NearClip = Distance * 0.001f;
        FarClip = Distance * 10f;

        // Default viewing angle: slightly above and to the side
        Azimuth = MathF.PI / 4f;
        Elevation = MathF.PI / 6f;
    }
}
