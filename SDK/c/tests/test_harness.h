/* test_harness.h - Minimal in-process unit-test harness used by the
 * csm-tcp-router-client C SDK tests.
 *
 * Tests register themselves via the CSM_TEST() macro; the runner in
 * test_main.c picks them up via the link-time TESTS array, executes them,
 * and prints a summary. Failures abort the current test only; assertions
 * use longjmp to unwind back to the runner.
 */
#ifndef CSM_TEST_HARNESS_H
#define CSM_TEST_HARNESS_H

#include <setjmp.h>
#include <stdio.h>
#include <string.h>

typedef void (*csm_test_fn)(void);

typedef struct {
    const char *name;
    csm_test_fn fn;
} csm_test_t;

/* Provided by test_main.c. */
extern jmp_buf csm_test_jmp;
extern int     csm_test_failed;
extern int     csm_test_assertions;

#define CSM_TEST_FAIL(...) do {                              \
    csm_test_failed = 1;                                     \
    fprintf(stderr, "    FAIL %s:%d: ", __FILE__, __LINE__); \
    fprintf(stderr, __VA_ARGS__);                            \
    fprintf(stderr, "\n");                                   \
    longjmp(csm_test_jmp, 1);                                \
} while (0)

#define CSM_ASSERT(cond) do {                                  \
    csm_test_assertions++;                                     \
    if (!(cond)) CSM_TEST_FAIL("assertion failed: %s", #cond); \
} while (0)

#define CSM_ASSERT_EQ_INT(a, b) do {                                \
    csm_test_assertions++;                                          \
    long long _a = (long long)(a), _b = (long long)(b);             \
    if (_a != _b)                                                   \
        CSM_TEST_FAIL("expected %lld, got %lld (%s == %s)",         \
                      _b, _a, #a, #b);                              \
} while (0)

#define CSM_ASSERT_EQ_STR(a, b) do {                                \
    csm_test_assertions++;                                          \
    const char *_a = (a), *_b = (b);                                \
    if (_a == NULL || _b == NULL || strcmp(_a, _b) != 0)            \
        CSM_TEST_FAIL("expected \"%s\", got \"%s\"",                \
                      _b ? _b : "(null)", _a ? _a : "(null)");      \
} while (0)

/* Define a test function. The runner declares each test as extern via the
 * CSM_TEST_EXTERN macro and registers it in its tests table. */
#define CSM_TEST(name) void name(void)
#define CSM_TEST_EXTERN(name) extern void name(void)

#endif /* CSM_TEST_HARNESS_H */
