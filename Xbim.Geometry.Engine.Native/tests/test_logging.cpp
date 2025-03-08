/*
 * test_logging.cpp
 *
 * Quick validation of the logging callback API:
 *   1. Create context with a callback, invoke xbim_context_log, verify callback fires
 *   2. Create context without callback, invoke xbim_context_log, verify no crash
 *   3. Replace callback via xbim_context_set_logger, verify new callback fires
 *   4. Set callback to NULL (disable logging), verify no crash
 *   5. Verify log levels match: Debug=1, Info=2, Warning=3, Error=4, Critical=5
 *   6. Test xbim_context_log with NULL handle returns XBIM_INVALID_HANDLE
 *   7. Test xbim_context_set_logger with NULL handle returns XBIM_INVALID_HANDLE
 */

#include "xbim_geometry_api.h"

#include <cstdio>
#include <cstring>
#include <cstdlib>

static int g_callbackCount = 0;
static int g_lastLevel = -1;
static char g_lastMessage[512] = {0};

static void XBIM_CALL test_log_callback(int level, const char* msg)
{
    g_callbackCount++;
    g_lastLevel = level;
    if (msg)
    {
        strncpy(g_lastMessage, msg, sizeof(g_lastMessage) - 1);
        g_lastMessage[sizeof(g_lastMessage) - 1] = '\0';
    }
}

static int g_secondCallbackCount = 0;

static void XBIM_CALL second_log_callback(int level, const char* msg)
{
    (void)level;
    (void)msg;
    g_secondCallbackCount++;
}

static int tests_passed = 0;
static int tests_total = 0;

#define TEST(name, cond) do { \
    tests_total++; \
    if (cond) { tests_passed++; printf("  PASS: %s\n", name); } \
    else { printf("  FAIL: %s (line %d)\n", name, __LINE__); } \
} while(0)

int main()
{
    printf("=== Logging API Tests ===\n\n");

    /* Test 1: Create context with callback and invoke logging */
    {
        XbimContextHandle ctx = nullptr;
        XbimResult rc = xbim_context_create(
            0.001, 1000.0, 304.8, 1.0, 1.0, 30.0, 0.001,
            test_log_callback, &ctx);
        TEST("context created with callback", rc == XBIM_OK && ctx != nullptr);

        g_callbackCount = 0;
        g_lastLevel = -1;
        g_lastMessage[0] = '\0';

        rc = xbim_context_log(ctx, XBIM_LOG_INFO, "Hello from native");
        TEST("xbim_context_log returns OK", rc == XBIM_OK);
        TEST("callback was invoked", g_callbackCount == 1);
        TEST("level is Info(2)", g_lastLevel == XBIM_LOG_INFO);
        TEST("message is correct", strcmp(g_lastMessage, "Hello from native") == 0);

        xbim_context_destroy(ctx);
    }

    /* Test 2: Create context without callback, logging is a no-op */
    {
        XbimContextHandle ctx = nullptr;
        XbimResult rc = xbim_context_create(
            0.001, 1000.0, 304.8, 1.0, 1.0, 30.0, 0.001,
            nullptr, &ctx);
        TEST("context created without callback", rc == XBIM_OK);

        g_callbackCount = 0;
        rc = xbim_context_log(ctx, XBIM_LOG_WARNING, "This should be silently ignored");
        TEST("log with no callback returns OK", rc == XBIM_OK);
        TEST("callback not invoked", g_callbackCount == 0);

        xbim_context_destroy(ctx);
    }

    /* Test 3: Replace callback via set_logger */
    {
        XbimContextHandle ctx = nullptr;
        xbim_context_create(0.001, 1000.0, 304.8, 1.0, 1.0, 30.0, 0.001,
                            test_log_callback, &ctx);

        g_callbackCount = 0;
        g_secondCallbackCount = 0;

        XbimResult rc = xbim_context_set_logger(ctx, second_log_callback);
        TEST("set_logger returns OK", rc == XBIM_OK);

        xbim_context_log(ctx, XBIM_LOG_DEBUG, "Routed to second callback");
        TEST("original callback not invoked", g_callbackCount == 0);
        TEST("second callback invoked", g_secondCallbackCount == 1);

        xbim_context_destroy(ctx);
    }

    /* Test 4: Disable logging by setting callback to NULL */
    {
        XbimContextHandle ctx = nullptr;
        xbim_context_create(0.001, 1000.0, 304.8, 1.0, 1.0, 30.0, 0.001,
                            test_log_callback, &ctx);

        g_callbackCount = 0;
        xbim_context_set_logger(ctx, nullptr);
        xbim_context_log(ctx, XBIM_LOG_ERROR, "Should be silently dropped");
        TEST("callback not invoked after disable", g_callbackCount == 0);

        xbim_context_destroy(ctx);
    }

    /* Test 5: Verify all log levels */
    {
        XbimContextHandle ctx = nullptr;
        xbim_context_create(0.001, 1000.0, 304.8, 1.0, 1.0, 30.0, 0.001,
                            test_log_callback, &ctx);

        int levels[] = { XBIM_LOG_DEBUG, XBIM_LOG_INFO, XBIM_LOG_WARNING,
                         XBIM_LOG_ERROR, XBIM_LOG_CRITICAL };
        int expected[] = { 1, 2, 3, 4, 5 };
        const char* names[] = { "Debug", "Info", "Warning", "Error", "Critical" };

        for (int i = 0; i < 5; i++)
        {
            g_lastLevel = -1;
            xbim_context_log(ctx, levels[i], names[i]);
            char testName[64];
            snprintf(testName, sizeof(testName), "level %s == %d", names[i], expected[i]);
            TEST(testName, g_lastLevel == expected[i]);
        }

        xbim_context_destroy(ctx);
    }

    /* Test 6: NULL handle returns XBIM_INVALID_HANDLE */
    {
        XbimResult rc = xbim_context_log(nullptr, XBIM_LOG_INFO, "test");
        TEST("log with NULL handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);
    }

    /* Test 7: set_logger with NULL handle returns XBIM_INVALID_HANDLE */
    {
        XbimResult rc = xbim_context_set_logger(nullptr, test_log_callback);
        TEST("set_logger with NULL handle returns INVALID_HANDLE", rc == XBIM_INVALID_HANDLE);
    }

    printf("\n=== Results: %d/%d passed ===\n", tests_passed, tests_total);
    return (tests_passed == tests_total) ? 0 : 1;
}
