/*
 * test_dynamic_load.cpp
 *
 * Verifies the native DLL is loadable via LoadLibrary / GetProcAddress
 * and exercises every exported xbim_* function through dynamically
 * resolved function pointers. This simulates what .NET P/Invoke does
 * at runtime and ensures no missing symbols or runtime link failures.
 *
 * Steps tested:
 *   1. LoadLibrary succeeds for xbim_geometry_native.dll
 *   2. GetProcAddress resolves every xbim_* export
 *   3. xbim_native_version returns a non-empty string
 *   4. xbim_native_is_available returns 1
 *   5. xbim_context_create / xbim_context_destroy round-trip
 *   6. xbim_context_set_logger replaces callback
 *   7. xbim_context_log fires the callback
 *   8. xbim_location_create_identity / xbim_location_destroy round-trip
 *   9. xbim_location_create_from_axis2 round-trip
 *  10. xbim_location_compose round-trip
 *  11. xbim_get_last_error returns a valid pointer
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>
#include <cmath>

/* ── Counters ───────────────────────────────────────────────────────────────── */

static int g_passed = 0;
static int g_failed = 0;

#define CHECK(expr, msg) \
    do { \
        if (expr) { g_passed++; printf("  PASS: %s\n", msg); } \
        else      { g_failed++; printf("  FAIL: %s\n", msg); } \
    } while (0)

/* ── Type aliases matching the C API ────────────────────────────────────────── */

typedef struct XbimContext_*   XbimContextHandle;
typedef struct XbimShape_*     XbimShapeHandle;
typedef struct XbimLocation_*  XbimLocationHandle;
typedef int XbimResult;

typedef void (__stdcall *XbimLogCallback)(int level, const char* msg);

/* ── Function pointer typedefs ──────────────────────────────────────────────── */

typedef const char*  (__stdcall *fn_xbim_native_version)(void);
typedef int          (__stdcall *fn_xbim_native_is_available)(void);
typedef const char*  (__stdcall *fn_xbim_get_last_error)(void);

typedef XbimResult (__stdcall *fn_xbim_context_create)(
    double, double, double, double, double, double, double,
    XbimLogCallback, XbimContextHandle*);
typedef XbimResult (__stdcall *fn_xbim_context_destroy)(XbimContextHandle);
typedef XbimResult (__stdcall *fn_xbim_context_set_logger)(XbimContextHandle, XbimLogCallback);
typedef XbimResult (__stdcall *fn_xbim_context_log)(XbimContextHandle, int, const char*);

typedef XbimResult (__stdcall *fn_xbim_shape_destroy)(XbimShapeHandle);
typedef XbimResult (__stdcall *fn_xbim_shape_type)(XbimShapeHandle, int*);
typedef int        (__stdcall *fn_xbim_shape_is_valid)(XbimShapeHandle);
typedef int        (__stdcall *fn_xbim_shape_is_closed)(XbimShapeHandle);
typedef XbimResult (__stdcall *fn_xbim_shape_bounding_box)(
    XbimShapeHandle, double*, double*, double*, double*, double*, double*);
typedef XbimResult (__stdcall *fn_xbim_shape_volume)(XbimShapeHandle, double*);
typedef XbimResult (__stdcall *fn_xbim_shape_surface_area)(XbimShapeHandle, double*);
typedef XbimResult (__stdcall *fn_xbim_shape_moved)(
    XbimShapeHandle, XbimLocationHandle, XbimShapeHandle*);

typedef XbimResult (__stdcall *fn_xbim_location_create_from_axis2)(
    double, double, double, double, double, double, double, double, double,
    XbimLocationHandle*);
typedef XbimResult (__stdcall *fn_xbim_location_create_identity)(XbimLocationHandle*);
typedef XbimResult (__stdcall *fn_xbim_location_compose)(
    XbimLocationHandle, XbimLocationHandle, XbimLocationHandle*);
typedef XbimResult (__stdcall *fn_xbim_location_destroy)(XbimLocationHandle);

/* ── Resolved function pointers ─────────────────────────────────────────────── */

