using Moq;
using Xbim.Common;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProfileResource;

namespace Xbim.Geometry.Engine.Tests.Helpers;

/// <summary>
/// Additional mock creators migrated from IntegrationTests MoqCreators.
/// These methods support curve, surface, transform, and boolean tests.
/// </summary>
internal static partial class IfcMoq
{
    // ── 2D Direction / Vector ───────────────────────────────────────

    public static IIfcDirection Direction2d(double x = 1, double y = 0)
    {
        var dirMoq = MakeMoq<IIfcDirection>();
        dirMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var dir = dirMoq.Object;
        dir.DirectionRatios.AddRange(new IfcReal[] { x, y });
        dirMoq.SetupGet(v => v.X).Returns(dir.DirectionRatios[0]);
        dirMoq.SetupGet(v => v.Y).Returns(dir.DirectionRatios[1]);
        return dir;
    }

    public static IIfcVector Vector2d(IIfcDirection? direction = null, double magnitude = 10)
    {
        var vecMoq = MakeMoq<IIfcVector>();
        vecMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var vec = vecMoq.Object;
        vec.Orientation = direction ?? Direction2d();
        vec.Magnitude = magnitude;
        return vec;
    }

    public static IIfcVector Vector3d(IIfcDirection? direction = null, double magnitude = 10)
    {
        var vecMoq = MakeMoq<IIfcVector>();
        vecMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var vec = vecMoq.Object;
        vec.Orientation = direction ?? Direction3d();
        vec.Magnitude = magnitude;
        return vec;
    }

    // ── Line mocks ──────────────────────────────────────────────────

