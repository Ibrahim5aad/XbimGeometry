using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Collection of shells implementing <see cref="IXbimShellSet"/>.
    /// </summary>
    internal class XbimShellSet : IXbimShellSet
    {
        private readonly IXbimShell[] _shells;

        internal XbimShellSet(IXbimShell[] shells)
        {
            _shells = shells ?? Array.Empty<IXbimShell>();
        }

        public int Count => _shells.Length;

        public IXbimShell First => _shells.Length > 0
            ? _shells[0]
            : throw new InvalidOperationException("Shell set is empty.");

        public bool IsPolyhedron => false;

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimShellSetType;
        public bool IsValid => _shells.Length > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_shells.Length == 0) return XbimRect3D.Empty;
                var result = _shells[0].BoundingBox;
                for (int i = 1; i < _shells.Length; i++)
                    result.Union(_shells[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            var transformed = _shells
                .Select(s => (IXbimShell)s.Transform(matrix3D)).ToArray();
            return new XbimShellSet(transformed);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            var transformed = _shells
                .Select(s => (IXbimShell)((XbimShape)s).TransformShallow(matrix3D)).ToArray();
            return new XbimShellSet(transformed);
        }

        public void Add(IXbimGeometryObject shape)
            => throw new NotSupportedException("Shell set modification not supported.");

        public void Union(double tolerance)
            => throw new NotSupportedException("Shell set union not supported.");

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

        public IEnumerator<IXbimShell> GetEnumerator() => ((IEnumerable<IXbimShell>)_shells).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _shells.GetEnumerator();

        public void Dispose() { }
    }
}
