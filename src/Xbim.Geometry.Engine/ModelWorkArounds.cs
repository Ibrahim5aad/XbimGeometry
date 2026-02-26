using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.Interfaces;
using Microsoft.Extensions.Logging;

namespace Xbim.Geometry.Engine
{
    /// <summary>
    /// Extension methods for <see cref="IModel"/>s applying workarounds for known issues in varuous authoring tool versions
    /// </summary>
    public static class ModelWorkArounds
    {
        //this bug exists in ifc files exported by Revit up to releases 20.1
        internal const string RevitIncorrectBsplineSweptCurve = "#RevitIncorrectBsplineSweptCurve";
        //this bug exists in all current Revit exported Ifc files
        internal const string RevitIncorrectArcCentreSweptCurve = "#RevitIncorrectArcCentreSweptCurve";
        //this bug exists in all current Revit exported Ifc files
        internal const string RevitSweptSurfaceExtrusionInFeet = "#RevitSweptSurfaceExtrusionInFeet";
        const string PolylineTrimLengthOneForEntireLine = "#PolylineTrimLengthOneForEntireLine";

        // Incorrect precision specified in Archicad models,
        // we make the model more precise since Archicad is casual about it.
        const string ArchicadPrecisionWorkaround = "#ArchicadPrecisionWorkaround";

        // we do not have an implementation of composite curves used for 3d placement, 
        // this woraround replaces the curve in 2d point by distance entity with a gradient curve
        const string NotImplemented2DPointByDistanceWorkaround = "#NotImplemented2DPointByDistanceWorkaround";

        /// <summary>
        /// Adds ArchiCAD specific workarounds
        /// </summary>
        /// <param name="model"></param>
        /// <param name="_logger"></param>
        public static void AddArchicadWorkArounds(this IModel model, Microsoft.Extensions.Logging.ILogger _logger)
        {
            var header = model.Header;
            var modelFactors = model.ModelFactors as XbimModelFactors;
            string byPattern = @"by Graphisoft ArchiCAD";

            if (header.FileName == null || string.IsNullOrWhiteSpace(header.FileName.OriginatingSystem))
                return; //nothing to do
            var matches = Regex.Matches(header.FileName.OriginatingSystem, byPattern, RegexOptions.IgnoreCase);
            if (matches.Count > 0) //looks like Archicad
            {
                if (modelFactors.ApplyWorkAround(ArchicadPrecisionWorkaround)) // if the workadound has been added then the precision is already been changed.
                    return;
                var evaluatingPrecision = modelFactors.Precision;
                // faces of IFCCLOSEDSHELLs should be larger than precision
                foreach (IIfcClosedShell shell in model.Instances.OfType<IIfcClosedShell>())
                {
                    foreach (var face in shell.CfsFaces)
                    {
                        foreach (var faceBound in face.Bounds)
                        {
                            var lpPrec = GetPrec(faceBound.Bound, evaluatingPrecision);
                            if (lpPrec)
                            {
                                // we apply the workaround and exit
                                _logger.LogWarning("Added ArchicadPrecisionWorkaround for #{ifcEntityLabel}.", faceBound.Bound.EntityLabel);
                                modelFactors.Precision /= 100;
                                modelFactors.AddWorkAround(ArchicadPrecisionWorkaround);
                                return;
                            }
                        }
                    }
                }
            }
        }

