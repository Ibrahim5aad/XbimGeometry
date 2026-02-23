/*
 * xbim_location.h
 *
 * Internal definition of the XbimLocation struct.
 * Wraps a heap-allocated TopLoc_Location with lifecycle management.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_LOCATION_H
#define XBIM_LOCATION_H

#include "xbim_geometry_api.h"

#include <TopLoc_Location.hxx>

struct XbimLocation_
{
    TopLoc_Location location;
};

/*
 * Allocate a new XbimLocation_ on the heap from a TopLoc_Location.
 * Returns nullptr on allocation failure.
 */
XbimLocationHandle xbim_location_create_internal(const TopLoc_Location& loc);

#endif /* XBIM_LOCATION_H */
