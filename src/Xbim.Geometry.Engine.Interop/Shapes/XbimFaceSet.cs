using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Collection of faces implementing <see cref="IXbimFaceSet"/>.
    /// </summary>
    internal class XbimFaceSet : IXbimFaceSet
    {
        private readonly IXbimFace[] _faces;

        internal XbimFaceSet(IXbimFace[] faces)
        {
            _faces = faces ?? Array.Empty<IXbimFace>();
        }

        public int Count => _faces.Length;

        public IXbimFace First => _faces.Length > 0
            ? _faces[0]
            : throw new InvalidOperationException("Face set is empty.");

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimFaceSetType;
        public bool IsValid => _faces.Length > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_faces.Length == 0) return XbimRect3D.Empty;
                var result = _faces[0].BoundingBox;
                for (int i = 1; i < _faces.Length; i++)
                    result.Union(_faces[i].BoundingBox);
                return result;
            }
        }

        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            var transformed = _faces
                .Select(f => (IXbimFace)f.Transform(matrix3D)).ToArray();
            return new XbimFaceSet(transformed);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            var transformed = _faces
                .Select(f => (IXbimFace)((XbimShape)f).TransformShallow(matrix3D)).ToArray();
            return new XbimFaceSet(transformed);
        }

        public IEnumerator<IXbimFace> GetEnumerator() => ((IEnumerable<IXbimFace>)_faces).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _faces.GetEnumerator();

        public void Dispose() { }
    }
}
