using Moq;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Ifc4;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProfileResource;
using Xbim.Ifc4.TopologyResource;

namespace Xbim.Geometry.Engine.Interop.Tests.Helpers;

/// <summary>
/// Lightweight mock creators for IFC entities used in CSG primitive and profile tests.
/// Replicates the patterns from the main test project's MoqCreators.
/// </summary>
internal static class IfcMoq
{
    private static readonly ExpressMetaData MetaData =
        ExpressMetaData.GetMetadata(new EntityFactoryIfc4());

    private static Mock<T> MakeMoq<T>() where T : class, IPersistEntity
    {
        return new Mock<T>
        {
            DefaultValue = DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();
    }

    // ── Placement mocks ──────────────────────────────────────────────

    public static IIfcCartesianPoint CartesianPoint3d(double x = 0, double y = 0, double z = 0)
    {
        var cpMoq = MakeMoq<IIfcCartesianPoint>();
        cpMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var cp = cpMoq.Object;
        cp.Coordinates.AddRange(new IfcLengthMeasure[] { x, y, z });
        cpMoq.SetupGet(v => v.X).Returns(cp.Coordinates[0]);
        cpMoq.SetupGet(v => v.Y).Returns(cp.Coordinates[1]);
        cpMoq.SetupGet(v => v.Z).Returns(cp.Coordinates[2]);
        cpMoq.SetupGet(v => v.EntityLabel).Returns(1);
        return cp;
    }

    public static IIfcCartesianPoint CartesianPoint2d(double x = 0, double y = 0)
    {
        var cpMoq = MakeMoq<IIfcCartesianPoint>();
        cpMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(2));
        var cp = cpMoq.Object;
        cp.Coordinates.AddRange(new IfcLengthMeasure[] { x, y });
        cpMoq.SetupGet(v => v.X).Returns(cp.Coordinates[0]);
        cpMoq.SetupGet(v => v.Y).Returns(cp.Coordinates[1]);
        cpMoq.SetupGet(v => v.EntityLabel).Returns(1);
        return cp;
    }

    public static IIfcDirection Direction3d(double x = 0, double y = 0, double z = 1)
    {
        var dirMoq = MakeMoq<IIfcDirection>();
        dirMoq.SetupGet(v => v.Dim).Returns(new IfcDimensionCount(3));
        var dir = dirMoq.Object;
        dir.DirectionRatios.AddRange(new IfcReal[] { x, y, z });
        dirMoq.SetupGet(v => v.X).Returns(dir.DirectionRatios[0]);
        dirMoq.SetupGet(v => v.Y).Returns(dir.DirectionRatios[1]);
        dirMoq.SetupGet(v => v.Z).Returns(dir.DirectionRatios[2]);
        return dir;
    }

    public static IIfcAxis2Placement3D Axis2Placement3d(
        IIfcDirection? axis = null, IIfcDirection? refDir = null, IIfcCartesianPoint? loc = null)
    {
        var axisMoq = MakeMoq<IIfcAxis2Placement3D>();
        var ax = axisMoq.Object;
        ax.Axis = axis ?? Direction3d();
        ax.RefDirection = refDir;
        ax.Location = loc ?? CartesianPoint3d();
        return ax;
    }

    public static IIfcAxis2Placement2D Axis2Placement2d(
        IIfcDirection? refDir = null, IIfcCartesianPoint? loc = null)
    {
        var axisMoq = MakeMoq<IIfcAxis2Placement2D>();
        var ax = axisMoq.Object;
        ax.RefDirection = refDir;
        ax.Location = loc ?? CartesianPoint2d();
        return ax;
    }

    // ── Model mock ───────────────────────────────────────────────────

