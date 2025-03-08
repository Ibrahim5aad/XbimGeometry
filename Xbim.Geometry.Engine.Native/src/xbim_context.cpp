/*
 * xbim_context.cpp
 *
 * Implements the context lifecycle: create, destroy, and internal logging helper.
 * The context is an opaque handle that wraps model-level parameters needed
 * by all geometry operations (precision, unit factors, tolerances).
 */

#include "xbim_context.h"
#include "xbim_error.h"

#include <cstring>
#include <new>

/* ── Public API ────────────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_context_create(
    double precision,
    double oneMeter,
    double oneFoot,
    double oneMillimeter,
    double radianFactor,
    double timeout,
    double minimumGap,
    XbimLogCallback logCallback,
    XbimContextHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_context_create: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    *outHandle = nullptr;

    if (precision <= 0.0)
    {
        xbim_set_error("xbim_context_create: precision must be positive");
        return XBIM_INVALID_ARG;
    }

    if (oneMeter <= 0.0)
    {
        xbim_set_error("xbim_context_create: oneMeter must be positive");
        return XBIM_INVALID_ARG;
    }

    XbimContext_* ctx = new (std::nothrow) XbimContext_();
    if (!ctx)
    {
        xbim_set_error("xbim_context_create: out of memory");
        return XBIM_ERROR;
    }

    ctx->precision        = precision;
    ctx->precisionSquared = precision * precision;
    ctx->oneMeter         = oneMeter;
    ctx->oneFoot          = oneFoot;
    ctx->oneMillimeter    = oneMillimeter;
    ctx->radianFactor     = radianFactor;
    ctx->timeout          = timeout;
    ctx->minimumGap       = minimumGap;
    ctx->logCallback      = logCallback;

    /* Derived parameter: minimum area = (2mm)^2 in model units */
    double twoMm = 0.002 * oneMeter;
    ctx->minAreaM2 = twoMm * twoMm;

    *outHandle = ctx;
    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_context_destroy(XbimContextHandle handle)
{
    xbim_clear_error();

    if (!handle)
        return XBIM_OK; /* destroying null is a safe no-op */

    delete handle;
    return XBIM_OK;
}

/* ── Internal helper ───────────────────────────────────────────────────────── */

void xbim_context_log(const XbimContext_* ctx, int level, const char* msg)
{
    if (ctx && ctx->logCallback && msg)
        ctx->logCallback(level, msg);
}
