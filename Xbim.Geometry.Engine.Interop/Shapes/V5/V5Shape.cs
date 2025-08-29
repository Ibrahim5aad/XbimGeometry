using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Shape"/> to the legacy <see cref="IXbimGeometryObject"/> interface.
    /// This is a lightweight view — it does not own the underlying native handle.
    /// </summary>
    internal class V5Shape : IXbimGeometryObject
    {
        internal readonly Shape Inner;

        internal V5Shape(Shape inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public virtual XbimGeometryObjectType GeometryType => MapShapeType(Inner.ShapeType);

        public bool IsValid => Inner.IsValidShape();

        public virtual bool IsSet => false;

        public XbimRect3D BoundingBox
        {
            get
            {
                var bb = Inner.Bounds();
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
            var moved = ApplyMatrix(matrix3D);
            return Wrap(moved);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            // OCCT shapes share underlying geometry, so moved == shallow transform
            var moved = ApplyMatrix(matrix3D);
            return Wrap(moved);
        }

        public void Dispose()
        {
            // No-op: V5Shape is a view, the V6 Shape owns the handle
        }

        #region Static factory methods

        /// <summary>
        /// Wraps a V6 shape into the appropriate V5 adapter based on shape type.
        /// </summary>
        internal static IXbimGeometryObject Wrap(IXShape shape)
        {
            if (shape == null)
                throw new ArgumentNullException(nameof(shape));

            var s = (Shape)shape;
            return s.ShapeType switch
            {
                XShapeType.Solid => new V5Solid((Solid)s),
                XShapeType.Shell => new V5Shell((Shell)s),
                XShapeType.Face => new V5Face((Face)s),
                XShapeType.Wire => new V5Wire((Wire)s),
                XShapeType.Edge => new V5Edge((Edge)s),
                XShapeType.Vertex => new V5Vertex((Vertex)s),
                _ => new V5Shape(s),
            };
        }

        /// <summary>
        /// Wraps a V6 solid into a V5 solid adapter.
        /// </summary>
        internal static IXbimSolid WrapSolid(IXSolid solid)
        {
            return new V5Solid((Solid)solid);
        }

        /// <summary>
        /// Wraps a V6 face into a V5 face adapter.
        /// </summary>
        internal static IXbimFace WrapFace(IXFace face)
        {
            return new V5Face((Face)face);
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

        /// <summary>
        /// Decomposes a <see cref="XbimMatrix3D"/> into an axis2 placement and applies it
        /// to the inner shape via <c>xbim_shape_moved</c>.
        /// </summary>
        private Shape ApplyMatrix(XbimMatrix3D m)
        {
            // Extract translation (origin)
            double ox = m.OffsetX, oy = m.OffsetY, oz = m.OffsetZ;

            // Extract column vectors from rotation part (rows of XbimMatrix3D)
            // XbimMatrix3D is row-major: M11 M12 M13 = X-axis direction
            //                            M21 M22 M23 = Y-axis direction
            //                            M31 M32 M33 = Z-axis direction
            double xDirX = m.M11, xDirY = m.M12, xDirZ = m.M13;
            double zDirX = m.M31, zDirY = m.M32, zDirZ = m.M33;

            int locResult = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                ox, oy, oz,
                zDirX, zDirY, zDirZ,
                xDirX, xDirY, xDirZ,
                out var locationHandle);

            if (locResult != 0)
                throw new InvalidOperationException(
                    $"Failed to create location from matrix: {XbimGeometryNativeApi.GetLastError()}");

            using (locationHandle)
            {
                int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                    Inner.Handle, locationHandle, out var movedHandle);

                if (moveResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");

                return (Shape)NativeShapeWrapper.WrapShape(movedHandle);
            }
        }

        #endregion
    }
}
