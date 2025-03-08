/*
 * xbim_context.h
 *
 * Internal definition of the XbimContext struct.
 * Holds model parameters (precision, unit factors, tolerances) and the
 * logging callback supplied by managed code.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_CONTEXT_H
#define XBIM_CONTEXT_H

#include "xbim_geometry_api.h"

struct XbimContext_
{
    /* Model unit conversion factors */
    double precision;
    double precisionSquared;
    double oneMeter;
    double oneFoot;
    double oneMillimeter;

    /* Geometry processing parameters */
    double radianFactor;
    double timeout;
    double minimumGap;
    double minAreaM2;

    /* Logging callback (may be NULL if caller doesn't need logs) */
    XbimLogCallback logCallback;
};

/*
 * Internal helper: send a log message through the context's callback.
 * No-op if the context is NULL or the callback is not set.
 */
void xbim_context_log(const XbimContext_* ctx, int level, const char* msg);

#endif /* XBIM_CONTEXT_H */
