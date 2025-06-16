using System;
using System.Collections.Generic;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Base implementation of <see cref="IXShape"/> backed by a <see cref="NativeShapeHandle"/>.
    /// All shape type queries (type, validity, bounds, etc.) are delegated to native P/Invoke calls.
    /// </summary>
    internal class Shape : IXShape
    {
        private NativeShapeHandle _handle;
        private XShapeType? _cachedType;

        internal Shape(NativeShapeHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        internal NativeShapeHandle Handle =>
            _handle ?? throw new ObjectDisposedException(nameof(Shape));

        /// <summary>
        /// Transfers ownership of the underlying handle out of this shape.
        /// After calling this, the shape wrapper no longer owns or disposes the handle.
        /// Used by <see cref="Factories.BooleanFactory"/> to pass intermediate
        /// operand handles to native boolean operations without double-free.
        /// </summary>
        internal NativeShapeHandle TakeHandle()
        {
            var h = _handle ?? throw new ObjectDisposedException(nameof(Shape));
            _handle = null!;
            _disposed = true;
            return h;
        }

        public XShapeType ShapeType
        {
            get
            {
                if (_cachedType.HasValue)
                    return _cachedType.Value;

                int result = XbimGeometryNativeApi.xbim_shape_type(Handle, out int typeVal);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get shape type: {XbimGeometryNativeApi.GetLastError()}");

                _cachedType = (XShapeType)typeVal;
                return _cachedType.Value;
            }
        }

        public IXAxisAlignedBoundingBox Bounds()
        {
            int result = XbimGeometryNativeApi.xbim_shape_bounding_box(
                Handle,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (result != 0)
                return XAxisAlignedBoundingBox.Void;

            return new XAxisAlignedBoundingBox(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public string BrepString()
        {
            return ShapeBinarySerializer.ToBrep(this);
        }

        /// <summary>
        /// Writes this shape to a .brep file in OCCT ASCII BRep format.
        /// Useful for debugging and visual inspection in CAD Assistant or FreeCAD.
        /// </summary>
        public void WriteBrep(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_brep(Handle, filePath);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to write BRep file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }

        public bool IsValidShape()
        {
            // xbim_shape_is_valid returns 1 for valid, 0 for invalid, negative for error
            int result = XbimGeometryNativeApi.xbim_shape_is_valid(Handle);
            return result == 1;
        }

        public bool IsClosed
        {
            get
            {
                // xbim_shape_is_closed returns 1 for closed, 0 for not closed, negative for error
                int result = XbimGeometryNativeApi.xbim_shape_is_closed(Handle);
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
            int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                Handle, (int)XShapeType.Face, out int count);
            if (countResult != 0 || count == 0)
                return Array.Empty<IXFace>();

            var ptrs = new IntPtr[count];
            int capacity = count;
            int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                Handle, (int)XShapeType.Face, ptrs, ref capacity);
            if (getResult != 0)
                return Array.Empty<IXFace>();

            var faces = new IXFace[capacity];
            for (int i = 0; i < capacity; i++)
                faces[i] = new Face(NativeShapeHandle.FromIntPtr(ptrs[i]));
            return faces;
        }

        /// <summary>
        /// Extracts sub-shapes of the given type from the underlying shape handle.
        /// Each returned handle is owned by the caller.
        /// </summary>
        internal NativeShapeHandle[] GetSubShapeHandles(XShapeType subType)
        {
            int countResult = XbimGeometryNativeApi.xbim_shape_count_subshapes(
                Handle, (int)subType, out int count);
            if (countResult != 0 || count == 0)
                return Array.Empty<NativeShapeHandle>();

            var ptrs = new IntPtr[count];
            int capacity = count;
            int getResult = XbimGeometryNativeApi.xbim_shape_get_subshapes(
                Handle, (int)subType, ptrs, ref capacity);
            if (getResult != 0)
                return Array.Empty<NativeShapeHandle>();

            var handles = new NativeShapeHandle[capacity];
            for (int i = 0; i < capacity; i++)
                handles[i] = NativeShapeHandle.FromIntPtr(ptrs[i]);
            return handles;
        }

        public bool IsEqual(IXShape other)
        {
            if (other is Shape ns)
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
