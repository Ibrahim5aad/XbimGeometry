using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Mixed collection of geometry objects implementing <see cref="IXbimGeometryObjectSet"/>.
    /// </summary>
    internal class XbimGeometryObjectSet : IXbimGeometryObjectSet
    {
        private readonly List<IXbimGeometryObject> _items;

        internal XbimGeometryObjectSet()
        {
            _items = new List<IXbimGeometryObject>();
        }

        internal XbimGeometryObjectSet(IEnumerable<IXbimGeometryObject> items)
        {
            _items = items != null
                ? new List<IXbimGeometryObject>(items)
                : new List<IXbimGeometryObject>();
        }

        public int Count => _items.Count;

        public IXbimGeometryObject First => _items.Count > 0
            ? _items[0]
            : throw new InvalidOperationException("Geometry object set is empty.");

        public IXbimSolidSet Solids
        {
            get
            {
                var solids = _items.OfType<IXbimSolid>();
                return new XbimSolidSet(solids);
            }
        }

        public IXbimShellSet Shells
        {
            get
            {
                var shells = _items.OfType<IXbimShell>().ToArray();
                return new XbimShellSet(shells);
            }
        }

        public IXbimFaceSet Faces
        {
            get
            {
                var faces = _items.OfType<IXbimFace>().ToArray();
                return new XbimFaceSet(faces);
            }
        }

        public IXbimEdgeSet Edges
        {
            get
            {
                var edges = _items.OfType<IXbimEdge>().ToArray();
                return new XbimEdgeSet(edges);
            }
        }

        public IXbimVertexSet Vertices
        {
            get
            {
                var vertices = _items.OfType<IXbimVertex>().ToArray();
                return new XbimVertexSet(vertices);
            }
        }

        public void Add(IXbimGeometryObject shape)
        {
            if (shape != null)
                _items.Add(shape);
        }

        public bool Sew()
            => throw new NotSupportedException("Sewing not yet supported.");

        public string ToBRep
            => throw new NotSupportedException("BRep serialization of geometry object sets not yet supported.");

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimGeometryObjectSetType;
        public bool IsValid => _items.Count > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_items.Count == 0) return XbimRect3D.Empty;
                var result = _items[0].BoundingBox;
                for (int i = 1; i < _items.Count; i++)
                    result.Union(_items[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Geometry object set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Geometry object set transform not supported.");

        public IXbimGeometryObjectSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IXbimGeometryObjectSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => throw new NotSupportedException("Use BooleanFactory for boolean operations.");

        public IEnumerator<IXbimGeometryObject> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public void Dispose() { }
    }
}
