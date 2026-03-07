using System;
using System.Collections.Generic;
using System.Linq;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Tessellator
{
    public static class ProfilePolygonBuilder
    {
        public readonly struct ProfilePolygons
        {
            public readonly (double X, double Y)[] Outer;       // CCW
            public readonly (double X, double Y)[][] Inners;    // CW each

            public ProfilePolygons((double X, double Y)[] outer, (double X, double Y)[][] inners)
            {
                Outer = outer;
                Inners = inners;
            }
        }

        public static bool CanBuildPolygons(IIfcProfileDef profile)
        {
            // Check derived types before base types (hollow derives from non-hollow)
            switch (profile)
            {
                case IIfcRectangleHollowProfileDef rh:
                    // Reject if fillets present — can't represent as straight-edged polygon
                    return !HasFillets(rh);

                case IIfcRectangleProfileDef _:
                    return true;

                case IIfcCircleHollowProfileDef _:
                    return true;

                case IIfcCircleProfileDef _:
                    return true;

                case IIfcArbitraryProfileDefWithVoids apv:
                    return apv.OuterCurve is IIfcPolyline &&
                           apv.InnerCurves.All(c => c is IIfcPolyline);

                case IIfcArbitraryClosedProfileDef ap:
                    return ap.OuterCurve is IIfcPolyline;

                default:
                    return false;
            }
        }

        public static ProfilePolygons BuildPolygons(IIfcProfileDef profile, double angularTolerance)
        {
            switch (profile)
            {
                case IIfcRectangleHollowProfileDef rh:
                    return BuildRectangleHollow(rh);

                case IIfcRectangleProfileDef rect:
                    return BuildRectangle(rect);

                case IIfcCircleHollowProfileDef ch:
                    return BuildCircleHollow(ch, angularTolerance);

                case IIfcCircleProfileDef circle:
                    return BuildCircle(circle, angularTolerance);

                case IIfcArbitraryProfileDefWithVoids apv:
                    return BuildArbitraryWithVoids(apv);

                case IIfcArbitraryClosedProfileDef ap:
                    return BuildArbitrary(ap);

                default:
                    throw new ArgumentException($"Unsupported profile type: {profile.GetType().Name}");
            }
        }

        public static (double X, double Y)[] ApplyProfilePosition(
            (double X, double Y)[] points, IIfcAxis2Placement2D position)
        {
            if (position == null) return points;

            double ox = position.Location.Coordinates[0];
            double oy = position.Location.Coordinates[1];

            // Default X direction is (1,0) if RefDirection is null
            double xdx = 1.0, xdy = 0.0;
            if (position.RefDirection != null)
            {
                xdx = position.RefDirection.DirectionRatios[0];
                xdy = position.RefDirection.DirectionRatios[1];
                // Normalize
                double len = Math.Sqrt(xdx * xdx + xdy * xdy);
                if (len > 1e-12) { xdx /= len; xdy /= len; }
            }

            // Y direction = perpendicular to X: rotate 90° CCW
            double ydx = -xdy, ydy = xdx;

            var result = new (double X, double Y)[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                double px = points[i].X;
                double py = points[i].Y;
                result[i] = (ox + px * xdx + py * ydx, oy + px * xdy + py * ydy);
            }
            return result;
        }

        #region Rectangle profiles

        private static ProfilePolygons BuildRectangle(IIfcRectangleProfileDef rect)
        {
            double hw = (double)rect.XDim / 2.0;
            double hh = (double)rect.YDim / 2.0;

            // CCW winding centered on origin
            var outer = new (double, double)[]
            {
                (-hw, -hh),
                ( hw, -hh),
                ( hw,  hh),
                (-hw,  hh)
            };
            return new ProfilePolygons(outer, Array.Empty<(double, double)[]>());
        }

        private static ProfilePolygons BuildRectangleHollow(IIfcRectangleHollowProfileDef rh)
        {
            double hw = (double)rh.XDim / 2.0;
            double hh = (double)rh.YDim / 2.0;
            double wt = (double)rh.WallThickness;

            var outer = new (double, double)[]
            {
                (-hw, -hh),
                ( hw, -hh),
                ( hw,  hh),
                (-hw,  hh)
            };

            double ihw = hw - wt;
            double ihh = hh - wt;

            // Inner is CW (reversed winding)
            var inner = new (double, double)[]
            {
                (-ihw, -ihh),
                (-ihw,  ihh),
                ( ihw,  ihh),
                ( ihw, -ihh)
            };

            return new ProfilePolygons(outer, new[] { inner });
        }

        private static bool HasFillets(IIfcRectangleHollowProfileDef rh)
        {
            if (rh.InnerFilletRadius.HasValue && (double)rh.InnerFilletRadius.Value > 0) return true;
            if (rh.OuterFilletRadius.HasValue && (double)rh.OuterFilletRadius.Value > 0) return true;
            return false;
        }

        #endregion

        #region Circle profiles

        private static ProfilePolygons BuildCircle(IIfcCircleProfileDef circle, double angularTolerance)
        {
            double radius = (double)circle.Radius;
            int segments = CircleSegmentCount(angularTolerance);
            var outer = GenerateCirclePolygon(radius, segments);
            return new ProfilePolygons(outer, Array.Empty<(double, double)[]>());
        }

        private static ProfilePolygons BuildCircleHollow(IIfcCircleHollowProfileDef ch, double angularTolerance)
        {
            double outerRadius = (double)ch.Radius;
            double innerRadius = outerRadius - (double)ch.WallThickness;
            int segments = CircleSegmentCount(angularTolerance);

            var outer = GenerateCirclePolygon(outerRadius, segments);
            // Inner is CW: reverse the CCW polygon
            var innerCcw = GenerateCirclePolygon(innerRadius, segments);
            Array.Reverse(innerCcw);

            return new ProfilePolygons(outer, new[] { innerCcw });
        }

        private static (double X, double Y)[] GenerateCirclePolygon(double radius, int segments)
        {
            // CCW winding
            var points = new (double X, double Y)[segments];
            double step = 2.0 * Math.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                double angle = i * step;
                points[i] = (radius * Math.Cos(angle), radius * Math.Sin(angle));
            }
            return points;
        }

        private static int CircleSegmentCount(double angularTolerance)
        {
            if (angularTolerance <= 0) return 48; // sensible default
            int n = (int)Math.Ceiling(2.0 * Math.PI / angularTolerance);
            return Math.Clamp(n, 24, 72);
        }

        #endregion

        #region Arbitrary profiles

        private static ProfilePolygons BuildArbitrary(IIfcArbitraryClosedProfileDef ap)
        {
            var polyline = (IIfcPolyline)ap.OuterCurve;
            var outer = ExtractPolylinePoints(polyline);
            outer = EnsureCCW(outer);
            return new ProfilePolygons(outer, Array.Empty<(double, double)[]>());
        }

        private static ProfilePolygons BuildArbitraryWithVoids(IIfcArbitraryProfileDefWithVoids apv)
        {
            var outerPolyline = (IIfcPolyline)apv.OuterCurve;
            var outer = ExtractPolylinePoints(outerPolyline);
            outer = EnsureCCW(outer);

            var inners = new List<(double, double)[]>();
            foreach (var innerCurve in apv.InnerCurves)
            {
                var innerPolyline = (IIfcPolyline)innerCurve;
                var inner = ExtractPolylinePoints(innerPolyline);
                inner = EnsureCW(inner);
                inners.Add(inner);
            }

            return new ProfilePolygons(outer, inners.ToArray());
        }

        private static (double X, double Y)[] ExtractPolylinePoints(IIfcPolyline polyline)
        {
            var ifcPoints = polyline.Points.ToList();
            if (ifcPoints.Count < 3)
                throw new ArgumentException("Polyline must have at least 3 points");

            var points = new List<(double, double)>(ifcPoints.Count);
            foreach (var pt in ifcPoints)
                points.Add((pt.Coordinates[0], pt.Coordinates[1]));

            // Remove duplicate closing vertex if present
            var first = points[0];
            var last = points[points.Count - 1];
            if (Math.Abs(first.Item1 - last.Item1) < 1e-10 &&
                Math.Abs(first.Item2 - last.Item2) < 1e-10)
            {
                points.RemoveAt(points.Count - 1);
            }

            return points.ToArray();
        }

        #endregion

        #region Winding utilities

        private static double SignedArea((double X, double Y)[] polygon)
        {
            double area = 0;
            int n = polygon.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += polygon[i].X * polygon[j].Y;
                area -= polygon[j].X * polygon[i].Y;
            }
            return area / 2.0;
        }

        private static (double X, double Y)[] EnsureCCW((double X, double Y)[] polygon)
        {
            if (SignedArea(polygon) < 0)
                Array.Reverse(polygon);
            return polygon;
        }

        private static (double X, double Y)[] EnsureCW((double X, double Y)[] polygon)
        {
            if (SignedArea(polygon) > 0)
                Array.Reverse(polygon);
            return polygon;
        }

        #endregion
    }
}
