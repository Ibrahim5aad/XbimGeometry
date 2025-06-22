using System;
using System.Collections;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts an array of <see cref="IXbimFace"/> to the legacy <see cref="IXbimFaceSet"/> interface.
    /// </summary>
    internal class V5FaceSet : IXbimFaceSet
    {
        private readonly IXbimFace[] _faces;

        internal V5FaceSet(IXbimFace[] faces)
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
            => throw new NotSupportedException("Face set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Face set transform not supported.");

        public IEnumerator<IXbimFace> GetEnumerator() => ((IEnumerable<IXbimFace>)_faces).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _faces.GetEnumerator();

        public void Dispose() { }
    }
}
