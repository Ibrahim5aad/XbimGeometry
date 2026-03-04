using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Silk.NET.OpenGL;
using Xbim.Examples.Viewer.Rendering;
using Xbim.Geometry.Scene;

namespace Xbim.Examples.Viewer.Views;

/// <summary>
/// OpenGL viewport that renders a WexBIM scene with Blinn-Phong shading
/// and interactive orbit/pan/zoom camera controls.
/// </summary>
internal sealed class ViewportControl : OpenGlControlBase, ICustomHitTest
{
    private GL? _gl;
    private ShaderProgram? _shader;
    private PostProcessPass? _postProcess;
    private GlScene? _scene;
    private readonly OrbitCamera _camera = new();

    // Pending scene model to be built on the GL thread
    private WexBimScene? _pendingModel;

    // Mouse interaction state
    private bool _isOrbiting;
    private bool _isPanning;
    private Point _lastPointerPosition;

    // Sensitivity constants
    private const float OrbitSensitivity = 0.005f;
    private const float PanSensitivity = 1.0f;

    // Light direction (pointing down and slightly toward the camera)
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.3f, -1.0f, -0.4f));

    // IFC uses Z-up; rotate -90° around X to convert to OpenGL Y-up
    private static readonly Matrix4x4 ZupToYup = Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    // Background color (dark grey)
    private const float BgR = 0.18f;
    private const float BgG = 0.20f;
    private const float BgB = 0.22f;

    // Selection highlight overlay: yellow at 50% opacity
    private static readonly Vector4 HighlightColor = new(1.0f, 0.9f, 0.2f, 0.5f);

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);

        _gl = GL.GetApi(gl.GetProcAddress);

        try
        {
            _shader = ShaderProgram.CreateDefault(_gl);
        }
        catch (Exception ex)
        {
            // Shader compile fails when running on ANGLE/GL ES instead of desktop GL.
            // Program.cs must force WGL (Windows) or GLX (Linux) for GL 3.3 core.
            System.Diagnostics.Debug.WriteLine($"[ViewportControl] Shader init failed: {ex.Message}");
            _shader = null;
            return;
        }

        try
        {
            _postProcess = new PostProcessPass(_gl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ViewportControl] FXAA init failed: {ex.Message}");
            _postProcess = null;
        }

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null)
            return;

        var pixelSize = GetPixelSize();
        int width = Math.Max(1, (int)pixelSize.Width);
        int height = Math.Max(1, (int)pixelSize.Height);

        // Render to intermediate FBO for FXAA, or directly to Avalonia's FB
        if (_postProcess is not null)
        {
            _postProcess.Begin(width, height);
        }
        else
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
        }

        _gl.ClearColor(BgR, BgG, BgB, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_shader is null)
            return;

        // Build pending scene model on the GL thread
        if (_pendingModel is not null)
        {
            var pending = _pendingModel;
            _pendingModel = null;

            _scene?.Dispose();
            _scene = GlSceneBuilder.Build(_gl, pending);

            // Rotate the scene bounds through the Z-up→Y-up transform for correct framing
            var corners = new[]
            {
                Vector3.Transform(_scene.BoundsMin, ZupToYup),
                Vector3.Transform(_scene.BoundsMax, ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMin.Y, _scene.BoundsMax.Z), ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMax.Y, _scene.BoundsMin.Z), ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMin.Y, _scene.BoundsMin.Z), ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMax.Y, _scene.BoundsMax.Z), ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMin.Y, _scene.BoundsMax.Z), ZupToYup),
                Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMax.Y, _scene.BoundsMin.Z), ZupToYup),
            };
            var rotMin = corners[0];
            var rotMax = corners[0];
            foreach (var c in corners)
            {
                rotMin = Vector3.Min(rotMin, c);
                rotMax = Vector3.Max(rotMax, c);
            }
            _camera.FitToScene(rotMin, rotMax);
        }

        if (_scene is null)
            return;

        _camera.AspectRatio = (float)width / height;

        _shader.Use();

        // Camera matrices — model matrix rotates IFC Z-up to OpenGL Y-up
        var model = ZupToYup;
        var view = _camera.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix();
        var eye = _camera.GetEyePosition();

        _shader.SetMatrix4("uModel", model);
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProjection", projection);

        // Normal matrix = upper-left 3x3 of the model matrix (rotation is orthogonal,
        // so no need for inverse-transpose)
        Span<float> normalMatrix = stackalloc float[9]
        {
            model.M11, model.M12, model.M13,
            model.M21, model.M22, model.M23,
            model.M31, model.M32, model.M33
        };
        _shader.SetMatrix3("uNormalMatrix", normalMatrix);

        _shader.SetVector3("uLightDir", LightDirection);
        _shader.SetVector3("uViewPos", eye);

        // Isolation mode: draw only the isolated product's geometry
        if (_isolatedProductLabel >= 0 &&
            _scene.ProductRanges.TryGetValue(_isolatedProductLabel, out var isoRanges))
        {
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);

            foreach (var range in isoRanges)
            {
                var batchList = range.IsTransparent
                    ? _scene.TransparentBatches
                    : _scene.OpaqueBatches;
                var batch = batchList[range.BatchIndex];

                if (range.IsTransparent)
                {
                    _gl.Enable(EnableCap.Blend);
                    _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    _gl.DepthMask(false);
                }

                _shader.SetVector4("uColor", new Vector4(batch.R, batch.G, batch.B, batch.A));
                _gl.BindVertexArray(batch.Vao);
                unsafe
                {
                    _gl.DrawElements(PrimitiveType.Triangles,
                        (uint)range.IndexCount,
                        DrawElementsType.UnsignedInt,
                        (void*)(range.StartIndex * sizeof(uint)));
                }

                if (range.IsTransparent)
                {
                    _gl.DepthMask(true);
                    _gl.Disable(EnableCap.Blend);
                }
            }
        }
        else
        {
            // Normal rendering: all batches

            // Draw opaque batches first (depth write ON)
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);

            foreach (var batch in _scene.OpaqueBatches)
            {
                _shader.SetVector4("uColor", new Vector4(batch.R, batch.G, batch.B, batch.A));
                _gl.BindVertexArray(batch.Vao);
                unsafe
                {
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)batch.IndexCount,
                        DrawElementsType.UnsignedInt, null);
                }
            }

            // Draw transparent batches (depth write OFF, blending ON, back-to-front)
            if (_scene.TransparentBatches.Count > 0)
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthMask(false);

                var sorted = _scene.TransparentBatches
                    .OrderByDescending(b => Vector3.DistanceSquared(b.Centroid, eye))
                    .ToList();

                foreach (var batch in sorted)
                {
                    _shader.SetVector4("uColor", new Vector4(batch.R, batch.G, batch.B, batch.A));
                    _gl.BindVertexArray(batch.Vao);
                    unsafe
                    {
                        _gl.DrawElements(PrimitiveType.Triangles, (uint)batch.IndexCount,
                            DrawElementsType.UnsignedInt, null);
                    }
                }

                _gl.DepthMask(true);
                _gl.Disable(EnableCap.Blend);
            }

            // Draw selection highlight overlay
            if (_highlightedProductLabel >= 0 &&
                _scene.ProductRanges.TryGetValue(_highlightedProductLabel, out var ranges))
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthFunc(DepthFunction.Lequal);
                _gl.DepthMask(false);

                _shader.SetVector4("uColor", HighlightColor);

                foreach (var range in ranges)
                {
                    var batchList = range.IsTransparent
                        ? _scene.TransparentBatches
                        : _scene.OpaqueBatches;

                    var batch = batchList[range.BatchIndex];
                    _gl.BindVertexArray(batch.Vao);
                    unsafe
                    {
                        _gl.DrawElements(PrimitiveType.Triangles,
                            (uint)range.IndexCount,
                            DrawElementsType.UnsignedInt,
                            (void*)(range.StartIndex * sizeof(uint)));
                    }
                }

                _gl.DepthFunc(DepthFunction.Less);
                _gl.DepthMask(true);
                _gl.Disable(EnableCap.Blend);
            }
        }

        _gl.BindVertexArray(0);

        // Resolve intermediate FBO → Avalonia's FB with FXAA
        if (_postProcess is not null)
        {
            _postProcess.End((uint)fb, width, height);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _postProcess?.Dispose();
        _postProcess = null;

        _scene?.Dispose();
        _scene = null;

        _shader?.Dispose();
        _shader = null;

        _gl?.Dispose();
        _gl = null;

        base.OnOpenGlDeinit(gl);
    }

    /// <summary>
    /// Loads a new scene into the viewport, replacing any previous scene.
    /// Must be called before rendering will display anything.
    /// </summary>
    public void LoadScene(GlScene scene)
    {
        _scene?.Dispose();
        _scene = scene;
        FitCamera();
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Queues a scene model for GPU upload on the next render call.
    /// This is safe to call from any thread; the actual GL work happens
    /// during the next render cycle.
    /// </summary>
    public void LoadModel(WexBimScene model)
    {
        _pendingModel = model;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Adjusts the camera to frame the entire loaded scene.
    /// </summary>
    public void FitCamera()
    {
        if (_scene is null) return;

        // Rotate bounds through Z-up→Y-up for correct camera framing
        var corners = new[]
        {
            Vector3.Transform(_scene.BoundsMin, ZupToYup),
            Vector3.Transform(_scene.BoundsMax, ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMin.Y, _scene.BoundsMax.Z), ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMax.Y, _scene.BoundsMin.Z), ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMin.Y, _scene.BoundsMin.Z), ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMin.X, _scene.BoundsMax.Y, _scene.BoundsMax.Z), ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMin.Y, _scene.BoundsMax.Z), ZupToYup),
            Vector3.Transform(new Vector3(_scene.BoundsMax.X, _scene.BoundsMax.Y, _scene.BoundsMin.Z), ZupToYup),
        };
        var rotMin = corners[0];
        var rotMax = corners[0];
        foreach (var c in corners)
        {
            rotMin = Vector3.Min(rotMin, c);
            rotMax = Vector3.Max(rotMax, c);
        }
        _camera.FitToScene(rotMin, rotMax);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Highlights the product with the given label, or clears highlighting if -1.
    /// The visual highlight effect is rendered as part of the scene draw pass.
    /// </summary>
    public void HighlightProduct(int productLabel)
    {
        _highlightedProductLabel = productLabel;
        RequestNextFrameRendering();
    }

    private int _highlightedProductLabel = -1;
    private int _isolatedProductLabel = -1;

    /// <summary>
    /// Zooms the camera to frame a product's bounding box.
    /// Bounds are in IFC Z-up coordinates.
    /// </summary>
    public void ZoomToProduct(Vector3 boundsMin, Vector3 boundsMax)
    {
        TransformBoundsToYup(boundsMin, boundsMax, out var rotMin, out var rotMax);
        _camera.FitToScene(rotMin, rotMax);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Shows only the geometry belonging to the given product.
    /// </summary>
    public void IsolateProduct(int productLabel)
    {
        _isolatedProductLabel = productLabel;
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Restores full scene visibility after isolation.
    /// </summary>
    public void ShowAll()
    {
        _isolatedProductLabel = -1;
        RequestNextFrameRendering();
    }

    #region Mouse Interaction

    // ICustomHitTest: OpenGlControlBase has no visual content for Avalonia's hit testing.
    // Without this, pointer events pass through the control.
    // Must check bounds — ICustomHitTest replaces the default bounds check entirely,
    // so returning true unconditionally would steal clicks from the toolbar/sidebar.
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _lastPointerPosition = point.Position;

        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _isPanning = true;
            else
                _isOrbiting = true;

            e.Handled = true;
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isOrbiting && !_isPanning)
            return;

        var pos = e.GetPosition(this);
        float dx = (float)(pos.X - _lastPointerPosition.X);
        float dy = (float)(pos.Y - _lastPointerPosition.Y);
        _lastPointerPosition = pos;

        if (_isOrbiting)
        {
            _camera.Orbit(dx * OrbitSensitivity, dy * OrbitSensitivity);
        }
        else if (_isPanning)
        {
            _camera.Pan(dx * PanSensitivity, dy * PanSensitivity);
        }

        RequestNextFrameRendering();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isOrbiting = false;
        _isPanning = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _camera.Zoom((float)e.Delta.Y);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    #endregion

    private PixelSize GetPixelSize()
    {
        var scaling = VisualRoot?.RenderScaling ?? 1.0;
        return new PixelSize(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
    }

    /// <summary>
    /// Transforms an IFC Z-up AABB through the ZupToYup rotation, computing the
    /// enclosing Y-up AABB from all 8 corners.
    /// </summary>
    private static void TransformBoundsToYup(Vector3 min, Vector3 max,
        out Vector3 rotMin, out Vector3 rotMax)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        corners[0] = Vector3.Transform(min, ZupToYup);
        corners[1] = Vector3.Transform(max, ZupToYup);
        corners[2] = Vector3.Transform(new Vector3(min.X, min.Y, max.Z), ZupToYup);
        corners[3] = Vector3.Transform(new Vector3(min.X, max.Y, min.Z), ZupToYup);
        corners[4] = Vector3.Transform(new Vector3(max.X, min.Y, min.Z), ZupToYup);
        corners[5] = Vector3.Transform(new Vector3(min.X, max.Y, max.Z), ZupToYup);
        corners[6] = Vector3.Transform(new Vector3(max.X, min.Y, max.Z), ZupToYup);
        corners[7] = Vector3.Transform(new Vector3(max.X, max.Y, min.Z), ZupToYup);

        rotMin = corners[0];
        rotMax = corners[0];
        for (int i = 1; i < 8; i++)
        {
            rotMin = Vector3.Min(rotMin, corners[i]);
            rotMax = Vector3.Max(rotMax, corners[i]);
        }
    }
}