static fn_xbim_native_version               p_xbim_native_version;
static fn_xbim_native_is_available           p_xbim_native_is_available;
static fn_xbim_get_last_error                p_xbim_get_last_error;
static fn_xbim_context_create                p_xbim_context_create;
static fn_xbim_context_destroy               p_xbim_context_destroy;
static fn_xbim_context_set_logger            p_xbim_context_set_logger;
static fn_xbim_context_log                   p_xbim_context_log;
static fn_xbim_shape_destroy                 p_xbim_shape_destroy;
static fn_xbim_shape_type                    p_xbim_shape_type;
static fn_xbim_shape_is_valid                p_xbim_shape_is_valid;
static fn_xbim_shape_is_closed               p_xbim_shape_is_closed;
static fn_xbim_shape_bounding_box            p_xbim_shape_bounding_box;
static fn_xbim_shape_volume                  p_xbim_shape_volume;
static fn_xbim_shape_surface_area            p_xbim_shape_surface_area;
static fn_xbim_shape_moved                   p_xbim_shape_moved;
static fn_xbim_location_create_from_axis2    p_xbim_location_create_from_axis2;
static fn_xbim_location_create_identity      p_xbim_location_create_identity;
static fn_xbim_location_compose              p_xbim_location_compose;
static fn_xbim_location_destroy              p_xbim_location_destroy;

/* ── Logging callback for testing ───────────────────────────────────────────── */

static int g_logCallbackFired = 0;
static int g_lastLogLevel = -1;
static char g_lastLogMsg[256] = {0};

static void __stdcall test_log_callback(int level, const char* msg)
{
    g_logCallbackFired++;
    g_lastLogLevel = level;
    if (msg)
    {
        strncpy(g_lastLogMsg, msg, sizeof(g_lastLogMsg) - 1);
        g_lastLogMsg[sizeof(g_lastLogMsg) - 1] = '\0';
    }
}

/* ── Helper: resolve one export ─────────────────────────────────────────────── */

static void* resolve(HMODULE hLib, const char* name, int* resolvedCount)
{
    FARPROC proc = GetProcAddress(hLib, name);
    if (proc)
    {
        (*resolvedCount)++;
        printf("  PASS: GetProcAddress(%s) -> %p\n", name, (void*)proc);
    }
    else
    {
        g_failed++;
        printf("  FAIL: GetProcAddress(%s) -> NULL\n", name);
    }
    return (void*)proc;
}

/* ── Main ───────────────────────────────────────────────────────────────────── */

