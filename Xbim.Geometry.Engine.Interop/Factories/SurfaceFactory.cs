using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometricConstraintResource;
using Xbim.Ifc4x3.GeometricModelResource;
using Xbim.Ifc4x3.GeometryResource;
using Xbim.Ifc4x3.ProfileResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds surface geometry from IFC surface entities. Handles planes,
    /// cylindrical surfaces, spherical surfaces, B-spline surfaces,
    /// and sectioned surfaces.
    /// </summary>
    internal class SurfaceFactory : IXSurfaceFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public SurfaceFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXSurface Build(IIfcSurface surface)
        {
            if (surface is IIfcPlane ifcPlane)
                return BuildPlane(ifcPlane);

            if (surface is IIfcCylindricalSurface ifcCylinder)
                return BuildCylindricalSurface(ifcCylinder);

            if (surface is IIfcSphericalSurface ifcSphere)
                return BuildSphericalSurface(ifcSphere);

            if (surface is IIfcToroidalSurface ifcToroid)
                return BuildToroidalSurface(ifcToroid);

            if (surface is IIfcBSplineSurfaceWithKnots ifcBSpline)
                return BuildBSplineSurface(ifcBSpline);

            if (surface is IfcSectionedSurface ifcSectioned)
                return BuildSectionedSurface(ifcSectioned);

            if (surface is IIfcSurfaceOfRevolution ifcRevolution)
                return BuildSurfaceOfRevolution(ifcRevolution);

            if (surface is IIfcSurfaceOfLinearExtrusion ifcExtrusion)
                return BuildSurfaceOfLinearExtrusion(ifcExtrusion);

            if (surface is IIfcRectangularTrimmedSurface ifcRectTrimmed)
                return BuildRectangularTrimmedSurface(ifcRectTrimmed);

            if (surface is IIfcCurveBoundedPlane ifcCurveBoundedPlane)
                return BuildCurveBoundedPlane(ifcCurveBoundedPlane);

            if (surface is IIfcCurveBoundedSurface)
                throw new NotSupportedException(
                    $"IfcCurveBoundedSurface #{surface.EntityLabel} is not supported.");

            throw new NotSupportedException(
                $"Surface type {surface.ExpressType.ExpressName} #{surface.EntityLabel} is not yet supported.");
        }

        public IXPlane BuildPlane(IXPoint origin, IXDirection normal)
        {
            int result = XbimGeometryNativeApi.xbim_surface_build_plane(
                ContextHandle,
                origin.X, origin.Y, origin.Z,
                normal.X, normal.Y, normal.Z,
                0, 0, 0, // auto-compute refDir
                out var NativeSurfaceHandle,
                out double refX, out double refY, out double refZ);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build plane: {XbimGeometryNativeApi.GetLastError()}");

            var refDir = new XDirection(refX, refY, refZ);
            return new Plane(NativeSurfaceHandle, origin, normal, refDir);
        }

        #region Plane

        private IXPlane BuildPlane(IIfcPlane ifcPlane)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcPlane.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_surface_build_plane(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                out var NativeSurfaceHandle,
                out _, out _, out _);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build plane #{ifcPlane.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var origin = new XPoint(ox, oy, oz);
            var normal = new XDirection(zx, zy, zz);
            var refDir = new XDirection(xx, xy, xz);
            return new Plane(NativeSurfaceHandle, origin, normal, refDir);
        }

        #endregion

        #region Cylindrical

        private Surface BuildCylindricalSurface(IIfcCylindricalSurface ifcCylinder)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcCylinder.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_surface_build_cylindrical(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcCylinder.Radius,
                out var NativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build cylindrical surface #{ifcCylinder.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new CylindricalSurface(NativeSurfaceHandle, ifcCylinder.Radius)
            {
                Position = new XAxis2Placement3d(
                    new XPoint(ox, oy, oz),
                    new XDirection(zx, zy, zz),
                    new XDirection(xx, xy, xz))
            };
        }

        #endregion

        #region Spherical

        private SphericalSurface BuildSphericalSurface(IIfcSphericalSurface ifcSphere)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcSphere.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_surface_build_spherical(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcSphere.Radius,
                out var NativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build spherical surface #{ifcSphere.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new SphericalSurface(NativeSurfaceHandle, ifcSphere.Radius)
            {
                Position = new XAxis2Placement3d(
                    new XPoint(ox, oy, oz),
                    new XDirection(zx, zy, zz),
                    new XDirection(xx, xy, xz))
            };
        }

        #endregion

        #region Toroidal

        private ToroidalSurface BuildToroidalSurface(IIfcToroidalSurface ifcToroid)
        {
            if (ifcToroid.MajorRadius < 0)
                throw new XbimGeometryServiceException(
                    $"ToroidalSurface #{ifcToroid.EntityLabel}: MajorRadius must be >= 0.");
            if (ifcToroid.MinorRadius <= 0)
                throw new XbimGeometryServiceException(
                    $"ToroidalSurface #{ifcToroid.EntityLabel}: MinorRadius must be > 0.");

            GeometryFactory.BuildAxis2Placement3d(ifcToroid.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_surface_build_toroidal(
                ContextHandle,
                ox, oy, oz,
                zx, zy, zz,
                xx, xy, xz,
                ifcToroid.MajorRadius,
                ifcToroid.MinorRadius,
                out var nativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build toroidal surface #{ifcToroid.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new ToroidalSurface(nativeSurfaceHandle, ifcToroid.MajorRadius)
            {
                Position = new XAxis2Placement3d(
                    new XPoint(ox, oy, oz),
                    new XDirection(zx, zy, zz),
                    new XDirection(xx, xy, xz))
            };
        }

        #endregion

        #region Rectangular Trimmed Surface

        private Surface BuildRectangularTrimmedSurface(IIfcRectangularTrimmedSurface ifcRectTrimmed)
        {
            var basisSurface = Build(ifcRectTrimmed.BasisSurface);

            if (basisSurface is not Surface basis)
                throw new XbimGeometryServiceException(
                    $"RectangularTrimmedSurface #{ifcRectTrimmed.EntityLabel}: " +
                    $"basis surface type {basisSurface.SurfaceType} cannot be trimmed.");

            int result = XbimGeometryNativeApi.xbim_surface_build_rectangular_trimmed(
                ContextHandle,
                basis.Handle,
                ifcRectTrimmed.U1,
                ifcRectTrimmed.U2,
                ifcRectTrimmed.V1,
                ifcRectTrimmed.V2,
                out var nativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build rectangular trimmed surface #{ifcRectTrimmed.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Surface(nativeSurfaceHandle, XSurfaceType.IfcRectangularTrimmedSurface);
        }

        #endregion

        #region Curve Bounded Plane

        private FaceSurface BuildCurveBoundedPlane(IIfcCurveBoundedPlane ifcCurveBoundedPlane)
        {
            GeometryFactory.BuildAxis2Placement3d(ifcCurveBoundedPlane.BasisSurface.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            var wireFactory = (WireFactory)_modelService.WireFactory;

            // Build outer boundary wire (in local 2D space, z = 0)
            using var outerWire = (XbimWire)wireFactory.Build(ifcCurveBoundedPlane.OuterBoundary);

            // Build inner boundary wires (holes)
            var innerBoundaries = ifcCurveBoundedPlane.InnerBoundaries.ToArray();
            var innerWires = innerBoundaries
                .Select(c => (XbimWire)wireFactory.Build(c))
                .ToArray();

            try
            {
                var innerHandles = innerWires.Select(w => w.Handle).ToArray();
                using var innerWireArray = new NativeHandleArray(innerHandles);

                int result = XbimGeometryNativeApi.xbim_surface_build_curve_bounded_plane(
                    ContextHandle,
                    ox, oy, oz,
                    zx, zy, zz,
                    xx, xy, xz,
                    outerWire.Handle,
                    innerWireArray.Ptrs,
                    innerWireArray.Length,
                    out var faceHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build curve bounded plane #{ifcCurveBoundedPlane.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new FaceSurface(faceHandle, XSurfaceType.IfcCurveBoundedPlane);
            }
            finally
            {
                foreach (var w in innerWires)
                    w.Dispose();
            }
        }

        #endregion

        #region Surface of Revolution

        private Surface BuildSurfaceOfRevolution(IIfcSurfaceOfRevolution ifcRevolution)
        {
            if (ifcRevolution.SweptCurve.ProfileType != Ifc4.Interfaces.IfcProfileTypeEnum.CURVE)
                throw new XbimGeometryServiceException(
                    $"SurfaceOfRevolution #{ifcRevolution.EntityLabel}: only profiles of type CURVE are valid.");

            // Extract the curve from the profile — both open and closed profiles are valid
            IIfcCurve ifcCurve = ifcRevolution.SweptCurve switch
            {
                IIfcArbitraryOpenProfileDef open => open.Curve,
                IIfcArbitraryClosedProfileDef closed => closed.OuterCurve,
                _ => throw new NotSupportedException(
                    $"SurfaceOfRevolution #{ifcRevolution.EntityLabel}: unsupported SweptCurve profile type " +
                    $"{ifcRevolution.SweptCurve.ExpressType.ExpressName}.")
            };

            // Build the generatrix curve from the profile's curve property
            var curveFactory = (CurveFactory)_modelService.CurveFactory;
            using var curve = (XbimCurve)curveFactory.Build(ifcCurve);

           
            // Extract revolution axis
            var axisPoint = GeometryFactory.BuildPoint3d(ifcRevolution.AxisPosition.Location);
            double axisDirX = 0, axisDirY = 0, axisDirZ = 1;
            if (ifcRevolution.AxisPosition.Axis != null)
            {
                if (!GeometryFactory.BuildDirection3d(ifcRevolution.AxisPosition.Axis,
                        out axisDirX, out axisDirY, out axisDirZ))
                    throw new XbimGeometryServiceException(
                        $"SurfaceOfRevolution #{ifcRevolution.EntityLabel}: axis direction is incorrectly defined.");
            }

            int result = XbimGeometryNativeApi.xbim_surface_build_revolution(
                ContextHandle,
                curve.Handle,
                axisPoint.X, axisPoint.Y, axisPoint.Z,
                axisDirX, axisDirY, axisDirZ,
                out var NativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build SurfaceOfRevolution #{ifcRevolution.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return new Surface(NativeSurfaceHandle, XSurfaceType.IfcSurfaceOfRevolution);
           
        }

        #endregion

        #region Surface of Linear Extrusion

        private Surface BuildSurfaceOfLinearExtrusion(IIfcSurfaceOfLinearExtrusion ifcExtrusion)
        {
            if (ifcExtrusion.SweptCurve.ProfileType != Ifc4.Interfaces.IfcProfileTypeEnum.CURVE)
                throw new XbimGeometryServiceException(
                    $"SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: only profiles of type CURVE are valid.");

            // Extract the curve from the profile — both open and closed profiles are valid
            IIfcCurve ifcCurve = ifcExtrusion.SweptCurve switch
            {
                IIfcArbitraryOpenProfileDef open => open.Curve,
                IIfcArbitraryClosedProfileDef closed => closed.OuterCurve,
                _ => throw new NotSupportedException(
                    $"SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: unsupported SweptCurve profile type " +
                    $"{ifcExtrusion.SweptCurve.ExpressType.ExpressName}.")
            };

            // Try the Revit arc-centre workaround first: early Revit exporters
            // double-transform the centre of trimmed circular arcs.
            var model = _modelService.Model;
            var curveFactory = (CurveFactory)_modelService.CurveFactory;
            XbimCurve curve;
            if (ModelWorkArounds.TryFixArcCentreSweptCurve(model, ifcExtrusion,
                    c => curveFactory.Build3d(c), out var fixedCurve) && fixedCurve is XbimCurve fc)
            {
                curve = fc;
            }
            else
            {
                curve = curveFactory.Build3d(ifcCurve);
            }

            using (curve)
            {
                // Extract extrusion direction
                // NOTE: FixRevitSweptSurfaceExtrusionInFeet is intentionally omitted here.
                // The old code multiplied direction × depth × OneFoot, but
                // Geom_SurfaceOfLinearExtrusion normalises direction to a unit gp_Dir,
                // making the magnitude irrelevant for surface construction.
                if (!GeometryFactory.BuildDirection3d(ifcExtrusion.ExtrudedDirection,
                        out double dirX, out double dirY, out double dirZ))
                    throw new XbimGeometryServiceException(
                        $"SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: extrusion direction is incorrectly defined.");

                // Determine whether to apply the IFC Position transform.
                // For Revit ≤ 20.0.0.500 with BSpline swept curves, the control points
                // already include the placement — applying Position would double-displace.
                bool skipPosition = ModelWorkArounds.ShouldApplyBsplineWorkaround(model, ifcExtrusion);

                int hasPosition = 0;
                double posOX = 0, posOY = 0, posOZ = 0;
                double posZX = 0, posZY = 0, posZZ = 1;
                double posXX = 1, posXY = 0, posXZ = 0;

                if (!skipPosition && ifcExtrusion.Position != null)
                {
                    GeometryFactory.BuildAxis2Placement3d(ifcExtrusion.Position,
                        out posOX, out posOY, out posOZ,
                        out posZX, out posZY, out posZZ,
                        out posXX, out posXY, out posXZ);
                    hasPosition = 1;
                }

                int result = XbimGeometryNativeApi.xbim_surface_build_linear_extrusion(
                    ContextHandle,
                    curve.Handle,
                    dirX, dirY, dirZ,
                    posOX, posOY, posOZ,
                    posZX, posZY, posZZ,
                    posXX, posXY, posXZ,
                    hasPosition,
                    out var nativeSurfaceHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build SurfaceOfLinearExtrusion #{ifcExtrusion.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return new Surface(nativeSurfaceHandle, XSurfaceType.IfcSurfaceOfLinearExtrusion);
            }
        }

        #endregion

        #region BSpline Surface

        private Surface BuildBSplineSurface(IIfcBSplineSurfaceWithKnots ifcBSpline)
        {
            // Extract control points (2D grid: UUpper x VUpper)
            var controlPointGrid = ifcBSpline.ControlPointsList;
            int numU = controlPointGrid.Count;
            int numV = controlPointGrid[0].Count;

            var polesXYZ = new double[numU * numV * 3];
            for (int u = 0; u < numU; u++)
            {
                var row = controlPointGrid[u];
                for (int v = 0; v < numV; v++)
                {
                    var cp = row[v];
                    int idx = (u * numV + v) * 3;
                    polesXYZ[idx + 0] = cp.Coordinates[0];
                    polesXYZ[idx + 1] = cp.Coordinates[1];
                    polesXYZ[idx + 2] = (int)cp.Dim == 3 ? (double)cp.Coordinates[2] : 0.0;
                }
            }

            // Extract U knots
            var uKnotValues = ifcBSpline.UKnots.ToArray();
            var uKnots = new double[uKnotValues.Length];
            for (int i = 0; i < uKnotValues.Length; i++)
                uKnots[i] = uKnotValues[i];

            // Extract V knots
            var vKnotValues = ifcBSpline.VKnots.ToArray();
            var vKnots = new double[vKnotValues.Length];
            for (int i = 0; i < vKnotValues.Length; i++)
                vKnots[i] = vKnotValues[i];

            // Extract U multiplicities
            var uMultValues = ifcBSpline.UMultiplicities.ToArray();
            var uMultiplicities = new int[uMultValues.Length];
            for (int i = 0; i < uMultValues.Length; i++)
                uMultiplicities[i] = (int)uMultValues[i];

            // Extract V multiplicities
            var vMultValues = ifcBSpline.VMultiplicities.ToArray();
            var vMultiplicities = new int[vMultValues.Length];
            for (int i = 0; i < vMultValues.Length; i++)
                vMultiplicities[i] = (int)vMultValues[i];

            int uDegree = (int)ifcBSpline.UDegree;
            int vDegree = (int)ifcBSpline.VDegree;

            // Extract weights for rational B-spline surfaces
            double[]? weights = null;
            if (ifcBSpline is IIfcRationalBSplineSurfaceWithKnots rational)
            {
                var weightGrid = rational.WeightsData;
                weights = new double[numU * numV];
                for (int u = 0; u < numU; u++)
                {
                    var row = weightGrid[u];
                    for (int v = 0; v < numV; v++)
                        weights[u * numV + v] = row[v];
                }
            }

            int result = XbimGeometryNativeApi.xbim_surface_build_bspline(
                ContextHandle,
                polesXYZ, numU, numV,
                uKnots, uKnots.Length,
                vKnots, vKnots.Length,
                uMultiplicities, vMultiplicities,
                uDegree, vDegree,
                weights,
                out var NativeSurfaceHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build B-spline surface #{ifcBSpline.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            var surfaceType = ifcBSpline is IIfcRationalBSplineSurfaceWithKnots
                ? XSurfaceType.IfcRationalBSplineSurfaceWithKnots
                : XSurfaceType.IfcBSplineSurfaceWithKnots;

            return new Surface(NativeSurfaceHandle, surfaceType);
        }

        #endregion

        #region Sectioned Surface

        private SectionedSurface BuildSectionedSurface(IfcSectionedSurface ifcSurface)
        {
            int sectionCount = ifcSurface.CrossSections.Count;
            if (ifcSurface.CrossSectionPositions.Count != sectionCount)
                throw new XbimGeometryServiceException(
                    $"IfcSectionedSurface #{ifcSurface.EntityLabel}: " +
                    "number of cross-section positions doesn't match the number of cross-sections.");

            // Build directrix curve
            var directrix = _modelService.CurveFactory.Build(ifcSurface.Directrix);

            // Build section polyline points and locations
            var geoFactory = (GeometryFactory)_modelService.GeometryFactory;
            var locationHandles = new XLocation[sectionCount];
            var allPoints = new List<List<TaggedPoint>>(sectionCount);

            for (int i = 0; i < sectionCount; i++)
            {
                if (!(ifcSurface.CrossSections[i] is IfcOpenCrossProfileDef section))
                    throw new XbimGeometryServiceException(
                        $"IfcSectionedSurface #{ifcSurface.EntityLabel}: " +
                        $"cross-section [{i}] must be IfcOpenCrossProfileDef.");

                if (section.ProfileType != Xbim.Ifc4x3.ProfileResource.IfcProfileTypeEnum.CURVE)
                    throw new XbimGeometryServiceException(
                        $"IfcOpenCrossProfileDef #{section.EntityLabel}: " +
                        "ProfileType must be CURVE.");

                // Build location from linear placement
                var linearPlacement = ifcSurface.CrossSectionPositions[i];
                locationHandles[i] = (XLocation)geoFactory.BuildLocation(linearPlacement);

                // Extract widths, slopes, tags
                int segCount = section.Widths.Count;
                var widths = new double[segCount];
                var slopes = new double[segCount];

                for (int j = 0; j < segCount; j++)
                {
                    widths[j] = (double)section.Widths[j].Value;
                    slopes[j] = (double)section.Slopes[j].Value;
                }

                int tagCount = section.Tags.Count > 0 ? section.Tags.Count : segCount + 1;
                var tags = new string[tagCount];
                if (section.Tags.Count > 0)
                {
                    for (int j = 0; j < section.Tags.Count; j++)
                        tags[j] = section.Tags[j].Value?.ToString() ?? (j + 1).ToString();
                }
                else
                {
                    for (int j = 0; j < segCount + 1; j++)
                        tags[j] = (j + 1).ToString();
                }

                allPoints.Add(BuildPolylinePoints(widths, slopes, tags, section.HorizontalWidths));
            }

            // Align non-uniform profiles by inserting duplicate points for missing tags
            EnsureUniform(allPoints);

            int numPointsPerSection = allPoints[0].Count;

            // Flatten points for native call
            var flatPoints = new double[sectionCount * numPointsPerSection * 3];
            for (int s = 0; s < sectionCount; s++)
            {
                for (int p = 0; p < numPointsPerSection; p++)
                {
                    int idx = (s * numPointsPerSection + p) * 3;
                    flatPoints[idx] = allPoints[s][p].X;
                    flatPoints[idx + 1] = allPoints[s][p].Y;
                    flatPoints[idx + 2] = 0.0;
                }
            }

            var safeHandles = new SafeHandle[sectionCount];
            for (int i = 0; i < sectionCount; i++)
                safeHandles[i] = locationHandles[i].Handle;

            using var nativeHandles = new NativeHandleArray(safeHandles);

            int result = XbimGeometryNativeApi.xbim_surface_build_sectioned(
                ContextHandle,
                flatPoints,
                sectionCount,
                numPointsPerSection,
                nativeHandles.Ptrs,
                out var shapeHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to build sectioned surface #{ifcSurface.EntityLabel}: " +
                    XbimGeometryNativeApi.GetLastError());

            return new SectionedSurface(shapeHandle, directrix);
        }

        /// <summary>
        /// Converts widths and slopes into a sequence of tagged 2D polyline points.
        /// Starting from origin (0,0), each segment advances by its width/slope.
        /// </summary>
        private static List<TaggedPoint> BuildPolylinePoints(
            double[] widths, double[] slopes, string[] tags, bool horizontalWidths)
        {
            var points = new List<TaggedPoint>(widths.Length + 1);
            double cx = 0, cy = 0;
            points.Add(new TaggedPoint(cx, cy, tags[0]));

            for (int i = 0; i < widths.Length; i++)
            {
                double w = widths[i];
                double s = slopes[i];
                double dx, dy;

                if (horizontalWidths)
                {
                    dx = w;
                    dy = w * Math.Tan(s);
                }
                else
                {
                    dx = w * Math.Cos(s);
                    dy = w * Math.Sin(s);
                }

                cx += dx;
                cy += dy;
                points.Add(new TaggedPoint(cx, cy, tags[i + 1]));
            }

            return points;
        }

        /// <summary>
        /// Ensures all sections have the same number of points by inserting duplicate
        /// points where tags are missing (non-uniform cross-section alignment).
        /// </summary>
        private static void EnsureUniform(List<List<TaggedPoint>> allPoints)
        {
            // Check if already uniform
            int maxSize = 0;
            foreach (var pts in allPoints)
                maxSize = Math.Max(maxSize, pts.Count);

            bool uniform = true;
            foreach (var pts in allPoints)
            {
                if (pts.Count != maxSize)
                {
                    uniform = false;
                    break;
                }
            }

            if (uniform) return;

            // Align by inserting duplicate points for missing tags
            for (int i = 0; i < allPoints.Count - 1; i++)
            {
                var current = allPoints[i];
                var next = allPoints[i + 1];

                while (current.Count < maxSize && next.Count != current.Count)
                {
                    bool inserted = false;
                    for (int j = 1; j < next.Count; j++)
                    {
                        if (!ContainsTag(current, next[j].Tag))
                        {
                            current.Insert(j, new TaggedPoint(current[j - 1].X, current[j - 1].Y, next[j].Tag));
                            inserted = true;
                            break;
                        }
                    }
                    if (!inserted) break;
                }

                if (i > 0)
                {
                    var prev = allPoints[i - 1];
                    while (current.Count < maxSize && prev.Count != current.Count)
                    {
                        bool inserted = false;
                        for (int j = 1; j < prev.Count; j++)
                        {
                            if (!ContainsTag(current, prev[j].Tag))
                            {
                                current.Insert(j, new TaggedPoint(current[j - 1].X, current[j - 1].Y, prev[j].Tag));
                                inserted = true;
                                break;
                            }
                        }
                        if (!inserted) break;
                    }
                }
            }
        }

        private static bool ContainsTag(List<TaggedPoint> points, string tag)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Tag == tag)
                    return true;
            }
            return false;
        }

        private struct TaggedPoint
        {
            public double X;
            public double Y;
            public string Tag;

            public TaggedPoint(double x, double y, string tag)
            {
                X = x;
                Y = y;
                Tag = tag;
            }
        }

        #endregion
    }
}
