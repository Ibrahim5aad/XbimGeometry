using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Collection of shells implementing <see cref="IXbimShellSet"/>.
    /// </summary>
    internal class XbimShellSet : IXbimShellSet
    {
        private readonly List<IXbimShell> _shells;

        internal XbimShellSet(IXbimShell[] shells)
        {
            _shells = shells != null
                ? new List<IXbimShell>(shells)
                : new List<IXbimShell>();
        }

        public int Count => _shells.Count;

        public IXbimShell First => _shells.Count > 0
            ? _shells[0]
            : throw new InvalidOperationException("Shell set is empty.");

        public bool IsPolyhedron => false;

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimShellSetType;
        public bool IsValid => _shells.Count > 0;
        public bool IsSet => true;

        public XbimRect3D BoundingBox
        {
            get
            {
                if (_shells.Count == 0) return XbimRect3D.Empty;
                var result = _shells[0].BoundingBox;
                for (int i = 1; i < _shells.Count; i++)
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
        {
            if (shape is IXbimShell shell)
                _shells.Add(shell);
            else if (shape is IXbimShellSet shellSet)
            {
                foreach (var s in shellSet)
                    _shells.Add(s);
            }
            else if (shape is IXbimSolid solid)
            {
                foreach (var s in solid.Shells)
                    _shells.Add(s);
            }
        }

        public void Union(double tolerance)
        {
            if (_shells.Count < 2) return;

            var bodyHandles = new SafeHandle[] { ((XbimShape)_shells[0]).Handle };
            var toolHandleArr = _shells.Skip(1).Cast<XbimShape>()
                .Select(s => (SafeHandle)s.Handle).ToArray();

            using var bodies = new NativeHandleArray(bodyHandles);
            using var tools = new NativeHandleArray(toolHandleArr);

            int result = XbimGeometryNativeApi.xbim_boolean_union_multi(
                NativeContextHandle.NullHandle,
                bodies.Ptrs, bodies.Length,
                tools.Ptrs, tools.Length,
                tolerance,
                out _, out var outHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Shell union failed: {XbimGeometryNativeApi.GetLastError()}");

            _shells.Clear();
            var shape = NativeShapeWrapper.WrapShape(outHandle);
            if (shape is XbimCompound compound)
            {
                foreach (var child in compound.GetDirectChildren())
                {
                    if (child is IXbimShell childShell)
                        _shells.Add(childShell);
                }
            }
            else if (shape is IXbimShell resultShell)
            {
                _shells.Add(resultShell);
            }
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

        public IEnumerator<IXbimShell> GetEnumerator() => _shells.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _shells.GetEnumerator();

        public void Dispose() { }

        #region Helpers

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
            var bodyHandleArr = _shells.Cast<XbimShape>()
                .Select(s => (SafeHandle)s.Handle).ToArray();
            var toolHandleList = new List<SafeHandle>();
            foreach (var tool in tools)
            {
                if (tool is XbimShape xs)
                    toolHandleList.Add(xs.Handle);
                else if (tool is IXbimSolidSet solidSet)
                    foreach (var solid in solidSet)
                        if (solid is XbimShape ss)
                            toolHandleList.Add(ss.Handle);
            }

            if (bodyHandleArr.Length == 0)
                return new XbimGeometryObjectSet();

            using var bodies = new NativeHandleArray(bodyHandleArr);
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

            var shape = NativeShapeWrapper.WrapShape(outHandle);
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

        #endregion
    }
}
