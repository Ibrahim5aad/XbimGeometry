using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXCompound"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Compound.
    /// </summary>
    internal class NativeCompound : NativeShape, IXCompound
    {
        internal NativeCompound(NativeShapeHandle handle) : base(handle)
        {
        }

        // Shape content queries - require traversal API (TOPO-008)
        public bool IsCompoundsOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool IsSolidsOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool IsShellsOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool IsFacesOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool IsWiresOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool IsEdgesOnly => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");

        public bool HasCompounds => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasSolids => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasShells => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasFaces => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasWires => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasEdges => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public bool HasVertices => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");

        // Child shape collections - require traversal API (TOPO-008)
        public IXCompound[] Compounds => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXSolid[] Solids => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXShell[] Shells => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXFace[] Faces => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXWire[] Wires => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXEdge[] Edges => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");
        public IXVertex[] Vertices => throw new NotImplementedException("Shape traversal will be available after TOPO-008.");

        public void Add(IXShape shape)
        {
            // Compound modification not yet available in native API (BOOL-005)
            throw new NotImplementedException("Compound modification will be available after BOOL-005.");
        }
    }
}
