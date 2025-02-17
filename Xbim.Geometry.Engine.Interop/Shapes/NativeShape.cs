using System;
using System.Collections.Generic;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Base implementation of <see cref="IXShape"/> backed by a <see cref="NativeShapeHandle"/>.
    /// All shape type queries (type, validity, bounds, etc.) are delegated to native P/Invoke calls.
    /// </summary>
    internal class NativeShape : IXShape
    {
        private NativeShapeHandle _handle;
        private XShapeType? _cachedType;

        internal NativeShape(NativeShapeHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        internal NativeShapeHandle Handle =>
            _handle ?? throw new ObjectDisposedException(nameof(NativeShape));

        public XShapeType ShapeType
        {
            get
            {
                if (_cachedType.HasValue)
                    return _cachedType.Value;

                int result = NativeMethods.xbim_shape_type(Handle, out int typeVal);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get shape type: {NativeMethods.GetLastError()}");

                _cachedType = (XShapeType)typeVal;
                return _cachedType.Value;
            }
        }

        public IXAxisAlignedBoundingBox Bounds()
        {
            int result = NativeMethods.xbim_shape_bounding_box(
                Handle,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (result != 0)
                return XAxisAlignedBoundingBox.Void;

            return new XAxisAlignedBoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public string BrepString()
        {
            // BRep serialization not yet available in native API (MESH-002)
            throw new NotImplementedException(
                "BRep string serialization will be available after MESH-002.");
        }

        public bool IsValidShape()
        {
            // xbim_shape_is_valid returns 1 for valid, 0 for invalid, negative for error
            int result = NativeMethods.xbim_shape_is_valid(Handle);
            return result == 1;
        }

        public bool IsClosed
        {
            get
            {
                // xbim_shape_is_closed returns 1 for closed, 0 for not closed, negative for error
                int result = NativeMethods.xbim_shape_is_closed(Handle);
                return result == 1;
            }
        }

        public bool IsEmptyShape()
        {
            return Handle.IsInvalid;
        }

        public bool Triangulate(IXMeshFactors meshFactors)
        {
            // Triangulation not yet available in native API (MESH-001)
            throw new NotImplementedException(
                "Triangulation will be available after MESH-001.");
        }

        public IXLocation Location
        {
            get
            {
                // Shape location extraction not yet available in native API.
                // Return identity for now - the actual location is typically
                // managed at a higher level via IIfcObjectPlacement.
                return new XLocation();
            }
        }

        public IEnumerable<IXFace> AllFaces()
        {
            // Shape traversal not yet available in native API (TOPO-008)
            throw new NotImplementedException(
                "Shape traversal will be available after TOPO-008.");
        }

        public bool IsEqual(IXShape other)
        {
            if (other is NativeShape ns)
                return Handle.DangerousGetHandle() == ns.Handle.DangerousGetHandle();
            return false;
        }

        public int ShapeHashCode()
        {
            return Handle.DangerousGetHandle().GetHashCode();
        }

        public XOrientation Orientation => XOrientation.Forward;

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _handle?.Dispose();
                _handle = null!;
            }
        }

        #endregion
    }
}
