using System;
using System.Linq;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXCompound"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Compound.
    /// </summary>
    internal class XbimCompound : XbimShape, IXCompound
    {
        private IXShape[]? _children;

        internal XbimCompound(NativeShapeHandle handle) : base(handle)
        {
        }

        private IXShape[] GetDirectChildren()
        {
            if (_children != null)
                return _children;

            int result = XbimGeometryNativeApi.xbim_compound_child_count(Handle, out int count);
            if (result != 0 || count == 0)
            {
                _children = Array.Empty<IXShape>();
                return _children;
            }

            var ptrs = new IntPtr[count];
            int capacity = count;
            int getResult = XbimGeometryNativeApi.xbim_compound_get_children(Handle, ptrs, ref capacity);
            if (getResult != 0)
            {
                _children = Array.Empty<IXShape>();
                return _children;
            }

            var children = new IXShape[capacity];
            for (int i = 0; i < capacity; i++)
                children[i] = NativeShapeWrapper.WrapShape(NativeShapeHandle.FromIntPtr(ptrs[i]));

            _children = children;
            return _children;
        }

        private T[] ChildrenOfType<T>() where T : IXShape
        {
            return GetDirectChildren().OfType<T>().ToArray();
        }

        // Shape content queries
        public bool IsCompoundsOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimCompound);
        public bool IsSolidsOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimSolid);
        public bool IsShellsOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimShell);
        public bool IsFacesOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimFace);
        public bool IsWiresOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimWire);
        public bool IsEdgesOnly => GetDirectChildren().Length > 0 && GetDirectChildren().All(c => c is XbimEdge);

        public bool HasCompounds => GetDirectChildren().Any(c => c is XbimCompound);
        public bool HasSolids => GetDirectChildren().Any(c => c is XbimSolid);
        public bool HasShells => GetDirectChildren().Any(c => c is XbimShell);
        public bool HasFaces => GetDirectChildren().Any(c => c is XbimFace);
        public bool HasWires => GetDirectChildren().Any(c => c is XbimWire);
        public bool HasEdges => GetDirectChildren().Any(c => c is XbimEdge);
        public bool HasVertices => GetDirectChildren().Any(c => c is XbimVertex);

        // Child shape collections
        public IXCompound[] Compounds => ChildrenOfType<IXCompound>();
        public IXSolid[] Solids => ChildrenOfType<IXSolid>();
        public IXShell[] Shells => ChildrenOfType<IXShell>();
        public IXFace[] Faces => ChildrenOfType<IXFace>();
        public IXWire[] Wires => ChildrenOfType<IXWire>();
        public IXEdge[] Edges => ChildrenOfType<IXEdge>();
        public IXVertex[] Vertices => ChildrenOfType<IXVertex>();

        public void Add(IXShape shape)
        {
            if (shape is not XbimShape native)
                throw new ArgumentException("Shape must be a native shape.", nameof(shape));

            int result = XbimGeometryNativeApi.xbim_compound_add(Handle, native.Handle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to add shape to compound: {XbimGeometryNativeApi.GetLastError()}");

            _children = null; // invalidate cache
        }
    }
}
