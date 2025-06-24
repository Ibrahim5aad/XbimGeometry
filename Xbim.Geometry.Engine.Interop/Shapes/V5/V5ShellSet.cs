using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts an array of <see cref="IXbimShell"/> to the legacy <see cref="IXbimShellSet"/> interface.
    /// </summary>
    internal class V5ShellSet : IXbimShellSet
    {
        private readonly IXbimShell[] _shells;

        internal V5ShellSet(IXbimShell[] shells)
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
            => throw new NotSupportedException("Shell set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Shell set transform not supported.");

        public void Add(IXbimGeometryObject shape)
            => throw new NotSupportedException("Shell set modification not supported.");

        public void Union(double tolerance)
            => throw new NotSupportedException("Shell set union not supported.");

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

        public IEnumerator<IXbimShell> GetEnumerator() => ((IEnumerable<IXbimShell>)_shells).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _shells.GetEnumerator();

        public void Dispose() { }
    }
}
