using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometricConstraintResource;
using Xbim.Ifc4x3.GeometryResource;

namespace Xbim.Geometry.Engine.Factories
{
    /// <summary>
    /// Converts IFC placement, direction, and point entities into native geometry
    /// representations. Handles axis-2 placement to location transform conversion
    /// and provides helper methods for extracting 2D/3D direction and point data.
    /// </summary>
    internal class GeometryFactory : IXGeometryFactory
    {
        private readonly IXModelGeometryService _modelService;
        private readonly ILogger _logger;

        public GeometryFactory(IXModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => ((Services.ModelGeometryService)_modelService).ContextHandle;

        #region Point Building

        public IXPoint BuildPoint2d(double x, double y) => new XPoint(x, y);

        public IXPoint BuildPoint3d(double x, double y, double z) => new XPoint(x, y, z);

        public IXPoint BuildPoint3d(IfcPointByDistanceExpression pointByDistanceExpression)
        {
            return EvaluatePointByDistanceExpression(pointByDistanceExpression,
                out _, out _, out _, out _, out _, out _);
        }

        /// <summary>
        /// Evaluates a point on a curve from an IfcPointByDistanceExpression,
        /// returning the offset-applied position along with the local coordinate frame
        /// (tangent and axis vectors, including superelevation rotation).
        /// </summary>
        internal XPoint EvaluatePointByDistanceExpression(
            IfcPointByDistanceExpression pointExpr,
            out double tangentX, out double tangentY, out double tangentZ,
            out double axisX, out double axisY, out double axisZ)
        {
            // Build the basis curve
            using var curve = (XbimCurve)_modelService.CurveFactory.Build(pointExpr.BasisCurve);
            var curveHandle = curve.Handle;

            // Get curve length for parameter normalization
            int lengthResult = XbimGeometryNativeApi.xbim_curve_length(curveHandle, out double curveLength);
            if (lengthResult != 0)
                throw new XbimGeometryServiceException(
                    $"IfcPointByDistanceExpression #{pointExpr.EntityLabel}: " +
                    $"failed to compute curve length: {XbimGeometryNativeApi.GetLastError()}");

            // Determine the curve evaluation parameter from DistanceAlong
            double len;
            var distanceAlong = pointExpr.DistanceAlong;
            bool isConic = pointExpr.BasisCurve is IIfcConic;

            if (distanceAlong is IIfcLengthMeasure lengthMeasure)
            {
                // IfcLengthMeasure is an arc length — convert to the curve parameter
                // using GCPnts_AbscissaPoint on the native side.
                int paramResult = XbimGeometryNativeApi.xbim_curve_parameter_at_length(
                    ContextHandle, curveHandle, lengthMeasure.Value, out len);
                if (paramResult != 0)
                    throw new XbimGeometryServiceException(
                        $"IfcPointByDistanceExpression #{pointExpr.EntityLabel}: " +
                        $"failed to convert arc length {lengthMeasure.Value} to parameter: " +
                        XbimGeometryNativeApi.GetLastError());
            }
            else if (distanceAlong is Xbim.Ifc4x3.MeasureResource.IfcParameterValue parameterValue)
            {
                double paramVal = (double)parameterValue.Value;
                if (isConic)
                    len = paramVal * _modelService.RadianFactor;
                else
                    len = paramVal * curveLength;
            }
            else
            {
                throw new XbimGeometryServiceException(
                    $"IfcPointByDistanceExpression #{pointExpr.EntityLabel}: " +
                    "DistanceAlong must be IfcLengthMeasure or IfcParameterValue.");
            }

            // Evaluate point and first derivative (tangent) at the parameter
            int d1Result = XbimGeometryNativeApi.xbim_curve_d1(curveHandle, len,
                out double px, out double py, out double pz,
                out double tx, out double ty, out double tz);
            if (d1Result != 0)
                throw new XbimGeometryServiceException(
                    $"IfcPointByDistanceExpression #{pointExpr.EntityLabel}: " +
                    $"failed to evaluate curve at u={len}: {XbimGeometryNativeApi.GetLastError()}");

            // Normalize tangent
            Normalize(ref tx, ref ty, ref tz);

            // Compute local coordinate frame: y = up × tangent, axis = tangent × y
            double yx = -ty, yy = tx, yz = 0; // (0,0,1) × (tx,ty,tz)
            Normalize(ref yx, ref yy, ref yz);

            double ax = ty * yz - tz * yy;
            double ay = tz * yx - tx * yz;
            double az = tx * yy - ty * yx;
            Normalize(ref ax, ref ay, ref az);

            // Apply superelevation/cant tilt if basis curve is a segmented reference curve
            int superResult = XbimGeometryNativeApi.xbim_curve_get_superelevation_and_tilt(
                curveHandle, len, out _, out double cantTilt);
            if (superResult == 0)
            {
                RotateAroundAxis(ref ax, ref ay, ref az, tx, ty, tz, cantTilt);
                RotateAroundAxis(ref yx, ref yy, ref yz, tx, ty, tz, cantTilt);
            }

            // Output the local coordinate frame (before offset translation)
            tangentX = tx; tangentY = ty; tangentZ = tz;
            axisX = ax; axisY = ay; axisZ = az;

            // Apply lateral offset (along y)
            if (pointExpr.OffsetLateral.HasValue)
            {
                double lateralOffset = (double)pointExpr.OffsetLateral.Value.Value;
                if (lateralOffset != 0.0)
                {
                    px += yx * lateralOffset;
                    py += yy * lateralOffset;
                    pz += yz * lateralOffset;
                }
            }

            // Apply vertical offset (along axis)
            if (pointExpr.OffsetVertical.HasValue)
            {
                double verticalOffset = (double)pointExpr.OffsetVertical.Value.Value;
                if (verticalOffset != 0.0)
                {
                    px += ax * verticalOffset;
                    py += ay * verticalOffset;
                    pz += az * verticalOffset;
                }
            }

            // Apply longitudinal offset (along tangent)
            if (pointExpr.OffsetLongitudinal.HasValue)
            {
                double longOffset = (double)pointExpr.OffsetLongitudinal.Value.Value;
                if (longOffset != 0.0)
                {
                    px += tx * longOffset;
                    py += ty * longOffset;
                    pz += tz * longOffset;
                }
            }

            return new XPoint(px, py, pz);
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

        /// <summary>
        /// Extracts origin, Z direction, and X direction from an IIfcAxis2Placement,
        /// which may be either 2D or 3D. For 2D placements, Z defaults to (0,0,1)
        /// and the X direction is taken from the 2D RefDirection.
        /// </summary>
        internal static void BuildAxis2PlacementAs3d(
            IIfcAxis2Placement placement,
            out double ox, out double oy, out double oz,
            out double zx, out double zy, out double zz,
            out double xx, out double xy, out double xz)
        {
            if (placement is IIfcAxis2Placement3D axis3D)
            {
                BuildAxis2Placement3d(axis3D, out ox, out oy, out oz,
                    out zx, out zy, out zz, out xx, out xy, out xz);
            }
            else if (placement is IIfcAxis2Placement2D axis2D)
            {
                ox = axis2D.Location.Coordinates[0];
                oy = axis2D.Location.Coordinates[1];
                oz = 0;

                zx = 0; zy = 0; zz = 1;

                if (axis2D.RefDirection != null)
                {
                    xx = axis2D.RefDirection.DirectionRatios[0];
                    xy = axis2D.RefDirection.DirectionRatios[1];
                    xz = 0;
                    double mag = Math.Sqrt(xx * xx + xy * xy);
                    if (mag > 1e-15) { xx /= mag; xy /= mag; }
                    else { xx = 1; xy = 0; }
                }
                else
                {
                    xx = 1; xy = 0; xz = 0;
                }
            }
            else
            {
                // Fallback: identity placement
                ox = 0; oy = 0; oz = 0;
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

            throw new XbimGeometryNotSupportedException($"Unsupported placement type: {placement.GetType().Name}");
        }

        public IXLocation BuildLocation(IIfcAxis2Placement placement)
        {
            if (placement is IIfcAxis2Placement3D axis3D)
                return BuildLocationFromAxis3D(axis3D);

            if (placement is IIfcAxis2Placement2D axis2D)
                return BuildLocationFromAxis2D(axis2D);

            throw new XbimGeometryNotSupportedException($"Unsupported axis placement type: {placement.GetType().Name}");
        }

        public IXLocation BuildLocation(IfcAxis2PlacementLinear linearPlacement)
        {
            if (linearPlacement.Location is not IfcPointByDistanceExpression pointExpr)
                throw new XbimGeometryServiceException(
                    $"IfcAxis2PlacementLinear #{linearPlacement.EntityLabel}: " +
                    "Location must be an IfcPointByDistanceExpression.");

            var loc = EvaluatePointByDistanceExpression(pointExpr,
                out double tx, out double ty, out double tz,
                out double ax, out double ay, out double az);

            // Override tangent with RefDirection if specified
            if (linearPlacement.RefDirection != null)
            {
                if (!BuildDirection3d(linearPlacement.RefDirection, out tx, out ty, out tz))
                    throw new XbimGeometryServiceException(
                        $"IfcAxis2PlacementLinear #{linearPlacement.EntityLabel}: " +
                        "RefDirection is invalid.");
                Normalize(ref tx, ref ty, ref tz);
            }

            // Override axis with Axis if specified
            if (linearPlacement.Axis != null)
            {
                if (!BuildDirection3d(linearPlacement.Axis, out ax, out ay, out az))
                    throw new XbimGeometryServiceException(
                        $"IfcAxis2PlacementLinear #{linearPlacement.EntityLabel}: " +
                        "Axis direction is invalid.");
                Normalize(ref ax, ref ay, ref az);
            }

            // Create location: Z direction = axis, X direction = tangent
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                loc.X, loc.Y, loc.Z,
                ax, ay, az,    // Z direction (axis)
                tx, ty, tz,    // X direction (tangent/refDir)
                out var handle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"IfcAxis2PlacementLinear #{linearPlacement.EntityLabel}: " +
                    $"failed to create location: {XbimGeometryNativeApi.GetLastError()}");

            // Y = Z × X
            double yx = ay * tz - az * ty;
            double yy = az * tx - ax * tz;
            double yz = ax * ty - ay * tx;

            return new XLocation(handle,
                tx, ty, tz,    // row 0: X axis (tangent)
                yx, yy, yz,    // row 1: Y axis
                ax, ay, az,    // row 2: Z axis
                loc.X, loc.Y, loc.Z,
                1.0);
        }

        /// <summary>
        /// Creates a native location handle from an IIfcAxis2Placement3D.
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
                throw new XbimGeometryServiceException(
                    $"Failed to create location from axis2 placement: {XbimGeometryNativeApi.GetLastError()}");

            // Reconstruct the local-to-global transform matrix from the axis2 placement.
            // Rows = local axes in global coordinates (same convention as XbimMatrix3D).
            // X axis = xDir (normalized), Z axis = zDir (normalized), Y axis = Z cross X
            double yx = zy * xz - zz * xy;
            double yy = zz * xx - zx * xz;
            double yz = zx * xy - zy * xx;

            return new XLocation(handle,
                xx, xy, xz,   // row 0: X axis direction
                yx, yy, yz,   // row 1: Y axis direction
                zx, zy, zz,   // row 2: Z axis direction
                ox, oy, oz,   // translation
                1.0);         // uniform scale
        }

        /// <summary>
        /// Creates a native location handle from an IIfcAxis2Placement2D.
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
                throw new XbimGeometryServiceException(
                    $"Failed to create location from 2D axis placement: {XbimGeometryNativeApi.GetLastError()}");

            // Y direction in 2D: perpendicular to X
            double yDirX = -xDirY;
            double yDirY = xDirX;

            return new XLocation(handle,
                xDirX, xDirY, 0,   // row 0: X axis direction
                yDirX, yDirY, 0,   // row 1: Y axis direction
                0, 0, 1,           // row 2: Z axis direction
                px, py, 0,
                1.0);
        }

