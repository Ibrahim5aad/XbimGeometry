using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Collection of solids that can be used as a single solid, a compound, or a solid set.
    /// Implements <see cref="IXbimSolidSet"/>, <see cref="IXbimSolid"/>,
    /// <see cref="IXSolid"/>, and <see cref="IXCompound"/>.
    /// </summary>
    internal class XbimSolidSet : IXbimSolidSet, IXbimSolid, IXSolid, IXCompound
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

            var solidHandles = compoundShape.GetSubShapeHandles(XShapeType.Solid);
            foreach (var h in solidHandles)
                _solids.Add(new XbimSolid(h));
        }

        #region IXbimSolidSet

        public int Count => _solids.Count;

        public IXbimSolid First => _solids.Count > 0
            ? _solids[0]
            : throw new InvalidOperationException("Solid set is empty.");

        public bool IsSimplified => false;

        public void Add(IXbimGeometryObject shape)
        {
            if (shape is IXbimSolid solid)
                _solids.Add(solid);
            else if (shape is IXbimSolidSet solidSet)
            {
                foreach (var s in solidSet)
                    _solids.Add(s);
            }
        }

        public IXbimSolidSet Range(int start, int count)
            => new XbimSolidSet(_solids.GetRange(start, count));

        public IEnumerator<IXbimSolid> GetEnumerator() => _solids.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _solids.GetEnumerator();

        #endregion

        #region IXbimSolid

        public double SurfaceArea => _solids.Sum(s => s.SurfaceArea);

        public bool IsPolyhedron => _solids.Count > 0 && _solids.All(s => s.IsPolyhedron);

        IXbimShellSet IXbimSolid.Shells
            => new XbimShellSet(_solids.SelectMany(s => s.Shells).ToArray());

        IXbimFaceSet IXbimSolid.Faces
            => new XbimFaceSet(_solids.SelectMany(s => s.Faces).ToArray());

        IXbimEdgeSet IXbimSolid.Edges
            => new XbimEdgeSet(_solids.SelectMany(s => s.Edges).ToArray());

        IXbimVertexSet IXbimSolid.Vertices
            => new XbimVertexSet(_solids.SelectMany(s => s.Vertices).ToArray());

        public IXbimSolidSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_cut_multi,
                new[] { toCut }, tolerance);

        public IXbimSolidSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_cut_multi,
                toCut, tolerance);

        public IXbimSolidSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_union_multi,
                new[] { toUnion }, tolerance);

        public IXbimSolidSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_union_multi,
                toUnion, tolerance);

        public IXbimSolidSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_intersect_multi,
                new[] { toIntersect }, tolerance);

        public IXbimSolidSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => PerformBatchBoolean(XbimGeometryNativeApi.xbim_boolean_intersect_multi,
                toIntersect, tolerance);

        public IXbimFaceSet Section(IXbimFace toSection, double tolerance, ILogger logger = null)
        {
            var faces = _solids
                .SelectMany(s => (IEnumerable<IXbimFace>)s.Section(toSection, tolerance, logger))
                .ToArray();
            return new XbimFaceSet(faces);
        }

        public void SaveAsBrep(string fileName)
        {
            if (_solids.Count == 1)
                _solids[0].SaveAsBrep(fileName);
            else
                throw new NotSupportedException(
                    "BRep serialization of multi-solid sets is not supported.");
        }

        public string ToBRep
        {
            get
            {
                if (_solids.Count == 0) return string.Empty;
                if (_solids.Count == 1) return _solids[0].ToBRep;
                throw new NotSupportedException(
                    "BRep serialization of multi-solid sets is not supported.");
            }
        }

        public bool Equals(IXbimSolid other) => ReferenceEquals(this, other);

        #endregion

        #region IXSolid

        public double Volume => _solids.OfType<IXSolid>().Sum(s => s.Volume);

        IXShell[] IXSolid.Shells
            => _solids.OfType<IXSolid>().SelectMany(s => s.Shells).ToArray();

        #endregion

        #region IXCompound

        public bool IsSolidsOnly => true;
        public bool IsCompoundsOnly => false;
        public bool IsShellsOnly => false;
        public bool IsFacesOnly => false;
        public bool IsWiresOnly => false;
        public bool IsEdgesOnly => false;

        public bool HasSolids => _solids.Count > 0;
        public bool HasCompounds => false;
        public bool HasShells => false;
        public bool HasFaces => false;
        public bool HasWires => false;
        public bool HasEdges => false;
        public bool HasVertices => false;

        public IXSolid[] Solids => _solids.Cast<IXSolid>().ToArray();
        public IXCompound[] Compounds => Array.Empty<IXCompound>();
        IXShell[] IXCompound.Shells => Array.Empty<IXShell>();
        IXFace[] IXCompound.Faces => Array.Empty<IXFace>();
        IXWire[] IXCompound.Wires => Array.Empty<IXWire>();
        IXEdge[] IXCompound.Edges => Array.Empty<IXEdge>();
        IXVertex[] IXCompound.Vertices => Array.Empty<IXVertex>();

        public void Add(IXShape shape)
        {
            if (shape is IXbimSolid solid)
                _solids.Add(solid);
            else
                throw new ArgumentException(
                    "Only solids can be added to a solid set.", nameof(shape));
        }

        #endregion

        #region IXShape

        public XShapeType ShapeType => XShapeType.Compound;

        public bool IsClosed => _solids.All(s => s is XbimShape xs && xs.IsClosed);

        public XOrientation Orientation => XOrientation.Forward;

        public IXLocation Location
            => throw new NotSupportedException(
                "Location is not available on a managed solid set.");

        public IXAxisAlignedBoundingBox Bounds()
            => throw new NotSupportedException(
                "Bounds is not available on a managed solid set.");

        public string BrepString()
        {
            if (_solids.Count == 1)
                return ((XbimShape)_solids[0]).BrepString();
            throw new NotSupportedException(
                "BRep serialization of multi-solid sets is not supported.");
        }

        public void WriteBrep(string filePath)
        {
            if (_solids.Count == 1)
                ((XbimShape)_solids[0]).WriteBrep(filePath);
            else
                throw new NotSupportedException(
                    "BRep serialization of multi-solid sets is not supported.");
        }

        public void WriteStl(string filePath)
            => throw new NotSupportedException(
                "STL export is not available on a managed solid set.");

        public bool IsValidShape()
            => _solids.Count > 0
            && _solids.All(s => s is XbimShape xs && xs.IsValidShape());

        public bool IsEmptyShape() => _solids.Count == 0;

        public bool Triangulate(IXMeshFactors meshFactors)
            => throw new NotSupportedException(
                "Triangulation is not available on a managed solid set.");

        public IEnumerable<IXFace> AllFaces()
            => _solids.OfType<XbimShape>().SelectMany(s => s.AllFaces());

        public bool IsEqual(IXShape other) => ReferenceEquals(this, other);

        public int ShapeHashCode() => GetHashCode();

        #endregion

        #region IXbimGeometryObject

        public XbimGeometryObjectType GeometryType
            => XbimGeometryObjectType.XbimSolidSetType;

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

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Solid set transform not supported.");

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
            => throw new NotSupportedException("Solid set transform not supported.");

        public void Dispose() { }

        #endregion

        #region Helpers

        private delegate int MultiBooleanOp(
            NativeContextHandle ctx,
            IntPtr[] bodyHandles, int bodyCount,
            IntPtr[] toolHandles, int toolCount,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        private XbimSolidSet PerformBatchBoolean(
            MultiBooleanOp op, IEnumerable<IXbimSolid> tools, double tolerance)
        {
            var bodyHandles = _solids.Cast<XbimShape>()
                .Select(s => (SafeHandle)s.Handle).ToArray();
            var toolHandles = tools.Cast<XbimShape>()
                .Select(s => (SafeHandle)s.Handle).ToArray();

            using var bodies = new NativeHandleArray(bodyHandles);
            using var toolsArr = new NativeHandleArray(toolHandles);

            int result = op(
                NativeContextHandle.NullHandle,
                bodies.Ptrs, bodies.Length,
                toolsArr.Ptrs, toolsArr.Length,
                tolerance,
                out _, out var outHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (XbimShape)NativeShapeWrapper.WrapShape(outHandle);
            return new XbimSolidSet(shape);
        }

        #endregion
    }
}
