using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Shapes
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
        /// If the shape is a Compound containing a single solid, extracts it.
        /// If the compound contains multiple solids, returns an <see cref="XbimSolidSet"/>
        /// that implements both <see cref="IXSolid"/> and <see cref="IXbimSolidSet"/>.
        /// </summary>
        internal static IXSolid WrapSolid(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0)
                throw new XbimGeometryServiceException("Failed to determine shape type.");

            var shapeType = (XShapeType)typeVal;

            if (shapeType == XShapeType.Solid)
                return new XbimSolid(handle);

            if (shapeType == XShapeType.Compound)
            {
                int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                    handle, (int)XShapeType.Solid, out int solidCount);
                if (countResult != 0 || solidCount == 0)
                    throw new XbimGeometryServiceException("Compound shape contains no solids.");

                var ptrs = new IntPtr[solidCount];
                int capacity = solidCount;
                int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                    handle, (int)XShapeType.Solid, ptrs, ref capacity);
                if (getResult != 0 || capacity == 0)
                    throw new XbimGeometryServiceException(
                        "Failed to extract solids from compound shape.");

                if (capacity == 1)
                {
                    var solidHandle = NativeShapeHandle.FromIntPtr(ptrs[0]);
                    handle.Dispose();
                    return new XbimSolid(solidHandle);
                }

                // Multiple solids — preserve all in a solid set
                var solids = new IXbimSolid[capacity];
                for (int i = 0; i < capacity; i++)
                    solids[i] = new XbimSolid(NativeShapeHandle.FromIntPtr(ptrs[i]));
                handle.Dispose();
                return new XbimSolidSet(solids);
            }

            throw new XbimGeometryServiceException(
                $"Expected a Solid shape but got {shapeType}.");
        }

        /// <summary>
        /// Wraps a native shape handle as an <see cref="IXShell"/>.
        /// Throws if the underlying shape is not a shell.
        /// </summary>
        internal static IXShell WrapShell(NativeShapeHandle handle)
        {
            if (handle == null || handle.IsInvalid)
                throw new ArgumentException("Cannot wrap an invalid shape handle.", nameof(handle));

            int result = XbimGeometryNativeApi.xbim_shape_type(handle, out int typeVal);
            if (result != 0 || (XShapeType)typeVal != XShapeType.Shell)
                throw new XbimGeometryServiceException(
                    $"Expected a Shell shape but got {(XShapeType)typeVal}.");

            return new XbimShell(handle);
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
                throw new XbimGeometryServiceException(
                    $"Expected a Face shape but got {(XShapeType)typeVal}.");

            return new XbimFace(handle);
        }
    }
}
