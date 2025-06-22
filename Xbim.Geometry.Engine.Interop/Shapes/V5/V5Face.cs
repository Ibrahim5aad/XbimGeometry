using System;
using System.Linq;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Face"/> to the legacy <see cref="IXbimFace"/> interface.
    /// </summary>
    internal class V5Face : V5Shape, IXbimFace, IEquatable<IXbimFace>
    {
        private readonly Face _face;

        internal V5Face(Face face) : base(face)
        {
            _face = face;
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimFaceType;

        public double Area => _face.Area;

        public double Perimeter
        {
            get
            {
                throw new NotSupportedException("Face perimeter not yet supported.");
            }
        }

        public XbimVector3D Normal
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_face_normal(
                    _face.Handle, 0.5, 0.5,
                    out double nx, out double ny, out double nz);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get face normal: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimVector3D(nx, ny, nz);
            }
        }

        public bool IsPlanar => false; // Conservative default

        public XbimPoint3D Location
        {
            get
            {
                // Use the centroid of the face bounding box as a location point
                var bb = Inner.Bounds();
                if (bb.IsVoid)
                    return new XbimPoint3D(0, 0, 0);
                var c = bb.Centroid;
                return new XbimPoint3D(c.X, c.Y, c.Z);
            }
        }

        public IXbimWire OuterBound
        {
            get
            {
                var wire = _face.OuterBound;
                return new V5Wire((Wire)wire);
            }
        }

        public IXbimWireSet InnerBounds
        {
            get
            {
                var inners = _face.InnerBounds;
                var v5Wires = inners.Select(w => (IXbimWire)new V5Wire((Wire)w)).ToArray();
                return new V5WireSet(v5Wires);
            }
        }

        public void SaveAsBrep(string fileName) => ((Shape)Inner).WriteBrep(fileName);

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimFace other)
        {
            if (other is V5Face v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }
    }
}
