using Moq;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Ifc4;
using Xbim.Ifc4.GeometricModelResource;
using Xbim.Ifc4.GeometryResource;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4.MeasureResource;
using Xbim.Ifc4.ProfileResource;

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
}
