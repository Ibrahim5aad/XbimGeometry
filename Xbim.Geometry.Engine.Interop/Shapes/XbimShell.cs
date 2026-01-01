using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents a shell shape (TopoDS_Shell), implementing both the V6 <see cref="IXShell"/>
    /// and the legacy <see cref="IXbimShell"/> interfaces.
    /// </summary>
    internal class XbimShell : XbimShape, IXShell, IXbimShell, IEquatable<IXbimShell>
    {
        internal XbimShell(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimShellType;

        #region IXShell

        public double SurfaceArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute surface area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public IXFace[] Faces
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Face);
                return handles.Select(h => (IXFace)new XbimFace(h)).ToArray();
            }
        }

        #endregion

        #region IXbimShell

        public bool IsPolyhedron => false;

        public bool CanCreateSolid() => IsClosed;

        public IXbimSolid CreateSolid()
        {
            int result = XbimGeometryNativeApi.xbim_shell_make_solid(
                NativeContextHandle.NullHandle,
                Handle,
                out var solidHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create solid from shell: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimSolid(solidHandle);
        }

        IXbimFaceSet IXbimShell.Faces
        {
            get
            {
                var v6Faces = Faces;
                var faces = v6Faces.Select(f => (IXbimFace)f).ToArray();
                return new XbimFaceSet(faces);
            }
        }

        IXbimEdgeSet IXbimShell.Edges
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Edge);
                var edges = handles.Select(h => (IXbimEdge)new XbimEdge(h)).ToArray();
                return new XbimEdgeSet(edges);
            }
        }

        IXbimVertexSet IXbimShell.Vertices
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                var verts = handles.Select(h => (IXbimVertex)new XbimVertex(h)).ToArray();
                return new XbimVertexSet(verts);
            }
        }

        public IXbimGeometryObjectSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimFaceSet Section(IXbimFace toSection, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for section operations.");

        public void SaveAsBrep(string fileName) => WriteBrep(fileName);

        public string ToBRep => BrepString();

        public bool Equals(IXbimShell other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion
    }
}
