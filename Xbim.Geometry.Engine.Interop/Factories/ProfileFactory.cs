using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;
using Xbim.Geometry.Exceptions;

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
            return _modelService.WireFactory.Build(profileDef);
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
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build RectangleProfileDef #{rectangleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildRoundedRectangleFace(IIfcRoundedRectangleProfileDef roundedRectProfile)
        {
            if (roundedRectProfile.XDim <= 0 || roundedRectProfile.YDim <= 0)
                throw new XbimGeometryServiceException(
                    $"RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel} has zero or negative dimensions.");

            if (roundedRectProfile.RoundingRadius <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCircleFace(IIfcCircleProfileDef circleProfile)
        {
            if (circleProfile.Radius <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build CircleProfileDef #{circleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildEllipseFace(IIfcEllipseProfileDef ellipseProfile)
        {
            if (ellipseProfile.SemiAxis1 <= 0 || ellipseProfile.SemiAxis2 <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build EllipseProfileDef #{ellipseProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Hollow Profiles

        private IXFace BuildRectangleHollowFace(IIfcRectangleHollowProfileDef hollowProfile)
        {
            if (hollowProfile.XDim <= 0 || hollowProfile.YDim <= 0)
                throw new XbimGeometryServiceException(
                    $"RectangleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative dimensions.");

            if (hollowProfile.WallThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build RectangleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCircleHollowFace(IIfcCircleHollowProfileDef hollowProfile)
        {
            if (hollowProfile.Radius <= 0)
                throw new XbimGeometryServiceException(
                    $"CircleHollowProfileDef #{hollowProfile.EntityLabel} has zero or negative radius.");

            if (hollowProfile.WallThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build CircleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Structural Profiles

        private IXFace BuildIShapeFace(IIfcIShapeProfileDef iProfile)
        {
            if (iProfile.OverallWidth <= 0 || iProfile.OverallDepth <= 0 ||
                iProfile.WebThickness <= 0 || iProfile.FlangeThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build IShapeProfileDef #{iProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildAsymmetricIShapeFace(IIfcAsymmetricIShapeProfileDef asymProfile)
        {
            if (asymProfile.BottomFlangeWidth <= 0 || asymProfile.OverallDepth <= 0 ||
                asymProfile.WebThickness <= 0 || asymProfile.BottomFlangeThickness <= 0 ||
                asymProfile.TopFlangeWidth <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build AsymmetricIShapeProfileDef #{asymProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildLShapeFace(IIfcLShapeProfileDef lProfile)
        {
            if (lProfile.Depth <= 0 || lProfile.Thickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build LShapeProfileDef #{lProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildTShapeFace(IIfcTShapeProfileDef tProfile)
        {
            if (tProfile.Depth <= 0 || tProfile.FlangeWidth <= 0 ||
                tProfile.WebThickness <= 0 || tProfile.FlangeThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build TShapeProfileDef #{tProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildUShapeFace(IIfcUShapeProfileDef uProfile)
        {
            if (uProfile.Depth <= 0 || uProfile.FlangeWidth <= 0 ||
                uProfile.WebThickness <= 0 || uProfile.FlangeThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build UShapeProfileDef #{uProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildZShapeFace(IIfcZShapeProfileDef zProfile)
        {
            if (zProfile.Depth <= 0 || zProfile.FlangeWidth <= 0 ||
                zProfile.WebThickness <= 0 || zProfile.FlangeThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build ZShapeProfileDef #{zProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildCShapeFace(IIfcCShapeProfileDef cProfile)
        {
            if (cProfile.Depth <= 0 || cProfile.Width <= 0 || cProfile.WallThickness <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build CShapeProfileDef #{cProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        private IXFace BuildTrapeziumFace(IIfcTrapeziumProfileDef trapProfile)
        {
            if (trapProfile.BottomXDim <= 0 || trapProfile.TopXDim <= 0 || trapProfile.YDim <= 0)
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"Failed to build TrapeziumProfileDef #{trapProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeWrapper.WrapFace(NativeShapeHandle);
        }

        #endregion

        #region Arbitrary Profiles

        private IXFace BuildArbitraryClosedFace(IIfcArbitraryClosedProfileDef arbitraryProfile)
        {
            var outerCurve = arbitraryProfile.OuterCurve;
            if (outerCurve == null)
                throw new XbimGeometryServiceException(
                    $"ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel} has no OuterCurve.");

            // Extract polyline points from the outer curve
            if (outerCurve is IIfcPolyline polyline)
            {
                var points = polyline.Points.ToList();
                if (points.Count < 3)
                    throw new XbimGeometryServiceException(
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
                    throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"IndexedPolyCurve #{entityLabel} has less than 3 points.");

            // If segments exist, build 2D curve via CurveFactory respecting line/arc types
            if (indexedPolyCurve.Segments != null && indexedPolyCurve.Segments.Any())
            {
                var curveFactory = (CurveFactory)_modelService.CurveFactory;
                var builtCurve = (XbimCurve2d)curveFactory.BuildCurve2d(indexedPolyCurve);
                var curves = new List<NativeCurve2dHandle> { builtCurve.DetachHandle() };
                try
                {
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
                throw new XbimGeometryServiceException(
                    $"IndexedPolyCurve #{entityLabel} resolved to less than 3 unique points.");

            double[] pointsX = orderedPoints.Select(p => p.x).ToArray();
            double[] pointsY = orderedPoints.Select(p => p.y).ToArray();

            int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                ContextHandle,
                pointsX, pointsY, orderedPoints.Count,
                out var profileHandle);

            if (result != 0)
                throw new XbimGeometryServiceException(
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
            var innerShapeHandles = new List<NativeShapeHandle>();

            try
            {
                foreach (var innerCurve in innerCurves.DistinctBy(c => c.EntityLabel))
                {
                    NativeShapeHandle innerHandle = BuildClosedCurveAsShape(innerCurve);
                    if (innerHandle != null && !innerHandle.IsInvalid)
                        innerShapeHandles.Add(innerHandle);
                }

                if (innerShapeHandles.Count == 0)
                    return outerFace;

                // Get the outer face handle
                var outerShapeHandle = ((XbimFace)outerFace).Handle;

                using var nativeInnerHandles = new NativeHandleArray(innerShapeHandles.ToArray());
                int result = XbimGeometryNativeApi.xbim_profile_build_with_voids(
                    ContextHandle,
                    outerShapeHandle,
                    nativeInnerHandles.Ptrs,
                    nativeInnerHandles.Length,
                    out var resultHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build ArbitraryProfileDefWithVoids #{arbitraryWithVoids.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                outerFace.Dispose();
                return NativeShapeWrapper.WrapFace(resultHandle);
            }
            finally
            {
                foreach (var h in innerShapeHandles)
                    h.Dispose();
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
                    return ((XbimFace)face).Handle;
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
                    return ((XbimFace)face).Handle;
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
                throw new XbimGeometryServiceException(
                    $"CompositeProfileDef #{compositeProfile.EntityLabel} has no profiles.");

            var faceShapes = new List<XbimFace>();

            try
            {
                foreach (var profile in profiles)
                    faceShapes.Add((XbimFace)BuildFace(profile));

                using var nativeHandles = new NativeHandleArray(
                    faceShapes.ConvertAll(f => f.Handle).ToArray());

                int result = XbimGeometryNativeApi.xbim_profile_build_composite(
                    ContextHandle,
                    nativeHandles.Ptrs,
                    nativeHandles.Length,
                    out var compositeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build CompositeProfileDef #{compositeProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                // Composite profiles produce a compound of faces, not a single face.
                // We wrap directly as XbimFace since the P/Invoke surface_area call
                // works on compounds (it sums all face areas via BRepGProp).
                return new XbimFace(compositeHandle);
            }
            finally
            {
                foreach (var f in faceShapes)
                    f.Dispose();
            }
        }

        private IXFace BuildDerivedFace(IIfcDerivedProfileDef derivedProfile)
        {
            var parentProfile = derivedProfile.ParentProfile;
            if (parentProfile == null)
                throw new XbimGeometryServiceException(
                    $"DerivedProfileDef #{derivedProfile.EntityLabel} has no ParentProfile.");

            var parentFace = (XbimFace)BuildFace(parentProfile);

            var op = derivedProfile.Operator;
            if (op == null)
                throw new XbimGeometryServiceException(
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
                    throw new XbimGeometryServiceException(
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
                throw new XbimGeometryServiceException(
                    $"MirroredProfileDef #{mirroredProfile.EntityLabel} has no ParentProfile.");

            var parentFace = (XbimFace)BuildFace(parentProfile);

            try
            {
                int result = XbimGeometryNativeApi.xbim_profile_build_mirrored(
                    ContextHandle,
                    parentFace.Handle,
                    out var NativeShapeHandle);

                if (result != 0)
                    throw new XbimGeometryServiceException(
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
                    throw new XbimGeometryServiceException(
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
            using var nativeCurves = new NativeHandleArray(curves.ToArray());
            int wireResult = XbimGeometryNativeApi.xbim_wire_build_from_2d_curves(
                ContextHandle, nativeCurves.Ptrs, nativeCurves.Length,
                _modelService.Precision, _modelService.MinimumGap,
                out var wireHandle);

            try
            {
                if (wireResult != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to build wire from 2D curves for profile #{entityLabel}: " +
                        XbimGeometryNativeApi.GetLastError());

                int faceResult = XbimGeometryNativeApi.xbim_face_build_from_wire(
                    ContextHandle, wireHandle, out var faceHandle);

                if (faceResult != 0)
                    throw new XbimGeometryServiceException(
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
        /// Builds 2D curves from an IFC curve and adds the detached handles to the list.
        /// Multi-point polylines are expanded into separate line segments to preserve
        /// individual edge topology in the profile wire.
        /// </summary>
        private void BuildCurves2dFromCurve(IIfcCurve curve, List<NativeCurve2dHandle> curves)
        {
            // Multi-point polylines must produce separate line edges, not a single
            // composite BSpline. The pipe sweep (BRepOffsetAPI_MakePipeShell) creates
            // one face per profile edge per spine segment; merging edges into a BSpline
            // changes the face topology and breaks corner transitions.
            if (curve is IIfcPolyline polyline && polyline.Points.Count > 2)
            {
                for (int i = 0; i < polyline.Points.Count - 1; i++)
                {
                    var p1 = polyline.Points[i];
                    var p2 = polyline.Points[i + 1];
                    double sx = p1.Coordinates[0], sy = p1.Coordinates[1];
                    double ex = p2.Coordinates[0], ey = p2.Coordinates[1];

                    double dist = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
                    if (dist < _modelService.Precision) continue;

                    int result = XbimGeometryNativeApi.xbim_curve2d_build_line(
                        ContextHandle, sx, sy, ex, ey, out var lineHandle);

                    if (result != 0)
                        throw new XbimGeometryServiceException(
                            $"Failed to build line segment for polyline #{curve.EntityLabel}: " +
                            XbimGeometryNativeApi.GetLastError());

                    curves.Add(lineHandle);
                }
                return;
            }

            var curveFactory = (CurveFactory)_modelService.CurveFactory;
            var builtCurve = (XbimCurve2d)curveFactory.BuildCurve2d(curve);
            curves.Add(builtCurve.DetachHandle());
        }

        #endregion
    }
}