    public static IModel ModelMock(double millimetre = 1, double precision = 1e-5)
    {
        var modelMoq = new Mock<IModel>
        {
            DefaultValue = DefaultValue.Mock,
            DefaultValueProvider = new MoqDefaultBehaviourProvider()
        }.SetupAllProperties();

        modelMoq.SetupGet(m => m.ModelFactors.Precision).Returns(precision);
        modelMoq.SetupGet(m => m.ModelFactors.OneMilliMeter).Returns(millimetre);
        modelMoq.SetupGet(m => m.ModelFactors.OneMilliMetre).Returns(millimetre);
        modelMoq.SetupGet(m => m.ModelFactors.OneMeter).Returns(millimetre * 1000);
        modelMoq.SetupGet(m => m.ModelFactors.OneFoot).Returns(millimetre * 304.8);
        modelMoq.SetupGet(m => m.ModelFactors.AngleToRadiansConversionFactor).Returns(Math.PI / 180);
        modelMoq.SetupGet(m => m.ModelFactors.PrecisionBoolean).Returns(precision * 10);

        // Set up Instances so OfType<T>() returns empty enumerables (avoids NullRef)
        var instancesMoq = new Mock<IEntityCollection>();
        instancesMoq.Setup(i => i.OfType<IIfcApplication>())
            .Returns(Enumerable.Empty<IIfcApplication>());
        instancesMoq.Setup(i => i.OfType<IIfcGeometricRepresentationSubContext>())
            .Returns(Enumerable.Empty<IIfcGeometricRepresentationSubContext>());
        instancesMoq.Setup(i => i.OfType<IIfcGeometricRepresentationContext>())
            .Returns(Enumerable.Empty<IIfcGeometricRepresentationContext>());
        modelMoq.SetupGet(m => m.Instances).Returns(instancesMoq.Object);

        return modelMoq.Object;
    }

    // ── CSG solid mocks ──────────────────────────────────────────────

