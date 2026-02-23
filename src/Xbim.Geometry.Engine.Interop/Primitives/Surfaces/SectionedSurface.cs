using System.Runtime.InteropServices;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Represents a surface defined by cross-section profiles swept along a directrix curve.
    /// The underlying geometry is a sewn compound of ruled surface faces.
    /// </summary>
    internal class SectionedSurface : NativeOwner<NativeShapeHandle>, IXSectionedSurface
    {
        internal SectionedSurface(NativeShapeHandle shapeHandle, IXCurve directrix)
            : base(shapeHandle)
        {
            Directrix = directrix;
        }

        public XSurfaceType SurfaceType => XSurfaceType.IfcSectionedSurface;

        public bool IsUPeriodic => false;

        public bool IsVPeriodic => false;

        public IXCurve Directrix { get; }

        /// <summary>
        /// Returns the BRep representation of the sewn surface shape as a string.
        /// </summary>
        public string BrepString()
        {
            int result = XbimGeometryNativeApi.xbim_shape_to_brep_string(
                Handle, out IntPtr strPtr, out int strLen);

            if (result != 0 || strPtr == IntPtr.Zero)
                throw new XbimGeometryServiceException(
                    $"Failed to serialize surface to BRep: {XbimGeometryNativeApi.GetLastError()}");

            try
            {
                return Marshal.PtrToStringAnsi(strPtr, strLen);
            }
            finally
            {
                XbimGeometryNativeApi.xbim_string_free(strPtr);
            }
        }

        /// <summary>
        /// Writes the sewn surface shape to a .brep file.
        /// </summary>
        public void WriteBrep(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_brep(Handle, filePath);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to write BRep file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Writes the sewn surface shape to a binary STL file.
        /// </summary>
        public void WriteStl(string filePath)
        {
            int result = XbimGeometryNativeApi.xbim_shape_write_stl(Handle, filePath, 0.1);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to write STL file '{filePath}': {XbimGeometryNativeApi.GetLastError()}");
        }
    }
}
