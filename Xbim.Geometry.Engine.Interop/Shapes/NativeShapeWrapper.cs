using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Factory for wrapping a <see cref="NativeShapeHandle"/> into the correct
    /// <see cref="IXShape"/> subtype based on the native shape's actual type.
    /// </summary>
    internal static class NativeShapeWrapper
    {
        /// <summary>
        /// Wraps a native shape handle into the most specific managed IXShape subtype.
        /// Takes ownership of the handle.
        /// </summary>
        internal static IXShape WrapShape(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0)
            {
                // Can't determine type - wrap as generic shape
                return new Shape(handle);
            }

            var shapeType = (XShapeType)typeVal;
            return shapeType switch
            {
                XShapeType.Solid => new Solid(handle),
                XShapeType.Face => new Face(handle),
                XShapeType.Shell => new Shell(handle),
                XShapeType.Wire => new Wire(handle),
                XShapeType.Edge => new Edge(handle),
                XShapeType.Vertex => new Vertex(handle),
                XShapeType.Compound => new Compound(handle),
                _ => new Shape(handle),
            };
        }

        /// <summary>
        /// Wraps a native shape handle and casts to the requested IXShape subtype.
        /// </summary>
        internal static T WrapShape<T>(NativeShapeHandle handle) where T : IXShape
            => (T)WrapShape(handle);

        /// <summary>
        /// Wraps a native shape handle as an <see cref="IXSolid"/>.
        /// Throws if the underlying shape is not a solid.
        /// </summary>
        internal static IXSolid WrapSolid(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0 || (XShapeType)typeVal != XShapeType.Solid)
                throw new InvalidOperationException(
                    $"Expected a Solid shape but got {(XShapeType)typeVal}.");

            return new Solid(handle);
        }

        /// <summary>
        /// Wraps a native shape handle as an <see cref="IXFace"/>.
        /// Throws if the underlying shape is not a face.
        /// </summary>
        internal static IXFace WrapFace(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0 || (XShapeType)typeVal != XShapeType.Face)
                throw new InvalidOperationException(
                    $"Expected a Face shape but got {(XShapeType)typeVal}.");

            return new Face(handle);
        }
    }
}
