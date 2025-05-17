using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Converts IFC placement, direction, and point entities into native geometry
    /// representations. Handles axis-2 placement to location transform conversion
    /// and provides helper methods for extracting 2D/3D direction and point data.
    /// </summary>
    internal class NativeGeometryFactory : IXGeometryFactory
    {
        private readonly IXModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeGeometryFactory(IXModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        #region Point Building

        public IXPoint BuildPoint2d(double x, double y) => new XPoint(x, y);

        public IXPoint BuildPoint3d(double x, double y, double z) => new XPoint(x, y, z);

        public IXPoint BuildPoint3d(IfcPointByDistanceExpression pointByDistanceExpression)
        {
            throw new NotImplementedException(
                "IfcPointByDistanceExpression requires curve evaluation (Ifc4x3 alignment support).");
        }

        /// <summary>
        /// Extracts a 3D point from an IIfcCartesianPoint.
        /// Z coordinate defaults to 0 for 2D points.
        /// </summary>
        internal static XPoint BuildPoint3d(IIfcCartesianPoint ifcPoint)
        {
            double x = ifcPoint.Coordinates[0];
            double y = ifcPoint.Coordinates[1];
            double z = (int)ifcPoint.Dim == 3 ? (double)ifcPoint.Coordinates[2] : 0.0;
            return new XPoint(x, y, z);
        }

        #endregion

        #region Direction Building

        public IXDirection BuildDirection2d(double x, double y) => new XDirection(x, y);

        public IXDirection BuildDirection3d(double x, double y, double z) => new XDirection(x, y, z);

        /// <summary>
        /// Extracts a 3D direction vector from an IIfcDirection.
        /// Returns false if the direction has zero magnitude.
        /// </summary>
        internal static bool BuildDirection3d(IIfcDirection ifcDir, out double x, out double y, out double z)
        {
            x = ifcDir.DirectionRatios[0];
            y = ifcDir.DirectionRatios[1];
            z = ifcDir.DirectionRatios.Count > 2 ? ifcDir.DirectionRatios[2] : 0.0;

            double mag = Math.Sqrt(x * x + y * y + z * z);
            if (mag < 1e-15)
                return false;

            x /= mag;
            y /= mag;
            z /= mag;
            return true;
        }

        #endregion

        #region Axis2Placement Building

        public IXAxis2Placement2d BuildAxis2Placement2d(IXPoint location, IXVector xAxisDirection)
        {
            return new XAxis2Placement2d(location, new XDirection(xAxisDirection.X, xAxisDirection.Y));
        }

        public IXAxis2Placement2d BuildAxis2Placement2d(IXPoint location, IXDirection xAxisDirection)
        {
            return new XAxis2Placement2d(location, xAxisDirection);
        }

        public IXAxis2Placement3d BuildAxis2Placement3d(IXPoint location, IXVector xAxisDirection, IXVector zAxisDirection)
        {
            return new XAxis2Placement3d(location,
                new XDirection(zAxisDirection.X, zAxisDirection.Y, zAxisDirection.Z),
                new XDirection(xAxisDirection.X, xAxisDirection.Y, xAxisDirection.Z));
        }

        public IXAxis2Placement3d BuildAxis2Placement3d(IXPoint location, IXDirection xAxisDirection, IXDirection zAxisDirection)
        {
            return new XAxis2Placement3d(location, zAxisDirection, xAxisDirection);
        }

        /// <summary>
        /// Extracts origin, Z direction, and X direction from an IIfcAxis2Placement3D.
        /// Uses default axes (Z=0,0,1; X=1,0,0) when directions are null.
        /// </summary>
        internal static void BuildAxis2Placement3d(
            IIfcAxis2Placement3D axis3D,
            out double ox, out double oy, out double oz,
            out double zx, out double zy, out double zz,
            out double xx, out double xy, out double xz)
        {
            var pt = BuildPoint3d(axis3D.Location);
            ox = pt.X; oy = pt.Y; oz = pt.Z;

            if (axis3D.Axis != null && axis3D.RefDirection != null)
            {
                if (!BuildDirection3d(axis3D.Axis, out zx, out zy, out zz))
                {
                    // Fallback to default Z if direction is degenerate
                    zx = 0; zy = 0; zz = 1;
                }
                if (!BuildDirection3d(axis3D.RefDirection, out xx, out xy, out xz))
                {
                    // Fallback to default X if direction is degenerate
                    xx = 1; xy = 0; xz = 0;
                }
            }
            else
            {
                // IFC rule: both must be given if one is; use defaults otherwise
                zx = 0; zy = 0; zz = 1;
                xx = 1; xy = 0; xz = 0;
            }
        }

        #endregion

        #region Location Building

        public IXLocation BuildLocation(IIfcObjectPlacement placement)
        {
            return ToLocation(placement);
        }

        public IXLocation BuildLocation(IIfcPlacement placement)
        {
            if (placement is IIfcAxis2Placement3D axis3D)
                return BuildLocationFromAxis3D(axis3D);

            if (placement is IIfcAxis2Placement2D axis2D)
                return BuildLocationFromAxis2D(axis2D);

            throw new NotSupportedException($"Unsupported placement type: {placement.GetType().Name}");
        }

        public IXLocation BuildLocation(IIfcAxis2Placement placement)
        {
            if (placement is IIfcAxis2Placement3D axis3D)
                return BuildLocationFromAxis3D(axis3D);

            if (placement is IIfcAxis2Placement2D axis2D)
                return BuildLocationFromAxis2D(axis2D);

            throw new NotSupportedException($"Unsupported axis placement type: {placement.GetType().Name}");
        }

        public IXLocation BuildLocation(IfcAxis2PlacementLinear linearPlacement)
        {
            throw new NotImplementedException(
                "IfcAxis2PlacementLinear requires curve evaluation (Ifc4x3 alignment support).");
        }

        /// <summary>
        /// Creates a native location handle from an IIfcAxis2Placement3D.
        /// Ports the C++/CLI GeometryFactory::ToLocation(IIfcAxis2Placement3D).
        /// </summary>
        internal XLocation BuildLocationFromAxis3D(IIfcAxis2Placement3D axis3D)
        {
            BuildAxis2Placement3d(axis3D,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                ox, oy, oz, zx, zy, zz, xx, xy, xz, out var handle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create location from axis2 placement: {XbimGeometryNativeApi.GetLastError()}");

            // Reconstruct the transform matrix from the axis2 placement.
            // The native side does: gp_Trsf.SetTransformation(gp_Ax3(origin, zDir, xDir), defaultAx3).Inverted()
            // which gives us the local-to-global transform.
            //
            // For the matrix: column vectors are the local axes in global coordinates.
            // X axis = xDir (normalized), Z axis = zDir (normalized), Y axis = Z cross X
            double yx = zy * xz - zz * xy;
            double yy = zz * xx - zx * xz;
            double yz = zx * xy - zy * xx;

            return new XLocation(handle,
                xx, yx, zx,   // column 1: what global X maps to
                xy, yy, zy,   // column 2: what global Y maps to
                xz, yz, zz,   // column 3: what global Z maps to
                ox, oy, oz,   // translation
                1.0);         // uniform scale
        }

        /// <summary>
        /// Creates a native location handle from an IIfcAxis2Placement2D.
        /// Ports the C++/CLI GeometryFactory::ToLocation(IIfcAxis2Placement2D).
        /// </summary>
        internal XLocation BuildLocationFromAxis2D(IIfcAxis2Placement2D axis2D)
        {
            double px, py;
            if (axis2D.Location != null)
            {
                px = axis2D.Location.Coordinates[0];
                py = axis2D.Location.Coordinates[1];
            }
            else
            {
                px = 0;
                py = 0;
            }

            double xDirX = 1, xDirY = 0;
            if (axis2D.RefDirection != null)
            {
                xDirX = axis2D.RefDirection.DirectionRatios[0];
                xDirY = axis2D.RefDirection.DirectionRatios[1];
                double mag = Math.Sqrt(xDirX * xDirX + xDirY * xDirY);
                if (mag > 1e-15)
                {
                    xDirX /= mag;
                    xDirY /= mag;
                }
                else
                {
                    xDirX = 1;
                    xDirY = 0;
                }
            }

            // 2D placement maps to 3D: origin at (px, py, 0), Z = (0,0,1), X = (xDirX, xDirY, 0)
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                px, py, 0, 0, 0, 1, xDirX, xDirY, 0, out var handle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create location from 2D axis placement: {XbimGeometryNativeApi.GetLastError()}");

            // Y direction in 2D: perpendicular to X
            double yDirX = -xDirY;
            double yDirY = xDirX;

            return new XLocation(handle,
                xDirX, yDirX, 0,
                xDirY, yDirY, 0,
                0, 0, 1,
                px, py, 0,
                1.0);
        }

        /// <summary>
        /// Converts an IIfcObjectPlacement hierarchy to a composed location handle.
        /// Traverses the placement tree iteratively (matching C++/CLI GeometryFactory::ToTransform).
        /// Handles IIfcLocalPlacement with IIfcAxis2Placement3D.
        /// Grid placements and linear placements are not yet supported.
        /// </summary>
        internal XLocation ToLocation(IIfcObjectPlacement objPlacement)
        {
            XLocation? accumulated = null;

            var localPlacement = objPlacement as IIfcLocalPlacement;

            while (localPlacement != null)
            {
                var axis3D = localPlacement.RelativePlacement as IIfcAxis2Placement3D;
                if (axis3D == null)
                {
                    _logger.LogWarning("Object placement with non-3D relative placement not supported, using identity.");
                    break;
                }

                var stepLocation = BuildLocationFromAxis3D(axis3D);

                if (accumulated == null)
                {
                    accumulated = stepLocation;
                }
                else
                {
                    // PreMultiply: accumulated = stepLocation * accumulated
                    // This matches the C++/CLI trsf.PreMultiply(relTrsf) pattern
                    var composed = (XLocation)stepLocation.Multiplied(accumulated);
                    accumulated.Dispose();
                    stepLocation.Dispose();
                    accumulated = composed;
                }

                // Navigate up the placement hierarchy
                if (localPlacement.PlacementRelTo is IIfcLocalPlacement parent)
                {
                    localPlacement = parent;
                }
                else
                {
                    localPlacement = null;
                }
            }

            return accumulated ?? new XLocation();
        }

        #endregion

        #region Transform Building

        public IXMatrix BuildTransform(IIfcCartesianTransformationOperator transOp)
        {
            // Check subtypes in specificity order (non-uniform before uniform)
            if (transOp is IIfcCartesianTransformationOperator3DnonUniform ct3dNU)
                return ToTransform3DnonUniform(ct3dNU);
            if (transOp is IIfcCartesianTransformationOperator2DnonUniform ct2dNU)
                return ToTransform2DnonUniform(ct2dNU);
            if (transOp is IIfcCartesianTransformationOperator3D ct3d)
                return ToTransform3D(ct3d);
            if (transOp is IIfcCartesianTransformationOperator2D ct2d)
                return ToTransform2D(ct2d);

            throw new NotSupportedException($"Unsupported transformation operator type: {transOp.GetType().Name}");
        }

        /// <summary>
        /// Ports GeometryFactory::ToTransform(IIfcCartesianTransformationOperator3D).
        /// Computes an orthonormalized 3D transform matrix from IFC axes.
        /// </summary>
        private XMatrix ToTransform3D(IIfcCartesianTransformationOperator3D ct3D)
        {
            // Default axes
            double u3x = 0, u3y = 0, u3z = 1; // Z axis
            double u1x = 1, u1y = 0, u1z = 0; // X axis
            double u2x = 0, u2y = 1, u2z = 0; // Y axis

            if (ct3D.Axis3 != null)
            {
                u3x = ct3D.Axis3.X;
                u3y = ct3D.Axis3.Y;
                u3z = ct3D.Axis3.Z;
                Normalize(ref u3x, ref u3y, ref u3z);
            }

            if (ct3D.Axis1 != null)
            {
                u1x = ct3D.Axis1.X;
                u1y = ct3D.Axis1.Y;
                u1z = ct3D.Axis1.Z;
                Normalize(ref u1x, ref u1y, ref u1z);
            }
            else
            {
                // If U3 is not parallel to default X, use default X; otherwise use Y
                if (!DirectionsEqual(u3x, u3y, u3z, 1, 0, 0))
                    { u1x = 1; u1y = 0; u1z = 0; }
                else
                    { u1x = 0; u1y = 1; u1z = 0; }
            }

            // Orthogonalize X against Z: xAxis = U1 - (U1·U3)*U3
            double dotU1U3 = u1x * u3x + u1y * u3y + u1z * u3z;
            double xAxisX = u1x - dotU1U3 * u3x;
            double xAxisY = u1y - dotU1U3 * u3y;
            double xAxisZ = u1z - dotU1U3 * u3z;
            Normalize(ref xAxisX, ref xAxisY, ref xAxisZ);

            if (ct3D.Axis2 != null)
            {
                u2x = ct3D.Axis2.X;
                u2y = ct3D.Axis2.Y;
                u2z = ct3D.Axis2.Z;
                Normalize(ref u2x, ref u2y, ref u2z);
            }

            // Orthogonalize Y against Z: yAxis = U2 - (U2·U3)*U3
            double dotU2U3 = u2x * u3x + u2y * u3y + u2z * u3z;
            double yAxisX = u2x - dotU2U3 * u3x;
            double yAxisY = u2y - dotU2U3 * u3y;
            double yAxisZ = u2z - dotU2U3 * u3z;

            // Then orthogonalize against X: yAxis = yAxis - (yAxis·xAxis)*xAxis
            double dotYX = yAxisX * xAxisX + yAxisY * xAxisY + yAxisZ * xAxisZ;
            yAxisX -= dotYX * xAxisX;
            yAxisY -= dotYX * xAxisY;
            yAxisZ -= dotYX * xAxisZ;
            Normalize(ref yAxisX, ref yAxisY, ref yAxisZ);

            double tx = ct3D.LocalOrigin.X;
            double ty = ct3D.LocalOrigin.Y;
            double tz = ct3D.LocalOrigin.Z;
            double scale = ct3D.Scl;

            var matrix = new XMatrix(
                xAxisX, xAxisY, xAxisZ,
                yAxisX, yAxisY, yAxisZ,
                u3x, u3y, u3z,
                tx, ty, tz,
                scale, scale, scale);
            return matrix;
        }

        /// <summary>
        /// Ports GeometryFactory::ToTransform(IIfcCartesianTransformationOperator3DnonUniform).
        /// </summary>
        private XMatrix ToTransform3DnonUniform(IIfcCartesianTransformationOperator3DnonUniform ct3D)
        {
            var matrix = ToTransform3D(ct3D);
            matrix.SetScale(ct3D.Scl, ct3D.Scl2, ct3D.Scl3);
            return matrix;
        }

        /// <summary>
        /// Ports GeometryFactory::ToTransform(IIfcCartesianTransformationOperator2D).
        /// </summary>
        private XMatrix ToTransform2D(IIfcCartesianTransformationOperator2D ct2D)
        {
            double d1x = 1, d1y = 0;
            double scale = ct2D.Scl;

            double m11 = 1, m12 = 0;
            double m21 = 0, m22 = 1;
            double tx = ct2D.LocalOrigin.X;
            double ty = ct2D.LocalOrigin.Y;

            if (ct2D.Axis1 != null)
            {
                d1x = ct2D.Axis1.X;
                d1y = ct2D.Axis1.Y;
                double mag = Math.Sqrt(d1x * d1x + d1y * d1y);
                if (mag > 1e-15) { d1x /= mag; d1y /= mag; }

                m11 = d1x; m12 = d1y;
                m21 = -d1y; m22 = d1x;

                if (ct2D.Axis2 != null)
                {
                    // Check if axis2 has been mirrored relative to the expected perpendicular
                    double vx = -d1y, vy = d1x; // expected perpendicular
                    double a2dot = ct2D.Axis2.X * vx + ct2D.Axis2.Y * vy;
                    if (a2dot < 0)
                    {
                        // Mirror: flip the Y axis
                        m21 = d1y;
                        m22 = -d1x;
                    }
                }
            }
            else if (ct2D.Axis2 != null)
            {
                d1x = ct2D.Axis2.X;
                d1y = ct2D.Axis2.Y;
                double mag = Math.Sqrt(d1x * d1x + d1y * d1y);
                if (mag > 1e-15) { d1x /= mag; d1y /= mag; }

                m11 = d1y; m12 = -d1x;
                m21 = d1x; m22 = d1y;
            }

            return new XMatrix(
                m11, m21, 0,
                m12, m22, 0,
                0, 0, 1,
                tx, ty, 0,
                scale, scale, 1.0);
        }

        /// <summary>
        /// Ports GeometryFactory::ToTransform(IIfcCartesianTransformationOperator2DnonUniform).
        /// </summary>
        private XMatrix ToTransform2DnonUniform(IIfcCartesianTransformationOperator2DnonUniform ct2D)
        {
            var matrix = ToTransform2D(ct2D);
            matrix.SetScale(ct2D.Scl, ct2D.Scl2, 1.0);
            return matrix;
        }

        #endregion

        #region BuildMapTransform

        public void BuildMapTransform(
            IIfcCartesianTransformationOperator transform,
            IIfcAxis2Placement origin,
            out IXLocation location,
            out IXMatrix matrix)
        {
            location = BuildLocation(origin);
            matrix = BuildTransform(transform);
        }

        #endregion

        #region Not Yet Implemented (depend on other factories)

        public bool IsFacingAwayFrom(IXFace face, IXDirection direction)
        {
            throw new NotImplementedException("IsFacingAwayFrom requires face normal evaluation.");
        }

        public IXPlane BuildPlane(IIfcPlane plane)
        {
            throw new NotImplementedException("BuildPlane requires surface factory support.");
        }

        public double Distance(IXPoint a, IXPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public double IsEqual(IXPoint a, IXPoint b, double tolerance)
        {
            return Distance(a, b) <= tolerance ? 1.0 : 0.0;
        }

        public IXDirection NormalAt(IXFace face, IXPoint position, double tolerance)
        {
            throw new NotImplementedException("NormalAt requires face surface evaluation.");
        }

        #endregion

        #region Helpers

        private static void Normalize(ref double x, ref double y, ref double z)
        {
            double mag = Math.Sqrt(x * x + y * y + z * z);
            if (mag > 1e-15)
            {
                x /= mag;
                y /= mag;
                z /= mag;
            }
        }

        private static bool DirectionsEqual(double ax, double ay, double az, double bx, double by, double bz)
        {
            // Compare using angular tolerance (matching OCCT Precision::Angular() ~ 1e-12)
            double dot = ax * bx + ay * by + az * bz;
            return Math.Abs(Math.Abs(dot) - 1.0) < 1e-10;
        }

        #endregion
    }
}