        /// <summary>
        /// Converts an IIfcObjectPlacement hierarchy to a composed location handle.
        /// Traverses the placement tree iteratively, handling local placements,
        /// linear placements (Ifc4x3), and grid placements.
        /// </summary>
        internal XLocation ToLocation(IIfcObjectPlacement objPlacement)
        {
            XLocation? accumulated = null;

            var localPlacement = objPlacement as IIfcLocalPlacement;
            var linearPlacement = objPlacement as IfcLinearPlacement;
            var gridPlacement = objPlacement as IIfcGridPlacement;

            while (localPlacement != null || linearPlacement != null || gridPlacement != null)
            {
                if (localPlacement != null)
                {
                    if (localPlacement.RelativePlacement is not IIfcAxis2Placement3D axis3D)
                    {
                        _logger.LogWarning("Object placement with non-3D relative placement not supported, using identity.");
                        break;
                    }

                    var stepLocation = BuildLocationFromAxis3D(axis3D);
                    accumulated = PreMultiplied(accumulated, stepLocation);

                    // Navigate up: PlacementRelTo can be local or linear
                    EvaluateNextPlacement(localPlacement.PlacementRelTo,
                        out localPlacement, out linearPlacement);
                    gridPlacement = null;
                }
                else if (linearPlacement != null)
                {
                    if (linearPlacement.RelativePlacement is IfcAxis2PlacementLinear axisLinear)
                    {
                        var stepLocation = (XLocation)BuildLocation(axisLinear);
                        accumulated = PreMultiplied(accumulated, stepLocation);
                    }
                    else
                    {
                        throw new XbimGeometryServiceException(
                            "RelativePlacement for IfcLinearPlacement must be specified.");
                    }

                    // Navigate up: PlacementRelTo can be local or linear
                    EvaluateNextPlacement(linearPlacement.PlacementRelTo,
                        out localPlacement, out linearPlacement);
                    gridPlacement = null;
                }
                else if (gridPlacement != null)
                {
                    var stepLocation = BuildLocationFromGridPlacement(gridPlacement);
                    if (stepLocation != null)
                        accumulated = PreMultiplied(accumulated, stepLocation);

                    // Grid placement doesn't chain further in the same way
                    localPlacement = null;
                    linearPlacement = null;
                    gridPlacement = null;
                }
            }

            return accumulated ?? new XLocation();
        }

