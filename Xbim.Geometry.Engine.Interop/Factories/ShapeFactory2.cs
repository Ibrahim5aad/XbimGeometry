using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Provides shape conversion, transformation, and boolean operations for
    /// geometric representation items. Delegates BRep serialization to
    /// <see cref="ShapeBinarySerializer"/> and boolean operations to
    /// <see cref="BooleanFactory"/>.
    /// </summary>
    internal class ShapeFactory2 : IXShapeFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public ShapeFactory2(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        #region BRep Conversion

        public IXShape Convert(string shape)
        {
            return ShapeBinarySerializer.FromBrep(shape);
        }

        public string Convert(IXShape shape)
        {
            if (shape is Shape ns)
                return ShapeBinarySerializer.ToBrep(ns);

            throw new ArgumentException("Shape must be a Shape instance.", nameof(shape));
        }

        public string Convert(IXbimGeometryObject shape)
        {
            if (shape is Shape ns)
                return ShapeBinarySerializer.ToBrep(ns);

            throw new ArgumentException("Shape must be a Shape instance.", nameof(shape));
        }

        #endregion

        #region Build

        public IXShape Build(IIfcGeometricRepresentationItem geomRep)
        {
            // Dispatch to appropriate factory based on IFC type
            if (geomRep is IIfcSolidModel solidModel)
                return _modelService.SolidFactory.Build(solidModel);

            if (geomRep is IIfcBooleanResult boolResult)
                return _modelService.BooleanFactory.Build(boolResult);

            if (geomRep is IIfcFaceBasedSurfaceModel faceBasedModel)
                return BuildFaceBasedSurfaceModel(faceBasedModel);

            if (geomRep is IIfcShellBasedSurfaceModel shellModel)
                return BuildShellBasedSurfaceModel(shellModel);

            throw new NotSupportedException(
                $"Geometric representation item type {geomRep.GetType().Name} is not supported.");
        }

        private IXShape BuildFaceBasedSurfaceModel(IIfcFaceBasedSurfaceModel model)
        {
            return _modelService.SolidFactory.Build(model);
        }

        private IXShape BuildShellBasedSurfaceModel(IIfcShellBasedSurfaceModel model)
        {
            // ShellBasedSurfaceModel requires decomposing IIfcFace into surface + wire topology,
            // which is not yet supported. Requires IFC face → native face pipeline.
            throw new NotSupportedException(
                $"ShellBasedSurfaceModel #{model.EntityLabel}: IFC face topology decomposition not yet available.");
        }

        #endregion

        #region Domain / Fix

        public IXShape UnifyDomain(IXShape toFix)
        {
            // Domain unification requires native OCCT ShapeUpgrade_UnifySameDomain.
            // Not yet available as a native export. Return the shape unchanged.
            _logger.LogDebug("UnifyDomain not yet available, returning shape unchanged.");
            return toFix;
        }

        public IEnumerable<IXFace> FixFace(IXFace face)
        {
            // Face repair requires native OCCT ShapeFix_Face.
            // Not yet available as a native export. Return the face unchanged.
            _logger.LogDebug("FixFace not yet available, returning face unchanged.");
            return new[] { face };
        }

        public IXFace Add(IXFace toFace, IXWire[] wires)
        {
            // Adding wires to a face requires native BRep_Builder::Add.
            // Not yet available as a native export.
            throw new NotImplementedException("Adding wires to a face requires native BRep_Builder support.");
        }

        #endregion

        #region Transform / Move

        public IXShape Transform(IXShape shape, XbimMatrix3D matrix)
        {
            if (shape is not Shape ns)
                throw new ArgumentException("Shape must be a Shape instance.", nameof(shape));

            // Build a location from the matrix (extracting rotation + translation)
            double ox = matrix.OffsetX, oy = matrix.OffsetY, oz = matrix.OffsetZ;
            double m11 = matrix.M11, m12 = matrix.M12, m13 = matrix.M13;
            double m21 = matrix.M21, m22 = matrix.M22, m23 = matrix.M23;
            double m31 = matrix.M31, m32 = matrix.M32, m33 = matrix.M33;

            // Extract Z direction and X direction from the matrix columns
            // Column 1 = X axis, Column 3 = Z axis
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                ox, oy, oz, m31, m32, m33, m11, m12, m13, out var locHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create transform location: {XbimGeometryNativeApi.GetLastError()}");

            try
            {
                result = XbimGeometryNativeApi.xbim_shape_moved(ns.Handle, locHandle, out var movedHandle);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");

                return ShapeFactory.WrapShape(movedHandle);
            }
            finally
            {
                locHandle.Dispose();
            }
        }

        public IXShape Moved(IXShape shape, IXLocation moveTo)
        {
            if (shape is not Shape ns)
                throw new ArgumentException("Shape must be a Shape instance.", nameof(shape));
            if (moveTo is not XLocation loc)
                throw new ArgumentException("Location must be an XLocation instance.", nameof(moveTo));

            int result = XbimGeometryNativeApi.xbim_shape_moved(ns.Handle, loc.Handle, out var movedHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to move shape: {XbimGeometryNativeApi.GetLastError()}");

            return ShapeFactory.WrapShape(movedHandle);
        }

        public IXShape Moved(IXShape shape, IIfcObjectPlacement placement, bool invertPlacement)
        {
            var geometryFactory = (GeometryFactory)_modelService.GeometryFactory;
            var location = geometryFactory.ToLocation(placement);
            try
            {
                var loc = invertPlacement ? (XLocation)location.Inverted() : location;
                var result = Moved(shape, loc);
                if (invertPlacement)
                    loc.Dispose();
                return result;
            }
            finally
            {
                location.Dispose();
            }
        }

        public IXShape RemovePlacement(IXShape shape)
        {
            // Move shape to identity (removes placement)
            var identity = new XLocation();
            try
            {
                return Moved(shape, identity);
            }
            finally
            {
                identity.Dispose();
            }
        }

        public IXShape SetPlacement(IXShape shape, IIfcObjectPlacement placement)
        {
            return Moved(shape, placement, false);
        }

        #endregion

        #region Boolean Operations

        public IXShape Union(IXShape body, IXShape addition)
        {
            return PerformBoolean(body, addition,
                XbimGeometryNativeApi.xbim_boolean_union);
        }

        public IXShape Cut(IXShape body, IXShape substraction)
        {
            return PerformBoolean(body, substraction,
                XbimGeometryNativeApi.xbim_boolean_cut);
        }

        public IXShape Union(IXShape body, IEnumerable<IXShape> addition)
        {
            IXShape current = body;
            foreach (var addShape in addition)
            {
                var result = Union(current, addShape);
                if (!ReferenceEquals(current, body))
                    current.Dispose();
                current = result;
            }
            return current;
        }

        public IXShape Cut(IXShape body, IEnumerable<IXShape> substraction)
        {
            IXShape current = body;
            foreach (var subShape in substraction)
            {
                var result = Cut(current, subShape);
                if (!ReferenceEquals(current, body))
                    current.Dispose();
                current = result;
            }
            return current;
        }

        private delegate int BooleanOp(
            NativeContextHandle ctx, NativeShapeHandle body, NativeShapeHandle tool,
            double fuzzy, out int hasWarnings, out NativeShapeHandle outHandle);

        private IXShape PerformBoolean(IXShape body, IXShape tool, BooleanOp operation)
        {
            if (body is not Shape nsBody)
                throw new ArgumentException("Body must be a Shape instance.", nameof(body));
            if (tool is not Shape nsTool)
                throw new ArgumentException("Tool must be a Shape instance.", nameof(tool));

            double fuzzyTolerance = _modelService.Model.ModelFactors.PrecisionBoolean;

            int result = operation(
                ContextHandle, nsBody.Handle, nsTool.Handle,
                fuzzyTolerance, out _, out var outHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            return ShapeFactory.WrapShape(outHandle);
        }

        #endregion
    }
}
