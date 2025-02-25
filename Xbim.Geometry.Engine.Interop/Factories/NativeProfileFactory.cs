using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Cross-platform implementation of <see cref="IXProfileFactory"/> porting the
    /// IFC data extraction from the C++/CLI ProfileFactory. Extracts geometric parameters
    /// from IFC profile entities in C# and delegates profile construction to native P/Invoke calls.
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
                XProfileDefType.IfcRectangleProfileDef => BuildRectangleFace((IIfcRectangleProfileDef)profileDef),
                XProfileDefType.IfcRoundedRectangleProfileDef => BuildRoundedRectangleFace((IIfcRoundedRectangleProfileDef)profileDef),
                XProfileDefType.IfcCircleProfileDef => BuildCircleFace((IIfcCircleProfileDef)profileDef),
                XProfileDefType.IfcEllipseProfileDef => BuildEllipseFace((IIfcEllipseProfileDef)profileDef),
                _ => throw new NotSupportedException(
                    $"Profile type {profileType} requires additional native support (PROFILE-001..PROFILE-004).")
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

            int result = NativeMethods.xbim_profile_build_rectangle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                rectangleProfile.XDim, rectangleProfile.YDim,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RectangleProfileDef #{rectangleProfile.EntityLabel}: {NativeMethods.GetLastError()}");

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

            int result = NativeMethods.xbim_profile_build_rounded_rectangle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                roundedRectProfile.XDim, roundedRectProfile.YDim, roundedRectProfile.RoundingRadius,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build RoundedRectangleProfileDef #{roundedRectProfile.EntityLabel}: {NativeMethods.GetLastError()}");

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

            int result = NativeMethods.xbim_profile_build_circle(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                circleProfile.Radius,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build CircleProfileDef #{circleProfile.EntityLabel}: {NativeMethods.GetLastError()}");

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

            int result = NativeMethods.xbim_profile_build_ellipse(
                ContextHandle,
                ox, oy, oz, zx, zy, zz, xx, xy, xz,
                ellipseProfile.SemiAxis1, ellipseProfile.SemiAxis2,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build EllipseProfileDef #{ellipseProfile.EntityLabel}: {NativeMethods.GetLastError()}");

            return NativeShapeFactory.WrapFace(shapeHandle);
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
    }
}
