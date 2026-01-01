/*
 * xbim_mesh.h
 *
 * Internal definitions for WexBim mesh creation.
 *
 */

#ifndef XBIM_MESH_H
#define XBIM_MESH_H

#include "xbim_geometry_api.h"

#include <vector>
#include <cmath>

#include <gp_XYZ.hxx>
#include <gp_Dir.hxx>
#include <gp_Pnt.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Trsf.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Face.hxx>
#include <Poly_Triangulation.hxx>
#include <Poly_Triangle.hxx>
#include <TopExp_Explorer.hxx>
#include <BRepLProp_SLProps.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <NCollection_Vector.hxx>
#include <NCollection_Vec3.hxx>
#include <NCollection_CellFilter.hxx>
#include <Graphic3d_BndBox3d.hxx>
#include <BVH_Types.hxx>

#pragma region PackedNormal

struct PackedNormal
{
    unsigned char bytes[2];

    PackedNormal() : bytes{0, 0} {}
    PackedNormal(unsigned char u, unsigned char v) : bytes{u, v} {}

    unsigned char U() const { return bytes[0]; }
    unsigned char V() const { return bytes[1]; }

    static PackedNormal FromDirection(const gp_Dir& vec);
};

#pragma endregion

#pragma region PointInspector

struct CellFilter_InspectorXYZ
{
    enum { Dimension = 3 };
    typedef gp_XYZ Point;

    static Standard_Real Coord(int i, const Point& thePnt)
    {
        return thePnt.Coord(i + 1);
    }

    gp_XYZ Shift(const gp_XYZ& thePnt, Standard_Real theTol) const
    {
        return gp_XYZ(thePnt.X() + theTol, thePnt.Y() + theTol, thePnt.Z() + theTol);
    }
};

class MeshPointInspector : public CellFilter_InspectorXYZ
{
public:
    typedef Standard_Integer Target;

    explicit MeshPointInspector(Standard_Real tol) : sqTolerance_(tol * tol) {}

    int Add(const gp_XYZ& pnt)
    {
        points_.Append(pnt);
        return points_.Length() - 1;
    }

    void SetCurrent(const gp_XYZ& pnt)
    {
        current_ = pnt;
        resultIndex_ = -1;
    }

    int ResInd() const { return resultIndex_; }

    NCollection_CellFilter_Action Inspect(Standard_Integer theID);

    NCollection_Vector<gp_XYZ> points_;
    Standard_Real sqTolerance_;

private:
    int resultIndex_ = -1;
    gp_XYZ current_;
};

#pragma endregion

#pragma region FaceMeshIterator

class FaceMeshIterator
{
public:
    FaceMeshIterator(const TopoDS_Shape& shape, bool checkEdges);

    bool More() const { return !polyTriang_.IsNull(); }
    void Next();

    bool IsEmptyMesh() const
    {
        return polyTriang_.IsNull()
            || (polyTriang_->NbNodes() < 1 && polyTriang_->NbTriangles() < 1);
    }

    bool IsPlanar() const { return isPlanar_; }
    bool HasCurves;

    Standard_Integer NodeLower() const { return 1; }
    Standard_Integer NodeUpper() const { return polyTriang_->NbNodes(); }

    gp_Pnt NodeTransformed(Standard_Integer theNode) const
    {
        gp_Pnt p = polyTriang_->Node(theNode);
        p.Transform(trsf_);
        return p;
    }

    gp_Dir NormalTransformed(Standard_Integer theNode)
    {
        gp_Dir n = normal(theNode);
        if (trsf_.Form() != gp_Identity)
            n.Transform(trsf_);
        if (face_.Orientation() == TopAbs_REVERSED)
            n.Reverse();
        return n;
    }

    Standard_Integer ElemLower() const { return 1; }
    Standard_Integer ElemUpper() const { return polyTriang_->NbTriangles(); }

    Poly_Triangle TriangleOriented(Standard_Integer idx) const
    {
        Poly_Triangle tri = polyTriang_->Triangle(idx);
        bool reversed = (face_.Orientation() == TopAbs_REVERSED);
        if (reversed ^ isMirrored_)
            return Poly_Triangle(tri.Value(1), tri.Value(3), tri.Value(2));
        return tri;
    }

private:
    void initFace();
    gp_Dir normal(Standard_Integer theNode);
    bool hasCurves();

    bool checkEdges_;
    TopExp_Explorer faceIter_;
    TopoDS_Face face_;
    Handle(Poly_Triangulation) polyTriang_;
    TopLoc_Location faceLocation_;
    BRepLProp_SLProps slTool_;
    BRepAdaptor_Surface faceAdaptor_;
    gp_Trsf trsf_;
    bool isMirrored_ = false;
    bool isPlanar_ = false;
    NCollection_Vector<gp_Dir> normals_;
};

#pragma endregion

#pragma region WexBimMesh

typedef NCollection_Vec3<int> Vec3Int;

class WexBimMesh
{
public:
    WexBimMesh(double tolerance, double scale);

    void SaveIndicesAndNormals(FaceMeshIterator& faceIter);
    std::vector<unsigned char> Serialize() const;

    int VertexCount() const { return pointInspector_.points_.Length(); }
    int FaceCount() const { return static_cast<int>(indicesPerFace_.size()); }
    int TriangleCount() const;

    bool HasCurves = false;
    Graphic3d_BndBox3d BndBox;

private:
    int addPoint(gp_XYZ point);
    void saveNodes(const FaceMeshIterator& faceIter, std::vector<int>& nodeIndexes);

    void writeTriangleIndices(std::vector<unsigned char>& buf,
                              const Vec3Int& tri,
                              unsigned int maxVertices) const;
    void writeTriangleIndicesWithNormals(std::vector<unsigned char>& buf,
                                         const Vec3Int& tri,
                                         const PackedNormal& a,
                                         const PackedNormal& b,
                                         const PackedNormal& c,
                                         unsigned int maxVertices) const;

    double tolerance_;
    double scale_;
    MeshPointInspector pointInspector_;
    NCollection_CellFilter<MeshPointInspector> pointFilter_;

    std::vector<NCollection_Vector<Vec3Int>> indicesPerFace_;
    std::vector<std::vector<PackedNormal>> normalsPerFace_;

    static const unsigned char VERSION = 1;
};

#pragma endregion

#endif /* XBIM_MESH_H */
