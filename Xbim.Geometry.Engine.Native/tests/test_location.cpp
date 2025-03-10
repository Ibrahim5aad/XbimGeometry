/*
 * test_location.cpp
 *
 * Validates the location/transform handle lifecycle and operations:
 *   - Create identity location
 *   - Create location from axis-2 placement
 *   - Compose two locations
 *   - Destroy location handles (including null)
 *   - Move a shape with a location transform
 *   - Verify moved shape preserves geometry (volume)
 *   - Verify moved shape bounding box is offset correctly
 *   - Null handle error handling
 */

#include "xbim_geometry_api.h"
#include "xbim_shape.h"
#include "xbim_location.h"

#include <BRepPrimAPI_MakeBox.hxx>
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

static XbimShapeHandle make_null_shape()
{
    TopoDS_Shape empty;
    return wrap_shape(empty);
}

int main()
{
    printf("=== Location Handle Lifecycle Tests ===\n\n");

    /* Test 1: Destroy null handle is a safe no-op */
    {
        XbimResult rc = xbim_location_destroy(nullptr);
        TEST("destroy null location returns OK", rc == XBIM_OK);
    }

    /* Test 2: Create identity location */
    {
        XbimLocationHandle loc = nullptr;
        XbimResult rc = xbim_location_create_identity(&loc);
        TEST("create_identity returns OK", rc == XBIM_OK);
        TEST("identity handle is not null", loc != nullptr);
        xbim_location_destroy(loc);
    }

    /* Test 3: Create identity with null outHandle returns INVALID_ARG */
    {
        XbimResult rc = xbim_location_create_identity(nullptr);
        TEST("create_identity with null outHandle returns INVALID_ARG", rc == XBIM_INVALID_ARG);
    }

    /* Test 4: Create location from axis-2 placement (standard orientation at origin) */
    {
        XbimLocationHandle loc = nullptr;
        XbimResult rc = xbim_location_create_from_axis2(
            0.0, 0.0, 0.0,    /* origin */
            0.0, 0.0, 1.0,    /* zDir = Z axis */
            1.0, 0.0, 0.0,    /* xDir = X axis */
            &loc);
        TEST("create_from_axis2 at origin returns OK", rc == XBIM_OK);
        TEST("axis2 handle is not null", loc != nullptr);
        xbim_location_destroy(loc);
    }

    /* Test 5: Create location from axis-2 with a translation offset */
    {
        XbimLocationHandle loc = nullptr;
        XbimResult rc = xbim_location_create_from_axis2(
            100.0, 200.0, 300.0,  /* origin offset */
            0.0, 0.0, 1.0,       /* zDir */
            1.0, 0.0, 0.0,       /* xDir */
            &loc);
        TEST("create_from_axis2 with offset returns OK", rc == XBIM_OK);
        TEST("offset handle is not null", loc != nullptr);
        xbim_location_destroy(loc);
    }

    /* Test 6: Create from axis2 with null outHandle returns INVALID_ARG */
    {
        XbimResult rc = xbim_location_create_from_axis2(
            0, 0, 0, 0, 0, 1, 1, 0, 0, nullptr);
        TEST("create_from_axis2 with null outHandle returns INVALID_ARG", rc == XBIM_INVALID_ARG);
    }

    /* Test 7: Compose identity with identity yields identity */
    {
        XbimLocationHandle id1 = nullptr, id2 = nullptr, composed = nullptr;
        xbim_location_create_identity(&id1);
        xbim_location_create_identity(&id2);
        XbimResult rc = xbim_location_compose(id1, id2, &composed);
        TEST("compose identity*identity returns OK", rc == XBIM_OK);
        TEST("composed handle is not null", composed != nullptr);
        xbim_location_destroy(id1);
        xbim_location_destroy(id2);
        xbim_location_destroy(composed);
    }

    /* Test 8: Compose two translation locations */
    {
        XbimLocationHandle loc1 = nullptr, loc2 = nullptr, composed = nullptr;
        xbim_location_create_from_axis2(
            10.0, 0.0, 0.0,  0.0, 0.0, 1.0,  1.0, 0.0, 0.0, &loc1);
        xbim_location_create_from_axis2(
            0.0, 20.0, 0.0,  0.0, 0.0, 1.0,  1.0, 0.0, 0.0, &loc2);

        XbimResult rc = xbim_location_compose(loc1, loc2, &composed);
        TEST("compose two translations returns OK", rc == XBIM_OK);
        TEST("composed translation handle is not null", composed != nullptr);

        xbim_location_destroy(loc1);
        xbim_location_destroy(loc2);
        xbim_location_destroy(composed);
    }

    /* Test 9: Compose with null handles returns INVALID_HANDLE */
    {
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);

        XbimLocationHandle out = nullptr;
        XbimResult rc = xbim_location_compose(nullptr, loc, &out);
        TEST("compose with null loc1 returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        rc = xbim_location_compose(loc, nullptr, &out);
        TEST("compose with null loc2 returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        xbim_location_destroy(loc);
    }

    /* Test 10: Compose with null outHandle returns INVALID_ARG */
    {
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);
        XbimResult rc = xbim_location_compose(loc, loc, nullptr);
        TEST("compose with null outHandle returns INVALID_ARG", rc == XBIM_INVALID_ARG);
        xbim_location_destroy(loc);
    }

    /* Test 11: Move a box with identity - volume preserved */
    {
        XbimShapeHandle box = make_box(10.0, 20.0, 30.0);
        XbimLocationHandle idLoc = nullptr;
        xbim_location_create_identity(&idLoc);

        XbimShapeHandle moved = nullptr;
        XbimResult rc = xbim_shape_moved(box, idLoc, &moved);
        TEST("shape_moved with identity returns OK", rc == XBIM_OK);
        TEST("moved shape is not null", moved != nullptr);

        double vol = 0.0;
        xbim_shape_volume(moved, &vol);
        TEST("moved box volume ~= 6000", APPROX(vol, 6000.0, 1.0));

        xbim_shape_destroy(box);
        xbim_shape_destroy(moved);
        xbim_location_destroy(idLoc);
    }

    /* Test 12: Move a box with a translation - bounding box shifts */
    {
        XbimShapeHandle box = make_box(10.0, 10.0, 10.0);
        XbimLocationHandle loc = nullptr;
        /* Translate by (100, 200, 300) with standard axes */
        xbim_location_create_from_axis2(
            100.0, 200.0, 300.0,
            0.0, 0.0, 1.0,
            1.0, 0.0, 0.0,
            &loc);

        XbimShapeHandle moved = nullptr;
        XbimResult rc = xbim_shape_moved(box, loc, &moved);
        TEST("shape_moved with translation returns OK", rc == XBIM_OK);

        double minX, minY, minZ, maxX, maxY, maxZ;
        rc = xbim_shape_bounding_box(moved, &minX, &minY, &minZ, &maxX, &maxY, &maxZ);
        TEST("moved bbox returns OK", rc == XBIM_OK);
        TEST("moved bbox minX ~= 100", APPROX(minX, 100.0, 1.0));
        TEST("moved bbox minY ~= 200", APPROX(minY, 200.0, 1.0));
        TEST("moved bbox minZ ~= 300", APPROX(minZ, 300.0, 1.0));
        TEST("moved bbox maxX ~= 110", APPROX(maxX, 110.0, 1.0));
        TEST("moved bbox maxY ~= 210", APPROX(maxY, 210.0, 1.0));
        TEST("moved bbox maxZ ~= 310", APPROX(maxZ, 310.0, 1.0));

        /* Volume should be unchanged */
        double vol = 0.0;
        xbim_shape_volume(moved, &vol);
        TEST("moved box volume ~= 1000", APPROX(vol, 1000.0, 1.0));

        xbim_shape_destroy(box);
        xbim_shape_destroy(moved);
        xbim_location_destroy(loc);
    }

    /* Test 13: Move shape with composed location (two translations) */
    {
        XbimShapeHandle box = make_box(5.0, 5.0, 5.0);

        XbimLocationHandle loc1 = nullptr, loc2 = nullptr, composed = nullptr;
        /* loc1: translate to (50, 0, 0) */
        xbim_location_create_from_axis2(
            50.0, 0.0, 0.0,  0.0, 0.0, 1.0,  1.0, 0.0, 0.0, &loc1);
        /* loc2: translate to (0, 100, 0) */
        xbim_location_create_from_axis2(
            0.0, 100.0, 0.0,  0.0, 0.0, 1.0,  1.0, 0.0, 0.0, &loc2);

        xbim_location_compose(loc1, loc2, &composed);

        XbimShapeHandle moved = nullptr;
        XbimResult rc = xbim_shape_moved(box, composed, &moved);
        TEST("shape_moved with composed returns OK", rc == XBIM_OK);

        double minX, minY, minZ, maxX, maxY, maxZ;
        xbim_shape_bounding_box(moved, &minX, &minY, &minZ, &maxX, &maxY, &maxZ);
        /* Composed: translate (50,0,0) then (0,100,0) => origin at (50,100,0) */
        TEST("composed bbox minX ~= 50",  APPROX(minX, 50.0, 2.0));
        TEST("composed bbox minY ~= 100", APPROX(minY, 100.0, 2.0));
        TEST("composed bbox minZ ~= 0",   APPROX(minZ, 0.0, 2.0));

        xbim_shape_destroy(box);
        xbim_shape_destroy(moved);
        xbim_location_destroy(loc1);
        xbim_location_destroy(loc2);
        xbim_location_destroy(composed);
    }

    /* Test 14: shape_moved with null shape handle */
    {
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);
        XbimShapeHandle out = nullptr;

        XbimResult rc = xbim_shape_moved(nullptr, loc, &out);
        TEST("shape_moved with null shape returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        xbim_location_destroy(loc);
    }

    /* Test 15: shape_moved with null location handle */
    {
        XbimShapeHandle box = make_box(5.0, 5.0, 5.0);
        XbimShapeHandle out = nullptr;

        XbimResult rc = xbim_shape_moved(box, nullptr, &out);
        TEST("shape_moved with null location returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);

        xbim_shape_destroy(box);
    }

    /* Test 16: shape_moved with null outHandle */
    {
        XbimShapeHandle box = make_box(5.0, 5.0, 5.0);
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);

        XbimResult rc = xbim_shape_moved(box, loc, nullptr);
        TEST("shape_moved with null outHandle returns INVALID_ARG", rc == XBIM_INVALID_ARG);

        xbim_shape_destroy(box);
        xbim_location_destroy(loc);
    }

    /* Test 17: shape_moved with a null (empty) shape */
    {
        XbimShapeHandle empty = make_null_shape();
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);
        XbimShapeHandle out = nullptr;

        XbimResult rc = xbim_shape_moved(empty, loc, &out);
        TEST("shape_moved with null shape returns NULL_SHAPE", rc == XBIM_NULL_SHAPE);

        xbim_shape_destroy(empty);
        xbim_location_destroy(loc);
    }

    /* Test 18: Destroy valid location returns OK */
    {
        XbimLocationHandle loc = nullptr;
        xbim_location_create_identity(&loc);
        XbimResult rc = xbim_location_destroy(loc);
        TEST("destroy valid location returns OK", rc == XBIM_OK);
    }

    /* Test 19: Create from rotated axes - 90deg rotation around Z */
    {
        XbimLocationHandle loc = nullptr;
        /* Rotate 90 deg around Z: xDir becomes Y, yDir becomes -X */
        XbimResult rc = xbim_location_create_from_axis2(
            0.0, 0.0, 0.0,    /* origin */
            0.0, 0.0, 1.0,    /* zDir = Z (unchanged) */
            0.0, 1.0, 0.0,    /* xDir = Y (90 deg rotated) */
            &loc);
        TEST("create rotated axis2 returns OK", rc == XBIM_OK);

        /* Apply to a box and check the bounding box is rotated */
        XbimShapeHandle box = make_box(10.0, 5.0, 3.0);
        XbimShapeHandle moved = nullptr;
        rc = xbim_shape_moved(box, loc, &moved);
        TEST("move with rotation returns OK", rc == XBIM_OK);

        double minX, minY, minZ, maxX, maxY, maxZ;
        xbim_shape_bounding_box(moved, &minX, &minY, &minZ, &maxX, &maxY, &maxZ);
        /* After 90 deg Z rotation: X extent (0..10) -> Y extent, Y extent (0..5) -> X extent (negated) */
        double xExtent = maxX - minX;
        double yExtent = maxY - minY;
        double zExtent = maxZ - minZ;
        TEST("rotated X extent ~= 5",  APPROX(xExtent, 5.0, 1.0));
        TEST("rotated Y extent ~= 10", APPROX(yExtent, 10.0, 1.0));
        TEST("rotated Z extent ~= 3",  APPROX(zExtent, 3.0, 1.0));

        /* Volume preserved */
        double vol = 0.0;
        xbim_shape_volume(moved, &vol);
        TEST("rotated box volume ~= 150", APPROX(vol, 150.0, 1.0));

        xbim_shape_destroy(box);
        xbim_shape_destroy(moved);
        xbim_location_destroy(loc);
    }

    printf("\n=== Results: %d/%d passed ===\n", tests_passed, tests_total);
    return (tests_passed == tests_total) ? 0 : 1;
}
