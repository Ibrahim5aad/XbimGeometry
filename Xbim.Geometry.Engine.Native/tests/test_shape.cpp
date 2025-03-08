/*
 * test_shape.cpp
 *
 * Validates the shape handle lifecycle and query API:
 *   - Destroy null handle (safe no-op)
 *   - Create box shape, verify type == SOLID
 *   - Verify is_valid returns 1 for a good box
 *   - Verify is_closed returns 1 for a closed solid
 *   - Verify bounding_box returns correct extents for a 10x20x30 box
 *   - Verify volume is approximately 6000 for a 10x20x30 box
 *   - Verify surface_area is approximately 2200 for a 10x20x30 box
 *   - Null handle returns appropriate error codes / 0 values
 *   - Null shape (empty TopoDS_Shape) returns XBIM_NULL_SHAPE
 */

#include "xbim_geometry_api.h"
#include "xbim_shape.h"

#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <TopoDS_Shape.hxx>
#include <gp_Pnt.hxx>

#include <cstdio>
#include <cmath>
#include <cstdlib>
#include <new>

static int tests_passed = 0;
static int tests_total = 0;

#define TEST(name, cond) do { \
    tests_total++; \
    if (cond) { tests_passed++; printf("  PASS: %s\n", name); } \
    else { printf("  FAIL: %s (line %d)\n", name, __LINE__); } \
} while(0)

#define APPROX(a, b, tol) (fabs((a) - (b)) < (tol))

/* Create an XbimShape_ handle directly - test-only helper.
 * The internal xbim_shape_create_from is not exported, so tests
 * allocate the struct and assign the TopoDS_Shape themselves. */
static XbimShapeHandle wrap_shape(const TopoDS_Shape& shape)
{
    XbimShape_* s = new (std::nothrow) XbimShape_();
    if (!s) return nullptr;
    s->shape = shape;
    return s;
}

static XbimShapeHandle make_box(double xLen, double yLen, double zLen)
{
    BRepPrimAPI_MakeBox maker(gp_Pnt(0, 0, 0), xLen, yLen, zLen);
    maker.Build();
    if (!maker.IsDone()) return nullptr;
    return wrap_shape(maker.Shape());
}

static XbimShapeHandle make_sphere(double radius)
{
    BRepPrimAPI_MakeSphere maker(radius);
    maker.Build();
    if (!maker.IsDone()) return nullptr;
    return wrap_shape(maker.Shape());
}

static XbimShapeHandle make_null_shape()
{
    TopoDS_Shape empty;
    return wrap_shape(empty);
}