    public static IIfcBlock Block(double xLen = 10, double yLen = 20, double zLen = 30,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcBlock>();
        var obj = moq.Object;
        obj.XLength = xLen;
        obj.YLength = yLen;
        obj.ZLength = zLen;
        obj.Position = position ?? Axis2Placement3d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcBlock)));
        return obj;
    }

    public static IIfcSphere Sphere(double radius = 5,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcSphere>();
        var obj = moq.Object;
        obj.Radius = radius;
        obj.Position = position ?? Axis2Placement3d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcSphere)));
        return obj;
    }

    public static IIfcRightCircularCylinder Cylinder(double radius = 3, double height = 10,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcRightCircularCylinder>();
        var obj = moq.Object;
        obj.Radius = radius;
        obj.Height = height;
        obj.Position = position ?? Axis2Placement3d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcRightCircularCylinder)));
        return obj;
    }

    public static IIfcRightCircularCone Cone(double radius = 5, double height = 10,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcRightCircularCone>();
        var obj = moq.Object;
        obj.BottomRadius = radius;
        obj.Height = height;
        obj.Position = position ?? Axis2Placement3d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcRightCircularCone)));
        return obj;
    }

    public static IIfcRectangularPyramid Pyramid(double xLen = 10, double yLen = 20, double height = 15,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcRectangularPyramid>();
        var obj = moq.Object;
        obj.XLength = xLen;
        obj.YLength = yLen;
        obj.Height = height;
        obj.Position = position ?? Axis2Placement3d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcRectangularPyramid)));
        return obj;
    }

    // ── Profile mocks ────────────────────────────────────────────────

    public static IIfcRectangleProfileDef RectangleProfile(double xDim = 100, double yDim = 200,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcRectangleProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.XDim = xDim;
        obj.YDim = yDim;
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcRectangleProfileDef)));
        return obj;
    }

    public static IIfcCircleProfileDef CircleProfile(double radius = 50,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcCircleProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Radius = radius;
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcCircleProfileDef)));
        return obj;
    }

    public static IIfcEllipseProfileDef EllipseProfile(double semi1 = 100, double semi2 = 50,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcEllipseProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.SemiAxis1 = semi1;
        obj.SemiAxis2 = semi2;
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType).Returns(MetaData.ExpressType(typeof(IfcEllipseProfileDef)));
        return obj;
    }

    // ── Hollow profile mocks ────────────────────────────────────────

    public static IIfcRectangleHollowProfileDef RectangleHollowProfile(
        double xDim = 200, double yDim = 100, double wallThickness = 10,
        double? innerFilletRadius = null, double? outerFilletRadius = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcRectangleHollowProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.XDim = xDim;
        obj.YDim = yDim;
        obj.WallThickness = wallThickness;
        if (innerFilletRadius.HasValue)
            moq.SetupGet(x => x.InnerFilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(innerFilletRadius.Value));
        if (outerFilletRadius.HasValue)
            moq.SetupGet(x => x.OuterFilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(outerFilletRadius.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcRectangleHollowProfileDef)));
        return obj;
    }

    public static IIfcCircleHollowProfileDef CircleHollowProfile(
        double radius = 50, double wallThickness = 5,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcCircleHollowProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Radius = radius;
        obj.WallThickness = wallThickness;
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcCircleHollowProfileDef)));
        return obj;
    }

    // ── Structural profile mocks ─────────────────────────────────────

    public static IIfcIShapeProfileDef IShapeProfile(
        double overallWidth = 100, double overallDepth = 200,
        double webThickness = 10, double flangeThickness = 15,
        double? filletRadius = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcIShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.OverallWidth = overallWidth;
        obj.OverallDepth = overallDepth;
        obj.WebThickness = webThickness;
        obj.FlangeThickness = flangeThickness;
        if (filletRadius.HasValue)
            moq.SetupGet(x => x.FilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(filletRadius.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcIShapeProfileDef)));
        return obj;
    }

    public static IIfcLShapeProfileDef LShapeProfile(
        double depth = 100, double thickness = 10,
        double? width = null, double? filletRadius = null,
        double? edgeRadius = null, double? legSlope = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcLShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Depth = depth;
        obj.Thickness = thickness;
        if (width.HasValue)
            moq.SetupGet(x => x.Width).Returns(new IfcPositiveLengthMeasure(width.Value));
        if (filletRadius.HasValue)
            moq.SetupGet(x => x.FilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(filletRadius.Value));
        if (edgeRadius.HasValue)
            moq.SetupGet(x => x.EdgeRadius)
                .Returns(new IfcNonNegativeLengthMeasure(edgeRadius.Value));
        if (legSlope.HasValue)
            moq.SetupGet(x => x.LegSlope)
                .Returns(new IfcPlaneAngleMeasure(legSlope.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcLShapeProfileDef)));
        return obj;
    }

    public static IIfcTShapeProfileDef TShapeProfile(
        double depth = 100, double flangeWidth = 100,
        double webThickness = 10, double flangeThickness = 15,
        double? filletRadius = null, double? flangeEdgeRadius = null,
        double? webEdgeRadius = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcTShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Depth = depth;
        obj.FlangeWidth = flangeWidth;
        obj.WebThickness = webThickness;
        obj.FlangeThickness = flangeThickness;
        if (filletRadius.HasValue)
            moq.SetupGet(x => x.FilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(filletRadius.Value));
        if (flangeEdgeRadius.HasValue)
            moq.SetupGet(x => x.FlangeEdgeRadius)
                .Returns(new IfcNonNegativeLengthMeasure(flangeEdgeRadius.Value));
        if (webEdgeRadius.HasValue)
            moq.SetupGet(x => x.WebEdgeRadius)
                .Returns(new IfcNonNegativeLengthMeasure(webEdgeRadius.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcTShapeProfileDef)));
        return obj;
    }

    public static IIfcUShapeProfileDef UShapeProfile(
        double depth = 100, double flangeWidth = 50,
        double webThickness = 8, double flangeThickness = 12,
        double? filletRadius = null, double? edgeRadius = null,
        double? flangeSlope = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcUShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Depth = depth;
        obj.FlangeWidth = flangeWidth;
        obj.WebThickness = webThickness;
        obj.FlangeThickness = flangeThickness;
        if (filletRadius.HasValue)
            moq.SetupGet(x => x.FilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(filletRadius.Value));
        if (edgeRadius.HasValue)
            moq.SetupGet(x => x.EdgeRadius)
                .Returns(new IfcNonNegativeLengthMeasure(edgeRadius.Value));
        if (flangeSlope.HasValue)
            moq.SetupGet(x => x.FlangeSlope)
                .Returns(new IfcPlaneAngleMeasure(flangeSlope.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcUShapeProfileDef)));
        return obj;
    }

    public static IIfcZShapeProfileDef ZShapeProfile(
        double depth = 100, double flangeWidth = 50,
        double webThickness = 8, double flangeThickness = 12,
        double? filletRadius = null, double? edgeRadius = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcZShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Depth = depth;
        obj.FlangeWidth = flangeWidth;
        obj.WebThickness = webThickness;
        obj.FlangeThickness = flangeThickness;
        if (filletRadius.HasValue)
            moq.SetupGet(x => x.FilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(filletRadius.Value));
        if (edgeRadius.HasValue)
            moq.SetupGet(x => x.EdgeRadius)
                .Returns(new IfcNonNegativeLengthMeasure(edgeRadius.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcZShapeProfileDef)));
        return obj;
    }

    public static IIfcCShapeProfileDef CShapeProfile(
        double depth = 100, double width = 50,
        double wallThickness = 8, double girth = 20,
        double? internalFilletRadius = null,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcCShapeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.Depth = depth;
        obj.Width = width;
        obj.WallThickness = wallThickness;
        obj.Girth = girth;
        if (internalFilletRadius.HasValue)
            moq.SetupGet(x => x.InternalFilletRadius)
                .Returns(new IfcNonNegativeLengthMeasure(internalFilletRadius.Value));
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcCShapeProfileDef)));
        return obj;
    }

    public static IIfcTrapeziumProfileDef TrapeziumProfile(
        double bottomXDim = 100, double topXDim = 60,
        double yDim = 80, double topXOffset = 20,
        IIfcAxis2Placement2D? position = null)
    {
        var moq = MakeMoq<IIfcTrapeziumProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        obj.BottomXDim = bottomXDim;
        obj.TopXDim = topXDim;
        obj.YDim = yDim;
        obj.TopXOffset = topXOffset;
        obj.Position = position ?? Axis2Placement2d();
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcTrapeziumProfileDef)));
        return obj;
    }

    // ── Arbitrary profile mocks ──────────────────────────────────────

    /// <summary>
    /// Creates an IIfcArbitraryClosedProfileDef with a polyline outer curve.
    /// Points are 2D coordinates given as (x,y) pairs.
    /// </summary>
    public static IIfcArbitraryClosedProfileDef ArbitraryClosedProfile(
        (double x, double y)[] points)
    {
        var moq = MakeMoq<IIfcArbitraryClosedProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;

        // Build polyline from points
        var polyMoq = MakeMoq<IIfcPolyline>();
        var poly = polyMoq.Object;
        foreach (var (x, y) in points)
            poly.Points.Add(CartesianPoint2d(x, y));

        moq.SetupGet(x => x.OuterCurve).Returns(poly);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcArbitraryClosedProfileDef)));
        return obj;
    }

    /// <summary>
    /// Creates an IIfcArbitraryProfileDefWithVoids: outer polyline with inner polyline voids.
    /// </summary>
    public static IIfcArbitraryProfileDefWithVoids ArbitraryProfileWithVoids(
        (double x, double y)[] outerPoints,
        (double x, double y)[][] innerCurves)
    {
        var moq = MakeMoq<IIfcArbitraryProfileDefWithVoids>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;

        // Outer curve
        var polyMoq = MakeMoq<IIfcPolyline>();
        var poly = polyMoq.Object;
        foreach (var (x, y) in outerPoints)
            poly.Points.Add(CartesianPoint2d(x, y));
        moq.SetupGet(x => x.OuterCurve).Returns(poly);

        // Inner curves
        var innerList = new ItemListMoq<IIfcCurve>();
        foreach (var innerPts in innerCurves)
        {
            var iPoly = MakeMoq<IIfcPolyline>();
            var ip = iPoly.Object;
            foreach (var (x, y) in innerPts)
                ip.Points.Add(CartesianPoint2d(x, y));
            innerList.Add(ip);
        }
        moq.SetupGet(x => x.InnerCurves).Returns(innerList);

        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcArbitraryProfileDefWithVoids)));
        return obj;
    }

    // ── Composite / Derived / Mirrored profile mocks ─────────────────

    public static IIfcCompositeProfileDef CompositeProfile(
        params IIfcProfileDef[] profiles)
    {
        var moq = MakeMoq<IIfcCompositeProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        var profileSet = new ItemListMoq<IIfcProfileDef>();
        profileSet.AddRange(profiles);
        moq.SetupGet(x => x.Profiles).Returns(profileSet);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcCompositeProfileDef)));
        return obj;
    }

    public static IIfcDerivedProfileDef DerivedProfile(
        IIfcProfileDef parent,
        double translateX = 0, double translateY = 0,
        double scale = 1.0)
    {
        var moq = MakeMoq<IIfcDerivedProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        moq.SetupGet(x => x.ParentProfile).Returns(parent);

        // Build a CartesianTransformationOperator2D
        var opMoq = MakeMoq<IIfcCartesianTransformationOperator2D>();
        var op = opMoq.Object;
        op.LocalOrigin = CartesianPoint2d(translateX, translateY);
        op.Scale = scale;
        opMoq.SetupGet(x => x.Scl).Returns(scale);
        // Also set up Scl on the base interface to handle explicit interface implementation
        opMoq.As<IIfcCartesianTransformationOperator>()
            .SetupGet(x => x.Scl).Returns(scale);
        // Axis1 and Axis2 remain null → identity rotation
        moq.SetupGet(x => x.Operator).Returns(op);

        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcDerivedProfileDef)));
        return obj;
    }

    public static IIfcMirroredProfileDef MirroredProfile(IIfcProfileDef parent)
    {
        var moq = MakeMoq<IIfcMirroredProfileDef>();
        var obj = moq.Object;
        obj.ProfileType = IfcProfileTypeEnum.AREA;
        moq.SetupGet(x => x.ParentProfile).Returns(parent);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcMirroredProfileDef)));
        return obj;
    }

    // ── Axis1Placement mock ─────────────────────────────────────────

    public static IIfcAxis1Placement Axis1Placement(
        IIfcCartesianPoint? loc = null, IIfcDirection? axis = null)
    {
        var moq = MakeMoq<IIfcAxis1Placement>();
        moq.SetupGet(x => x.Location).Returns(loc ?? CartesianPoint3d());
        moq.SetupGet(x => x.Axis).Returns(axis ?? Direction3d(1, 0, 0));
        return moq.Object;
    }

    // ── Sweep solid mocks ───────────────────────────────────────────

    public static IIfcExtrudedAreaSolid ExtrudedAreaSolid(
        IIfcProfileDef? sweptArea = null,
        IIfcDirection? direction = null,
        double depth = 100,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcExtrudedAreaSolid>();
        var obj = moq.Object;
        moq.SetupGet(x => x.SweptArea).Returns(sweptArea ?? RectangleProfile());
        moq.SetupGet(x => x.ExtrudedDirection).Returns(direction ?? Direction3d(0, 0, 1));
        obj.Depth = depth;
        if (position != null)
            moq.SetupGet(x => x.Position).Returns(position);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcExtrudedAreaSolid)));
        return obj;
    }

    public static IIfcExtrudedAreaSolidTapered ExtrudedAreaSolidTapered(
        IIfcProfileDef? sweptArea = null,
        IIfcProfileDef? endSweptArea = null,
        IIfcDirection? direction = null,
        double depth = 100,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcExtrudedAreaSolidTapered>();
        var obj = moq.Object;
        moq.SetupGet(x => x.SweptArea).Returns(sweptArea ?? RectangleProfile(100, 200));
        moq.SetupGet(x => x.EndSweptArea).Returns(endSweptArea ?? RectangleProfile(50, 100));
        moq.SetupGet(x => x.ExtrudedDirection).Returns(direction ?? Direction3d(0, 0, 1));
        obj.Depth = depth;
        if (position != null)
            moq.SetupGet(x => x.Position).Returns(position);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcExtrudedAreaSolidTapered)));
        return obj;
    }

    public static IIfcRevolvedAreaSolid RevolvedAreaSolid(
        IIfcProfileDef? sweptArea = null,
        IIfcAxis1Placement? axis = null,
        double angle = 360,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcRevolvedAreaSolid>();
        var obj = moq.Object;
        moq.SetupGet(x => x.SweptArea).Returns(sweptArea ?? RectangleProfile(10, 20));
        moq.SetupGet(x => x.Axis).Returns(axis ?? Axis1Placement(
            CartesianPoint3d(-50, 0, 0), Direction3d(0, 0, 1)));
        obj.Angle = angle;
        if (position != null)
            moq.SetupGet(x => x.Position).Returns(position);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcRevolvedAreaSolid)));
        return obj;
    }

    public static IIfcRevolvedAreaSolidTapered RevolvedAreaSolidTapered(
        IIfcProfileDef? sweptArea = null,
        IIfcProfileDef? endSweptArea = null,
        IIfcAxis1Placement? axis = null,
        double angle = 360,
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcRevolvedAreaSolidTapered>();
        var obj = moq.Object;
        moq.SetupGet(x => x.SweptArea).Returns(sweptArea ?? RectangleProfile(10, 20));
        moq.SetupGet(x => x.EndSweptArea).Returns(endSweptArea ?? RectangleProfile(5, 10));
        moq.SetupGet(x => x.Axis).Returns(axis ?? Axis1Placement(
            CartesianPoint3d(-50, 0, 0), Direction3d(0, 0, 1)));
        obj.Angle = angle;
        if (position != null)
            moq.SetupGet(x => x.Position).Returns(position);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcRevolvedAreaSolidTapered)));
        return obj;
    }

    public static IIfcSweptDiskSolid SweptDiskSolid(
        double radius = 20, double innerRadius = 0,
        IIfcCurve? directrix = null)
    {
        var moq = MakeMoq<IIfcSweptDiskSolid>();
        var obj = moq.Object;
        obj.Radius = radius;
        if (innerRadius > 0)
            obj.InnerRadius = innerRadius;
        // Directrix is a simple line mock — just needs to exist for validation
        if (directrix != null)
            moq.SetupGet(x => x.Directrix).Returns(directrix);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcSweptDiskSolid)));
        return obj;
    }

    // ── Boolean operation mocks ────────────────────────────────────

    public static IIfcBooleanResult BooleanResult(
        IIfcBooleanOperand firstOperand,
        IIfcBooleanOperand secondOperand,
        IfcBooleanOperator op = IfcBooleanOperator.DIFFERENCE,
        int entityLabel = 100)
    {
        var moq = MakeMoq<IIfcBooleanResult>();
        moq.SetupGet(x => x.FirstOperand).Returns(firstOperand);
        moq.SetupGet(x => x.SecondOperand).Returns(secondOperand);
        moq.SetupGet(x => x.Operator).Returns(op);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcBooleanResult)));
        return moq.Object;
    }

    public static IIfcBooleanClippingResult BooleanClippingResult(
        IIfcBooleanOperand firstOperand,
        IIfcBooleanOperand secondOperand,
        int entityLabel = 200)
    {
        var moq = MakeMoq<IIfcBooleanClippingResult>();
        moq.SetupGet(x => x.FirstOperand).Returns(firstOperand);
        moq.SetupGet(x => x.SecondOperand).Returns(secondOperand);
        moq.SetupGet(x => x.Operator).Returns(IfcBooleanOperator.DIFFERENCE);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcBooleanClippingResult)));
        return moq.Object;
    }

    public static IIfcHalfSpaceSolid HalfSpaceSolid(
        IIfcSurface? baseSurface = null,
        bool agreementFlag = false,
        int entityLabel = 300)
    {
        var moq = MakeMoq<IIfcHalfSpaceSolid>();
        moq.SetupGet(x => x.BaseSurface).Returns(baseSurface ?? Plane());
        moq.SetupGet(x => x.AgreementFlag).Returns(agreementFlag);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);
        return moq.Object;
    }

    public static IIfcPlane Plane(
        IIfcAxis2Placement3D? position = null)
    {
        var moq = MakeMoq<IIfcPlane>();
        moq.SetupGet(x => x.Position).Returns(position ?? Axis2Placement3d(
            axis: Direction3d(0, 0, 1),
            refDir: Direction3d(1, 0, 0),
            loc: CartesianPoint3d(0, 0, 5)));
        return moq.Object;
    }
}
