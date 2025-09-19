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

    // ── CSG solid mocks ────────────────────────────────────────────

    public static IIfcCsgSolid CsgSolid(
        IIfcCsgSelect treeRoot,
        int entityLabel = 500)
    {
        var moq = MakeMoq<IIfcCsgSolid>();
        moq.SetupGet(x => x.TreeRootExpression).Returns(treeRoot);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcCsgSolid)));
        return moq.Object;
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

    public static IIfcPolygonalBoundedHalfSpace PolygonalBoundedHalfSpace(
        IIfcPlane? baseSurface = null,
        bool agreementFlag = false,
        IIfcAxis2Placement3D? position = null,
        (double x, double y)[]? boundaryPoints = null,
        int entityLabel = 310)
    {
        var moq = MakeMoq<IIfcPolygonalBoundedHalfSpace>();

        var surface = baseSurface ?? Plane();
        moq.SetupGet(x => x.BaseSurface).Returns(surface);
        moq.SetupGet(x => x.AgreementFlag).Returns(agreementFlag);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);

        // Position (coordinate system of the boundary polygon)
        moq.SetupGet(x => x.Position).Returns(position ?? Axis2Placement3d(
            axis: Direction3d(0, 0, 1),
            refDir: Direction3d(1, 0, 0),
            loc: CartesianPoint3d(0, 0, 0)));

        // Build polyline boundary (2D points)
        var pts = boundaryPoints ?? new[] { (-5.0, -5.0), (5.0, -5.0), (5.0, 5.0), (-5.0, 5.0) };
        var polyMoq = MakeMoq<IIfcPolyline>();
        var poly = polyMoq.Object;
        foreach (var (x, y) in pts)
            poly.Points.Add(CartesianPoint2d(x, y));
        moq.SetupGet(x => x.PolygonalBoundary).Returns(poly);

        return moq.Object;
    }

    public static IIfcBoxedHalfSpace BoxedHalfSpace(
        IIfcSurface? baseSurface = null,
        bool agreementFlag = false,
        int entityLabel = 320)
    {
        var moq = MakeMoq<IIfcBoxedHalfSpace>();
        moq.SetupGet(x => x.BaseSurface).Returns(baseSurface ?? Plane());
        moq.SetupGet(x => x.AgreementFlag).Returns(agreementFlag);
        moq.SetupGet(x => x.EntityLabel).Returns(entityLabel);
        return moq.Object;
    }

    // ── Faceted BRep mocks ────────────────────────────────────────

    /// <summary>
    /// Creates an IIfcPolyLoop from 3D points. Points should define a closed polygon.
    /// </summary>
    public static IIfcPolyLoop PolyLoop(params (double x, double y, double z)[] points)
    {
        var moq = MakeMoq<IIfcPolyLoop>();
        var obj = moq.Object;
        var polygon = new ItemListMoq<IIfcCartesianPoint>();
        foreach (var (x, y, z) in points)
            polygon.Add(CartesianPoint3d(x, y, z));
        moq.SetupGet(p => p.Polygon).Returns(polygon);
        return obj;
    }

    /// <summary>
    /// Creates an IIfcFaceOuterBound with a PolyLoop bound.
    /// </summary>
    public static IIfcFaceOuterBound FaceOuterBound(IIfcPolyLoop polyLoop, bool orientation = true)
    {
        var moq = MakeMoq<IIfcFaceOuterBound>();
        moq.SetupGet(b => b.Bound).Returns(polyLoop);
        moq.SetupGet(b => b.Orientation).Returns(orientation);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcFaceBound (inner bound / void) with a PolyLoop.
    /// </summary>
    public static IIfcFaceBound FaceBound(IIfcPolyLoop polyLoop, bool orientation = true)
    {
        var moq = MakeMoq<IIfcFaceBound>();
        moq.SetupGet(b => b.Bound).Returns(polyLoop);
        moq.SetupGet(b => b.Orientation).Returns(orientation);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcFace with the given bounds (first should be FaceOuterBound).
    /// </summary>
    public static IIfcFace Face(params IIfcFaceBound[] bounds)
    {
        var moq = MakeMoq<IIfcFace>();
        var boundSet = new ItemListMoq<IIfcFaceBound>();
        foreach (var b in bounds)
            boundSet.Add(b);
        moq.SetupGet(f => f.Bounds).Returns(boundSet);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcClosedShell with the given faces.
    /// </summary>
    public static IIfcClosedShell ClosedShell(params IIfcFace[] faces)
    {
        var moq = MakeMoq<IIfcClosedShell>();
        var faceSet = new ItemListMoq<IIfcFace>();
        foreach (var f in faces)
            faceSet.Add(f);
        moq.SetupGet(s => s.CfsFaces).Returns(faceSet);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcFacetedBrep with a closed shell as its outer boundary.
    /// </summary>
    public static IIfcFacetedBrep FacetedBrep(IIfcClosedShell outer)
    {
        var moq = MakeMoq<IIfcFacetedBrep>();
        moq.SetupGet(b => b.Outer).Returns(outer);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcFacetedBrep)));
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcFacetedBrepWithVoids with an outer shell and void shells.
    /// </summary>
    public static IIfcFacetedBrepWithVoids FacetedBrepWithVoids(
        IIfcClosedShell outer, params IIfcClosedShell[] voids)
    {
        var moq = MakeMoq<IIfcFacetedBrepWithVoids>();
        moq.SetupGet(b => b.Outer).Returns(outer);
        var voidSet = new ItemListMoq<IIfcClosedShell>();
        foreach (var v in voids)
            voidSet.Add(v);
        moq.SetupGet(b => b.Voids).Returns(voidSet);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcFacetedBrepWithVoids)));
        return moq.Object;
    }

    // ── Advanced BRep mocks ────────────────────────────────────────

    private static int _advancedBrepEntityLabelCounter = 5000;

    public static IIfcVertexPoint VertexPoint(double x, double y, double z)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcVertexPoint>();
        moq.SetupGet(v => v.EntityLabel).Returns(label);
        moq.SetupGet(v => v.VertexGeometry).Returns(CartesianPoint3d(x, y, z));
        return moq.Object;
    }

    public static IIfcEdgeCurve EdgeCurve(
        IIfcVertexPoint start, IIfcVertexPoint end,
        IIfcCurve edgeGeometry, bool sameSense = true)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcEdgeCurve>();
        moq.SetupGet(e => e.EntityLabel).Returns(label);
        moq.SetupGet(e => e.EdgeStart).Returns(start);
        moq.SetupGet(e => e.EdgeEnd).Returns(end);
        moq.SetupGet(e => e.EdgeGeometry).Returns(edgeGeometry);
        moq.SetupGet(e => e.SameSense).Returns(sameSense);
        return moq.Object;
    }

    public static IIfcOrientedEdge OrientedEdge(IIfcEdgeCurve edgeElement, bool orientation = true)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcOrientedEdge>();
        moq.SetupGet(e => e.EntityLabel).Returns(label);
        moq.SetupGet(e => e.EdgeElement).Returns(edgeElement);
        moq.SetupGet(e => e.Orientation).Returns(orientation);
        return moq.Object;
    }

    public static IIfcEdgeLoop EdgeLoop(params IIfcOrientedEdge[] edges)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcEdgeLoop>();
        moq.SetupGet(l => l.EntityLabel).Returns(label);
        var edgeList = new ItemListMoq<IIfcOrientedEdge>();
        foreach (var e in edges)
            edgeList.Add(e);
        moq.SetupGet(l => l.EdgeList).Returns(edgeList);
        return moq.Object;
    }

    public static IIfcFaceOuterBound AdvancedFaceOuterBound(IIfcEdgeLoop edgeLoop, bool orientation = true)
    {
        var moq = MakeMoq<IIfcFaceOuterBound>();
        moq.SetupGet(b => b.Bound).Returns(edgeLoop);
        moq.SetupGet(b => b.Orientation).Returns(orientation);
        return moq.Object;
    }

    public static IIfcAdvancedFace AdvancedFace(
        IIfcSurface faceSurface, bool sameSense, params IIfcFaceBound[] bounds)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcAdvancedFace>();
        moq.SetupGet(f => f.EntityLabel).Returns(label);
        moq.SetupGet(f => f.FaceSurface).Returns(faceSurface);
        moq.SetupGet(f => f.SameSense).Returns(sameSense);
        var boundSet = new ItemListMoq<IIfcFaceBound>();
        foreach (var b in bounds)
            boundSet.Add(b);
        moq.SetupGet(f => f.Bounds).Returns(boundSet);
        return moq.Object;
    }

    public static IIfcPlane IfcPlane(
        double ox, double oy, double oz,
        double nx, double ny, double nz,
        double rx = 1, double ry = 0, double rz = 0)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcPlane>();
        moq.SetupGet(p => p.EntityLabel).Returns(label);
        moq.SetupGet(p => p.ExpressType).Returns(MetaData.ExpressType(typeof(IfcPlane)));
        moq.SetupGet(p => p.Position).Returns(Axis2Placement3d(
            axis: Direction3d(nx, ny, nz),
            refDir: Direction3d(rx, ry, rz),
            loc: CartesianPoint3d(ox, oy, oz)));
        return moq.Object;
    }

    public static IIfcLine IfcLine(
        double ox, double oy, double oz,
        double dx, double dy, double dz,
        double magnitude = 1.0)
    {
        var label = Interlocked.Increment(ref _advancedBrepEntityLabelCounter);
        var moq = MakeMoq<IIfcLine>();
        moq.SetupGet(l => l.EntityLabel).Returns(label);
        moq.SetupGet(l => l.Pnt).Returns(CartesianPoint3d(ox, oy, oz));
        var dirMoq = MakeMoq<IIfcVector>();
        dirMoq.SetupGet(v => v.Orientation).Returns(Direction3d(dx, dy, dz));
        dirMoq.SetupGet(v => v.Magnitude).Returns(magnitude);
        moq.SetupGet(l => l.Dir).Returns(dirMoq.Object);
        return moq.Object;
    }

    public static IIfcAdvancedBrep AdvancedBrep(IIfcClosedShell outer)
    {
        var moq = MakeMoq<IIfcAdvancedBrep>();
        moq.SetupGet(b => b.Outer).Returns(outer);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcAdvancedBrep)));
        return moq.Object;
    }

    public static IIfcAdvancedBrepWithVoids AdvancedBrepWithVoids(
        IIfcClosedShell outer, params IIfcClosedShell[] voids)
    {
        var moq = MakeMoq<IIfcAdvancedBrepWithVoids>();
        moq.SetupGet(b => b.Outer).Returns(outer);
        var voidSet = new ItemListMoq<IIfcClosedShell>();
        foreach (var v in voids)
            voidSet.Add(v);
        moq.SetupGet(b => b.Voids).Returns(voidSet);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcAdvancedBrepWithVoids)));
        return moq.Object;
    }

    /// <summary>
    /// Builds a box as an Advanced BRep with planar faces and line edge curves.
    /// Each face is an IIfcAdvancedFace with an IIfcPlane surface and an IIfcEdgeLoop.
    /// </summary>
    public static IIfcClosedShell AdvancedBoxShell(
        double x0, double y0, double z0,
        double x1, double y1, double z1)
    {
        // 8 corner vertices
        var v000 = VertexPoint(x0, y0, z0);
        var v100 = VertexPoint(x1, y0, z0);
        var v110 = VertexPoint(x1, y1, z0);
        var v010 = VertexPoint(x0, y1, z0);
        var v001 = VertexPoint(x0, y0, z1);
        var v101 = VertexPoint(x1, y0, z1);
        var v111 = VertexPoint(x1, y1, z1);
        var v011 = VertexPoint(x0, y1, z1);

        // Helper: create a line edge curve between two vertices
        IIfcEdgeCurve MakeEdge(IIfcVertexPoint start, IIfcVertexPoint end)
        {
            var sp = (IIfcCartesianPoint)start.VertexGeometry;
            var ep = (IIfcCartesianPoint)end.VertexGeometry;
            double dx = ep.X - sp.X, dy = ep.Y - sp.Y, dz = ep.Z - sp.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var line = IfcLine(sp.X, sp.Y, sp.Z, dx / len, dy / len, dz / len, len);
            return EdgeCurve(start, end, line);
        }

        // 12 edges of the box
        var e_000_100 = MakeEdge(v000, v100);
        var e_100_110 = MakeEdge(v100, v110);
        var e_110_010 = MakeEdge(v110, v010);
        var e_010_000 = MakeEdge(v010, v000);
        var e_001_101 = MakeEdge(v001, v101);
        var e_101_111 = MakeEdge(v101, v111);
        var e_111_011 = MakeEdge(v111, v011);
        var e_011_001 = MakeEdge(v011, v001);
        var e_000_001 = MakeEdge(v000, v001);
        var e_100_101 = MakeEdge(v100, v101);
        var e_110_111 = MakeEdge(v110, v111);
        var e_010_011 = MakeEdge(v010, v011);

        // Helper: build a planar advanced face
        IIfcAdvancedFace MakeFace(
            double nx, double ny, double nz,
            double px, double py, double pz,
            params (IIfcEdgeCurve edge, bool forward)[] edges)
        {
            var plane = IfcPlane(px, py, pz, nx, ny, nz);
            var orientedEdges = edges.Select(e => OrientedEdge(e.edge, e.forward)).ToArray();
            var loop = EdgeLoop(orientedEdges);
            var outerBound = AdvancedFaceOuterBound(loop);
            return AdvancedFace(plane, true, outerBound);
        }

        // 6 faces with correct edge orientation (outward normals)
        // Bottom (z=z0, normal -Z)
        var bottom = MakeFace(0, 0, -1, x0, y0, z0,
            (e_000_100, true), (e_100_110, true), (e_110_010, true), (e_010_000, true));

        // Top (z=z1, normal +Z)
        var top = MakeFace(0, 0, 1, x0, y0, z1,
            (e_001_101, true), (e_101_111, true), (e_111_011, true), (e_011_001, true));

        // Front (y=y0, normal -Y)
        var front = MakeFace(0, -1, 0, x0, y0, z0,
            (e_000_100, true), (e_100_101, true), (e_001_101, false), (e_000_001, false));

        // Back (y=y1, normal +Y)
        var back = MakeFace(0, 1, 0, x0, y1, z0,
            (e_110_010, true), (e_010_011, true), (e_111_011, false), (e_110_111, false));

        // Left (x=x0, normal -X)
        var left = MakeFace(-1, 0, 0, x0, y0, z0,
            (e_010_000, true), (e_000_001, true), (e_011_001, false), (e_010_011, false));

        // Right (x=x1, normal +X)
        var right = MakeFace(1, 0, 0, x1, y0, z0,
            (e_100_110, true), (e_110_111, true), (e_101_111, false), (e_100_101, false));

        return ClosedShell(bottom, top, front, back, left, right);
    }

    /// <summary>
    /// Helper: builds a box closed shell from corner coordinates.
    /// Creates 6 rectangular faces forming a closed box.
    /// </summary>
    /// <summary>
    /// Creates an IIfcConnectedFaceSet (open shell) with the given faces.
    /// </summary>
    public static IIfcConnectedFaceSet ConnectedFaceSet(params IIfcFace[] faces)
    {
        var moq = MakeMoq<IIfcConnectedFaceSet>();
        var faceSet = new ItemListMoq<IIfcFace>();
        foreach (var f in faces)
            faceSet.Add(f);
        moq.SetupGet(s => s.CfsFaces).Returns(faceSet);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcFaceBasedSurfaceModel from connected face sets.
    /// </summary>
    public static IIfcFaceBasedSurfaceModel FaceBasedSurfaceModel(
        params IIfcConnectedFaceSet[] faceSets)
    {
        var moq = MakeMoq<IIfcFaceBasedSurfaceModel>();
        var faceSetCollection = new ItemListMoq<IIfcConnectedFaceSet>();
        foreach (var fs in faceSets)
            faceSetCollection.Add(fs);
        moq.SetupGet(m => m.FbsmFaces).Returns(faceSetCollection);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcFaceBasedSurfaceModel)));
        return moq.Object;
    }

    public static IIfcClosedShell BoxShell(
        double x0, double y0, double z0,
        double x1, double y1, double z1)
    {
        // 8 vertices of the box
        // Bottom: (x0,y0,z0), (x1,y0,z0), (x1,y1,z0), (x0,y1,z0)
        // Top:    (x0,y0,z1), (x1,y0,z1), (x1,y1,z1), (x0,y1,z1)

        // Bottom face (Z=z0) — CW when viewed from outside (facing -Z)
        var bottom = Face(FaceOuterBound(PolyLoop(
            (x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0))));

        // Top face (Z=z1) — CCW when viewed from outside (facing +Z)
        var top = Face(FaceOuterBound(PolyLoop(
            (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))));

        // Front face (Y=y0)
        var front = Face(FaceOuterBound(PolyLoop(
            (x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1))));

        // Back face (Y=y1)
        var back = Face(FaceOuterBound(PolyLoop(
            (x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0))));

        // Left face (X=x0)
        var left = Face(FaceOuterBound(PolyLoop(
            (x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0))));

        // Right face (X=x1)
        var right = Face(FaceOuterBound(PolyLoop(
            (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1))));

        return ClosedShell(bottom, top, front, back, left, right);
    }

    /// <summary>
    /// Creates an IIfcOpenShell with the given faces.
    /// </summary>
    public static IIfcOpenShell OpenShell(params IIfcFace[] faces)
    {
        var moq = MakeMoq<IIfcOpenShell>();
        var faceSet = new ItemListMoq<IIfcFace>();
        foreach (var f in faces)
            faceSet.Add(f);
        moq.SetupGet(s => s.CfsFaces).Returns(faceSet);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcShellBasedSurfaceModel from open or closed shells.
    /// </summary>
    public static IIfcShellBasedSurfaceModel ShellBasedSurfaceModel(
        params IIfcShell[] shells)
    {
        var moq = MakeMoq<IIfcShellBasedSurfaceModel>();
        var shellCollection = new ItemListMoq<IIfcShell>();
        foreach (var s in shells)
            shellCollection.Add(s);
        moq.SetupGet(m => m.SbsmBoundary).Returns(shellCollection);
        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcShellBasedSurfaceModel)));
        return moq.Object;
    }

    // ── Tessellated item mocks ───────────────────────────────────────

    /// <summary>
    /// Creates an IIfcCartesianPointList3D from a flat array of (x,y,z) tuples.
    /// </summary>
    public static IIfcCartesianPointList3D CartesianPointList3D(params (double x, double y, double z)[] points)
    {
        var moq = MakeMoq<IIfcCartesianPointList3D>();
        var coordList = new ItemListMoq<IItemSet<IfcLengthMeasure>>();
        foreach (var (x, y, z) in points)
        {
            var row = new ItemListMoq<IfcLengthMeasure>();
            row.Add(new IfcLengthMeasure(x));
            row.Add(new IfcLengthMeasure(y));
            row.Add(new IfcLengthMeasure(z));
            coordList.Add(row);
        }
        moq.SetupGet(c => c.CoordList).Returns(coordList);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcTriangulatedFaceSet from coordinates and triangle indices (1-based).
    /// </summary>
    public static IIfcTriangulatedFaceSet TriangulatedFaceSet(
        IIfcCartesianPointList3D coordinates,
        (long i1, long i2, long i3)[] triangles,
        bool? closed = null)
    {
        var moq = MakeMoq<IIfcTriangulatedFaceSet>();
        moq.SetupGet(t => t.Coordinates).Returns(coordinates);

        var coordIndex = new ItemListMoq<IItemSet<IfcPositiveInteger>>();
        foreach (var (i1, i2, i3) in triangles)
        {
            var tri = new ItemListMoq<IfcPositiveInteger>();
            tri.Add(new IfcPositiveInteger(i1));
            tri.Add(new IfcPositiveInteger(i2));
            tri.Add(new IfcPositiveInteger(i3));
            coordIndex.Add(tri);
        }
        moq.SetupGet(t => t.CoordIndex).Returns(coordIndex);

        if (closed.HasValue)
            moq.SetupGet(t => t.Closed).Returns(new IfcBoolean(closed.Value));
        else
            moq.SetupGet(t => t.Closed).Returns((IfcBoolean?)null);

        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcTriangulatedFaceSet)));
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcIndexedPolygonalFace from 1-based coordinate indices.
    /// </summary>
    public static IIfcIndexedPolygonalFace IndexedPolygonalFace(params long[] indices)
    {
        var moq = MakeMoq<IIfcIndexedPolygonalFace>();
        var coordIndex = new ItemListMoq<IfcPositiveInteger>();
        foreach (var idx in indices)
            coordIndex.Add(new IfcPositiveInteger(idx));
        moq.SetupGet(f => f.CoordIndex).Returns(coordIndex);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcIndexedPolygonalFaceWithVoids from outer and inner 1-based index arrays.
    /// </summary>
    public static IIfcIndexedPolygonalFaceWithVoids IndexedPolygonalFaceWithVoids(
        long[] outerIndices, params long[][] innerLoops)
    {
        var moq = MakeMoq<IIfcIndexedPolygonalFaceWithVoids>();
        var coordIndex = new ItemListMoq<IfcPositiveInteger>();
        foreach (var idx in outerIndices)
            coordIndex.Add(new IfcPositiveInteger(idx));
        moq.SetupGet(f => f.CoordIndex).Returns(coordIndex);

        var innerCoordIndices = new ItemListMoq<IItemSet<IfcPositiveInteger>>();
        foreach (var loop in innerLoops)
        {
            var innerLoop = new ItemListMoq<IfcPositiveInteger>();
            foreach (var idx in loop)
                innerLoop.Add(new IfcPositiveInteger(idx));
            innerCoordIndices.Add(innerLoop);
        }
        moq.SetupGet(f => f.InnerCoordIndices).Returns(innerCoordIndices);
        return moq.Object;
    }

    /// <summary>
    /// Creates an IIfcPolygonalFaceSet from coordinates and faces.
    /// </summary>
    public static IIfcPolygonalFaceSet PolygonalFaceSet(
        IIfcCartesianPointList3D coordinates,
        IIfcIndexedPolygonalFace[] faces,
        bool? closed = null)
    {
        var moq = MakeMoq<IIfcPolygonalFaceSet>();
        moq.SetupGet(p => p.Coordinates).Returns(coordinates);

        var faceList = new ItemListMoq<IIfcIndexedPolygonalFace>();
        foreach (var f in faces)
            faceList.Add(f);
        moq.SetupGet(p => p.Faces).Returns(faceList);

        if (closed.HasValue)
            moq.SetupGet(p => p.Closed).Returns(new IfcBoolean(closed.Value));
        else
            moq.SetupGet(p => p.Closed).Returns((IfcBoolean?)null);

        moq.SetupGet(x => x.ExpressType)
            .Returns(MetaData.ExpressType(typeof(IfcPolygonalFaceSet)));
        return moq.Object;
    }
}