int main()
{
    printf("=== Shape Handle Lifecycle Tests ===\n\n");

    /* Test 1: Destroy null handle is a safe no-op */
    {
        XbimResult rc = xbim_shape_destroy(nullptr);
        TEST("destroy null handle returns OK", rc == XBIM_OK);
    }

    /* Test 2: Create a 10x20x30 box, verify shape type is SOLID */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        TEST("box created", box != nullptr);

        XbimShapeType stype;
        XbimResult rc = xbim_shape_type(box, &stype);
        TEST("shape_type returns OK", rc == XBIM_OK);
        TEST("box type is SOLID", stype == XBIM_SHAPE_SOLID);

        xbim_shape_destroy(box);
    }

    /* Test 3: is_valid returns 1 for a good box */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        int valid = xbim_shape_is_valid(box);
        TEST("box is valid", valid == 1);
        xbim_shape_destroy(box);
    }

    /* Test 4: is_closed returns 1 for a closed solid */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        int closed = xbim_shape_is_closed(box);
        TEST("box is closed", closed == 1);
        xbim_shape_destroy(box);
    }

    /* Test 5: Bounding box of a 10x20x30 box at origin */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        double minX, minY, minZ, maxX, maxY, maxZ;
        XbimResult rc = xbim_shape_bounding_box(box, &minX, &minY, &minZ,
                                                     &maxX, &maxY, &maxZ);
        TEST("bounding_box returns OK", rc == XBIM_OK);
        /* BRepPrimAPI_MakeBox(origin, xLen, yLen, zLen) creates from (0,0,0) to (10,20,30) */
        TEST("bbox minX ~= 0",  APPROX(minX, 0.0, 1.0));
        TEST("bbox minY ~= 0",  APPROX(minY, 0.0, 1.0));
        TEST("bbox minZ ~= 0",  APPROX(minZ, 0.0, 1.0));
        TEST("bbox maxX ~= 10", APPROX(maxX, 10.0, 1.0));
        TEST("bbox maxY ~= 20", APPROX(maxY, 20.0, 1.0));
        TEST("bbox maxZ ~= 30", APPROX(maxZ, 30.0, 1.0));
        xbim_shape_destroy(box);
    }

    /* Test 6: Volume of a 10x20x30 box is ~6000 */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        double volume = 0.0;
        XbimResult rc = xbim_shape_volume(box, &volume);
        TEST("volume returns OK", rc == XBIM_OK);
        TEST("box volume ~= 6000", APPROX(volume, 6000.0, 1.0));
        xbim_shape_destroy(box);
    }

    /* Test 7: Surface area of a 10x20x30 box is 2*(10*20 + 20*30 + 10*30) = 2200 */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        double area = 0.0;
        XbimResult rc = xbim_shape_surface_area(box, &area);
        TEST("surface_area returns OK", rc == XBIM_OK);
        TEST("box surface area ~= 2200", APPROX(area, 2200.0, 1.0));
        xbim_shape_destroy(box);
    }

    /* Test 8: Sphere volume ~= (4/3)*pi*r^3 for r=5 => ~523.6 */
    {
        XbimShapeHandle sphere = make_sphere(5.0);
        TEST("sphere created", sphere != nullptr);

        double volume = 0.0;
        XbimResult rc = xbim_shape_volume(sphere, &volume);
        TEST("sphere volume returns OK", rc == XBIM_OK);
        double expected = (4.0 / 3.0) * 3.14159265358979 * 5.0 * 5.0 * 5.0;
        TEST("sphere volume ~= 523.6", APPROX(volume, expected, 1.0));

        XbimShapeType stype;
        xbim_shape_type(sphere, &stype);
        TEST("sphere type is SOLID", stype == XBIM_SHAPE_SOLID);

        xbim_shape_destroy(sphere);
    }

    /* Test 9: Null handle error handling */
    {
        XbimShapeType stype;
        XbimResult rc = xbim_shape_type(nullptr, &stype);
        TEST("type with null handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        int valid = xbim_shape_is_valid(nullptr);
        TEST("is_valid with null handle returns 0", valid == 0);

        int closed = xbim_shape_is_closed(nullptr);
        TEST("is_closed with null handle returns 0", closed == 0);

        double minX, minY, minZ, maxX, maxY, maxZ;
        rc = xbim_shape_bounding_box(nullptr, &minX, &minY, &minZ, &maxX, &maxY, &maxZ);
        TEST("bbox with null handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        double vol;
        rc = xbim_shape_volume(nullptr, &vol);
        TEST("volume with null handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        double area;
        rc = xbim_shape_surface_area(nullptr, &area);
        TEST("area with null handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);
    }

    /* Test 10: Null shape (empty TopoDS_Shape) returns XBIM_NULL_SHAPE */
    {
        XbimShapeHandle empty = make_null_shape();
        TEST("null shape handle created", empty != nullptr);

        XbimShapeType stype;
        XbimResult rc = xbim_shape_type(empty, &stype);
        TEST("type with null shape returns NULL_SHAPE", rc == XBIM_NULL_SHAPE);

        int valid = xbim_shape_is_valid(empty);
        TEST("is_valid with null shape returns 0", valid == 0);

        double minX, minY, minZ, maxX, maxY, maxZ;
        rc = xbim_shape_bounding_box(empty, &minX, &minY, &minZ, &maxX, &maxY, &maxZ);
        TEST("bbox with null shape returns NULL_SHAPE", rc == XBIM_NULL_SHAPE);

        double vol;
        rc = xbim_shape_volume(empty, &vol);
        TEST("volume with null shape returns NULL_SHAPE", rc == XBIM_NULL_SHAPE);

        double area;
        rc = xbim_shape_surface_area(empty, &area);
        TEST("area with null shape returns NULL_SHAPE", rc == XBIM_NULL_SHAPE);

        xbim_shape_destroy(empty);
    }

    /* Test 11: Null output pointer returns XBIM_INVALID_ARG */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);

        XbimResult rc = xbim_shape_type(box, nullptr);
        TEST("type with null outType returns INVALID_ARG", rc == XBIM_INVALID_ARG);

        rc = xbim_shape_volume(box, nullptr);
        TEST("volume with null outVolume returns INVALID_ARG", rc == XBIM_INVALID_ARG);

        rc = xbim_shape_surface_area(box, nullptr);
        TEST("area with null outArea returns INVALID_ARG", rc == XBIM_INVALID_ARG);

        rc = xbim_shape_bounding_box(box, nullptr, nullptr, nullptr, nullptr, nullptr, nullptr);
        TEST("bbox with null outputs returns INVALID_ARG", rc == XBIM_INVALID_ARG);

        xbim_shape_destroy(box);
    }

    /* Test 12: Destroy and verify no double-free crash (use-after-destroy is undefined, but verify single destroy works) */
    {
        XbimShapeHandle box = make_box(5.0, 5.0, 5.0);
        XbimResult rc = xbim_shape_destroy(box);
        TEST("destroy valid box returns OK", rc == XBIM_OK);
    }

    printf("\n=== Results: %d/%d passed ===\n", tests_passed, tests_total);
    return (tests_passed == tests_total) ? 0 : 1;
}
