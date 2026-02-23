/*
 * xbim_spiral.h
 *
 * IFC4x3 spiral type enumeration and C API declarations for building
 * spiral curves. All spiral types (clothoid, sine, cosine, polynomial)
 * are now standalone Geom2d_Spiral subclasses in their own headers.
 */

#ifndef XBIM_SPIRAL_H
#define XBIM_SPIRAL_H

/*
 * Spiral type enumeration for the generic polynomial spiral builder.
 */
enum XbimSpiralType
{
    XBIM_SPIRAL_CLOTHOID = 0,
    XBIM_SPIRAL_SINE = 1,
    XBIM_SPIRAL_COSINE = 2,
    XBIM_SPIRAL_POLYNOMIAL = 3
};

#endif /* XBIM_SPIRAL_H */
