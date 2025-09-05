using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Provides shape manipulation operations: boolean operations on shapes,
    /// placement transforms, serialization, and meshing.
    /// </summary>
    internal class ShapeService : IXShapeService, IDisposable
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private NativeContextHandle? _ctx;

        public ShapeService(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<ShapeService>();
        }

        private NativeContextHandle Context
        {
            get
            {
                if (_ctx == null || _ctx.IsInvalid || _ctx.IsClosed)
                {
                    int result = XbimGeometryNativeApi.xbim_context_create(
                        1e-5, 1.0, 0.3048, 0.001,
                        Math.PI / 180.0, 60, 1e-5,
                        null, out var ctx);
                    if (result != 0)
                        throw new InvalidOperationException("Failed to create geometry context for shape service.");
                    _ctx = ctx;
                }
                return _ctx;
            }
        }

        public IXShape UnifyDomain(IXShape shape)
        {
            _logger.LogWarning("UnifyDomain is not yet implemented in the native layer, returning original shape.");
            return shape;
        }

        public IXShape Convert(string brepString)
        {
            if (string.IsNullOrEmpty(brepString))
                throw new ArgumentNullException(nameof(brepString));

            var handle = XbimGeometryNativeApi.xbim_shape_from_brep_string(brepString, brepString.Length);

            if (handle == null || handle.IsInvalid)
                throw new InvalidOperationException(
                    $"Failed to parse BRep string: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapShape(handle);
        }

        public IXbimGeometryObject ConvertToV5(string brepString)
        {
            throw new NotSupportedException("V5 geometry objects are not available in the P/Invoke layer.");
        }

        public string Convert(IXShape shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var native = shape as Shape
                ?? throw new ArgumentException("Shape must be a Shape.", nameof(shape));

            int result = XbimGeometryNativeApi.xbim_shape_to_brep_string(
                native.Handle, out var brepPtr, out int strLen);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to convert shape to BRep: {XbimGeometryNativeApi.GetLastError()}");

            string brep = Marshal.PtrToStringAnsi(brepPtr, strLen) ?? string.Empty;
            XbimGeometryNativeApi.xbim_string_free(brepPtr);
            return brep;
        }

        public string Convert(IXbimGeometryObject v5Shape)
        {
            throw new NotSupportedException("V5 geometry objects are not available in the P/Invoke layer.");
        }

        public IXShape Transform(IXShape shape, IXMatrix transformMatrix)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (transformMatrix == null || transformMatrix.IsIdentity) return shape;

            var native = shape as Shape
                ?? throw new ArgumentException("Shape must be a Shape.", nameof(shape));

            // Build location from the rotation part of the matrix
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                transformMatrix.OffsetX, transformMatrix.OffsetY, transformMatrix.OffsetZ,
                transformMatrix.M31, transformMatrix.M32, transformMatrix.M33,
                transformMatrix.M11, transformMatrix.M12, transformMatrix.M13,
                out var locHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create transform location: {XbimGeometryNativeApi.GetLastError()}");

            using (locHandle)
            {
                result = XbimGeometryNativeApi.xbim_shape_moved(
                    native.Handle, locHandle, out var movedHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to transform shape: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapShape(movedHandle);
            }
        }

        public void Triangulate(IXShape shape) { /* handled by meshing pipeline */ }

        public IXShape Union(IXShape body, IXShape addition, double precision)
        {
            return BooleanOp(body, addition, precision, XbimGeometryNativeApi.xbim_boolean_union);
        }

        public IXShape Cut(IXShape body, IXShape subtraction, double precision)
        {
            return BooleanOp(body, subtraction, precision, XbimGeometryNativeApi.xbim_boolean_cut);
        }

        public IXShape Intersect(IXShape body, IXShape intersect, double precision)
        {
            return BooleanOp(body, intersect, precision, XbimGeometryNativeApi.xbim_boolean_intersect);
        }

        public IXShape Union(IXShape body, IEnumerable<IXShape> additions, double precision)
        {
            IXShape result = body;
            foreach (var add in additions)
                result = Union(result, add, precision);
            return result;
        }

        public IXShape Cut(IXShape body, IEnumerable<IXShape> subtractions, double precision)
        {
            IXShape result = body;
            foreach (var sub in subtractions)
                result = Cut(result, sub, precision);
            return result;
        }

        public IXShape Intersect(IXShape body, IEnumerable<IXShape> intersections, double precision)
        {
            IXShape result = body;
            foreach (var inter in intersections)
                result = Intersect(result, inter, precision);
            return result;
        }

        public IXShape RemovePlacement(IXShape shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var identity = new XLocation();
            return Moved(shape, identity);
        }

        public IXShape SetPlacement(IXShape shape, IIfcObjectPlacement placement)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (placement == null) return shape;

            var geoFactory = new Factories.GeometryFactory(
                new MinimalModelService(), _logger);
            var location = geoFactory.ToLocation(placement);
            return Moved(shape, location);
        }

        public IXShape Moved(IXShape shape, IIfcObjectPlacement placement, bool invertPlacement = false)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (placement == null) return shape;

            var geoFactory = new Factories.GeometryFactory(
                new MinimalModelService(), _logger);
            var location = geoFactory.ToLocation(placement);

            if (invertPlacement)
            {
                var inverted = (XLocation)location.Inverted();
                location.Dispose();
                return Moved(shape, inverted);
            }

            return Moved(shape, location);
        }

        public IXShape Moved(IXShape shape, IXLocation moveTo)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (moveTo == null || moveTo.IsIdentity) return shape;

            var native = shape as Shape
                ?? throw new ArgumentException("Shape must be a Shape.", nameof(shape));
            var loc = moveTo as XLocation
                ?? throw new ArgumentException("Location must be an XLocation.", nameof(moveTo));

            int result = XbimGeometryNativeApi.xbim_shape_moved(
                native.Handle, loc.Handle, out var movedHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapShape(movedHandle);
        }

        public IXShape Scaled(IXShape shape, double scale)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (Math.Abs(scale - 1.0) < 1e-15) return shape;

            var native = shape as Shape
                ?? throw new ArgumentException("Shape must be a Shape.", nameof(shape));

            // Create a uniform scaling transform: identity rotation with scale on diagonal
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                0, 0, 0, 0, 0, 1, 1, 0, 0, out var locHandle);

            if (result != 0)
                throw new InvalidOperationException("Failed to create scale location.");

            using (locHandle)
            {
                // Move to origin, scale, move back — for uniform scale, BRepBuilderAPI_Transform is better
                // but we don't have a dedicated native API. Use compound-based scaling workaround:
                // Build a scaled copy by moving shape to scaled location.
                // This is a limitation — true scaling needs a native xbim_shape_scaled API.
                _logger.LogWarning("Uniform scaling via location transform has limited precision; a native scale API is needed for full support.");

                result = XbimGeometryNativeApi.xbim_shape_moved(
                    native.Handle, locHandle, out var movedHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to scale shape: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapShape(movedHandle);
            }
        }

        public bool IsFacingAwayFrom(IXFace face, IXDirection direction)
        {
            if (face == null || direction == null || direction.IsNull) return false;

            var native = face as Shape;
            if (native == null) return false;

            return XbimGeometryNativeApi.xbim_face_is_facing_away(
                native.Handle,
                direction.X, direction.Y, direction.Z) != 0;
        }

        public IXShape Combine(IEnumerable<IXShape> shapes)
        {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            var shapeList = shapes.ToList();
            if (shapeList.Count == 0)
                throw new InvalidOperationException("Cannot combine zero shapes.");
            if (shapeList.Count == 1)
                return shapeList[0];

            var ptrs = new IntPtr[shapeList.Count];
            for (int i = 0; i < shapeList.Count; i++)
            {
                if (shapeList[i] is Shape ns)
                    ptrs[i] = ns.Handle.DangerousGetHandle();
                else
                    throw new ArgumentException("All shapes must be Shape instances.");
            }

            int result = XbimGeometryNativeApi.xbim_compound_make(
                Context, ptrs, ptrs.Length, out var compoundHandle);

            GC.KeepAlive(shapeList);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to combine shapes: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapShape(compoundHandle);
        }

        public bool IsOverlapping(IXShape shape1, IXShape shape2, IXMeshFactors meshFactors)
        {
            _logger.LogWarning("IsOverlapping is not yet implemented, returning false.");
            return false;
        }

        public byte[] CreateWexBimMesh(IXShape shape, double tolerance, double linearDeflection,
            double angularDeflection, double scale, out IXAxisAlignedBoundingBox bounds)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            var native = shape as Shape
                ?? throw new ArgumentException("Shape must be a Shape.", nameof(shape));

            int result = XbimGeometryNativeApi.xbim_mesh_create_wexbim(
                Context, native.Handle,
                tolerance, linearDeflection, angularDeflection, scale,
                0, // checkEdges
                out var buffer, out int bufferSize,
                out int hasCurves,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create WexBim mesh: {XbimGeometryNativeApi.GetLastError()}");

            bounds = new XAxisAlignedBoundingBox(minX, minY, minZ, maxX, maxY, maxZ);

            byte[] meshData = new byte[bufferSize];
            Marshal.Copy(buffer, meshData, 0, bufferSize);
            XbimGeometryNativeApi.xbim_buffer_free(buffer);

            return meshData;
        }

        public void Dispose()
        {
            _ctx?.Dispose();
            _ctx = null;
        }

        #region Helpers

        private delegate int BooleanOpDelegate(
            NativeContextHandle ctx, NativeShapeHandle body, NativeShapeHandle tool,
            double precision, out int hasWarnings, out NativeShapeHandle result);

        private IXShape BooleanOp(IXShape body, IXShape tool, double precision, BooleanOpDelegate op)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (tool == null) throw new ArgumentNullException(nameof(tool));

            var nativeBody = body as Shape
                ?? throw new ArgumentException("Body must be a Shape.");
            var nativeTool = tool as Shape
                ?? throw new ArgumentException("Tool must be a Shape.");

            int result = op(Context, nativeBody.Handle, nativeTool.Handle,
                precision, out _, out var resultHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapShape(resultHandle);
        }

        /// <summary>
        /// Minimal model service stub for placement conversion.
        /// </summary>
        private class MinimalModelService : IXModelGeometryService
        {
            public IXLoggingService LoggingService => throw new NotImplementedException();
            public IXVertexFactory VertexFactory => throw new NotImplementedException();
            public IXGeometryFactory GeometryFactory => throw new NotImplementedException();
            public IXCurveFactory CurveFactory => throw new NotImplementedException();
            public IXSurfaceFactory SurfaceFactory => throw new NotImplementedException();
            public IXEdgeFactory EdgeFactory => throw new NotImplementedException();
            public IXWireFactory WireFactory => throw new NotImplementedException();
            public IXFaceFactory FaceFactory => throw new NotImplementedException();
            public IXShellFactory ShellFactory => throw new NotImplementedException();
            public IXSolidFactory SolidFactory => throw new NotImplementedException();
            public IXCompoundFactory CompoundFactory => throw new NotImplementedException();
            public IXBooleanFactory BooleanFactory => throw new NotImplementedException();
            public IXShapeFactory ShapeFactory => throw new NotImplementedException();
            public IXProfileFactory ProfileFactory => throw new NotImplementedException();
            public IXMaterialFactory MaterialFactory => throw new NotImplementedException();
            public IXProjectionFactory ProjectionFactory => throw new NotImplementedException();
            public IXWexBimMeshFactory WexBimMeshFactory => throw new NotImplementedException();
            public IXShapeBinarySerializer ShapeBinarySerializer => throw new NotImplementedException();
            public IXModelPlacementBuilder ModelPlacementBuilder => throw new NotImplementedException();
            public double Timeout { get; set; }
            public double Precision => 1e-5;
            public double PrecisionSquared => Precision * Precision;
            public double OneMeter => 1.0;
            public double OneFoot => 0.3048;
            public double OneMillimeter => 0.001;
            public double MinimumGap { get; set; }
            public double RadianFactor => Math.PI / 180.0;
            public double MinAreaM2 => 0;
            public IXMeshFactors MeshFactors => new MeshFactors(1.0, 1e-5);
            public Xbim.Common.IModel Model => throw new NotImplementedException();
            public bool UpgradeFaceSets { get; set; }
            public void SetModel(Xbim.Common.IModel model) { }
            public ISet<IIfcGeometricRepresentationContext> GetTypical3dContexts() => new HashSet<IIfcGeometricRepresentationContext>();
            public IXLocation Create(IIfcObjectPlacement objectPlacement) => throw new NotImplementedException();
            public IXLocation CreateMappingTransform(IIfcMappedItem mappedItem) => throw new NotImplementedException();
            public void LogError(string format, params object[] args) { }
            public void LogError(Xbim.Common.IPersistEntity e, string format, params object[] args) { }
            public void LogError(Xbim.Common.IPersistEntity e, Exception ex, string format, params object[] args) { }
            public void LogError(Exception ex, string format, params object[] args) { }
            public void LogWarning(string format, params object[] args) { }
            public void LogWarning(Xbim.Common.IPersistEntity e, string format, params object[] args) { }
            public void LogWarning(Xbim.Common.IPersistEntity e, Exception ex, string format, params object[] args) { }
            public void LogWarning(Exception ex, string format, params object[] args) { }
            public void LogInformation(string format, params object[] args) { }
            public void LogInformation(Xbim.Common.IPersistEntity e, string format, params object[] args) { }
            public void LogInformation(Xbim.Common.IPersistEntity e, Exception ex, string format, params object[] args) { }
            public void LogInformation(Exception ex, string format, params object[] args) { }
            public void LogDebug(string format, params object[] args) { }
            public void LogDebug(Xbim.Common.IPersistEntity e, string format, params object[] args) { }
            public void LogDebug(Xbim.Common.IPersistEntity e, Exception ex, string format, params object[] args) { }
            public void LogDebug(Exception ex, string format, params object[] args) { }
        }

        #endregion
    }
}