int main()
{
    printf("=== Dynamic Load Test for xbim_geometry_native.dll ===\n\n");

    /* ── Step 1: LoadLibrary ─────────────────────────────────────────────── */
    printf("[1] Loading DLL...\n");
    HMODULE hLib = LoadLibraryA("xbim_geometry_native.dll");
    CHECK(hLib != NULL, "LoadLibrary(xbim_geometry_native.dll) succeeded");
    if (!hLib)
    {
        DWORD err = GetLastError();
        printf("  LoadLibrary failed with error code %lu\n", err);
        printf("\nResults: %d passed, %d failed\n", g_passed, g_failed);
        return 1;
    }

    /* ── Step 2: Resolve all xbim_* exports ──────────────────────────────── */
    printf("\n[2] Resolving all xbim_* exports...\n");
    int resolved = 0;

    /* The list of all 19 xbim_* exports that must be present */
    p_xbim_native_version            = (fn_xbim_native_version)           resolve(hLib, "xbim_native_version", &resolved);
    p_xbim_native_is_available       = (fn_xbim_native_is_available)      resolve(hLib, "xbim_native_is_available", &resolved);
    p_xbim_get_last_error            = (fn_xbim_get_last_error)           resolve(hLib, "xbim_get_last_error", &resolved);
    p_xbim_context_create            = (fn_xbim_context_create)           resolve(hLib, "xbim_context_create", &resolved);
    p_xbim_context_destroy           = (fn_xbim_context_destroy)          resolve(hLib, "xbim_context_destroy", &resolved);
    p_xbim_context_set_logger        = (fn_xbim_context_set_logger)       resolve(hLib, "xbim_context_set_logger", &resolved);
    p_xbim_context_log               = (fn_xbim_context_log)              resolve(hLib, "xbim_context_log", &resolved);
    p_xbim_shape_destroy             = (fn_xbim_shape_destroy)            resolve(hLib, "xbim_shape_destroy", &resolved);
    p_xbim_shape_type                = (fn_xbim_shape_type)               resolve(hLib, "xbim_shape_type", &resolved);
    p_xbim_shape_is_valid            = (fn_xbim_shape_is_valid)           resolve(hLib, "xbim_shape_is_valid", &resolved);
    p_xbim_shape_is_closed           = (fn_xbim_shape_is_closed)          resolve(hLib, "xbim_shape_is_closed", &resolved);
    p_xbim_shape_bounding_box        = (fn_xbim_shape_bounding_box)       resolve(hLib, "xbim_shape_bounding_box", &resolved);
    p_xbim_shape_volume              = (fn_xbim_shape_volume)             resolve(hLib, "xbim_shape_volume", &resolved);
    p_xbim_shape_surface_area        = (fn_xbim_shape_surface_area)       resolve(hLib, "xbim_shape_surface_area", &resolved);
    p_xbim_shape_moved               = (fn_xbim_shape_moved)              resolve(hLib, "xbim_shape_moved", &resolved);
    p_xbim_location_create_from_axis2 = (fn_xbim_location_create_from_axis2)resolve(hLib, "xbim_location_create_from_axis2", &resolved);
    p_xbim_location_create_identity  = (fn_xbim_location_create_identity) resolve(hLib, "xbim_location_create_identity", &resolved);
    p_xbim_location_compose          = (fn_xbim_location_compose)         resolve(hLib, "xbim_location_compose", &resolved);
    p_xbim_location_destroy          = (fn_xbim_location_destroy)         resolve(hLib, "xbim_location_destroy", &resolved);

    printf("  Resolved %d / 19 xbim_* exports\n", resolved);
    g_passed++; /* count the batch resolution as one test */
    CHECK(resolved == 19, "All 19 xbim_* exports resolved");

    /* ── Step 3: xbim_native_version ─────────────────────────────────────── */
    printf("\n[3] Testing xbim_native_version...\n");
    if (p_xbim_native_version)
    {
        const char* ver = p_xbim_native_version();
        CHECK(ver != NULL && strlen(ver) > 0, "xbim_native_version returns non-empty string");
        if (ver) printf("     Version: %s\n", ver);
    }

    /* ── Step 4: xbim_native_is_available ────────────────────────────────── */
    printf("\n[4] Testing xbim_native_is_available...\n");
    if (p_xbim_native_is_available)
    {
        int avail = p_xbim_native_is_available();
        CHECK(avail == 1, "xbim_native_is_available returns 1 (OCCT operational)");
    }

    /* ── Step 5: Context create / destroy ────────────────────────────────── */
    printf("\n[5] Testing context create/destroy...\n");
    if (p_xbim_context_create && p_xbim_context_destroy)
    {
        XbimContextHandle ctx = NULL;
        XbimResult rc = p_xbim_context_create(
            1e-5,   /* precision */
            1000.0, /* oneMeter (mm) */
            304.8,  /* oneFoot */
            1.0,    /* oneMillimeter */
            1.0,    /* radianFactor */
            0.0,    /* timeout */
            0.0,    /* minimumGap */
            test_log_callback,
            &ctx);
        CHECK(rc == 0, "xbim_context_create returns XBIM_OK");
        CHECK(ctx != NULL, "Context handle is non-NULL");

        /* ── Step 6: set_logger ──────────────────────────────────────── */
        printf("\n[6] Testing xbim_context_set_logger...\n");
        if (p_xbim_context_set_logger && ctx)
        {
            XbimResult rc2 = p_xbim_context_set_logger(ctx, test_log_callback);
            CHECK(rc2 == 0, "xbim_context_set_logger returns XBIM_OK");
        }

        /* ── Step 7: context_log ─────────────────────────────────────── */
        printf("\n[7] Testing xbim_context_log...\n");
        if (p_xbim_context_log && ctx)
        {
            g_logCallbackFired = 0;
            XbimResult rc3 = p_xbim_context_log(ctx, 2, "Hello from dynamic test");
            CHECK(rc3 == 0, "xbim_context_log returns XBIM_OK");
            CHECK(g_logCallbackFired == 1, "Log callback was invoked");
            CHECK(g_lastLogLevel == 2, "Log level is Info (2)");
            CHECK(strcmp(g_lastLogMsg, "Hello from dynamic test") == 0, "Log message content matches");
        }

        /* Clean up context */
        if (ctx)
        {
            XbimResult rc4 = p_xbim_context_destroy(ctx);
            CHECK(rc4 == 0, "xbim_context_destroy returns XBIM_OK");
        }
    }

    /* ── Step 8: Location identity create / destroy ──────────────────────── */
    printf("\n[8] Testing location identity create/destroy...\n");
    if (p_xbim_location_create_identity && p_xbim_location_destroy)
    {
        XbimLocationHandle loc = NULL;
        XbimResult rc = p_xbim_location_create_identity(&loc);
        CHECK(rc == 0, "xbim_location_create_identity returns XBIM_OK");
        CHECK(loc != NULL, "Identity location handle is non-NULL");

        if (loc)
        {
            XbimResult rc2 = p_xbim_location_destroy(loc);
            CHECK(rc2 == 0, "xbim_location_destroy(identity) returns XBIM_OK");
        }
    }

    /* ── Step 9: Location from axis2 ─────────────────────────────────────── */
    printf("\n[9] Testing location from axis2 placement...\n");
    if (p_xbim_location_create_from_axis2 && p_xbim_location_destroy)
    {
        XbimLocationHandle loc = NULL;
        XbimResult rc = p_xbim_location_create_from_axis2(
            10.0, 20.0, 30.0,   /* origin */
            0.0, 0.0, 1.0,      /* zDir (normal) */
            1.0, 0.0, 0.0,      /* xDir (reference) */
            &loc);
        CHECK(rc == 0, "xbim_location_create_from_axis2 returns XBIM_OK");
        CHECK(loc != NULL, "Axis2 location handle is non-NULL");

        if (loc)
        {
            p_xbim_location_destroy(loc);
        }
    }

    /* ── Step 10: Location composition ───────────────────────────────────── */
    printf("\n[10] Testing location composition...\n");
    if (p_xbim_location_create_from_axis2 && p_xbim_location_compose && p_xbim_location_destroy)
    {
        XbimLocationHandle loc1 = NULL, loc2 = NULL, composed = NULL;

        p_xbim_location_create_from_axis2(
            10.0, 0.0, 0.0,
            0.0, 0.0, 1.0,
            1.0, 0.0, 0.0,
            &loc1);

        p_xbim_location_create_from_axis2(
            0.0, 20.0, 0.0,
            0.0, 0.0, 1.0,
            1.0, 0.0, 0.0,
            &loc2);

        if (loc1 && loc2)
        {
            XbimResult rc = p_xbim_location_compose(loc1, loc2, &composed);
            CHECK(rc == 0, "xbim_location_compose returns XBIM_OK");
            CHECK(composed != NULL, "Composed location handle is non-NULL");
        }

        if (composed) p_xbim_location_destroy(composed);
        if (loc2) p_xbim_location_destroy(loc2);
        if (loc1) p_xbim_location_destroy(loc1);
    }

    /* ── Step 11: xbim_get_last_error ────────────────────────────────────── */
    printf("\n[11] Testing xbim_get_last_error...\n");
    if (p_xbim_get_last_error)
    {
        const char* err = p_xbim_get_last_error();
        CHECK(err != NULL, "xbim_get_last_error returns non-NULL pointer");
    }

    /* ── Step 12: Shape functions with NULL (graceful error handling) ─────── */
    printf("\n[12] Testing shape functions handle NULL gracefully...\n");
    if (p_xbim_shape_is_valid)
    {
        int valid = p_xbim_shape_is_valid(NULL);
        CHECK(valid == 0, "xbim_shape_is_valid(NULL) returns 0");
    }
    if (p_xbim_shape_is_closed)
    {
        int closed = p_xbim_shape_is_closed(NULL);
        CHECK(closed == 0, "xbim_shape_is_closed(NULL) returns 0");
    }
    if (p_xbim_shape_destroy)
    {
        XbimResult rc = p_xbim_shape_destroy(NULL);
        CHECK(rc == 0, "xbim_shape_destroy(NULL) returns XBIM_OK (safe no-op)");
    }
    if (p_xbim_shape_type)
    {
        int type = -1;
        XbimResult rc = p_xbim_shape_type(NULL, &type);
        CHECK(rc == 2, "xbim_shape_type(NULL) returns XBIM_INVALID_HANDLE");
    }
    if (p_xbim_shape_volume)
    {
        double vol = 0.0;
        XbimResult rc = p_xbim_shape_volume(NULL, &vol);
        CHECK(rc == 2, "xbim_shape_volume(NULL) returns XBIM_INVALID_HANDLE");
    }
    if (p_xbim_shape_surface_area)
    {
        double area = 0.0;
        XbimResult rc = p_xbim_shape_surface_area(NULL, &area);
        CHECK(rc == 2, "xbim_shape_surface_area(NULL) returns XBIM_INVALID_HANDLE");
    }
    if (p_xbim_shape_bounding_box)
    {
        double a, b, c, d, e, f;
        XbimResult rc = p_xbim_shape_bounding_box(NULL, &a, &b, &c, &d, &e, &f);
        CHECK(rc == 2, "xbim_shape_bounding_box(NULL) returns XBIM_INVALID_HANDLE");
    }
    if (p_xbim_shape_moved)
    {
        XbimShapeHandle out = NULL;
        XbimResult rc = p_xbim_shape_moved(NULL, NULL, &out);
        CHECK(rc == 2, "xbim_shape_moved(NULL, NULL) returns XBIM_INVALID_HANDLE");
    }

    /* ── Unload ──────────────────────────────────────────────────────────── */
    printf("\n[13] Unloading DLL...\n");
    BOOL freed = FreeLibrary(hLib);
    CHECK(freed != 0, "FreeLibrary succeeded");

    /* ── Summary ─────────────────────────────────────────────────────────── */
    printf("\n=== Results: %d passed, %d failed ===\n", g_passed, g_failed);
    return g_failed > 0 ? 1 : 0;
}
