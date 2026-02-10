using System;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents a face shape (TopoDS_Face), implementing <see cref="IXFace"/>
    /// and <see cref="IXbimFace"/> interfaces.
    /// </summary>
    internal class XbimFace : XbimShape, IXFace, IXbimFace, IEquatable<IXbimFace>
    {
        internal XbimFace(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimFaceType;

        #region IXFace

        public double Area
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute face area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public double Perimeter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_perimeter(Handle, out double perimeter);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute face perimeter: {XbimGeometryNativeApi.GetLastError()}");
                return perimeter;
            }
        }

        public double Tolerance
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_tolerance(Handle, out double tol);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get face tolerance: {XbimGeometryNativeApi.GetLastError()}");
                return tol;
            }
        }

        public IXWire OuterBound
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_outer_wire(Handle, out var wireHandle);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get outer wire: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimWire(wireHandle);
            }
        }

        public IXWire[] InnerBounds
        {
            get
            {
                int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                    Handle, (int)XShapeType.Wire, out int totalWires);
                if (countResult != 0 || totalWires <= 1)
                    return Array.Empty<IXWire>();

                int capacity = totalWires - 1;
                var ptrs = new IntPtr[capacity];
                int innerCount = capacity;
                int getResult = XbimGeometryNativeApi.xbim_face_inner_wires(Handle, ptrs, ref innerCount);
                if (getResult != 0 || innerCount == 0)
                    return Array.Empty<IXWire>();

                var wires = new IXWire[innerCount];
                for (int i = 0; i < innerCount; i++)
                    wires[i] = new XbimWire(NativeShapeHandle.FromIntPtr(ptrs[i]));
                return wires;
            }
        }

        public IXSurface Surface
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_get_surface(Handle, out var surfHandle, out int surfType);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get face surface: {XbimGeometryNativeApi.GetLastError()}");
                var type = (XSurfaceType)surfType;

                // Try to extract elementary surface properties (position + radius).
                // Succeeds for Geom_ElementarySurface subtypes: plane, cylinder, cone, sphere, torus.
                int propsResult = XbimGeometryNativeApi.xbim_surface_get_elementary_props(
                    surfHandle,
                    out double ox, out double oy, out double oz,
                    out double zx, out double zy, out double zz,
                    out double xx, out double xy, out double xz,
                    out double radius);

                if (propsResult == 0)
                {
                    switch (type)
                    {
                        case XSurfaceType.IfcCylindricalSurface:
                            return new CylindricalSurface(surfHandle, radius)
                            {
                                Position = new XAxis2Placement3d(
                                    new XPoint(ox, oy, oz),
                                    new XDirection(zx, zy, zz),
                                    new XDirection(xx, xy, xz))
                            };
                        case XSurfaceType.IfcSurfaceOfRevolution:
                            // Geom_ConicalSurface is an elementary surface classified as revolution
                            return new ConicalSurface(surfHandle, radius)
                            {
                                Position = new XAxis2Placement3d(
                                    new XPoint(ox, oy, oz),
                                    new XDirection(zx, zy, zz),
                                    new XDirection(xx, xy, xz))
                            };
                        case XSurfaceType.IfcSphericalSurface:
                            return new SphericalSurface(surfHandle, radius)
                            {
                                Position = new XAxis2Placement3d(
                                    new XPoint(ox, oy, oz),
                                    new XDirection(zx, zy, zz),
                                    new XDirection(xx, xy, xz))
                            };
                        case XSurfaceType.IfcToroidalSurface:
                            return new ToroidalSurface(surfHandle, radius)
                            {
                                Position = new XAxis2Placement3d(
                                    new XPoint(ox, oy, oz),
                                    new XDirection(zx, zy, zz),
                                    new XDirection(xx, xy, xz))
                            };
                        case XSurfaceType.IfcPlane:
                            return new Plane(surfHandle,
                                new XPoint(ox, oy, oz),
                                new XDirection(zx, zy, zz),
                                new XDirection(xx, xy, xz));
                    }
                }

                return new Surface(surfHandle, type);
            }
        }

        #endregion

        #region IXbimFace

        public XbimVector3D Normal
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_normal(
                    Handle, 0.5, 0.5,
                    out double nx, out double ny, out double nz);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get face normal: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimVector3D(nx, ny, nz);
            }
        }

        public bool IsPlanar => false;

        XbimPoint3D IXbimFace.Location
        {
            get
            {
                var bb = Bounds();
                if (bb.IsVoid)
                    return new XbimPoint3D(0, 0, 0);
                var c = bb.Centroid;
                return new XbimPoint3D(c.X, c.Y, c.Z);
            }
        }

        IXbimWire IXbimFace.OuterBound
        {
            get
            {
                return (XbimWire)OuterBound;
            }
        }

        IXbimWireSet IXbimFace.InnerBounds
        {
            get
            {
                var inners = InnerBounds;
                var wires = inners.Select(w => (IXbimWire)w).ToArray();
                return new XbimWireSet(wires);
            }
        }

        public void SaveAsBrep(string fileName) => WriteBrep(fileName);

        public string ToBRep => BrepString();

        public bool Equals(IXbimFace other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion
    }
}
