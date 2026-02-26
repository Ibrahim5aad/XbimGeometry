using Silk.NET.OpenGL;

namespace Xbim.Examples.Viewer.Rendering;

/// <summary>
/// Renders the scene to an intermediate framebuffer and applies FXAA anti-aliasing
/// when resolving to the final output framebuffer.
/// </summary>
internal sealed class PostProcessPass : IDisposable
{
    private readonly GL _gl;
    private readonly ShaderProgram _fxaaShader;
    private readonly uint _quadVao;
    private readonly uint _quadVbo;

    private uint _fbo;
    private uint _colorTexture;
    private uint _depthRbo;
    private int _width;
    private int _height;
    private bool _disposed;

    #region FXAA Shaders

    private const string FxaaVertexBody = @"
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

    // FXAA 3.11 Quality — simplified single-pass implementation
    private const string FxaaFragmentBody = @"
in vec2 vTexCoord;

uniform sampler2D uScreen;
uniform float uTexelSizeX;
uniform float uTexelSizeY;

out vec4 FragColor;

void main()
{
    vec2 uTexelSize = vec2(uTexelSizeX, uTexelSizeY);

    // Sample center and 4 neighbours
    vec3 rgbM  = texture(uScreen, vTexCoord).rgb;
    vec3 rgbNW = texture(uScreen, vTexCoord + vec2(-1.0, -1.0) * uTexelSize).rgb;
    vec3 rgbNE = texture(uScreen, vTexCoord + vec2( 1.0, -1.0) * uTexelSize).rgb;
    vec3 rgbSW = texture(uScreen, vTexCoord + vec2(-1.0,  1.0) * uTexelSize).rgb;
    vec3 rgbSE = texture(uScreen, vTexCoord + vec2( 1.0,  1.0) * uTexelSize).rgb;

    // Luma (perceptual brightness)
    vec3 lumaCoeff = vec3(0.299, 0.587, 0.114);
    float lumaM  = dot(rgbM,  lumaCoeff);
    float lumaNW = dot(rgbNW, lumaCoeff);
    float lumaNE = dot(rgbNE, lumaCoeff);
    float lumaSW = dot(rgbSW, lumaCoeff);
    float lumaSE = dot(rgbSE, lumaCoeff);

    float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
    float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));
    float lumaRange = lumaMax - lumaMin;

    // Skip FXAA if contrast is low
    if (lumaRange < max(0.0312, lumaMax * 0.125))
    {
        FragColor = vec4(rgbM, 1.0);
        return;
    }

    // Compute edge direction
    vec2 dir;
    dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
    dir.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));

    float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * 0.25 * 0.25, 1.0 / 128.0);
    float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
    dir = clamp(dir * rcpDirMin, vec2(-8.0), vec2(8.0)) * uTexelSize;

    // Two-tap filter along the edge
    vec3 rgbA = 0.5 * (
        texture(uScreen, vTexCoord + dir * (1.0 / 3.0 - 0.5)).rgb +
        texture(uScreen, vTexCoord + dir * (2.0 / 3.0 - 0.5)).rgb);

    // Four-tap filter for wider reach
    vec3 rgbB = rgbA * 0.5 + 0.25 * (
        texture(uScreen, vTexCoord + dir * -0.5).rgb +
        texture(uScreen, vTexCoord + dir *  0.5).rgb);

    float lumaB = dot(rgbB, lumaCoeff);

    // Use the wider filter only if it stays within the local luma range
    vec3 result = (lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB;
    FragColor = vec4(result, 1.0);
}
";

    #endregion

    public PostProcessPass(GL gl)
    {
        _gl = gl;

        // Detect GL ES vs desktop
        string? version = gl.GetStringS(StringName.Version);
        bool isGLES = version?.Contains("OpenGL ES") == true;
        string prefix = isGLES
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";

        _fxaaShader = ShaderProgram.Create(gl, prefix + FxaaVertexBody, prefix + FxaaFragmentBody);

        // Fullscreen quad: 2 triangles as a triangle strip
        // [posX, posY, texU, texV] per vertex
        float[] quadVertices =
        {
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
            -1f,  1f, 0f, 1f,
             1f,  1f, 1f, 1f,
        };

        _quadVao = gl.GenVertexArray();
        _quadVbo = gl.GenBuffer();
        gl.BindVertexArray(_quadVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        unsafe
        {
            fixed (float* ptr = quadVertices)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(quadVertices.Length * sizeof(float)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        uint stride = 4 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.EnableVertexAttribArray(1);
        unsafe
        {
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride,
                (void*)(2 * sizeof(float)));
        }
        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Binds the intermediate FBO so subsequent draw calls render into it.
    /// Creates or resizes the FBO if the viewport dimensions changed.
    /// </summary>
    public void Begin(int width, int height)
    {
        if (width != _width || height != _height)
            Resize(width, height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    /// <summary>
    /// Resolves the intermediate FBO to the given output framebuffer by drawing
    /// a fullscreen quad with the FXAA shader.
    /// </summary>
    public void End(uint outputFbo, int width, int height)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, outputFbo);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);

        _fxaaShader.Use();
        _fxaaShader.SetInt("uScreen", 0);
        _fxaaShader.SetFloat("uTexelSizeX", 1.0f / width);
        _fxaaShader.SetFloat("uTexelSizeY", 1.0f / height);

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);

        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);

        // Restore state for next frame
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DeleteFbo();
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteBuffer(_quadVbo);
        _fxaaShader.Dispose();
    }

    private void Resize(int width, int height)
    {
        DeleteFbo();

        _width = width;
        _height = height;

        // Color texture — pass null data pointer to allocate without uploading
        _colorTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        // Depth renderbuffer
        _depthRbo = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRbo);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8,
            (uint)width, (uint)height);

        // Framebuffer
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _colorTexture, 0);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _depthRbo);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            System.Diagnostics.Debug.WriteLine($"[PostProcessPass] FBO incomplete: {status}");
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void DeleteFbo()
    {
        if (_fbo != 0)
        {
            _gl.DeleteFramebuffer(_fbo);
            _fbo = 0;
        }
        if (_colorTexture != 0)
        {
            _gl.DeleteTexture(_colorTexture);
            _colorTexture = 0;
        }
        if (_depthRbo != 0)
        {
            _gl.DeleteRenderbuffer(_depthRbo);
            _depthRbo = 0;
        }
        _width = 0;
        _height = 0;
    }
}
