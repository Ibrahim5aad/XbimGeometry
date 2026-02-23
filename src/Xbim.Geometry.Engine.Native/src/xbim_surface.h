/*
 * xbim_surface.h
 *
 * Internal definition of the XbimSurface struct.
 * Wraps a heap-allocated Geom_Surface with lifecycle management.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_SURFACE_H
#define XBIM_SURFACE_H

#include "xbim_geometry_api.h"

#include <Geom_Surface.hxx>

struct XbimSurface_
{
    Handle(Geom_Surface) surface;
};

/*
 * Allocate a new XbimSurface_ on the heap from a Geom_Surface handle.
 * Returns nullptr if the surface handle is null or allocation fails.
 */
XbimSurfaceHandle xbim_surface_create_from(const Handle(Geom_Surface)& surface);

#endif /* XBIM_SURFACE_H */
