using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Shapes
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
        {
            if (_items.Count == 0) return false;

            var handleList = new List<SafeHandle>();
            foreach (var item in _items)
                CollectHandles(item, handleList);
            if (handleList.Count == 0) return false;

            using var handles = new NativeHandleArray(handleList.ToArray());
            int result = XbimGeometryNativeApi.xbim_compound_sew(
                NativeContextHandle.NullHandle,
                handles.Ptrs, handles.Length,
                1e-6,
                out var outHandle);

            if (result != 0) return false;

            _items.Clear();
            var shape = NativeShapeWrapper.WrapShape(outHandle);
            if (shape is XbimCompound compound)
            {
                foreach (var child in compound.GetDirectChildren())
                {
                    if (child is IXbimGeometryObject geo)
                        _items.Add(geo);
                }
            }
            else if (shape is IXbimGeometryObject singleShape)
            {
                _items.Add(singleShape);
            }
            return true;
        }

        public string ToBRep
        {
            get
            {
                if (_items.Count == 0) return string.Empty;

                var handleList = new List<SafeHandle>();
                foreach (var item in _items)
                    CollectHandles(item, handleList);
                if (handleList.Count == 0) return string.Empty;

                if (handleList.Count == 1 && _items[0] is XbimShape xs)
                    return xs.BrepString();

                using var handles = new NativeHandleArray(handleList.ToArray());
                int result = XbimGeometryNativeApi.xbim_compound_make(
                    NativeContextHandle.NullHandle,
                    handles.Ptrs, handles.Length,
                    out var compoundHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to create compound: {XbimGeometryNativeApi.GetLastError()}");

                using (compoundHandle)
                {
                    result = XbimGeometryNativeApi.xbim_shape_to_brep_string(
                        compoundHandle, out IntPtr strPtr, out int strLen);
                    if (result != 0 || strPtr == IntPtr.Zero)
                        throw new XbimGeometryServiceException(
                            $"Failed to serialize to BRep: {XbimGeometryNativeApi.GetLastError()}");
                    try { return Marshal.PtrToStringAnsi(strPtr, strLen)!; }
                    finally { XbimGeometryNativeApi.xbim_string_free(strPtr); }
                }
            }
        }

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
        {
            var transformed = _items.Select(i => i.Transform(matrix3D)).ToList();
            return new XbimGeometryObjectSet(transformed);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D)
        {
            var transformed = _items.Select(i => i.TransformShallow(matrix3D)).ToList();
            return new XbimGeometryObjectSet(transformed);
        }

        public IXbimGeometryObjectSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_cut_multi, toCut, tolerance);

        public IXbimGeometryObjectSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_cut_multi,
                new[] { toCut }, tolerance);

        public IXbimGeometryObjectSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_union_multi, toUnion, tolerance);

        public IXbimGeometryObjectSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_union_multi,
                new[] { toUnion }, tolerance);

        public IXbimGeometryObjectSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_intersect_multi,
                toIntersect, tolerance);

        public IXbimGeometryObjectSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_intersect_multi,
                new[] { toIntersect }, tolerance);

        private delegate int MultiBooleanOp(
            NativeContextHandle ctx,
            IntPtr[] bodyHandles, int bodyCount,
            IntPtr[] toolHandles, int toolCount,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        private XbimGeometryObjectSet PerformBoolean(
            MultiBooleanOp op, IEnumerable<IXbimGeometryObject> tools, double tolerance)
        {
            var bodyHandleList = new List<SafeHandle>();
            foreach (var item in _items)
                CollectHandles(item, bodyHandleList);

            var toolHandleList = new List<SafeHandle>();
            foreach (var tool in tools)
                CollectHandles(tool, toolHandleList);

            if (bodyHandleList.Count == 0)
                return new XbimGeometryObjectSet();

            using var bodies = new NativeHandleArray(bodyHandleList.ToArray());
            using var toolsArr = new NativeHandleArray(toolHandleList.ToArray());

            int result = op(
                NativeContextHandle.NullHandle,
                bodies.Ptrs, bodies.Length,
                toolsArr.Ptrs, toolsArr.Length,
                tolerance,
                out _, out var outHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            return DecomposeResult(outHandle);
        }

        private static void CollectHandles(IXbimGeometryObject obj, List<SafeHandle> handles)
        {
            if (obj is XbimShape shape)
            {
                handles.Add(shape.Handle);
            }
            else if (obj is IXbimSolidSet solidSet)
            {
                foreach (var solid in solidSet)
                    CollectHandles(solid, handles);
            }
            else if (obj is IEnumerable<IXbimGeometryObject> set)
            {
                foreach (var item in set)
                    CollectHandles(item, handles);
            }
        }

        private static XbimGeometryObjectSet DecomposeResult(NativeShapeHandle handle)
        {
            var shape = NativeShapeWrapper.WrapShape(handle);
            var resultSet = new XbimGeometryObjectSet();

            if (shape is XbimCompound compound)
            {
                foreach (var child in compound.GetDirectChildren())
                {
                    if (child is IXbimGeometryObject geo)
                        resultSet.Add(geo);
                }
            }
            else if (shape is IXbimGeometryObject singleShape)
            {
                resultSet.Add(singleShape);
            }

            return resultSet;
        }

        public IEnumerator<IXbimGeometryObject> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        public void Dispose() { }
    }
}
