#if DEBUG
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Diagnostics
{
    /// <summary>
    /// Interactive 3D shape viewer for debugging.
    /// Available only in Debug builds (requires debug native library).
    /// Call from the VS Immediate Window at a breakpoint:
    ///   DebugViz.Show(myShape)
    ///   DebugViz.Show(wire, solid)
    ///   DebugViz.DumpBrep(myShape)
    /// </summary>
    public static class DebugViz
    {
        private const string Lib = NativeLibraryLoader.LibraryName;
        private const CallingConvention CC = CallingConvention.Cdecl;

        [DllImport(Lib, CallingConvention = CC)]
        private static extern int xbim_debug_view_shape(NativeShapeHandle shape);

        [DllImport(Lib, CallingConvention = CC)]
        private static extern int xbim_debug_view_shapes(
            IntPtr[] shapes, int count,
            double[]? r, double[]? g, double[]? b);

        [DllImport(Lib, CallingConvention = CC, CharSet = CharSet.Ansi)]
        private static extern int xbim_debug_dump_brep(
            NativeShapeHandle shape,
            [MarshalAs(UnmanagedType.LPStr)] string filepath);

        [DllImport(Lib, CallingConvention = CC, CharSet = CharSet.Ansi)]
        private static extern int xbim_debug_dump_stl(
            NativeShapeHandle shape,
            [MarshalAs(UnmanagedType.LPStr)] string filepath,
            double deflection);

        static DebugViz()
        {
            NativeLibraryLoader.EnsureLoaded();
        }

        /// <summary>
        /// Opens an interactive 3D viewer showing the shape.
        /// The viewer runs on a background thread and this method returns immediately.
        /// Controls: left-drag=rotate, right-drag=pan, wheel=zoom,
        /// F=fit, W=wireframe, S=shaded, T=top, Esc=close.
        /// </summary>
        public static void Show(IXShape shape)
        {
            var handle = GetHandle(shape);
            int result = xbim_debug_view_shape(handle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Debug viewer failed (code {result}): {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Opens a viewer showing multiple shapes overlaid with distinct colors.
        /// The viewer runs on a background thread and this method returns immediately.
        /// The first shape is opaque; subsequent shapes are semi-transparent.
        /// </summary>
        public static void Show(params IXShape[] shapes)
        {
            if (shapes == null || shapes.Length == 0)
                throw new ArgumentException("At least one shape required.", nameof(shapes));

            if (shapes.Length == 1)
            {
                Show(shapes[0]);
                return;
            }

            var handles = new NativeShapeHandle[shapes.Length];
            var ptrs = new IntPtr[shapes.Length];
            for (int i = 0; i < shapes.Length; i++)
            {
                handles[i] = GetHandle(shapes[i]);
                ptrs[i] = handles[i].DangerousGetHandle();
            }

            int result = xbim_debug_view_shapes(ptrs, shapes.Length, null, null, null);
            GC.KeepAlive(handles); // prevent collection during native call
            if (result != 0)
                throw new InvalidOperationException(
                    $"Debug viewer failed (code {result}): {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Writes the shape to a .brep file for external viewing (FreeCAD, OCCT Draw, etc.).
        /// Returns the file path for convenience.
        /// </summary>
        public static string DumpBrep(IXShape shape, [CallerMemberName] string? name = null)
        {
            string path = Path.Combine(Path.GetTempPath(), $"xbim_debug_{name ?? "shape"}.brep");
            var handle = GetHandle(shape);
            int result = xbim_debug_dump_brep(handle, path);
            if (result != 0)
                throw new InvalidOperationException(
                    $"BREP dump failed (code {result}): {XbimGeometryNativeApi.GetLastError()}");
            return path;
        }

        /// <summary>
        /// Writes the shape to a .brep file at the specified path.
        /// </summary>
        public static void DumpBrepTo(IXShape shape, string filepath)
        {
            var handle = GetHandle(shape);
            int result = xbim_debug_dump_brep(handle, filepath);
            if (result != 0)
                throw new InvalidOperationException(
                    $"BREP dump failed (code {result}): {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Writes the shape to a binary STL file (tessellated mesh).
        /// Returns the file path. Used by the VS Code viewer extension.
        /// </summary>
        public static string DumpStl(IXShape shape, [CallerMemberName] string? name = null)
        {
            string path = Path.Combine(Path.GetTempPath(), $"xbim_debug_{name ?? "shape"}.stl");
            var handle = GetHandle(shape);
            int result = xbim_debug_dump_stl(handle, path, 0.1);
            if (result != 0)
                throw new InvalidOperationException(
                    $"STL dump failed (code {result}): {XbimGeometryNativeApi.GetLastError()}");
            return path;
        }

        private static NativeShapeHandle GetHandle(IXShape shape)
        {
            if (shape is NativeOwner<NativeShapeHandle> owner)
                return owner.Handle;

            throw new ArgumentException(
                $"Shape of type {shape.GetType().Name} is not a native shape. " +
                "DebugViz only works with shapes created by the native engine.",
                nameof(shape));
        }
    }
}
#endif