        /// <summary>
        /// = accumulated.PreMultiply(stepLocation)
        /// </summary>
        private static XLocation PreMultiplied(XLocation? accumulated, XLocation stepLocation)
        {
            if (accumulated == null)
                return stepLocation;
 
            var composed = (XLocation)accumulated.PreMultiplied(stepLocation);

            // PreMultiplied may return 'this' when the argument is identity; only dispose if distinct.
            if (!ReferenceEquals(composed, accumulated))
                accumulated.Dispose();
            if (!ReferenceEquals(composed, stepLocation))
                stepLocation.Dispose();
            return composed;
        }

        /// <summary>
        /// Navigates up the placement hierarchy, determining whether the next
        /// placement is a local or linear placement.
        /// </summary>
        private static void EvaluateNextPlacement(
            IIfcObjectPlacement? placementRelTo,
            out IIfcLocalPlacement? nextLocal,
            out IfcLinearPlacement? nextLinear)
        {
            if (placementRelTo is IIfcLocalPlacement lp)
            {
                nextLocal = lp;
                nextLinear = null;
            }
            else if (placementRelTo is IfcLinearPlacement linP)
            {
                nextLocal = null;
                nextLinear = linP;
            }
            else
            {
                nextLocal = null;
                nextLinear = null;
            }
        }

