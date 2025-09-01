using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.GeometryResource;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds 2D profile faces from IFC profile definition entities (parametric, arbitrary,
    /// composite, derived). Extracts geometric parameters from IFC entities and delegates
    /// profile face construction to native OCCT operations.
    /// </summary>
    internal class ProfileFactory : IXProfileFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public ProfileFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        #region BuildFace

        public IXFace BuildFace(IIfcProfileDef profileDef)
        {
            if (!Enum.TryParse<XProfileDefType>(profileDef.ExpressType.ExpressName, out var profileType))
                throw new NotSupportedException(
                    $"Profile type is not implemented: {profileDef.ExpressType.ExpressName}");

            return profileType switch
            {
                // Basic parametric profiles
                XProfileDefType.IfcRectangleProfileDef => BuildRectangleFace((IIfcRectangleProfileDef)profileDef),
                XProfileDefType.IfcRoundedRectangleProfileDef => BuildRoundedRectangleFace((IIfcRoundedRectangleProfileDef)profileDef),
                XProfileDefType.IfcCircleProfileDef => BuildCircleFace((IIfcCircleProfileDef)profileDef),
                XProfileDefType.IfcEllipseProfileDef => BuildEllipseFace((IIfcEllipseProfileDef)profileDef),
                // Hollow profiles
                XProfileDefType.IfcRectangleHollowProfileDef => BuildRectangleHollowFace((IIfcRectangleHollowProfileDef)profileDef),
                XProfileDefType.IfcCircleHollowProfileDef => BuildCircleHollowFace((IIfcCircleHollowProfileDef)profileDef),
                // Structural profiles
                XProfileDefType.IfcIShapeProfileDef => BuildIShapeFace((IIfcIShapeProfileDef)profileDef),
                XProfileDefType.IfcAsymmetricIShapeProfileDef => BuildAsymmetricIShapeFace((IIfcAsymmetricIShapeProfileDef)profileDef),
                XProfileDefType.IfcLShapeProfileDef => BuildLShapeFace((IIfcLShapeProfileDef)profileDef),
                XProfileDefType.IfcTShapeProfileDef => BuildTShapeFace((IIfcTShapeProfileDef)profileDef),
                XProfileDefType.IfcUShapeProfileDef => BuildUShapeFace((IIfcUShapeProfileDef)profileDef),
                XProfileDefType.IfcZShapeProfileDef => BuildZShapeFace((IIfcZShapeProfileDef)profileDef),
                XProfileDefType.IfcCShapeProfileDef => BuildCShapeFace((IIfcCShapeProfileDef)profileDef),
                XProfileDefType.IfcTrapeziumProfileDef => BuildTrapeziumFace((IIfcTrapeziumProfileDef)profileDef),
                // Arbitrary profiles
                XProfileDefType.IfcArbitraryClosedProfileDef => BuildArbitraryClosedFace((IIfcArbitraryClosedProfileDef)profileDef),
                XProfileDefType.IfcArbitraryProfileDefWithVoids => BuildArbitraryWithVoidsFace((IIfcArbitraryProfileDefWithVoids)profileDef),
                // Composite / derived / mirrored profiles
                XProfileDefType.IfcCompositeProfileDef => BuildCompositeFace((IIfcCompositeProfileDef)profileDef),
                XProfileDefType.IfcDerivedProfileDef => BuildDerivedFace((IIfcDerivedProfileDef)profileDef),
                XProfileDefType.IfcMirroredProfileDef => BuildMirroredFace((IIfcMirroredProfileDef)profileDef),
                _ => throw new NotSupportedException(
                    $"Profile type {profileType} is not yet supported. " +
                    $"CenterLine/OpenCross profiles require curve factory (TOPO-006).")
            };
        }

        #endregion

        #region BuildWire / BuildEdge / BuildCurve (not yet implemented)

        public IXWire BuildWire(IIfcProfileDef profileDef)
        {
            throw new NotImplementedException(
                "BuildWire requires wire factory and topology support (TOPO-002).");
        }

        public IXEdge BuildEdge(IIfcProfileDef profileDef)
        {
            throw new NotImplementedException(
                "BuildEdge requires edge factory and topology support (TOPO-003).");
        }

        public IXCurve BuildCurve(IIfcProfileDef profileDef)
        {
            throw new NotImplementedException(
                "BuildCurve requires curve factory and topology support (TOPO-006).");
        }

        #endregion

        #region Basic Parametric Profiles

        private IXFace BuildRectangleFace(IIfcRectangleProfileDef rectangleProfile)
        {
            if (rectangleProfile.XDim <= 0 || rectangleProfile.YDim <= 0)
                throw new InvalidOperationException(
                    $"RectangleProfileDef #{rectangleProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(rectangleProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_rectangle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                rectangleProfile.XDim, rectangleProfile.YDim,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RectangleProfileDef #{rectangleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildRoundedRectangleFace(IIfcRoundedRectangleProfileDef roundedRectProfile)
        {
            if (roundedRectProfile.XDim <= 0 || roundedRectProfile.YDim <= 0)
                throw new InvalidOperationException(
                    $"RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel} has zero or negative dimensions.");

            if (roundedRectProfile.RoundingRadius <= 0)
                throw new InvalidOperationException(
                    $"RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel} has zero or negative rounding radius.");

            BuildProfilePlacement(roundedRectProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_rounded_rectangle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                roundedRectProfile.XDim, roundedRectProfile.YDim, roundedRectProfile.RoundingRadius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCircleFace(IIfcCircleProfileDef circleProfile)
        {
            if (circleProfile.Radius <= 0)
                throw new InvalidOperationException(
                    $"CircleProfileDef #{circleProfile.EntityLabel} has zero or negative radius.");

            BuildProfilePlacement(circleProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_circle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                circleProfile.Radius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CircleProfileDef #{circleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildEllipseFace(IIfcEllipseProfileDef ellipseProfile)
        {
            if (ellipseProfile.SemiAxis1 <= 0 || ellipseProfile.SemiAxis2 <= 0)
                throw new InvalidOperationException(
                    $"EllipseProfileDef #{ellipseProfile.EntityLabel} has zero or negative semi-axis.");

            BuildProfilePlacement(ellipseProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_ellipse(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ellipseProfile.SemiAxis1, ellipseProfile.SemiAxis2,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build EllipseProfileDef #{ellipseProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Hollow Profiles

        private IXFace BuildRectangleHollowFace(IIfcRectangleHollowProfileDef hollowProfile)
        {
            if (hollowProfile.XDim <= 0 || hollowProfile.YDim <= 0)
                throw new InvalidOperationException(
                    $"RectangleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative dimensions.");

            if (hollowProfile.WallThickness <= 0)
                throw new InvalidOperationException(
                    $"RectangleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative wall thickness.");

            BuildProfilePlacement(hollowProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double innerFillet = hollowProfile.InnerFilletRadius.HasValue
                ? (double)hollowProfile.InnerFilletRadius.Value : 0.0;
            double outerFillet = hollowProfile.OuterFilletRadius.HasValue
                ? (double)hollowProfile.OuterFilletRadius.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_rectangle_hollow(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                hollowProfile.XDim, hollowProfile.YDim, hollowProfile.WallThickness,
                innerFillet, outerFillet,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RectangleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCircleHollowFace(IIfcCircleHollowProfileDef hollowProfile)
        {
            if (hollowProfile.Radius <= 0)
                throw new InvalidOperationException(
                    $"CircleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative radius.");

            if (hollowProfile.WallThickness <= 0)
                throw new InvalidOperationException(
                    $"CircleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative wall thickness.");

            BuildProfilePlacement(hollowProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_circle_hollow(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                hollowProfile.Radius, hollowProfile.WallThickness,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CircleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Structural Profiles

        private IXFace BuildIShapeFace(IIfcIShapeProfileDef iProfile)
        {
            if (iProfile.OverallWidth <= 0 || iProfile.OverallDepth <= 0 ||
                iProfile.WebThickness <= 0 || iProfile.FlangeThickness <= 0)
                throw new InvalidOperationException(
                    $"IShapeProfileDef #{iProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(iProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double filletRadius = iProfile.FilletRadius.HasValue
                ? (double)iProfile.FilletRadius.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_ishape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                iProfile.OverallWidth, iProfile.OverallDepth,
                iProfile.WebThickness, iProfile.FlangeThickness,
                filletRadius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build IShapeProfileDef #{iProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildAsymmetricIShapeFace(IIfcAsymmetricIShapeProfileDef asymProfile)
        {
            if (asymProfile.BottomFlangeWidth <= 0 || asymProfile.OverallDepth <= 0 ||
                asymProfile.WebThickness <= 0 || asymProfile.BottomFlangeThickness <= 0 ||
                asymProfile.TopFlangeWidth <= 0)
                throw new InvalidOperationException(
                    $"AsymmetricIShapeProfileDef #{asymProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(asymProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double topFlangeThickness = asymProfile.TopFlangeThickness.HasValue
                ? (double)asymProfile.TopFlangeThickness.Value : 0.0;
            double bottomFilletRadius = asymProfile.BottomFlangeFilletRadius.HasValue
                ? (double)asymProfile.BottomFlangeFilletRadius.Value : 0.0;
            double topFilletRadius = asymProfile.TopFlangeFilletRadius.HasValue
                ? (double)asymProfile.TopFlangeFilletRadius.Value : 0.0;
            double bottomEdgeRadius = asymProfile.BottomFlangeEdgeRadius.HasValue
                ? (double)asymProfile.BottomFlangeEdgeRadius.Value : 0.0;
            double topEdgeRadius = asymProfile.TopFlangeEdgeRadius.HasValue
                ? (double)asymProfile.TopFlangeEdgeRadius.Value : 0.0;
            double bottomSlope = asymProfile.BottomFlangeSlope.HasValue
                ? (double)asymProfile.BottomFlangeSlope.Value : 0.0;
            double topSlope = asymProfile.TopFlangeSlope.HasValue
                ? (double)asymProfile.TopFlangeSlope.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_asymmetric_ishape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                asymProfile.BottomFlangeWidth, asymProfile.OverallDepth,
                asymProfile.WebThickness, asymProfile.BottomFlangeThickness,
                asymProfile.TopFlangeWidth, topFlangeThickness,
                bottomFilletRadius, topFilletRadius,
                bottomEdgeRadius, topEdgeRadius,
                bottomSlope, topSlope,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build AsymmetricIShapeProfileDef #{asymProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildLShapeFace(IIfcLShapeProfileDef lProfile)
        {
            if (lProfile.Depth <= 0 || lProfile.Thickness <= 0)
                throw new InvalidOperationException(
                    $"LShapeProfileDef #{lProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(lProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double width = lProfile.Width.HasValue ? (double)lProfile.Width.Value : 0.0;
            double filletRadius = lProfile.FilletRadius.HasValue
                ? (double)lProfile.FilletRadius.Value : 0.0;
            double edgeRadius = lProfile.EdgeRadius.HasValue
                ? (double)lProfile.EdgeRadius.Value : 0.0;
            double legSlope = lProfile.LegSlope.HasValue
                ? (double)lProfile.LegSlope.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_lshape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                lProfile.Depth, width, lProfile.Thickness,
                filletRadius, edgeRadius, legSlope,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build LShapeProfileDef #{lProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildTShapeFace(IIfcTShapeProfileDef tProfile)
        {
            if (tProfile.Depth <= 0 || tProfile.FlangeWidth <= 0 ||
                tProfile.WebThickness <= 0 || tProfile.FlangeThickness <= 0)
                throw new InvalidOperationException(
                    $"TShapeProfileDef #{tProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(tProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double filletRadius = tProfile.FilletRadius.HasValue
                ? (double)tProfile.FilletRadius.Value : 0.0;
            double flangeEdgeRadius = tProfile.FlangeEdgeRadius.HasValue
                ? (double)tProfile.FlangeEdgeRadius.Value : 0.0;
            double webEdgeRadius = tProfile.WebEdgeRadius.HasValue
                ? (double)tProfile.WebEdgeRadius.Value : 0.0;
            double flangeSlope = tProfile.FlangeSlope.HasValue
                ? (double)tProfile.FlangeSlope.Value : 0.0;
            double webSlope = tProfile.WebSlope.HasValue
                ? (double)tProfile.WebSlope.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_tshape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                tProfile.Depth, tProfile.FlangeWidth,
                tProfile.WebThickness, tProfile.FlangeThickness,
                filletRadius, flangeEdgeRadius, webEdgeRadius,
                flangeSlope, webSlope,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build TShapeProfileDef #{tProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildUShapeFace(IIfcUShapeProfileDef uProfile)
        {
            if (uProfile.Depth <= 0 || uProfile.FlangeWidth <= 0 ||
                uProfile.WebThickness <= 0 || uProfile.FlangeThickness <= 0)
                throw new InvalidOperationException(
                    $"UShapeProfileDef #{uProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(uProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double filletRadius = uProfile.FilletRadius.HasValue
                ? (double)uProfile.FilletRadius.Value : 0.0;
            double edgeRadius = uProfile.EdgeRadius.HasValue
                ? (double)uProfile.EdgeRadius.Value : 0.0;
            double flangeSlope = uProfile.FlangeSlope.HasValue
                ? (double)uProfile.FlangeSlope.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_ushape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                uProfile.Depth, uProfile.FlangeWidth,
                uProfile.WebThickness, uProfile.FlangeThickness,
                filletRadius, edgeRadius, flangeSlope,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build UShapeProfileDef #{uProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildZShapeFace(IIfcZShapeProfileDef zProfile)
        {
            if (zProfile.Depth <= 0 || zProfile.FlangeWidth <= 0 ||
                zProfile.WebThickness <= 0 || zProfile.FlangeThickness <= 0)
                throw new InvalidOperationException(
                    $"ZShapeProfileDef #{zProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(zProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double filletRadius = zProfile.FilletRadius.HasValue
                ? (double)zProfile.FilletRadius.Value : 0.0;
            double edgeRadius = zProfile.EdgeRadius.HasValue
                ? (double)zProfile.EdgeRadius.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_zshape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                zProfile.Depth, zProfile.FlangeWidth,
                zProfile.WebThickness, zProfile.FlangeThickness,
                filletRadius, edgeRadius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build ZShapeProfileDef #{zProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCShapeFace(IIfcCShapeProfileDef cProfile)
        {
            if (cProfile.Depth <= 0 || cProfile.Width <= 0 || cProfile.WallThickness <= 0)
                throw new InvalidOperationException(
                    $"CShapeProfileDef #{cProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(cProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            double girth = cProfile.Girth;
            double internalFilletRadius = cProfile.InternalFilletRadius.HasValue
                ? (double)cProfile.InternalFilletRadius.Value : 0.0;

            int result = XbimGeometryNativeApi.xbim_profile_build_cshape(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                cProfile.Depth, cProfile.Width, cProfile.WallThickness,
                girth, internalFilletRadius,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CShapeProfileDef #{cProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildTrapeziumFace(IIfcTrapeziumProfileDef trapProfile)
        {
            if (trapProfile.BottomXDim <= 0 || trapProfile.TopXDim <= 0 || trapProfile.YDim <= 0)
                throw new InvalidOperationException(
                    $"TrapeziumProfileDef #{trapProfile.EntityLabel} has zero or negative dimensions.");

            BuildProfilePlacement(trapProfile.Position,
                out double ox, out double oy, out double oz,
                out double zx, out double zy, out double zz,
                out double xx, out double xy, out double xz);

            int result = XbimGeometryNativeApi.xbim_profile_build_trapezium(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                trapProfile.BottomXDim, trapProfile.TopXDim,
                trapProfile.YDim, trapProfile.TopXOffset,
                out var NativeShapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build TrapeziumProfileDef #{trapProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Arbitrary Profiles

        private IXFace BuildArbitraryClosedFace(IIfcArbitraryClosedProfileDef arbitraryProfile)
        {
            var outerCurve = arbitraryProfile.OuterCurve;
            if (outerCurve == null)
                throw new InvalidOperationException(
                    $"ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel} has no OuterCurve.");

            // Extract polyline points from the outer curve
            if (outerCurve is IIfcPolyline polyline)
            {
                var points = polyline.Points.ToList();
                if (points.Count < 3)
                    throw new InvalidOperationException(
                        $"ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel} polyline has less than 3 points.");

                double[] pointsX = new double[points.Count];
                double[] pointsY = new double[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    var coords = points[i].Coordinates;
                    pointsX[i] = coords[0];
                    pointsY[i] = coords[1];
                }

                int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                    ContextHandle,
                    pointsX, pointsY, points.Count,
                    out var NativeShapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapFace(NativeShapeHandle);
            }

            if (outerCurve is IIfcIndexedPolyCurve indexedPolyCurve)
            {
                return BuildArbitraryFromIndexedPolyCurve(indexedPolyCurve, arbitraryProfile.EntityLabel);
            }

            if (outerCurve is IIfcCompositeCurve compositeCurve)
            {
                return BuildFaceFromCompositeCurve(compositeCurve, arbitraryProfile.EntityLabel);
            }

            throw new NotSupportedException(
                $"ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel} outer curve type " +
                $"{outerCurve.ExpressType.ExpressName} is not yet supported. " +
                "Only IIfcPolyline, IIfcIndexedPolyCurve, and IIfcCompositeCurve are supported.");
        }

        private IXFace BuildArbitraryFromIndexedPolyCurve(IIfcIndexedPolyCurve indexedPolyCurve, int entityLabel)
        {
            var pointList = indexedPolyCurve.Points;
            if (pointList == null)
                throw new InvalidOperationException(
                    $"IndexedPolyCurve #{entityLabel} has no Points coordinate list.");

            if (!(pointList is IIfcCartesianPointList2D pointList2D))
                throw new NotSupportedException(
                    $"IndexedPolyCurve #{entityLabel} points type {pointList.ExpressType.ExpressName} is not supported for 2D profiles.");

            var coords = new List<(double x, double y)>();
            foreach (var coordList in pointList2D.CoordList)
            {
                var c = coordList.ToList();
                coords.Add((c[0], c[1]));
            }

            if (coords.Count < 3)
                throw new InvalidOperationException(
                    $"IndexedPolyCurve #{entityLabel} has less than 3 points.");

            // If segments exist, build 2D curves respecting line/arc types
            if (indexedPolyCurve.Segments != null && indexedPolyCurve.Segments.Any())
            {
                var curves = new List<NativeCurve2dHandle>();
                try
                {
                    BuildCurves2dFromIndexedPolyCurve(indexedPolyCurve, curves);

                    if (curves.Count == 0)
                        throw new InvalidOperationException(
                            $"IndexedPolyCurve #{entityLabel} produced no valid 2D curves.");

                    return BuildFaceFrom2dCurves(curves, entityLabel);
                }
                finally
                {
                    foreach (var curve in curves)
                        curve.Dispose();
                }
            }

            // No segments: use points in order as a polygon
            // Skip last point if it duplicates the first (closing point)
            var orderedPoints = new List<(double x, double y)>(coords);
            if (orderedPoints.Count > 1)
            {
                var first = orderedPoints[0];
                var last = orderedPoints[orderedPoints.Count - 1];
                if (Math.Abs(first.x - last.x) < 1e-10 &&
                    Math.Abs(first.y - last.y) < 1e-10)
                {
                    orderedPoints.RemoveAt(orderedPoints.Count - 1);
                }
            }

            if (orderedPoints.Count < 3)
                throw new InvalidOperationException(
                    $"IndexedPolyCurve #{entityLabel} resolved to less than 3 unique points.");

            double[] pointsX = orderedPoints.Select(p => p.x).ToArray();
            double[] pointsY = orderedPoints.Select(p => p.y).ToArray();

            int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                ContextHandle,
                pointsX, pointsY, orderedPoints.Count,
                out var profileHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build IndexedPolyCurve profile #{entityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(profileHandle);
        }

        private IXFace BuildArbitraryWithVoidsFace(IIfcArbitraryProfileDefWithVoids arbitraryWithVoids)
        {
            // Build the outer face first
            var outerFace = BuildArbitraryClosedFace(arbitraryWithVoids);

            var innerCurves = arbitraryWithVoids.InnerCurves;
            if (innerCurves == null || !innerCurves.Any())
                return outerFace;

            // Build each inner curve as a closed face, then use with_voids to punch holes
            var innerHandles = new List<IntPtr>();
            var innerShapes = new List<IDisposable>();

            try
            {
                foreach (var innerCurve in innerCurves.DistinctBy(c => c.EntityLabel))
                {
                    NativeShapeHandle innerHandle = BuildClosedCurveAsShape(innerCurve);
                    if (innerHandle != null && !innerHandle.IsInvalid)
                    {
                        innerHandles.Add(innerHandle.DangerousGetHandle());
                        innerShapes.Add(innerHandle);
                    }
                }

                if (innerHandles.Count == 0)
                    return outerFace;

                // Get the outer face handle
                var outerShapeHandle = ((Face)outerFace).Handle;

                int result = XbimGeometryNativeApi.xbim_profile_build_with_voids(
                    ContextHandle,
                    outerShapeHandle,
                    innerHandles.ToArray(),
                    innerHandles.Count,
                    out var resultHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build ArbitraryProfileDefWithVoids #{arbitraryWithVoids.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                outerFace.Dispose();
                return NativeShapeWrapper.WrapFace(resultHandle);
            }
            finally
            {
                foreach (var shape in innerShapes)
                    shape.Dispose();
            }
        }

        private NativeShapeHandle BuildClosedCurveAsShape(IIfcCurve curve)
        {
            if (curve is IIfcPolyline polyline)
            {
                var points = polyline.Points.ToList();
                if (points.Count < 3) return null;

                double[] pointsX = new double[points.Count];
                double[] pointsY = new double[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    var coords = points[i].Coordinates;
                    pointsX[i] = coords[0];
                    pointsY[i] = coords[1];
                }

                int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                    ContextHandle,
                    pointsX, pointsY, points.Count,
                    out var profileHandle);

                return result == 0 ? profileHandle : null;
            }

            if (curve is IIfcCompositeCurve compositeCurve)
            {
                try
                {
                    var face = BuildFaceFromCompositeCurve(compositeCurve, 0);
                    return ((Face)face).Handle;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to build inner composite curve: {Message}", ex.Message);
                    return null;
                }
            }

            if (curve is IIfcIndexedPolyCurve indexedPolyCurve)
            {
                try
                {
                    var face = BuildArbitraryFromIndexedPolyCurve(indexedPolyCurve, 0);
                    return ((Face)face).Handle;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to build inner indexed poly curve: {Message}", ex.Message);
                    return null;
                }
            }

            _logger.LogWarning("Inner curve type {CurveType} is not yet supported for void extraction, skipping",
                curve.ExpressType.ExpressName);
            return null;
        }

        #endregion

        #region Composite / Derived / Mirrored Profiles

        private IXFace BuildCompositeFace(IIfcCompositeProfileDef compositeProfile)
        {
            var profiles = compositeProfile.Profiles?.ToList();
            if (profiles == null || profiles.Count == 0)
                throw new InvalidOperationException(
                    $"CompositeProfileDef #{compositeProfile.EntityLabel} has no profiles.");

            var handles = new List<IntPtr>();
            var shapes = new List<IDisposable>();

            try
            {
                foreach (var profile in profiles)
                {
                    var face = BuildFace(profile);
                    var Face = (Face)face;
                    handles.Add(Face.Handle.DangerousGetHandle());
                    shapes.Add(Face);
                }

                int result = XbimGeometryNativeApi.xbim_profile_build_composite(
                    ContextHandle,
                    handles.ToArray(),
                    handles.Count,
                    out var NativeShapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build CompositeProfileDef #{compositeProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                // Composite profiles produce a compound of faces, not a single face.
                // We wrap directly as Face since the P/Invoke surface_area call
                // works on compounds (it sums all face areas via BRepGProp).
                return new Face(NativeShapeHandle);
            }
            finally
            {
                foreach (var shape in shapes)
                    shape.Dispose();
            }
        }

        private IXFace BuildDerivedFace(IIfcDerivedProfileDef derivedProfile)
        {
            var parentProfile = derivedProfile.ParentProfile;
            if (parentProfile == null)
                throw new InvalidOperationException(
                    $"DerivedProfileDef #{derivedProfile.EntityLabel} has no ParentProfile.");

            var parentFace = (Face)BuildFace(parentProfile);

            var op = derivedProfile.Operator;
            if (op == null)
                throw new InvalidOperationException(
                    $"DerivedProfileDef #{derivedProfile.EntityLabel} has no Operator.");

            // Determine if this is a non-uniform scale transform
            bool isNonUniform = op is IIfcCartesianTransformationOperator2DnonUniform;

            // Build the 2x3 affine transform matrix from the IFC operator
            BuildTransform2DMatrix(op,
                out double m00, out double m01, out double m02,
                out double m10, out double m11, out double m12);

            try
            {
                int result = XbimGeometryNativeApi.xbim_profile_build_derived(
                    ContextHandle,
                    parentFace.Handle,
                    m00, m01, m02,
                    m10, m11, m12,
                    isNonUniform ? 1 : 0,
                    out var NativeShapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build DerivedProfileDef #{derivedProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapFace(NativeShapeHandle);
            }
            finally
            {
                parentFace.Dispose();
            }
        }

        private IXFace BuildMirroredFace(IIfcMirroredProfileDef mirroredProfile)
        {
            var parentProfile = mirroredProfile.ParentProfile;
            if (parentProfile == null)
                throw new InvalidOperationException(
                    $"MirroredProfileDef #{mirroredProfile.EntityLabel} has no ParentProfile.");

            var parentFace = (Face)BuildFace(parentProfile);

            try
            {
                int result = XbimGeometryNativeApi.xbim_profile_build_mirrored(
                    ContextHandle,
                    parentFace.Handle,
                    out var NativeShapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build MirroredProfileDef #{mirroredProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeWrapper.WrapFace(NativeShapeHandle);
            }
            finally
            {
                parentFace.Dispose();
            }
        }

        #endregion

        #region Placement Helpers

        /// <summary>
        /// Converts an IIfcAxis2Placement (from a parameterized profile's Position property)
        /// into the 9 axis2 placement doubles expected by the native profile C API.
        /// Profile positions are 2D placements mapped to 3D with Z=0.
        /// When the position is null, returns identity (origin at 0,0,0, Z=0,0,1, X=1,0,0).
        /// </summary>
        internal static void BuildProfilePlacement(
            IIfcAxis2Placement position,
            out double ox, out double oy, out double oz,
            out double zx, out double zy, out double zz,
            out double xx, out double xy, out double xz)
        {
            if (position == null)
            {
                ox = 0; oy = 0; oz = 0;
                zx = 0; zy = 0; zz = 1;
                xx = 1; xy = 0; xz = 0;
                return;
            }

            if (position is IIfcAxis2Placement2D pos2D)
            {
                // Extract origin
                if (pos2D.Location != null)
                {
                    var coords = pos2D.Location.Coordinates;
                    ox = coords[0];
                    oy = coords[1];
                }
                else
                {
                    ox = 0;
                    oy = 0;
                }
                oz = 0; // 2D profiles live in XY plane

                // Z direction is always (0,0,1) for 2D placements
                zx = 0; zy = 0; zz = 1;

                // Extract X direction from RefDirection, default to (1,0,0)
                if (pos2D.RefDirection != null)
                {
                    double dirX = pos2D.RefDirection.DirectionRatios[0];
                    double dirY = pos2D.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(dirX * dirX + dirY * dirY);
                    if (mag > 1e-15)
                    {
                        xx = dirX / mag;
                        xy = dirY / mag;
                    }
                    else
                    {
                        xx = 1;
                        xy = 0;
                    }
                }
                else
                {
                    xx = 1;
                    xy = 0;
                }
                xz = 0; // X direction Z component is always 0 for 2D placements
            }
            else if (position is IIfcAxis2Placement3D pos3D)
            {
                GeometryFactory.BuildAxis2Placement3d(pos3D,
                    out ox, out oy, out oz,
                    out zx, out zy, out zz,
                    out xx, out xy, out xz);
            }
            else
            {
                throw new NotSupportedException(
                    $"Unsupported profile placement type: {position.GetType().Name}");
            }
        }

        #endregion

        #region Transform Helpers

        /// <summary>
        /// Builds a 2x3 affine transform matrix from an IIfcCartesianTransformationOperator2D.
        /// The matrix format is: X' = m00*X + m01*Y + m02, Y' = m10*X + m11*Y + m12
        /// </summary>
        private static void BuildTransform2DMatrix(
            IIfcCartesianTransformationOperator op,
            out double m00, out double m01, out double m02,
            out double m10, out double m11, out double m12)
        {
            double d1x = 1, d1y = 0;
            double scale1 = op.Scl;
            double scale2 = scale1;

            if (op is IIfcCartesianTransformationOperator2DnonUniform nu)
                scale2 = nu.Scl2;

            double r11 = 1, r12 = 0;
            double r21 = 0, r22 = 1;

            if (op.Axis1 != null)
            {
                d1x = op.Axis1.X;
                d1y = op.Axis1.Y;
                double mag = Math.Sqrt(d1x * d1x + d1y * d1y);
                if (mag > 1e-15) { d1x /= mag; d1y /= mag; }

                r11 = d1x; r12 = d1y;
                r21 = -d1y; r22 = d1x;

                if (op.Axis2 != null)
                {
                    double vx = -d1y, vy = d1x;
                    double a2dot = op.Axis2.X * vx + op.Axis2.Y * vy;
                    if (a2dot < 0)
                    {
                        r21 = d1y;
                        r22 = -d1x;
                    }
                }
            }
            else if (op.Axis2 != null)
            {
                d1x = op.Axis2.X;
                d1y = op.Axis2.Y;
                double mag = Math.Sqrt(d1x * d1x + d1y * d1y);
                if (mag > 1e-15) { d1x /= mag; d1y /= mag; }

                r11 = d1y; r12 = -d1x;
                r21 = d1x; r22 = d1y;
            }

            double tx = op.LocalOrigin.X;
            double ty = op.LocalOrigin.Y;

            // Apply scale to the rotation matrix columns
            m00 = r11 * scale1;
            m01 = r21 * scale2;
            m02 = tx;
            m10 = r12 * scale1;
            m11 = r22 * scale2;
            m12 = ty;
        }

        #endregion

        #region Composite Curve Helpers

        /// <summary>
        /// Builds a planar face from a composite curve by constructing 2D curves
        /// for each segment, assembling them into a wire with shared vertices.
        /// </summary>
        private IXFace BuildFaceFromCompositeCurve(IIfcCompositeCurve compositeCurve, int entityLabel)
        {
            var curves = new List<NativeCurve2dHandle>();
            try
            {
                int lastLabel = -1;
                foreach (var segment in compositeCurve.Segments)
                {
                    // Skip duplicate segments (archicad bug workaround)
                    if (segment.EntityLabel == lastLabel)
                        continue;
                    lastLabel = segment.EntityLabel;

                    var parentCurve = segment.ParentCurve;
                    if (parentCurve == null) continue;

                    var segmentCurves = new List<NativeCurve2dHandle>();
                    BuildCurves2dFromCurve(parentCurve, segmentCurves);

                    if (!segment.SameSense)
                    {
                        // Reverse each curve and reverse the order (matching legacy curve->Reverse())
                        segmentCurves.Reverse();
                        foreach (var c in segmentCurves)
                            XbimGeometryNativeApi.xbim_curve2d_reverse(c);
                    }

                    curves.AddRange(segmentCurves);
                }

                if (curves.Count == 0)
                    throw new InvalidOperationException(
                        $"CompositeCurve for profile #{entityLabel} produced no valid 2D curves.");

                return BuildFaceFrom2dCurves(curves, entityLabel);
            }
            finally
            {
                foreach (var curve in curves)
                    curve.Dispose();
            }
        }

        /// <summary>
        /// Builds a wire from 2D curves and creates a planar face from the wire.
        /// </summary>
        private IXFace BuildFaceFrom2dCurves(List<NativeCurve2dHandle> curves, int entityLabel)
        {
            IntPtr[] curvePtrs = curves.Select(c => c.DangerousGetHandle()).ToArray();
            int wireResult = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                ContextHandle, curvePtrs, curvePtrs.Length,
                _modelService.Precision, _modelService.MinimumGap,
                out var wireHandle);

            try
            {
                if (wireResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build wire from 2D curves for profile #{entityLabel}: " +
                        XbimGeometryNativeApi.GetLastError());

                int faceResult = XbimGeometryNativeApi.xbim_face_build_from_wire(
                    ContextHandle, wireHandle, out var faceHandle);

                if (faceResult != 0)
                    throw new InvalidOperationException(
                        $"Failed to build face from 2D curve wire for profile #{entityLabel}: " +
                        XbimGeometryNativeApi.GetLastError());

                return NativeShapeWrapper.WrapFace(faceHandle);
            }
            finally
            {
                wireHandle?.Dispose();
            }
        }

        /// <summary>
        /// Dispatches curve types to the appropriate 2D curve builder.
        /// </summary>
        private void BuildCurves2dFromCurve(IIfcCurve curve, List<NativeCurve2dHandle> curves)
        {
            if (curve is IIfcPolyline polyline)
            {
                BuildCurves2dFromPolyline(polyline, curves);
            }
            else if (curve is IIfcTrimmedCurve trimmedCurve)
            {
                BuildCurve2dFromTrimmedCurve(trimmedCurve, curves);
            }
            else if (curve is IIfcCircle circle)
            {
                BuildCurve2dFromFullCircle(circle, curves);
            }
            else if (curve is IIfcIndexedPolyCurve indexedPolyCurve)
            {
                BuildCurves2dFromIndexedPolyCurve(indexedPolyCurve, curves);
            }
            else
            {
                _logger.LogWarning("Unsupported composite curve segment type: {CurveType}, skipping",
                    curve.ExpressType.ExpressName);
            }
        }

        private void BuildCurves2dFromPolyline(IIfcPolyline polyline, List<NativeCurve2dHandle> curves)
        {
            var points = polyline.Points.ToList();
            for (int i = 0; i < points.Count - 1; i++)
            {
                var c1 = points[i].Coordinates;
                var c2 = points[i + 1].Coordinates;
                double x1 = c1[0], y1 = c1[1];
                double x2 = c2[0], y2 = c2[1];

                double dx = x2 - x1, dy = y2 - y1;
                if (Math.Sqrt(dx * dx + dy * dy) < 1e-10)
                    continue;

                int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                    ContextHandle, x1, y1, x2, y2,
                    out var curveHandle);

                if (result == 0 && curveHandle != null && !curveHandle.IsInvalid)
                    curves.Add(curveHandle);
            }
        }

        private void BuildCurve2dFromTrimmedCurve(IIfcTrimmedCurve trimmedCurve, List<NativeCurve2dHandle> curves)
        {
            var basisCurve = trimmedCurve.BasisCurve;

            if (basisCurve is IIfcCircle ifcCircle)
            {
                BuildCurve2dFromTrimmedCircle(trimmedCurve, ifcCircle, curves);
            }
            else if (basisCurve is IIfcEllipse ifcEllipse)
            {
                BuildCurve2dFromTrimmedEllipse(trimmedCurve, ifcEllipse, curves);
            }
            else if (basisCurve is IIfcLine)
            {
                BuildCurve2dFromTrimmedLine(trimmedCurve, curves);
            }
            else
            {
                _logger.LogWarning(
                    "Unsupported trimmed curve basis type: {BasisType}, skipping",
                    basisCurve.ExpressType.ExpressName);
            }
        }

        private void BuildCurve2dFromTrimmedCircle(
            IIfcTrimmedCurve trimmedCurve, IIfcCircle ifcCircle, List<NativeCurve2dHandle> curves)
        {
            double radius = ifcCircle.Radius;
            ExtractPlacementCenter2D(ifcCircle.Position, out double cx, out double cy,
                out double refDirX, out double refDirY);

            int circleResult = XbimGeometryNativeApi.xbim_curve2d_build_circle(
                ContextHandle, cx, cy, radius, refDirX, refDirY,
                out var circleHandle);

            if (circleResult != 0)
            {
                _logger.LogWarning("Failed to build 2D circle curve: {Error}",
                    XbimGeometryNativeApi.GetLastError());
                return;
            }

            try
            {
                // Extract trim parameters — prefer CARTESIAN (project onto 2D circle)
                double u1, u2;
                double tolerance = _modelService.Precision;

                var startPt = GetCartesianTrimPoint(trimmedCurve.Trim1);
                if (startPt != null)
                {
                    int r1 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, circleHandle,
                        startPt.Value.x, startPt.Value.y, tolerance, out u1);
                    if (r1 != 0) { _logger.LogWarning("Failed to project start point onto 2D circle"); return; }
                }
                else
                {
                    double? param1 = GetParameterTrimValue(trimmedCurve.Trim1);
                    if (!param1.HasValue) { _logger.LogWarning("Cannot extract start trim for circle"); return; }
                    u1 = param1.Value * _modelService.RadianFactor;
                }

                var endPt = GetCartesianTrimPoint(trimmedCurve.Trim2);
                if (endPt != null)
                {
                    int r2 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, circleHandle,
                        endPt.Value.x, endPt.Value.y, tolerance, out u2);
                    if (r2 != 0) { _logger.LogWarning("Failed to project end point onto 2D circle"); return; }
                }
                else
                {
                    double? param2 = GetParameterTrimValue(trimmedCurve.Trim2);
                    if (!param2.HasValue) { _logger.LogWarning("Cannot extract end trim for circle"); return; }
                    u2 = param2.Value * _modelService.RadianFactor;
                }

                int sense = trimmedCurve.SenseAgreement ? 1 : 0;
                int arcResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_of_circle(
                    ContextHandle, circleHandle, u1, u2, sense,
                    out var arcHandle);

                if (arcResult == 0 && arcHandle != null && !arcHandle.IsInvalid)
                    curves.Add(arcHandle);
                else
                    _logger.LogWarning("Failed to build 2D circle arc: {Error}",
                        XbimGeometryNativeApi.GetLastError());
            }
            finally
            {
                circleHandle.Dispose();
            }
        }

        private void BuildCurve2dFromTrimmedEllipse(
            IIfcTrimmedCurve trimmedCurve, IIfcEllipse ifcEllipse, List<NativeCurve2dHandle> curves)
        {
            ExtractPlacementCenter2D(ifcEllipse.Position, out double cx, out double cy,
                out double refDirX, out double refDirY);

            int ellipseResult = XbimGeometryNativeApi.xbim_curve2d_build_ellipse(
                ContextHandle, cx, cy,
                ifcEllipse.SemiAxis1, ifcEllipse.SemiAxis2,
                refDirX, refDirY,
                out var ellipseHandle);

            if (ellipseResult != 0)
            {
                _logger.LogWarning("Failed to build 2D ellipse curve: {Error}",
                    XbimGeometryNativeApi.GetLastError());
                return;
            }

            try
            {
                double u1, u2;
                double tolerance = _modelService.Precision;

                var startPt = GetCartesianTrimPoint(trimmedCurve.Trim1);
                if (startPt != null)
                {
                    int r1 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, ellipseHandle,
                        startPt.Value.x, startPt.Value.y, tolerance, out u1);
                    if (r1 != 0) { _logger.LogWarning("Failed to project start point onto 2D ellipse"); return; }
                }
                else
                {
                    double? param1 = GetParameterTrimValue(trimmedCurve.Trim1);
                    if (!param1.HasValue) { _logger.LogWarning("Cannot extract start trim for ellipse"); return; }
                    u1 = param1.Value * _modelService.RadianFactor;
                }

                var endPt = GetCartesianTrimPoint(trimmedCurve.Trim2);
                if (endPt != null)
                {
                    int r2 = XbimGeometryNativeApi.xbim_curve2d_project_point(
                        ContextHandle, ellipseHandle,
                        endPt.Value.x, endPt.Value.y, tolerance, out u2);
                    if (r2 != 0) { _logger.LogWarning("Failed to project end point onto 2D ellipse"); return; }
                }
                else
                {
                    double? param2 = GetParameterTrimValue(trimmedCurve.Trim2);
                    if (!param2.HasValue) { _logger.LogWarning("Cannot extract end trim for ellipse"); return; }
                    u2 = param2.Value * _modelService.RadianFactor;
                }

                int sense = trimmedCurve.SenseAgreement ? 1 : 0;
                int arcResult = XbimGeometryNativeApi.xbim_curve2d_build_arc_of_ellipse(
                    ContextHandle, ellipseHandle, u1, u2, sense,
                    out var arcHandle);

                if (arcResult == 0 && arcHandle != null && !arcHandle.IsInvalid)
                    curves.Add(arcHandle);
                else
                    _logger.LogWarning("Failed to build 2D ellipse arc: {Error}",
                        XbimGeometryNativeApi.GetLastError());
            }
            finally
            {
                ellipseHandle.Dispose();
            }
        }

        private void BuildCurve2dFromTrimmedLine(IIfcTrimmedCurve trimmedCurve, List<NativeCurve2dHandle> curves)
        {
            var startPt = GetCartesianTrimPoint(trimmedCurve.Trim1);
            var endPt = GetCartesianTrimPoint(trimmedCurve.Trim2);

            if (startPt == null || endPt == null)
            {
                _logger.LogWarning("Trimmed line requires CARTESIAN trim points, skipping");
                return;
            }

            double x1 = startPt.Value.x, y1 = startPt.Value.y;
            double x2 = endPt.Value.x, y2 = endPt.Value.y;

            if (!trimmedCurve.SenseAgreement)
                (x1, y1, x2, y2) = (x2, y2, x1, y1);

            double dx = x2 - x1, dy = y2 - y1;
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-10)
                return;

            int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                ContextHandle, x1, y1, x2, y2,
                out var curveHandle);

            if (result == 0 && curveHandle != null && !curveHandle.IsInvalid)
                curves.Add(curveHandle);
        }

        private void BuildCurve2dFromFullCircle(IIfcCircle ifcCircle, List<NativeCurve2dHandle> curves)
        {
            ExtractPlacementCenter2D(ifcCircle.Position, out double cx, out double cy,
                out double refDirX, out double refDirY);

            int circResult = XbimGeometryNativeApi.xbim_curve2d_build_circle(
                ContextHandle, cx, cy, ifcCircle.Radius, refDirX, refDirY,
                out var circleHandle);

            if (circResult != 0) return;

            try
            {
                int trimResult = XbimGeometryNativeApi.xbim_curve2d_build_trimmed(
                    ContextHandle, circleHandle, 0, 2 * Math.PI, 1,
                    out var trimmedHandle);

                if (trimResult == 0 && trimmedHandle != null && !trimmedHandle.IsInvalid)
                    curves.Add(trimmedHandle);
            }
            finally
            {
                circleHandle.Dispose();
            }
        }

        private void BuildCurves2dFromIndexedPolyCurve(
            IIfcIndexedPolyCurve indexedPolyCurve, List<NativeCurve2dHandle> curves)
        {
            if (!(indexedPolyCurve.Points is IIfcCartesianPointList2D pointList2D))
            {
                _logger.LogWarning("IndexedPolyCurve points must be 2D for profiles, skipping");
                return;
            }

            var coords = new List<(double x, double y)>();
            foreach (var coordList in pointList2D.CoordList)
            {
                var c = coordList.ToList();
                coords.Add((c[0], c[1]));
            }

            if (indexedPolyCurve.Segments != null && indexedPolyCurve.Segments.Any())
            {
                foreach (var segment in indexedPolyCurve.Segments)
                {
                    if (segment is IfcLineIndex lineIndex)
                    {
                        var indices = (System.Collections.IList)lineIndex.Value;
                        for (int i = 0; i < indices.Count - 1; i++)
                        {
                            int idx1 = (int)(long)indices[i]! - 1;
                            int idx2 = (int)(long)indices[i + 1]! - 1;
                            if (idx1 < 0 || idx1 >= coords.Count || idx2 < 0 || idx2 >= coords.Count)
                                continue;

                            var p1 = coords[idx1];
                            var p2 = coords[idx2];
                            double dx = p2.x - p1.x, dy = p2.y - p1.y;
                            if (Math.Sqrt(dx * dx + dy * dy) < 1e-10) continue;

                            int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                                ContextHandle, p1.x, p1.y, p2.x, p2.y,
                                out var curveHandle);
                            if (result == 0 && curveHandle != null && !curveHandle.IsInvalid)
                                curves.Add(curveHandle);
                        }
                    }
                    else if (segment is IfcArcIndex arcIndex)
                    {
                        var indices = (System.Collections.IList)arcIndex.Value;
                        if (indices.Count != 3) continue;

                        int idx1 = (int)(long)indices[0]! - 1;
                        int idx2 = (int)(long)indices[1]! - 1;
                        int idx3 = (int)(long)indices[2]! - 1;
                        if (idx1 < 0 || idx1 >= coords.Count ||
                            idx2 < 0 || idx2 >= coords.Count ||
                            idx3 < 0 || idx3 >= coords.Count)
                            continue;

                        var p1 = coords[idx1];
                        var p2 = coords[idx2];
                        var p3 = coords[idx3];

                        int result = XbimGeometryNativeApi.xbim_curve2d_build_arc_3pt(
                            ContextHandle,
                            p1.x, p1.y, p2.x, p2.y, p3.x, p3.y,
                            out var curveHandle);
                        if (result == 0 && curveHandle != null && !curveHandle.IsInvalid)
                            curves.Add(curveHandle);
                    }
                }
            }
            else
            {
                for (int i = 0; i < coords.Count - 1; i++)
                {
                    var p1 = coords[i];
                    var p2 = coords[i + 1];
                    double dx = p2.x - p1.x, dy = p2.y - p1.y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 1e-10) continue;

                    int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                        ContextHandle, p1.x, p1.y, p2.x, p2.y,
                        out var curveHandle);
                    if (result == 0 && curveHandle != null && !curveHandle.IsInvalid)
                        curves.Add(curveHandle);
                }
            }
        }

        /// <summary>
        /// Extracts the center coordinates and reference direction from an IFC placement.
        /// Defaults to origin (0,0) and X-direction (1,0) when not specified.
        /// </summary>
        private static void ExtractPlacementCenter2D(
            IIfcAxis2Placement placement,
            out double cx, out double cy,
            out double refDirX, out double refDirY)
        {
            cx = 0; cy = 0;
            refDirX = 1; refDirY = 0;

            if (placement is IIfcAxis2Placement2D pos2D)
            {
                if (pos2D.Location != null)
                {
                    var coords = pos2D.Location.Coordinates;
                    cx = coords[0];
                    cy = coords[1];
                }
                if (pos2D.RefDirection != null)
                {
                    double dx = pos2D.RefDirection.DirectionRatios[0];
                    double dy = pos2D.RefDirection.DirectionRatios[1];
                    double mag = Math.Sqrt(dx * dx + dy * dy);
                    if (mag > 1e-15) { refDirX = dx / mag; refDirY = dy / mag; }
                }
            }
            else if (placement is IIfcAxis2Placement3D pos3D)
            {
                if (pos3D.Location != null)
                {
                    var coords = pos3D.Location.Coordinates;
                    cx = coords[0];
                    cy = coords[1];
                }
            }
        }

        private static (double x, double y)? GetCartesianTrimPoint(
            IEnumerable<IIfcTrimmingSelect> trims)
        {
            foreach (var trim in trims)
            {
                if (trim is IIfcCartesianPoint cp)
                {
                    var coords = cp.Coordinates;
                    return (coords[0], coords[1]);
                }
            }
            return null;
        }

        private static double? GetParameterTrimValue(
            IEnumerable<IIfcTrimmingSelect> trims)
        {
            foreach (var trim in trims)
            {
                if (trim is Xbim.Ifc4.MeasureResource.IfcParameterValue pv)
                    return (double)pv;
            }
            return null;
        }

        #endregion
    }
}
