using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Shapes
{
    /// <summary>
    /// Collection of wires implementing <see cref="IXbimWireSet"/>.
    /// </summary>
    internal class XbimWireSet : IXbimWireSet
    {
        private readonly IXbimWire[] _wires;

        internal XbimWireSet(IXbimWire[] wires)
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

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_wires.Length == 0) return XbimRect3D.Empty;
                var result = _wires[0].BoundingBox;
                for (int i = 1; i < _wires.Length; i++)
                    result.Union(_wires[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            var transformed = _wires
                .Select(w => (IXbimWire)w.Transform(matrix3D)).ToArray();
            return new XbimWireSet(transformed);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            var transformed = _wires
                .Select(w => (IXbimWire)((XbimShape)w).TransformShallow(matrix3D)).ToArray();
            return new XbimWireSet(transformed);
        }

        public IEnumerator<IXbimWire> GetEnumerator() => ((IEnumerable<IXbimWire>)_wires).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _wires.GetEnumerator();

        public void Dispose() { }
    }
}