        /// <summary>
        /// Builds a location from an IIfcGridPlacement by computing the grid axis
        /// intersection, applying offsets and the grid's own object placement.
        /// </summary>
        private XLocation? BuildLocationFromGridPlacement(IIfcGridPlacement gridPlacement)
        {
            var vi = gridPlacement.PlacementLocation;
            if (vi == null)
            {
                _logger.LogWarning("Grid placement has no PlacementLocation, using identity.");
                return null;
            }

            var axes = vi.IntersectingAxes.ToList();
            if (axes.Count < 2)
            {
                _logger.LogWarning("Grid placement needs at least 2 intersecting axes.");
                return null;
            }

            // Compute 2D intersection of the two grid axis curves
            if (!TryIntersectGridAxes(axes[0], axes[1], out double ix, out double iy))
            {
                _logger.LogWarning("Could not compute grid axis intersection for grid placement.");
                return null;
            }

            // Determine ref direction
            double refDirX = 1, refDirY = 0;
            if (gridPlacement.PlacementRefDirection == null)
            {
                // Default: tangent of first axis at intersection
                if (TryGetGridAxisTangentAt(axes[0], out double tanX, out double tanY))
                {
                    refDirX = tanX;
                    refDirY = tanY;
                }
            }
            else if (gridPlacement.PlacementRefDirection is IIfcDirection dir)
            {
                refDirX = dir.DirectionRatios[0];
                refDirY = dir.DirectionRatios.Count > 1 ? dir.DirectionRatios[1] : 0;
                double mag = Math.Sqrt(refDirX * refDirX + refDirY * refDirY);
                if (mag > 1e-15) { refDirX /= mag; refDirY /= mag; }
            }
            else if (gridPlacement.PlacementRefDirection is IIfcVirtualGridIntersection v2)
            {
                var axes2 = v2.IntersectingAxes.ToList();
                if (axes2.Count >= 2 &&
                    TryIntersectGridAxes(axes2[0], axes2[1], out double ix2, out double iy2))
                {
                    double vx = ix - ix2, vy = iy - iy2;
                    double mag = Math.Sqrt(vx * vx + vy * vy);
                    if (mag > 1e-15) { refDirX = vx / mag; refDirY = vy / mag; }
                }
            }

            // Apply offset distances via 2D transform
            double offX = 0, offY = 0, offZ = 0;
            var offsets = vi.OffsetDistances.ToList();
            if (offsets.Count > 0) offX = offsets[0];
            if (offsets.Count > 1) offY = offsets[1];
            if (offsets.Count > 2) offZ = offsets[2];

            // Transform offsets by the ref direction (2D rotation)
            double perpX = -refDirY, perpY = refDirX;
            double finalX = ix + refDirX * offX + perpX * offY;
            double finalY = iy + refDirY * offX + perpY * offY;

            // Build the translation location
            int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                finalX, finalY, offZ,
                0, 0, 1,
                1, 0, 0,
                out var handle);

