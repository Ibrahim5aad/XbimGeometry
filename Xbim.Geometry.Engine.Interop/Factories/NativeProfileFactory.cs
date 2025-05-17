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
    internal class NativeProfileFactory : IXProfileFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeProfileFactory(NativeModelGeometryService modelService, ILogger logger)
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RectangleProfileDef #{rectangleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CircleProfileDef #{circleProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build EllipseProfileDef #{ellipseProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RectangleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CircleHollowProfileDef #{hollowProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build IShapeProfileDef #{iProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build AsymmetricIShapeProfileDef #{asymProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build LShapeProfileDef #{lProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build TShapeProfileDef #{tProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build UShapeProfileDef #{uProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build ZShapeProfileDef #{zProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CShapeProfileDef #{cProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build TrapeziumProfileDef #{trapProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                    out var shapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeFactory.WrapFace(shapeHandle);
            }

            if (outerCurve is IIfcIndexedPolyCurve indexedPolyCurve)
            {
                return BuildArbitraryFromIndexedPolyCurve(indexedPolyCurve, arbitraryProfile.EntityLabel);
            }

            // For compound curves and other complex types, fall back to extracting
            // polyline points from the curve if possible
            if (outerCurve is IIfcCompositeCurve compositeCurve)
            {
                var points = ExtractPointsFromCompositeCurve(compositeCurve);
                if (points.Count < 3)
                    throw new InvalidOperationException(
                        $"ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel} composite curve has less than 3 unique points.");

                double[] pointsX = points.Select(p => p.Item1).ToArray();
                double[] pointsY = points.Select(p => p.Item2).ToArray();

                int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                    ContextHandle,
                    pointsX, pointsY, points.Count,
                    out var shapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build ArbitraryClosedProfileDef #{arbitraryProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeFactory.WrapFace(shapeHandle);
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

            // Extract coordinates from the point list
            List<(double, double)> points;

            if (pointList is IIfcCartesianPointList2D pointList2D)
            {
                points = new List<(double, double)>();
                foreach (var coordList in pointList2D.CoordList)
                {
                    var coords = coordList.ToList();
                    points.Add((coords[0], coords[1]));
                }
            }
            else
            {
                throw new NotSupportedException(
                    $"IndexedPolyCurve #{entityLabel} points type {pointList.ExpressType.ExpressName} is not supported for 2D profiles.");
            }

            if (points.Count < 3)
                throw new InvalidOperationException(
                    $"IndexedPolyCurve #{entityLabel} has less than 3 points.");

            // If there are segments, use them to order points; otherwise use points in order
            List<(double, double)> orderedPoints;
            if (indexedPolyCurve.Segments != null && indexedPolyCurve.Segments.Any())
            {
                orderedPoints = new List<(double, double)>();
                foreach (var segment in indexedPolyCurve.Segments)
                {
                    if (segment is IfcLineIndex lineIndex)
                    {
                        var indices = (System.Collections.IList)lineIndex.Value;
                        // Add all points except the last (it's the first of the next segment)
                        for (int i = 0; i < indices.Count - 1; i++)
                        {
                            int idx = (int)(long)indices[i]! - 1; // 1-based to 0-based
                            if (idx >= 0 && idx < points.Count)
                                orderedPoints.Add(points[idx]);
                        }
                    }
                    // Arc segments would need curve support - skip for now, treat as line
                }
                // Don't close - the native function will close automatically
            }
            else
            {
                // Use points directly, skip last point if it duplicates the first
                orderedPoints = new List<(double, double)>(points);
                if (orderedPoints.Count > 1)
                {
                    var first = orderedPoints[0];
                    var last = orderedPoints[orderedPoints.Count - 1];
                    if (Math.Abs(first.Item1 - last.Item1) < 1e-10 &&
                        Math.Abs(first.Item2 - last.Item2) < 1e-10)
                    {
                        orderedPoints.RemoveAt(orderedPoints.Count - 1);
                    }
                }
            }

            if (orderedPoints.Count < 3)
                throw new InvalidOperationException(
                    $"IndexedPolyCurve #{entityLabel} resolved to less than 3 unique points.");

            double[] pointsX = orderedPoints.Select(p => p.Item1).ToArray();
            double[] pointsY = orderedPoints.Select(p => p.Item2).ToArray();

            int result = XbimGeometryNativeApi.xbim_profile_build_arbitrary_closed(
                ContextHandle,
                pointsX, pointsY, orderedPoints.Count,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build IndexedPolyCurve profile #{entityLabel}: {XbimGeometryNativeApi.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
                foreach (var innerCurve in innerCurves)
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
                var outerNativeFace = (NativeFace)outerFace;
                var outerShapeHandle = outerNativeFace.Handle;

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
                return NativeShapeFactory.WrapFace(resultHandle);
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
                    out var shapeHandle);

                return result == 0 ? shapeHandle : null;
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
                    var nativeFace = (NativeFace)face;
                    handles.Add(nativeFace.Handle.DangerousGetHandle());
                    shapes.Add(nativeFace);
                }

                int result = XbimGeometryNativeApi.xbim_profile_build_composite(
                    ContextHandle,
                    handles.ToArray(),
                    handles.Count,
                    out var shapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build CompositeProfileDef #{compositeProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                // Composite profiles produce a compound of faces, not a single face.
                // We wrap directly as NativeFace since the P/Invoke surface_area call
                // works on compounds (it sums all face areas via BRepGProp).
                return new NativeFace(shapeHandle);
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

            var parentFace = BuildFace(parentProfile);
            var parentNativeFace = (NativeFace)parentFace;

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
                    parentNativeFace.Handle,
                    m00, m01, m02,
                    m10, m11, m12,
                    isNonUniform ? 1 : 0,
                    out var shapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build DerivedProfileDef #{derivedProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeFactory.WrapFace(shapeHandle);
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

            var parentFace = BuildFace(parentProfile);
            var parentNativeFace = (NativeFace)parentFace;

            try
            {
                int result = XbimGeometryNativeApi.xbim_profile_build_mirrored(
                    ContextHandle,
                    parentNativeFace.Handle,
                    out var shapeHandle);

                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to build MirroredProfileDef #{mirroredProfile.EntityLabel}: {XbimGeometryNativeApi.GetLastError()}");

                return NativeShapeFactory.WrapFace(shapeHandle);
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
                NativeGeometryFactory.BuildAxis2Placement3d(pos3D,
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

        #region Curve Extraction Helpers

        private List<(double, double)> ExtractPointsFromCompositeCurve(IIfcCompositeCurve compositeCurve)
        {
            var points = new List<(double, double)>();

            foreach (var segment in compositeCurve.Segments)
            {
                if (segment.ParentCurve is IIfcPolyline polyline)
                {
                    foreach (var pt in polyline.Points)
                    {
                        var coords = pt.Coordinates;
                        var point = (coords[0], coords[1]);
                        if (points.Count == 0 || PointDistance(points[points.Count - 1], point) > 1e-10)
                            points.Add(point);
                    }
                }
                else if (segment.ParentCurve is IIfcTrimmedCurve trimmedCurve)
                {
                    // Extract trimming points for line segments
                    foreach (var trim in trimmedCurve.Trim1)
                    {
                        if (trim is IIfcCartesianPoint cp)
                        {
                            var coords = cp.Coordinates;
                            var point = (coords[0], coords[1]);
                            if (points.Count == 0 || PointDistance(points[points.Count - 1], point) > 1e-10)
                                points.Add(point);
                        }
                    }
                }
            }

            // Remove duplicated closing point
            if (points.Count > 1 && PointDistance(points[0], points[points.Count - 1]) < 1e-10)
                points.RemoveAt(points.Count - 1);

            return points;
        }

        private static double PointDistance((double, double) a, (double, double) b)
        {
            double dx = a.Item1 - b.Item1;
            double dy = a.Item2 - b.Item2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
    }
}
