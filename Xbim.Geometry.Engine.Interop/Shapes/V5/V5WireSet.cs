using System;
using System.Collections;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts an array of <see cref="IXbimWire"/> to the legacy <see cref="IXbimWireSet"/> interface.
    /// </summary>
    internal class V5WireSet : IXbimWireSet
    {
        private readonly IXbimWire[] _wires;

        internal V5WireSet(IXbimWire[] wires)
        {
            _wires = wires ?? Array.Empty<IXbimWire>();
        }

        public int Count => _wires.Length;

        public IXbimWire First => _wires.Length > 0
            ? _wires[0]
            : throw new InvalidOperationException("Wire set is empty.");

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimWireSetType;
        public bool IsValid => _wires.Length > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox => XbimRect3D.Empty;

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Wire set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Wire set transform not supported.");

        public IEnumerator<IXbimWire> GetEnumerator() => ((IEnumerable<IXbimWire>)_wires).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _wires.GetEnumerator();

        public void Dispose() { }
    }
}