            if (result != 0)
            {
                _logger.LogWarning("Failed to create grid placement location: {Error}",
                    XbimGeometryNativeApi.GetLastError());
                return null;
            }

            var gridLoc = new XLocation(handle,
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                finalX, finalY, offZ,
                1.0);

            // Apply the grid's own object placement
            IIfcGrid? grid = axes[0].PartOfU.FirstOrDefault()
                          ?? axes[0].PartOfV.FirstOrDefault()
                          ?? axes[0].PartOfW.FirstOrDefault();

            if (grid?.ObjectPlacement != null)
            {
                var gridObjLoc = ToLocation(grid.ObjectPlacement);
                var composed = PreMultiplied(gridLoc, gridObjLoc);
                return composed;
            }

            return gridLoc;
        }

        /// <summary>
        /// Attempts to compute the 2D intersection of two grid axis curves.
        /// Supports line-based grid axes (the common case).
        /// </summary>
        private bool TryIntersectGridAxes(IIfcGridAxis axis1, IIfcGridAxis axis2,
            out double ix, out double iy)
        {
            ix = 0; iy = 0;

            if (!TryGetLineFromGridAxis(axis1, out double o1x, out double o1y, out double d1x, out double d1y))
                return false;
            if (!TryGetLineFromGridAxis(axis2, out double o2x, out double o2y, out double d2x, out double d2y))
                return false;

            // Solve: o1 + t*d1 = o2 + s*d2 for t
            // d1x*t - d2x*s = o2x - o1x
            // d1y*t - d2y*s = o2y - o1y
            double det = d1x * (-d2y) - d1y * (-d2x);
            if (Math.Abs(det) < 1e-15)
            {
                _logger.LogWarning("Grid axes are parallel, cannot compute intersection.");
                return false;
            }

            double bx = o2x - o1x;
            double by = o2y - o1y;
            double t = (bx * (-d2y) - by * (-d2x)) / det;

            ix = o1x + t * d1x;
            iy = o1y + t * d1y;
            return true;
        }

