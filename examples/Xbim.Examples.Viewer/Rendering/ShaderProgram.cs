using System.Numerics;
using Silk.NET.OpenGL;

namespace Xbim.Examples.Viewer.Rendering;

/// <summary>
/// Compiles and links a GLSL vertex/fragment shader pair, and provides
/// uniform setters with a location cache for efficient per-frame updates.
/// </summary>
internal sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();
    private bool _disposed;

    #region GLSL Shaders (body only — version/precision prepended at runtime)

    /// <summary>
    /// Vertex shader body (no #version line — prepended at compile time based on GL context).
    /// </summary>
    private const string VertexShaderBody = @"
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform mat3 uNormalMatrix;

out vec3 vNormal;
out vec3 vFragPos;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vFragPos = worldPos.xyz;
    vNormal = normalize(uNormalMatrix * aNormal);
    gl_Position = uProjection * uView * worldPos;
}
";

    /// <summary>
    /// Fragment shader body (no #version line — prepended at compile time based on GL context).
    /// Two-light Blinn-Phong with hemisphere ambient for softer, more natural lighting.
    /// </summary>
    private const string FragmentShaderBody = @"
in vec3 vNormal;
in vec3 vFragPos;

uniform vec4 uColor;
uniform vec3 uLightDir;
uniform vec3 uViewPos;

out vec4 FragColor;

void main()
{
    vec3 norm = normalize(vNormal);

    // Hemisphere ambient: cool sky blending to warm ground via normal Y
    vec3 skyColor  = vec3(0.14, 0.16, 0.20);
    vec3 gndColor  = vec3(0.12, 0.10, 0.08);
    float hemi = norm.y * 0.5 + 0.5;
    vec3 ambient = mix(gndColor, skyColor, hemi);

    // Key light: Lambertian diffuse + Blinn-Phong specular
    vec3 keyDir = normalize(-uLightDir);
    float keyDiff = max(dot(norm, keyDir), 0.0);
    vec3 viewDir = normalize(uViewPos - vFragPos);
    vec3 halfDir = normalize(keyDir + viewDir);
    float spec = pow(max(dot(norm, halfDir), 0.0), 48.0);
    float keySpec = 0.25 * spec;

    // Fill light: reversed key direction, wrap diffuse for soft shadow fill
    vec3 fillDir = normalize(uLightDir);
    float fillDiff = dot(norm, fillDir) * 0.5 + 0.5;
    fillDiff *= fillDiff;
    float fillStrength = 0.4;

    vec3 lighting = ambient
                  + uColor.rgb * keyDiff
                  + uColor.rgb * keySpec
                  + uColor.rgb * fillDiff * fillStrength;

    FragColor = vec4(lighting, uColor.a);
}
";

    #endregion

    private ShaderProgram(GL gl, uint handle)
    {
        _gl = gl;
        _handle = handle;
    }

    /// <summary>
    /// Compiles vertex and fragment shaders, links the program, and returns
    /// a ready-to-use <see cref="ShaderProgram"/>. Throws on compile/link failure.
    /// </summary>
    public static ShaderProgram Create(GL gl, string vertexSource, string fragmentSource)
    {
        uint vertexShader = CompileShader(gl, ShaderType.VertexShader, vertexSource);
        uint fragmentShader = CompileShader(gl, ShaderType.FragmentShader, fragmentSource);

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        gl.LinkProgram(program);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus != (int)GLEnum.True)
        {
            string log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }

        // Shaders are linked into the program and no longer needed individually
        gl.DetachShader(program, vertexShader);
        gl.DetachShader(program, fragmentShader);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        return new ShaderProgram(gl, program);
    }

    /// <summary>
    /// Creates the default Blinn-Phong shader program, automatically detecting
    /// whether the GL context is desktop GL or GL ES (ANGLE) and prepending
    /// the appropriate version/precision directives.
    /// </summary>
    public static ShaderProgram CreateDefault(GL gl)
    {
        // Detect GL ES (ANGLE on Windows) vs desktop GL
        string? version = gl.GetStringS(StringName.Version);
        bool isGLES = version?.Contains("OpenGL ES") == true;

        string prefix = isGLES
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";

        System.Diagnostics.Debug.WriteLine($"[ShaderProgram] GL version: {version}, using {(isGLES ? "GLES 3.0" : "GL 3.3")} shaders");

        return Create(gl, prefix + VertexShaderBody, prefix + FragmentShaderBody);
    }

    /// <summary>Activates this shader program for subsequent draw calls.</summary>
    public void Use() => _gl.UseProgram(_handle);

    #region Uniform Setters

    public void SetMatrix4(string name, Matrix4x4 value)
    {
        int location = GetLocation(name);
        unsafe
        {
            _gl.UniformMatrix4(location, 1, false, (float*)&value);
        }
    }

    public void SetMatrix3(string name, ReadOnlySpan<float> value)
    {
        int location = GetLocation(name);
        unsafe
        {
            fixed (float* ptr = value)
            {
                _gl.UniformMatrix3(location, 1, false, ptr);
            }
        }
    }

    public void SetVector3(string name, Vector3 value)
    {
        int location = GetLocation(name);
        _gl.Uniform3(location, value);
    }

    public void SetVector4(string name, Vector4 value)
    {
        int location = GetLocation(name);
        _gl.Uniform4(location, value);
    }

    public void SetFloat(string name, float value)
    {
        int location = GetLocation(name);
        _gl.Uniform1(location, value);
    }

    public void SetInt(string name, int value)
    {
        int location = GetLocation(name);
        _gl.Uniform1(location, value);
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteProgram(_handle);
            _disposed = true;
        }
    }

    private int GetLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int location))
            return location;

        location = _gl.GetUniformLocation(_handle, name);
        _uniformLocations[name] = location;
        return location;
    }

    private static uint CompileShader(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != (int)GLEnum.True)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}");
        }

        return shader;
    }
}