    public static IIfcLine Line2d(IIfcCartesianPoint? origin = null, double magnitude = 1, IIfcDirection? direction = null)
    {
        var lineMoq = MakeMoq<IIfcLine>();
        lineMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var line = lineMoq.Object;
        line.Pnt = origin ?? CartesianPoint2d();
        line.Dir = Vector2d(direction: direction, magnitude: magnitude);
        lineMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcLine)));
        return line;
    }

    public static IIfcLine Line3d(IIfcCartesianPoint? origin = null, double magnitude = 1, IIfcDirection? direction = null)
    {
        var lineMoq = MakeMoq<IIfcLine>();
        lineMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var line = lineMoq.Object;
        line.Pnt = origin ?? CartesianPoint3d();
        line.Dir = Vector3d(direction: direction, magnitude: magnitude);
        lineMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcLine)));
        return line;
    }

    // ── Circle mocks (SetupAllProperties style for curve factory) ──

    public static IIfcCircle Circle3d(double radius = 100, IIfcAxis2Placement? location = null)
    {
        var circleMoq = MakeMoq<IIfcCircle>();
        circleMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var circle = circleMoq.Object;
        circle.Radius = radius;
        circle.Position = location ?? Axis2Placement3d();
        circleMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCircle)));
        return circle;
    }

    public static IIfcCircle Circle2d(double radius = 100, IIfcAxis2Placement? location = null)
    {
        var circleMoq = MakeMoq<IIfcCircle>();
        circleMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var circle = circleMoq.Object;
        circle.Radius = radius;
        circle.Position = location ?? Axis2Placement2d();
        circleMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCircle)));
        return circle;
    }

    // ── Ellipse mocks ───────────────────────────────────────────────

    public static IIfcEllipse Ellipse3d(double semi1 = 100, double semi2 = 50)
    {
        var ellipseMoq = MakeMoq<IIfcEllipse>();
        ellipseMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var ellipse = ellipseMoq.Object;
        ellipse.SemiAxis1 = semi1;
        ellipse.SemiAxis2 = semi2;
        ellipse.Position = Axis2Placement3d();
        ellipseMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcEllipse)));
        return ellipse;
    }

    public static IIfcEllipse Ellipse2d(double semi1 = 100, double semi2 = 50)
    {
        var ellipseMoq = MakeMoq<IIfcEllipse>();
        ellipseMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var ellipse = ellipseMoq.Object;
        ellipse.SemiAxis1 = semi1;
        ellipse.SemiAxis2 = semi2;
        ellipse.Position = Axis2Placement2d();
        ellipseMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcEllipse)));
        return ellipse;
    }

    // ── Trimmed curve mocks ─────────────────────────────────────────

    public static IIfcTrimmedCurve TrimmedCurve3d(IIfcCurve? basisCurve = null, double trimParam1 = 0, double trimParam2 = 1, bool sense = true)
    {
        var trimMoq = MakeMoq<IIfcTrimmedCurve>();
        trimMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var trim = trimMoq.Object;
        trim.BasisCurve = basisCurve ?? Line3d();
        trim.MasterRepresentation = IfcTrimmingPreference.PARAMETER;
        trim.SenseAgreement = sense;
        trim.Trim1.Add(new IfcParameterValue(trimParam1));
        trim.Trim2.Add(new IfcParameterValue(trimParam2));
        trimMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcTrimmedCurve)));
        return trim;
    }

    public static IIfcTrimmedCurve TrimmedCurve2d(IIfcCurve? basisCurve = null, double trimParam1 = 0, double trimParam2 = 1, bool sense = true)
    {
        var trimMoq = MakeMoq<IIfcTrimmedCurve>();
        trimMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var trim = trimMoq.Object;
        trim.BasisCurve = basisCurve ?? Line2d();
        trim.MasterRepresentation = IfcTrimmingPreference.PARAMETER;
        trim.SenseAgreement = sense;
        trim.Trim1.Add(new IfcParameterValue(trimParam1));
        trim.Trim2.Add(new IfcParameterValue(trimParam2));
        trimMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcTrimmedCurve)));
        return trim;
    }

    // ── Composite curve mocks ───────────────────────────────────────

    public static IIfcCompositeCurveSegment CompositeCurveSegment3d(IIfcCurve? parent = null, bool sameSense = true, int entityLabel = 0)
    {
        var cCurveSegMoq = MakeMoq<IIfcCompositeCurveSegment>();
        cCurveSegMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        cCurveSegMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCompositeCurveSegment)));
        cCurveSegMoq.SetupGet(v => v.EntityLabel).Returns(entityLabel);
        var cCurveSeg = cCurveSegMoq.Object;
        cCurveSeg.ParentCurve = parent ?? TrimmedCurve3d(Circle3d(), 0, Math.PI / 2);
        cCurveSeg.SameSense = sameSense;
        return cCurveSeg;
    }

    public static IIfcCompositeCurve CompositeCurve3d(IEnumerable<IIfcCompositeCurveSegment>? segments = null)
    {
        var cCurveMoq = MakeMoq<IIfcCompositeCurve>();
        cCurveMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        cCurveMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCompositeCurve)));
        var cCurve = cCurveMoq.Object;
        if (segments == null)
            cCurve.Segments.Add(CompositeCurveSegment3d());
        else
            cCurve.Segments.AddRange(segments);
        cCurveMoq.SetupGet(v => v.NSegments).Returns(new IfcInteger(cCurve.Segments.Count));
        return cCurve;
    }

    public static IIfcCompositeCurve TypicalCompositeCurve(IXCurveFactory curveFactory, out double totalParametricLength, out double totalLength)
    {
        var lineLen1 = 10;
        var lineLen2 = 20;
        var circ1 = Circle3d(radius: 20);
        var circ2 = Circle3d(radius: 20, Axis2Placement3d(refDir: Direction3d(-1, 0, 0), loc: CartesianPoint3d(0, 40, 0)));
        var circ3 = Circle3d(radius: 20, Axis2Placement3d(loc: CartesianPoint3d(-40, 60, 0)));

        var line1 = Line3d(origin: CartesianPoint3d(x: 20, y: -lineLen1, z: 0), direction: Direction3d(x: 0, y: 1, z: 0));
        var lineSeg1 = TrimmedCurve3d(line1, trimParam1: 0, trimParam2: lineLen1);

        var line2 = Line3d(origin: CartesianPoint3d(x: -20, y: 40, z: 0), direction: Direction3d(x: 0, y: 1, z: 0));
        var lineSeg2 = TrimmedCurve3d(line2, trimParam1: 0, trimParam2: lineLen2);

        var arc1 = TrimmedCurve3d(circ1, trimParam2: Math.PI / 2);
        var arc2 = TrimmedCurve3d(circ2, trimParam2: Math.PI / 2, trimParam1: 0, sense: true);
        var arc3 = TrimmedCurve3d(circ3, trimParam2: Math.PI / 2);

        var x1 = curveFactory.Build(lineSeg1) as IXTrimmedCurve;
        var x2 = curveFactory.Build(arc1) as IXTrimmedCurve;
        var x3 = curveFactory.Build(arc2) as IXTrimmedCurve;
        var x4 = curveFactory.Build(lineSeg2) as IXTrimmedCurve;
        var x5 = curveFactory.Build(arc3) as IXTrimmedCurve;

        var paramLength1 = x1!.LastParameter - x1.FirstParameter;
        var paramLength2 = x2!.LastParameter - x2.FirstParameter;
        var paramLength3 = x3!.LastParameter - x3.FirstParameter;
        var paramLength4 = x4!.LastParameter - x4.FirstParameter;
        var paramLength5 = x5!.LastParameter - x5.FirstParameter;

        totalParametricLength = paramLength1 + paramLength2 + paramLength3 + paramLength4 + paramLength5;
        totalLength = x1.Length + x2.Length + x3.Length + x4.Length + x5.Length;
        var seg1 = CompositeCurveSegment3d(lineSeg1, entityLabel: 1);
        var seg2 = CompositeCurveSegment3d(arc1, entityLabel: 2);
        var seg3 = CompositeCurveSegment3d(arc2, entityLabel: 3, sameSense: false);
        var seg4 = CompositeCurveSegment3d(lineSeg2, entityLabel: 4);
        var seg5 = CompositeCurveSegment3d(arc3, entityLabel: 5);

        return CompositeCurve3d(new[] { seg1, seg2, seg3, seg4, seg5 });
    }

    // ── Polyline mock ───────────────────────────────────────────────

    public static IIfcPolyline Polyline(int dim = 2)
    {
        var polylineMoq = MakeMoq<IIfcPolyline>();
        polylineMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(dim));
        polylineMoq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcPolyline)));
        return polylineMoq.Object;
    }

    // ── Profile mocks ───────────────────────────────────────────────

    public static IIfcCenterLineProfileDef CenterLineProfile(IIfcBoundedCurve centreLine, double thickness = 10)
    {
        var moq = MakeMoq<IIfcCenterLineProfileDef>();
        var obj = moq.Object;
        moq.SetupGet(v => v.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCenterLineProfileDef)));
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Thickness = thickness;
        obj.Curve = centreLine;
        return obj;
    }

    // ── Swept disk solid with start/end params ──────────────────────

    public static IIfcSweptDiskSolid SweptDiskSolidParametric(
        IIfcCurve? directrix = null, double radius = 20, double innerRadius = 10,
        double? startParam = null, double? endParam = null)
    {
        var sweptMoq = MakeMoq<IIfcSweptDiskSolid>();
        sweptMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var swept = sweptMoq.Object;
        swept.Radius = radius;
        swept.InnerRadius = innerRadius;
        swept.Directrix = directrix ?? Line3d(magnitude: endParam ?? 1);
        swept.StartParam = startParam;
        swept.EndParam = endParam;
        sweptMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcSweptDiskSolid)));
        return swept;
    }

    // ── Surface mocks ───────────────────────────────────────────────

    public static IIfcPlane PlaneSurface(IIfcAxis2Placement3D? position = null)
    {
        var planeMoq = MakeMoq<IIfcPlane>();
        planeMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcPlane)));
        var plane = planeMoq.Object;
        plane.Position = position ?? Axis2Placement3d();
        return plane;
    }

    // ── Boundary curve mocks ────────────────────────────────────────

    /// <summary>
    /// Creates a rectangular boundary curve (closed composite curve) on the XY plane.
    /// The rectangle has corners at (ox,oy,oz), (ox+w,oy,oz), (ox+w,oy+h,oz), (ox,oy+h,oz).
    /// </summary>
    public static T RectangularBoundaryCurve<T>(
        double w = 10, double h = 20,
        double ox = 0, double oy = 0, double oz = 0) where T : class, IIfcBoundaryCurve
    {
        var p0 = CartesianPoint3d(ox, oy, oz);
        var p1 = CartesianPoint3d(ox + w, oy, oz);
        var p2 = CartesianPoint3d(ox + w, oy + h, oz);
        var p3 = CartesianPoint3d(ox, oy + h, oz);

        var seg1 = CompositeCurveSegment3d(
            TrimmedCurve3d(Line3d(p0, direction: Direction3d(1, 0, 0)), 0, w), entityLabel: 101);
        var seg2 = CompositeCurveSegment3d(
            TrimmedCurve3d(Line3d(p1, direction: Direction3d(0, 1, 0)), 0, h), entityLabel: 102);
        var seg3 = CompositeCurveSegment3d(
            TrimmedCurve3d(Line3d(p2, direction: Direction3d(-1, 0, 0)), 0, w), entityLabel: 103);
        var seg4 = CompositeCurveSegment3d(
            TrimmedCurve3d(Line3d(p3, direction: Direction3d(0, -1, 0)), 0, h), entityLabel: 104);

        var moq = MakeMoq<T>();
        moq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        moq.SetupGet(x => x.ExpressType).Returns(
            typeof(T) == typeof(IIfcOuterBoundaryCurve)
                ? MetaData.ExpressType(typeof(IfcOuterBoundaryCurve))
                : MetaData.ExpressType(typeof(IfcBoundaryCurve)));
        var curve = moq.Object;
        curve.Segments.AddRange(new[] { seg1, seg2, seg3, seg4 });
        moq.SetupGet(v => v.NSegments).Returns(new IfcInteger(4));
        return curve;
    }

    /// <summary>
    /// Creates a mock IIfcCurveBoundedSurface.
    /// </summary>
    public static IIfcCurveBoundedSurface CurveBoundedSurface(
        IIfcSurface basisSurface,
        bool implicitOuter,
        params IIfcBoundaryCurve[] boundaries)
    {
        var moq = MakeMoq<IIfcCurveBoundedSurface>();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCurveBoundedSurface)));
        var cbs = moq.Object;
        cbs.BasisSurface = basisSurface;
        cbs.ImplicitOuter = implicitOuter;
        foreach (var b in boundaries)
            cbs.Boundaries.Add(b);
        return cbs;
    }

    // ── Transform mocks ─────────────────────────────────────────────

    public static IIfcCartesianTransformationOperator3D CartesianTransformationOperator3d(double scale = 1, double x = 0, double y = 0, double z = 0)
    {
        var ct3dMoq = MakeMoq<IIfcCartesianTransformationOperator3D>();
        ct3dMoq.SetupGet(t => t.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCartesianTransformationOperator3D)));
        ct3dMoq.SetupGet(s => s.Scl).Returns(scale);
        var ct3d = ct3dMoq.Object;
        ct3d.LocalOrigin = CartesianPoint3d(x, y, z);
        ct3d.Scale = scale;
        return ct3d;
    }

    public static IIfcCartesianTransformationOperator3D CartesianTransformationOperator3dNonUniform(
        double scale1 = 1, double scale2 = 1, double scale3 = 1)
    {
        var ct3dMoq = MakeMoq<IIfcCartesianTransformationOperator3DnonUniform>();
        ct3dMoq.SetupGet(t => t.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCartesianTransformationOperator3DnonUniform)));
        ct3dMoq.SetupGet(s => s.Scale).Returns(scale1);
        ct3dMoq.SetupGet(s => s.Scl).Returns(scale1);
        ct3dMoq.SetupGet(s => s.Scale2).Returns(scale2);
        ct3dMoq.SetupGet(s => s.Scl2).Returns(scale2);
        ct3dMoq.SetupGet(s => s.Scale3).Returns(scale3);
        ct3dMoq.SetupGet(s => s.Scl3).Returns(scale3);
        ct3dMoq.SetupGet(s => s.LocalOrigin).Returns(CartesianPoint3d(0, 0, 0));
        ct3dMoq.SetupGet(s => s.Axis1).Returns(Direction3d(1, 0, 0));
        ct3dMoq.SetupGet(s => s.Axis2).Returns(Direction3d(0, 1, 0));
        ct3dMoq.SetupGet(s => s.Axis3).Returns(Direction3d(0, 0, 1));
        return ct3dMoq.Object;
    }

    // ── Boolean mocks with displacement API ─────────────────────────

    public static IIfcBooleanResult BooleanResultFromDisplacement(
        IfcBooleanOperator boolOp = IfcBooleanOperator.UNION,
        double originX = 0, double originY = 0, double originZ = 0,
        double lenX = 10, double lenY = 20, double lenZ = 30,
        double displacementX = 0, double displacementY = 0, double displacementZ = 0)
    {
        var booleanResultMoq = MakeMoq<IIfcBooleanResult>();
        booleanResultMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcBooleanResult)));
        var booleanResult = booleanResultMoq.Object;
        var firstPosition = Axis2Placement3d(loc: CartesianPoint3d(x: originX, y: originY, z: originZ));
        var secondPosition = Axis2Placement3d(loc: CartesianPoint3d(x: displacementX + originX, y: displacementY + originY, z: displacementZ + originZ));
        booleanResult.FirstOperand = Block(xLen: lenX, yLen: lenY, zLen: lenZ, position: firstPosition);
        booleanResult.SecondOperand = Block(xLen: lenX, yLen: lenY, zLen: lenZ, position: secondPosition);
        booleanResult.Operator = boolOp;
        return booleanResult;
    }

    public static IIfcBooleanResult DeepBooleanResult(
        IfcBooleanOperator boolOp = IfcBooleanOperator.UNION,
        int depth = 2, double displacement = 10)
    {
        var booleanResultMoq = MakeMoq<IIfcBooleanResult>();
        booleanResultMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcBooleanResult)));
        var booleanResult = booleanResultMoq.Object;
        var position = Axis2Placement3d(loc: CartesianPoint3d(x: 0, y: 0, z: 0));
        booleanResult.FirstOperand = Block(xLen: displacement * 2, yLen: 20, zLen: displacement * 2, position: position);
        var offset = 0.0;

        var nextBooleanRes = booleanResult;
        for (int i = 0; i < depth; i++)
        {
            offset += displacement;
            var secondOpMoq = MakeMoq<IIfcBooleanResult>();
            nextBooleanRes.SecondOperand = secondOpMoq.Object;
            secondOpMoq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcBooleanResult)));
            position = Axis2Placement3d(loc: CartesianPoint3d(x: offset, y: offset, z: offset));
            secondOpMoq.Object.FirstOperand = Block(xLen: displacement * 2, yLen: 20, zLen: displacement * 3, position: position);
            nextBooleanRes = secondOpMoq.Object;
        }
        offset += displacement;
        position = Axis2Placement3d(loc: CartesianPoint3d(x: offset, y: offset, z: offset));
        nextBooleanRes.SecondOperand = Block(xLen: displacement * 2, yLen: 20, zLen: displacement * 3, position: position);
        return booleanResult;
    }
}
