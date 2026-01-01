using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Collection of solids implementing <see cref="IXbimSolidSet"/>.
    /// </summary>
    internal class XbimSolidSet : IXbimSolidSet
    {
        private readonly List<IXbimSolid> _solids;

        internal XbimSolidSet()
        {
            _solids = new List<IXbimSolid>();
        }

        internal XbimSolidSet(IEnumerable<IXbimSolid> solids)
        {
            _solids = solids != null
                ? new List<IXbimSolid>(solids)
                : new List<IXbimSolid>();
        }

        /// <summary>
        /// Creates a solid set by extracting solids from a compound shape.
        /// </summary>
        internal XbimSolidSet(XbimShape compoundShape)
        {
            _solids = new List<IXbimSolid>();
            if (compoundShape == null) return;

            var solidHandles = compoundShape.GetSubShapeHandles(Abstractions.XShapeType.Solid);
            foreach (var h in solidHandles)
                _solids.Add(new XbimSolid(h));
        }

        public int Count => _solids.Count;

        public IXbimSolid First => _solids.Count > 0
            ? _solids[0]
            : throw new InvalidOperationException("Solid set is empty.");

        public bool IsPolyhedron => _solids.Count > 0 && _solids.All(s => s.IsPolyhedron);

        public bool IsSimplified => false;

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimSolidSetType;
        public bool IsValid => _solids.Count > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_solids.Count == 0) return XbimRect3D.Empty;
                var result = _solids[0].BoundingBox;
                for (int i = 1; i < _solids.Count; i++)
                    result.Union(_solids[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public void Add(IXbimGeometryObject shape)
        {
            if (shape is IXbimSolid solid)
            {
                _solids.Add(solid);
            }
            else if (shape is IXbimSolidSet solidSet)
            {
                foreach (var s in solidSet)
                    _solids.Add(s);
            }
        }

        public IXbimSolidSet Range(int start, int count)
        {
            return new XbimSolidSet(_solids.GetRange(start, count));
        }

        public string ToBRep
        {
            get
            {
                if (_solids.Count == 0) return string.Empty;
                if (_solids.Count == 1) return _solids[0].ToBRep;
                throw new NotSupportedException("BRep serialization of multi-solid sets not yet supported.");
            }
        }

        public IXbimSolidSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Cut(toCut, tolerance, logger));

        public IXbimSolidSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Cut(toCut, tolerance, logger));

        public IXbimSolidSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Union(toUnion, tolerance, logger));

        public IXbimSolidSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Union(toUnion, tolerance, logger));

        public IXbimSolidSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Intersection(toIntersect, tolerance, logger));

        public IXbimSolidSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => ApplyToEach(s => s.Intersection(toIntersect, tolerance, logger));

        private XbimSolidSet ApplyToEach(Func<IXbimSolid, IXbimSolidSet> op)
        {
            var results = new List<IXbimSolid>();
            foreach (var solid in _solids)
            {
                var partial = op(solid);
                foreach (var r in partial)
                    results.Add(r);
            }
            return new XbimSolidSet(results);
        }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Solid set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Solid set transform not supported.");

        public IEnumerator<IXbimSolid> GetEnumerator() => _solids.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _solids.GetEnumerator();

        public void Dispose() { }
    }
}
