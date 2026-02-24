using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Tests.Helpers;
using Xbim.Geometry.WexBim;
using Xbim.Ifc4;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;
using Xunit;

namespace Xbim.Geometry.Engine.Interop.Tests;

public class WexBimTests
{
    private readonly GeometryConverterFactory _geomConverterFactory;
    private readonly ILoggerFactory _loggerFactory;

    public WexBimTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _geomConverterFactory = new GeometryConverterFactory();
    }

    [Fact]
    public void Can_read_and_write_block_as_wexbim()
    {
        var geomEngineV6 = _geomConverterFactory.CreateGeometryEngineV6(new MemoryModel(new EntityFactoryIfc4()), _loggerFactory);

        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10) as IIfcCsgPrimitive3D;
        var solid = (IXSolid)geomEngineV6.Build(blockMoq);
        var meshFactors = geomEngineV6.MeshFactors.SetGranularity(MeshGranularity.Normal);

        IXAxisAlignedBoundingBox bounds;
        byte[] bytes = geomEngineV6.WexBimMeshFactory.CreateWexBimMesh(solid, meshFactors, 0.001, out bounds);

        var wexBimMesh = new WexBimMesh(bytes);
        wexBimMesh.Vertices.Count().Should().Be(8);
        wexBimMesh.TriangleCount.Should().Be(12);
        wexBimMesh.FaceCount.Should().Be(6);
    }

    [Fact]
    public void Can_read_and_write_different_mesh_granularity()
    {
        var geomEngineV6 = _geomConverterFactory.CreateGeometryEngineV6(new MemoryModel(new EntityFactoryIfc4()), _loggerFactory);

        var sphereMoq = IfcMoq.Sphere(radius: 10) as IIfcCsgPrimitive3D;
        var solid = (IXSolid)geomEngineV6.Build(sphereMoq);
        IXAxisAlignedBoundingBox bounds;

        var meshFactors = geomEngineV6.MeshFactors.SetGranularity(MeshGranularity.Fine);
        byte[] bytes = geomEngineV6.WexBimMeshFactory.CreateWexBimMesh(solid, meshFactors, 0.001, out bounds);

        var wexBimMesh = new WexBimMesh(bytes);
        wexBimMesh.TriangleCount.Should().Be(1224);
        var fineCount = wexBimMesh.TriangleCount;

        meshFactors = geomEngineV6.MeshFactors.SetGranularity(MeshGranularity.Normal);
        bytes = geomEngineV6.WexBimMeshFactory.CreateWexBimMesh(solid, meshFactors, 0.001, out bounds);
        wexBimMesh = new WexBimMesh(bytes);
        var normalCount = wexBimMesh.TriangleCount;

        meshFactors = geomEngineV6.MeshFactors.SetGranularity(MeshGranularity.Course);
        bytes = geomEngineV6.WexBimMeshFactory.CreateWexBimMesh(solid, meshFactors, 0.001, out bounds);
        wexBimMesh = new WexBimMesh(bytes);
        var courseCount = wexBimMesh.TriangleCount;
        fineCount.Should().BeGreaterThan(normalCount);
        normalCount.Should().BeGreaterThan(courseCount);
    }

    [Fact]
    public void Can_Create_Correct_Mesh_For_Sphere()
    {
        var sphereMoq = IfcMoq.Sphere(radius: 10);

        var model = new MemoryModel(new EntityFactoryIfc4());
        model.ModelFactors = new XbimModelFactors(1, 0.001, 1e-5);
        var geomEngineV6 = _geomConverterFactory.CreateGeometryEngineV6(model, _loggerFactory);
        var solid = geomEngineV6.Create(sphereMoq) as IXbimSolid;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        geomEngineV6.WriteTriangulation(bw, solid, model.ModelFactors.Precision, 20, 10);
        var wexBimV6 = new WexBimMesh(ms.ToArray());
        wexBimV6.Should().NotBeNull();
        var numTriangles = wexBimV6.TriangleCount;
        var normals = wexBimV6.Faces.First().Normals.ToArray();

        var nodes = wexBimV6.Vertices.ToArray();
        var centre = new XbimPoint3D(sphereMoq.Position.Location.X, sphereMoq.Position.Location.Y, sphereMoq.Position.Location.Z);
        int i = 0;
        foreach (var index in wexBimV6.Faces.First().Indices)
        {
            var node = new XbimPoint3D(nodes[index].X, nodes[index].Y, nodes[index].Z);
            var normal = new XbimVector3D(normals[i].X, normals[i].Y, normals[i].Z);
            var dir = (node - centre).Normalized();
            normal.Angle(dir).Should().BeApproximately(0, 1e-5, "All normals should point away from the centre to the node");
            i++;
        }
    }

    [Fact]
    public void Can_Create_Correct_Mesh_For_Block()
    {
        var blockMoq = IfcMoq.Block(xLen: 10, yLen: 10, zLen: 10);

        var model = new MemoryModel(new EntityFactoryIfc4());
        model.ModelFactors = new XbimModelFactors(1, 0.001, 1e-5);
        var geomEngineV6 = _geomConverterFactory.CreateGeometryEngineV6(model, _loggerFactory);
        var solid = geomEngineV6.Create(blockMoq) as IXbimSolid;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        geomEngineV6.WriteTriangulation(bw, solid, model.ModelFactors.Precision, 20, 10);
        var wexBimV6 = new WexBimMesh(ms.ToArray());
        var numTriangles = wexBimV6.TriangleCount;
        wexBimV6.Should().NotBeNull();
        wexBimV6.FaceCount.Should().Be(6);
        var correctNormals = new HashSet<XbimVector3D>(new[] { new XbimVector3D(1,0,0), new XbimVector3D(0, 1, 0), new XbimVector3D(0, 0, 1),
                                    new XbimVector3D(-1,0,0), new XbimVector3D(0, -1, 0), new XbimVector3D(0, 0, -1)});

        foreach (var face in wexBimV6.Faces)
        {
            face.Normals.Count().Should().Be(1);
            var n = face.Normals.First();
            var normal = new XbimVector3D((Math.Abs(n.X) < 1e-5) ? 0 : n.X, (Math.Abs(n.Y) < 1e-5) ? 0 : n.Y, (Math.Abs(n.Z) < 1e-5) ? 0 : n.Z);
            bool gone = correctNormals.Remove(normal);
        }
        correctNormals.Any().Should().BeFalse();
    }
}
