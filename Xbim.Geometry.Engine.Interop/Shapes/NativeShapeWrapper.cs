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
                return new XbimShape(handle);
            }

            var shapeType = (XShapeType)typeVal;
            return shapeType switch
            {
                XShapeType.Solid => new XbimSolid(handle),
                XShapeType.Face => new XbimFace(handle),
                XShapeType.Shell => new XbimShell(handle),
                XShapeType.Wire => new XbimWire(handle),
                XShapeType.Edge => new XbimEdge(handle),
                XShapeType.Vertex => new XbimVertex(handle),
                XShapeType.Compound => new XbimCompound(handle),
                _ => new XbimShape(handle),
            };
        }

        /// <summary>
        /// Wraps a native shape handle and casts to the requested IXShape subtype.
        /// </summary>
        internal static T WrapShape<T>(NativeShapeHandle handle) where T : IXShape
            => (T)WrapShape(handle);

        /// <summary>
        /// Wraps a native shape handle as an <see cref="IXSolid"/>.
        /// If the shape is a Compound, extracts the single solid from it.
        /// Throws if the underlying shape is not a solid or a compound containing exactly one solid.
        /// </summary>
        internal static IXSolid WrapSolid(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0)
                throw new InvalidOperationException("Failed to determine shape type.");

            var shapeType = (XShapeType)typeVal;

            if (shapeType == XShapeType.Solid)
                return new XbimSolid(handle);

            if (shapeType == XShapeType.Compound)
            {
                int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                    handle, (int)XShapeType.Solid, out int solidCount);
                if (countResult != 0 || solidCount == 0)
                    throw new InvalidOperationException(
                        "Compound shape contains no solids.");

                var ptrs = new IntPtr[solidCount];
                int capacity = solidCount;
                int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                    handle, (int)XShapeType.Solid, ptrs, ref capacity);
                if (getResult != 0 || capacity == 0)
                    throw new InvalidOperationException(
                        "Failed to extract solids from compound shape.");

                // Take the first solid; dispose any extras
                var solidHandle = NativeShapeHandle.FromIntPtr(ptrs[0]);
                for (int i = 1; i < capacity; i++)
                    NativeShapeHandle.FromIntPtr(ptrs[i]).Dispose();

                handle.Dispose();
                return new XbimSolid(solidHandle);
            }

            throw new InvalidOperationException(
                $"Expected a Solid shape but got {shapeType}.");
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

            return new XbimFace(handle);
        }
    }
}