        /// <summary>
        /// Extracts a 2D line (origin + direction) from a grid axis curve.
        /// </summary>
        private bool TryGetLineFromGridAxis(IIfcGridAxis gridAxis,
            out double ox, out double oy, out double dx, out double dy)
        {
            ox = oy = dx = dy = 0;

            var axisCurve = gridAxis.AxisCurve;
            if (axisCurve is IIfcTrimmedCurve trimmed)
                axisCurve = trimmed.BasisCurve;

            if (axisCurve is IIfcLine line)
            {
                ox = line.Pnt.Coordinates[0];
                oy = line.Pnt.Coordinates.Count > 1 ? line.Pnt.Coordinates[1] : 0;
                dx = line.Dir.Orientation.DirectionRatios[0];
                dy = line.Dir.Orientation.DirectionRatios.Count > 1
                    ? line.Dir.Orientation.DirectionRatios[1] : 0;
                double mag = Math.Sqrt(dx * dx + dy * dy);
                if (mag > 1e-15) { dx /= mag; dy /= mag; }
                return true;
            }

            if (axisCurve is IIfcPolyline polyline && polyline.Points.Count >= 2)
            {
                var p0 = polyline.Points[0];
                var p1 = polyline.Points[polyline.Points.Count - 1];
                ox = p0.Coordinates[0];
                oy = p0.Coordinates.Count > 1 ? p0.Coordinates[1] : 0;
                dx = p1.Coordinates[0] - ox;
                dy = (p1.Coordinates.Count > 1 ? p1.Coordinates[1] : 0) - oy;
                double mag = Math.Sqrt(dx * dx + dy * dy);
                if (mag > 1e-15) { dx /= mag; dy /= mag; return true; }
            }

            _logger.LogWarning("Grid axis curve type {Type} is not supported for intersection.",
                axisCurve?.GetType().Name ?? "null");
            return false;
        }

        /// <summary>
        /// Computes the tangent direction of a grid axis at a given 2D point.
        /// For line-based axes, this is simply the line direction.
        /// </summary>
        private bool TryGetGridAxisTangentAt(IIfcGridAxis gridAxis,
            out double tanX, out double tanY)
        {
            return TryGetLineFromGridAxis(gridAxis, out _, out _, out tanX, out tanY);
        }


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

            throw new XbimGeometryNotSupportedException($"Unsupported transformation operator type: {transOp.GetType().Name}");
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


        public void BuildMapTransform(
            IIfcCartesianTransformationOperator transform,
            IIfcAxis2Placement origin,
            out IXLocation location,
            out IXMatrix matrix)
        {
            location = BuildLocation(origin);
            matrix = BuildTransform(transform);
        }


        public bool IsFacingAwayFrom(IXFace face, IXDirection direction)
        {
            if (direction.IsNull) return false;
            var nativeFace = (XbimFace)face;
            return XbimGeometryNativeApi.xbim_face_is_facing_away(
                nativeFace.Handle,
                direction.X, direction.Y, direction.Z) != 0;
        }

        public IXPlane BuildPlane(IIfcPlane plane)
        {
            return (IXPlane)_modelService.SurfaceFactory.Build(plane);
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
            var nativeFace = (Shapes.XbimFace)face;
            int result = XbimGeometryNativeApi.xbim_face_normal_at_point(
                ContextHandle,
                nativeFace.Handle,
                position.X, position.Y, position.Z,
                out double nx, out double ny, out double nz);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to compute normal at point: {XbimGeometryNativeApi.GetLastError()}");

            return BuildDirection3d(nx, ny, nz);
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

        /// <summary>
        /// Rotates vector (vx,vy,vz) around unit axis (kx,ky,kz) by the given angle
        /// using Rodrigues' rotation formula.
        /// </summary>
        private static void RotateAroundAxis(
            ref double vx, ref double vy, ref double vz,
            double kx, double ky, double kz,
            double angle)
        {
            double cosA = Math.Cos(angle);
            double sinA = Math.Sin(angle);
            double dot = vx * kx + vy * ky + vz * kz;

            // k × v
            double cx = ky * vz - kz * vy;
            double cy = kz * vx - kx * vz;
            double cz = kx * vy - ky * vx;

            double nx = vx * cosA + cx * sinA + kx * dot * (1 - cosA);
            double ny = vy * cosA + cy * sinA + ky * dot * (1 - cosA);
            double nz = vz * cosA + cz * sinA + kz * dot * (1 - cosA);

            vx = nx;
            vy = ny;
            vz = nz;
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
