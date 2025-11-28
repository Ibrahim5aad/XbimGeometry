using System;
using System.Collections.Generic;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Base implementation of <see cref="IXShape"/> backed by a <see cref="NativeShapeHandle"/>.
    /// All shape type queries (type, validity, bounds, etc.) are delegated to native P/Invoke calls.
    /// </summary>
    internal class Shape : NativeOwner<NativeShapeHandle>, IXShape
    {
        private XShapeType? _cachedType;

        internal Shape(NativeShapeHandle handle) : base(handle) { }

        public XShapeType ShapeType
        {
            get
            {
                if (_cachedType.HasValue)
                    return _cachedType.Value;

                int result = XbimGeometryNativeApi.xbim_shape_type(Handle, out int typeVal);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get shape type: {XbimGeometryNativeApi.GetLastError()}");

                _cachedType = (XShapeType)typeVal;
                return _cachedType.Value;
            }
        }

        public IXAxisAlignedBoundingBox Bounds()
        {
            int result = XbimGeometryNativeApi.xbim_shape_bounding_box(
                Handle,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (result != 0)
                return XAxisAlignedBoundingBox.Void;

            return new XAxisAlignedBoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public string BrepString()
        {
            return ShapeBinarySerializer.ToBrep(this);
        }

        /// <summary>
        /// Writes this shape to a .brep file in OCCT ASCII BRep format.
        /// </summary>
        public void WriteBrep(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_brep(Handle, filePath);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to write BRep file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Writes this shape to a binary STL file.
        /// </summary>
        public void WriteStl(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_stl(Handle, filePath, 0.1);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to write STL file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }

        public bool IsValidShape()
        {
            return XbimGeometryNativeApi.xbim_shape_is_valid(Handle) != 0;
        }

        public bool IsClosed
        {
            get
            {
                return XbimGeometryNativeApi.xbim_shape_is_closed(Handle) != 0;
            }
        }

        public bool IsEmptyShape()
        {
            if (Handle.IsInvalid)
                return true;

            return XbimGeometryNativeApi.xbim_shape_is_null(Handle) != 0;
        }

        public bool Triangulate(IXMeshFactors meshFactors)
        {
            int result = XbimGeometryNativeApi.xbim_shape_triangulate(
                Handle,
                meshFactors.LinearDefection,
                meshFactors.AngularDeflection,
                meshFactors.Relative ? 1 : 0);
            return result == 0;
        }

        public IXLocation Location
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_get_location(
                    Handle, out var locHandle,
                    out double m11, out double m12, out double m13,
                    out double m21, out double m22, out double m23,
                    out double m31, out double m32, out double m33,
                    out double ox, out double oy, out double oz,
                    out double scale);
                if (result != 0)
                    return new XLocation(); // fallback to identity

                return new XLocation(locHandle,
                    m11, m12, m13, m21, m22, m23, m31, m32, m33,
                    ox, oy, oz, scale);
            }
        }

        public IEnumerable<IXFace> AllFaces()
        {
            int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                Handle, (int)XShapeType.Face, out int count);
            if (countResult != 0 || count == 0)
                return Array.Empty<IXFace>();

            var ptrs = new IntPtr[count];
            int capacity = count;
            int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                Handle, (int)XShapeType.Face, ptrs, ref capacity);
            if (getResult != 0)
                return Array.Empty<IXFace>();

            var faces = new IXFace[capacity];
            for (int i = 0; i < capacity; i++)
                faces[i] = new Face(NativeShapeHandle.FromIntPtr(ptrs[i]));
            return faces;
        }

        /// <summary>
        /// Extracts sub-shapes of the given type from the underlying shape handle.
        /// Each returned handle is owned by the caller.
        /// </summary>
        internal NativeShapeHandle[] GetSubShapeHandles(XShapeType subType)
        {
            int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                Handle, (int)subType, out int count);
            if (countResult != 0 || count == 0)
                return Array.Empty<NativeShapeHandle>();

            var ptrs = new IntPtr[count];
            int capacity = count;
            int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                Handle, (int)subType, ptrs, ref capacity);
            if (getResult != 0)
                return Array.Empty<NativeShapeHandle>();

            var handles = new NativeShapeHandle[capacity];
            for (int i = 0; i < capacity; i++)
                handles[i] = NativeShapeHandle.FromIntPtr(ptrs[i]);
            return handles;
        }

        public bool IsEqual(IXShape other)
        {
            if (other is Shape ns)
            {
                XbimGeometryNativeApi.xbim_shape_is_same(Handle, ns.Handle, out int same);
                return same != 0;
            }
            return false;
        }

        public int ShapeHashCode()
        {
            XbimGeometryNativeApi.xbim_shape_hash_code(Handle, out int hash);
            return hash;
        }

        public XOrientation Orientation => XOrientation.Forward;
    }
}
