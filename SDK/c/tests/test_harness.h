/* test_harness.h - csm-tcp-router-client C SDK 测试使用的极简进程内单元测试框架。
 *
 * 测试通过 CSM_TEST() 宏注册自身；test_main.c 中的运行器
 * 通过链接时的 TESTS 数组收集测试，依次执行并打印汇总结果。
 * 失败仅中止当前测试；断言使用 longjmp 回退到运行器。
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

/* 由 test_main.c 提供。 */
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

/* 定义一个测试函数。运行器通过 CSM_TEST_EXTERN 宏将每个测试声明为 extern，
 * 并在测试表中注册。 */
#define CSM_TEST(name) void name(void)
#define CSM_TEST_EXTERN(name) extern void name(void)

#endif /* CSM_TEST_HARNESS_H */