        private static bool GetPrec(IIfcLoop bound, double precision)
        {
            if (bound is IIfcPolyLoop pl)
            {
                var dropped = 0;
                var count = pl.Polygon.Count();
                var last = pl.Polygon[count - 1]; // start from last, we measure all distances closing the loop
                XbimPoint3D prevPoint = new XbimPoint3D(last.X, last.Y, last.Z);
                for (int i = 0; i < count; i++)
                {
                    XbimPoint3D thisPoint = new XbimPoint3D(pl.Polygon[i].X, pl.Polygon[i].Y, pl.Polygon[i].Z);
                    var dist = (prevPoint - thisPoint).Length;
                    if (dist < precision)
                    {
                        dropped++;
                    }
                    prevPoint = thisPoint;
                }
                if (count - dropped < 3)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Adds a work around for versions of the Revit exporter prior to and inclusing Version(17, 4, 0, 0);
        /// This did not correctly align linear extrusions bounds and surface due to a missing placement value
        /// </summary>
        /// <param name="model"></param>
        /// <returns>The lookup tag in ModelFactors.WorkArounds for the workaround or null if not required due to a later version of the exporter</returns>
        public static void AddRevitWorkArounds(this IModel model)
        {
            //it looks like all revit exports up to the 2020 release do not consider the local placement, so broadening the previous catch
            var header = model.Header;
            var modelFactors = model.ModelFactors as XbimModelFactors;
            //typical pattern for the revit exporter
            string revitPattern = @"- Exporter (\d*.\d*.\d*.\d*)";
            string revitAltUIPattern = @"- Exporter- Alternate UI (\d*.\d*.\d*.\d*)";
            if (header.FileName == null || string.IsNullOrWhiteSpace(header.FileName.OriginatingSystem))
                return; //nothing to do
            var matches = Regex.Matches(header.FileName.OriginatingSystem, revitPattern, RegexOptions.IgnoreCase);
            var matchesAltUI = Regex.Matches(header.FileName.OriginatingSystem, revitAltUIPattern, RegexOptions.IgnoreCase);
            string version = null;
            if (matches.Count > 0 && matches[0].Groups.Count == 2)
                version = matches[0].Groups[1].Value;
            else if (matchesAltUI.Count > 0 && matchesAltUI[0].Groups.Count == 2)
                version = matchesAltUI[0].Groups[1].Value;
            if (matches.Count > 0 || matchesAltUI.Count > 0) //looks like Revit
            {
                //SurfaceOfLinearExtrusion bug found in all current versions, comment this code out when it is fixed               
                modelFactors.AddWorkAround(RevitIncorrectArcCentreSweptCurve);
                modelFactors.AddWorkAround(RevitSweptSurfaceExtrusionInFeet);
                if (!string.IsNullOrEmpty(version)) //we have the build versions
                {
                    if (Version.TryParse(version, out Version modelVersion))
                    {
                        
                        var revitIncorrectBsplineSweptCurveVersion = new Version(20, 0, 0, 500);
                        if (modelVersion <= revitIncorrectBsplineSweptCurveVersion)
                        {
                            modelFactors.AddWorkAround(RevitIncorrectBsplineSweptCurve);
                        }
                    }

                }
            }
        }
        /// <summary>
        /// In some processors the directrix of a sweep has a trimmed polyline, where the upper trim parameter has been set to 1
        /// The publisher incorrectly intended to mean the entire length of the Polyline
        /// The Polyline should be defined as the sum of the lengths of all of the segments, and partial segments required
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        public static string AddWorkAroundTrimForPolylinesIncorrectlySetToOneForEntireCurve(this IModel model)
        {
            var header = model.Header;
            var modelFactors = model.ModelFactors as XbimModelFactors;
            modelFactors.AddWorkAround(PolylineTrimLengthOneForEntireLine);
            return PolylineTrimLengthOneForEntireLine;
        }

        /// <summary>
        /// Because xbim does not implement some cases for ifcpointbydistanceexpression we adjust the ifc to replace those scenarios 
        /// with one that can be calculated
        /// </summary>
        /// <param name="model">The model to fix</param>
        /// <param name="logger"></param>
        public static bool AddNotImplemented2DPointByDistanceWorkaround(this IModel model, ILogger logger)
        {
            // we need to replace IfcCompositeCurve but not their subclasses
            //
            var curvesToReplace = model.Instances.OfType<Ifc4x3.GeometryResource.IfcPointByDistanceExpression>()
                .Where(x =>
                    x.BasisCurve.GetType().Name == nameof(Xbim.Ifc4x3.GeometryResource.IfcCompositeCurve) // testing by name to avoid subclasses
                    ).Select(x => x.BasisCurve.EntityLabel).Distinct().ToList();
            if (!curvesToReplace.Any())
                return false;
            var modelFactors = model.ModelFactors as XbimModelFactors;
            if (modelFactors is null)
            {
                logger.LogWarning("Could not evaluated ArchicadPrecisionWorkaround.");
                return false;
            }
            if (modelFactors.ApplyWorkAround(NotImplemented2DPointByDistanceWorkaround)) // if the workadound has been added then the precision is already been changed.
                return false;
            logger.LogInformation("Applying NotImplemented2DPointByDistance Workaround.");
            using (var txn = model.BeginTransaction("Create new entities"))
            {
                foreach (var curveId in curvesToReplace)
                {
                    var curve = model.Instances[curveId] as Xbim.Ifc4x3.GeometryResource.IfcCompositeCurve;
                    if (curve is null)
                        continue;
                    var relavantPoints = model.Instances.OfType<Ifc4x3.GeometryResource.IfcPointByDistanceExpression>().Where(x => x.BasisCurve.EntityLabel == curveId).ToList();
                    var maxDistanceObj = relavantPoints.Select(x => x.DistanceAlong).Max(x => x.Value);
                    if (maxDistanceObj is not double mxD)
                    {
                        logger.LogError("Invalid max distance when NotImplemented2DPointByDistance Workaround on {entityLabel}.", curveId);
                        continue;
                    }
                    // var maxParameterObj = model.Instances.OfType<IfcPointByDistanceExpression>().Where(x => x.DistanceAlong is IfcParameterValue).Select(x => x.DistanceAlong).Max(x => x.Value);
                    // we create an horizontal line segment that covers the entire lenght of the gradient curve (maxDistance)
                    var zeroPoint = model.Instances.New<Xbim.Ifc4x3.GeometryResource.IfcCartesianPoint>((x) => x.SetXY(0, 0));
                    var xDirection = model.Instances.New<Xbim.Ifc4x3.GeometryResource.IfcDirection>((x) => x.SetXY(1, 0));
                    var xVector = model.Instances.New<Xbim.Ifc4x3.GeometryResource.IfcVector>((x) =>
                    {
                        x.Orientation = xDirection;
                        x.Magnitude = new Ifc4x3.MeasureResource.IfcLengthMeasure(1);
                    });
                    var horizontalLine = model.Instances.New<Xbim.Ifc4x3.GeometryResource.IfcLine>((x) =>
                    {
                        x.Pnt = zeroPoint;
                        x.Dir = xVector;
                    });

                    var segmentPlacement = model.Instances.New<Xbim.Ifc4x3.GeometryResource.IfcAxis2Placement2D>((x) => x.Location = zeroPoint); // direction defaults to 1.0, 0.0 according to specs
                    var curveSegment = model.Instances.New<Ifc4x3.GeometryResource.IfcCurveSegment>((x) =>
                    {
                        x.Transition = Ifc4x3.GeometryResource.IfcTransitionCode.CONTSAMEGRADIENTSAMECURVATURE;
                        x.SegmentStart = new Ifc4x3.MeasureResource.IfcLengthMeasure(0);
                        x.SegmentLength = new Ifc4x3.MeasureResource.IfcLengthMeasure(mxD + modelFactors.OneMeter);
                        x.Placement = segmentPlacement;
                        x.ParentCurve = horizontalLine;
                    });
                    var gradient = model.Instances.New<Ifc4x3.GeometryResource.IfcGradientCurve>((x) =>
                    {
                        x.Segments.Add(curveSegment);
                        x.SelfIntersect = false;
                        x.BaseCurve = curve;
                    });

                    relavantPoints.ForEach(x => x.BasisCurve = gradient);
                }
                txn.Commit();
                modelFactors.AddWorkAround(NotImplemented2DPointByDistanceWorkaround);
            }
            return true;
        }

        #region Swept Surface Workaround Application

        /// <summary>
        /// Checks whether the BSpline swept-curve workaround should be applied to
        /// this surface of linear extrusion. When true, the surface Position must be
        /// skipped because the BSpline control points already include the placement.
        /// Applies to Revit exports version 20.0.0.500 and earlier.
        /// </summary>
        internal static bool ShouldApplyBsplineWorkaround(IModel model, IIfcSurfaceOfLinearExtrusion ifcExtrusion)
        {
            if (ifcExtrusion.SweptCurve is not IIfcArbitraryOpenProfileDef pDef)
                return false;

            // Check if the profile curve is a BSpline (directly or wrapped in a TrimmedCurve)
            IIfcBSplineCurveWithKnots bspline;
            if (pDef.Curve is IIfcTrimmedCurve tc)
                bspline = tc.BasisCurve as IIfcBSplineCurveWithKnots;
            else
                bspline = pDef.Curve as IIfcBSplineCurveWithKnots;

            if (bspline == null)
                return false;

            if (ifcExtrusion.Position == null)
                return false;

            var modelFactors = model.ModelFactors as XbimModelFactors;
            return modelFactors?.ApplyWorkAround(RevitIncorrectBsplineSweptCurve) == true;
        }

        /// <summary>
        /// Fixes a bug in early Revit exporters where the arc centre in an
        /// IfcArbitraryOpenProfileDef (TrimmedCurve of a Circle) is transformed twice.
        /// Recalculates the correct centre from the trim points and radius, temporarily
        /// updates the model, invokes the curve builder, then rolls back.
        /// </summary>
        /// <param name="model">The IFC model (must support transactions).</param>
        /// <param name="ifcExtrusion">The surface of linear extrusion entity.</param>
        /// <param name="buildCurve">Delegate that builds a curve from an IFC curve definition.</param>
        /// <param name="fixedCurve">The curve built from the corrected geometry, if the fix was applied.</param>
        /// <returns>True if the workaround was applied; false if conditions were not met.</returns>
        internal static bool TryFixArcCentreSweptCurve(
            IModel model,
            IIfcSurfaceOfLinearExtrusion ifcExtrusion,
            Func<IIfcCurve, IXCurve> buildCurve,
            out IXCurve fixedCurve)
        {
            fixedCurve = null;

            var modelFactors = model.ModelFactors as XbimModelFactors;
            if (modelFactors?.ApplyWorkAround(RevitIncorrectArcCentreSweptCurve) != true)
                return false;

            if (ifcExtrusion.Position == null)
                return false;

            if (ifcExtrusion.SweptCurve is not IIfcArbitraryOpenProfileDef openProfile)
                return false;

            if (openProfile.Curve is not IIfcTrimmedCurve tc)
                return false;

            if (tc.BasisCurve is not IIfcCircle circle)
                return false;

            var trim1 = tc.Trim1.OfType<IIfcCartesianPoint>().FirstOrDefault();
            var trim2 = tc.Trim2.OfType<IIfcCartesianPoint>().FirstOrDefault();
            if (trim1 == null || trim2 == null)
                return false;

            // Recalculate the correct arc centre from the two trim points and the radius
            double p1X = trim1.X, p1Y = trim1.Y;
            double p2X = trim2.X, p2Y = trim2.Y;
            double radius = circle.Radius;
            double rSq = radius * radius;

            double q = Math.Sqrt((p2X - p1X) * (p2X - p1X) + (p2Y - p1Y) * (p2Y - p1Y));
            double midX = (p1X + p2X) / 2.0;
            double midY = (p1Y + p2Y) / 2.0;
            double halfQ = q / 2.0;
            double centreX = midX - Math.Sqrt(rSq - halfQ * halfQ) * (p1Y - p2Y) / q;
            double centreY = midY - Math.Sqrt(rSq - halfQ * halfQ) * (p2X - p1X) / q;

            // Temporarily update the circle's position via a transaction, build the curve,
            // then roll back to restore the original IFC data
            var placement = circle.Position as IIfcPlacement;
            if (placement?.Location == null)
                return false;

            var txn = model.BeginTransaction("Fix arc centre");
            try
            {
                placement.Location.Coordinates[0] = centreX;
                placement.Location.Coordinates[1] = centreY;
                placement.Location.Coordinates[2] = 0;

                fixedCurve = buildCurve(openProfile.Curve);
            }
            finally
            {
                txn.RollBack();
            }

            return fixedCurve != null;
        }

        #endregion
    }
}
