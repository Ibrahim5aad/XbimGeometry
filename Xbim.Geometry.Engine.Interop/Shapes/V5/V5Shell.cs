using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Shell"/> to the legacy <see cref="IXbimShell"/> interface.
    /// </summary>
    internal class V5Shell : V5Shape, IXbimShell, IEquatable<IXbimShell>
    {
        private readonly Shell _shell;

        internal V5Shell(Shell shell) : base(shell)
        {
            _shell = shell;
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimShellType;

        public double SurfaceArea => _shell.SurfaceArea;

        public bool IsPolyhedron => false;

        public bool IsClosed => Inner.IsClosed;

        public IXbimFaceSet Faces
        {
            get
            {
                var v6Faces = _shell.Faces;
                var v5Faces = v6Faces.Select(f => (IXbimFace)new V5Face((Face)f)).ToArray();
                return new V5FaceSet(v5Faces);
            }
        }

        public IXbimEdgeSet Edges
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Edge);
                var v5Edges = handles.Select(h => (IXbimEdge)new V5Edge(new Edge(h))).ToArray();
                return new V5EdgeSet(v5Edges);
            }
        }

        public IXbimVertexSet Vertices
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                var v5Verts = handles.Select(h => (IXbimVertex)new V5Vertex(new Vertex(h))).ToArray();
                return new V5VertexSet(v5Verts);
            }
        }

        public bool CanCreateSolid() => IsClosed;

        public IXbimSolid CreateSolid()
        {
            throw new NotSupportedException("Shell-to-solid conversion not yet supported via V5 adapter.");
        }

        public IXbimGeometryObjectSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for boolean operations.");

        public IXbimFaceSet Section(IXbimFace toSection, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use V6 BooleanFactory for section operations.");

        public void SaveAsBrep(string fileName) => ((Shape)Inner).WriteBrep(fileName);

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimShell other)
        {
            if (other is V5Shell v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }
    }
}
