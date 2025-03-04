#include "xbim_geometry_native.h"

// OCCT headers – verify linkage against OpenCASCADE 7.8.1
#include <BRep_Builder.hxx>
#include <gp_Pnt.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Compound.hxx>
#include <BRepPrimAPI_MakeBox.hxx>

static const char* XBIM_NATIVE_VERSION = "0.2.0";

XBIM_EXPORT const char* XBIM_CALL xbim_native_version(void)
{
    return XBIM_NATIVE_VERSION;
}

XBIM_EXPORT int XBIM_CALL xbim_native_is_available(void)
{
    // Exercise OCCT types to prove runtime linkage works.
    // Build a trivial box via BRepPrimAPI and check the result is valid.
    BRepPrimAPI_MakeBox makeBox(gp_Pnt(0, 0, 0), 1.0, 1.0, 1.0);
    makeBox.Build();
    if (!makeBox.IsDone())
        return 0;

    const TopoDS_Shape& shape = makeBox.Shape();
    return shape.IsNull() ? 0 : 1;
}
