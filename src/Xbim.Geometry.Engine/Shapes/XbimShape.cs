using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Shapes
{
    /// <summary>
    /// Base implementation of <see cref="IXShape"/> and <see cref="IXbimGeometryObject"/>
    /// backed by a <see cref="NativeShapeHandle"/>.
    /// </summary>
    internal class XbimShape : NativeOwner<NativeShapeHandle>, IXShape, IXbimGeometryObject
    {
        private XShapeType? _cachedType;

        internal XbimShape(NativeShapeHandle handle) : base(handle) { }

        #region IXShape

        public XShapeType ShapeType
        {
            get
            {
                if (_cachedType.HasValue)
                    return _cachedType.Value;

                int result = XbimGeometryNativeApi.xbim_shape_type(Handle, out int typeVal);
                if (result != 0)
                    throw new XbimGeometryServiceException(
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

        public void WriteBrep(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_brep(Handle, filePath);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to write BRep file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }

        public void WriteStl(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_stl(Handle, filePath, 0.1);
            if (result != 0)
                throw new XbimGeometryServiceException(
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
                faces[i] = new XbimFace(NativeShapeHandle.FromIntPtr(ptrs[i]));
            return faces;
        }

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
            if (other is XbimShape ns)
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

        #endregion

        #region IXbimGeometryObject

        public virtual XbimGeometryObjectType GeometryType => MapShapeType(ShapeType);

        public bool IsValid => Handle != null && !Handle.IsInvalid && !Handle.IsClosed;

        public virtual bool IsSet => false;

        public XbimRect3D BoundingBox
        {
            get
            {
                var bb = Bounds();
                if (bb.IsVoid)
                    return XbimRect3D.Empty;

                var min = bb.CornerMin;
                var max = bb.CornerMax;
                return new XbimRect3D(
                    min.X, min.Y, min.Z,
                    max.X - min.X, max.Y - min.Y, max.Z - min.Z);
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            return ApplyMatrix(matrix3D);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            return ApplyMatrix(matrix3D);
        }

        #endregion

        #region Helpers

        internal static XbimGeometryObjectType MapShapeType(XShapeType type)
        {
            return type switch
            {
                XShapeType.Solid => XbimGeometryObjectType.XbimSolidType,
                XShapeType.Shell => XbimGeometryObjectType.XbimShellType,
                XShapeType.Face => XbimGeometryObjectType.XbimFaceType,
                XShapeType.Wire => XbimGeometryObjectType.XbimWireType,
                XShapeType.Edge => XbimGeometryObjectType.XbimEdgeType,
                XShapeType.Vertex => XbimGeometryObjectType.XbimVertexType,
                XShapeType.Compound => XbimGeometryObjectType.XbimCompoundType,
                _ => XbimGeometryObjectType.XbimGeometryObjectSetType,
            };
        }

        private XbimShape ApplyMatrix(XbimMatrix3D m)
        {
            // scale is baked into the rotation part
            int result = XbimGeometryNativeApi.xbim_shape_gtransform(
                Handle,
                m.M11, m.M21, m.M31, m.OffsetX,
                m.M12, m.M22, m.M32, m.OffsetY,
                m.M13, m.M23, m.M33, m.OffsetZ,
                1, 1, 1,
                out var transformedHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to transform shape: {XbimGeometryNativeApi.GetLastError()}");

            return (XbimShape)NativeShapeWrapper.WrapShape(transformedHandle);
        }

        #endregion
    }
}
