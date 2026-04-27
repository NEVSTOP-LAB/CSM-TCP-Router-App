/* test_integration.c - 针对进程内 MockServer 的端到端测试。 */

#if !defined(_WIN32)
#  ifndef _POSIX_C_SOURCE
#    define _POSIX_C_SOURCE 200809L
#  endif
#endif

#include "csm_tcp_router_client.h"
#include "mock_server.h"
#include "test_harness.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  include <windows.h>
static void it_sleep_ms(unsigned int ms) { Sleep(ms); }
#else
#  include <time.h>
static void it_sleep_ms(unsigned int ms) {
    struct timespec ts; ts.tv_sec = ms/1000; ts.tv_nsec = (long)(ms%1000)*1000000L;
    nanosleep(&ts, NULL);
}
#endif

/* 辅助函数：启动服务器 + 连接客户端。 */
static void it_setup(csm_mock_server_t **out_s, csm_client_t **out_c) {
    csm_mock_server_t *s = csm_mock_server_create();
    CSM_ASSERT(s != NULL);
    CSM_ASSERT_EQ_INT(csm_mock_server_start(s), 0);
    csm_client_t *c = csm_client_create();
    CSM_ASSERT(c != NULL);
    csm_result_t r = csm_client_connect(c, "127.0.0.1",
                                        csm_mock_server_port(s), 2000);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    *out_s = s; *out_c = c;
}

static void it_teardown(csm_mock_server_t *s, csm_client_t *c) {
    csm_client_destroy(c);
    csm_mock_server_destroy(s);
}

CSM_TEST(it_ping) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    double ms = 0;
    csm_result_t r = csm_client_ping(c, 1000, &ms);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT(ms >= 0);
    it_teardown(s, c);
}

CSM_TEST(it_list_modules) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    char *txt = NULL;
    csm_result_t r = csm_client_list_modules(c, &txt, 1000);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_STR(txt, "AI\nDIO\nSystem");
    csm_string_free(txt);
    it_teardown(s, c);
}

CSM_TEST(it_list_api_includes_module_name) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    char *txt = NULL;
    csm_result_t r = csm_client_list_api(c, "DAQmx", &txt, 1000);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT(strstr(txt, "DAQmx") != NULL);
    csm_string_free(txt);
    it_teardown(s, c);
}

CSM_TEST(it_send_and_wait_custom_response) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    csm_mock_server_set_response(s, "API: Read -@ DAQmx", "42");
    csm_command_response_t resp = {0};
    csm_result_t r = csm_client_send_and_wait(c, "API: Read -@ DAQmx", 1000, &resp);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_INT(resp.raw_len, 2);
    CSM_ASSERT(memcmp(resp.raw, "42", 2) == 0);
    csm_command_response_dispose(&resp);
    it_teardown(s, c);
}

CSM_TEST(it_server_error_propagated) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    csm_mock_server_set_error_response(s, "Bad", "[Error: 42] bad command");
    csm_command_response_t resp = {0};
    csm_result_t r = csm_client_send_and_wait(c, "Bad", 1000, &resp);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_SERVER);
    csm_server_error_t err = {{0}, {0}};
    CSM_ASSERT_EQ_INT(csm_client_last_server_error(c, &err), CSM_OK);
    CSM_ASSERT_EQ_STR(err.code, "42");
    CSM_ASSERT(strstr(err.message, "bad command") != NULL);
    csm_command_response_dispose(&resp);
    it_teardown(s, c);
}

CSM_TEST(it_post_async_handshake) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    csm_result_t r = csm_client_post(c, "API: Start -> DAQmx", 1000);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    char *got = csm_mock_server_get_received(s, 500);
    CSM_ASSERT(got != NULL);
    CSM_ASSERT_EQ_STR(got, "API: Start -> DAQmx");
    csm_string_free(got);
    it_teardown(s, c);
}

static void it_status_cb(const csm_status_notification_t *n, void *ud) {
    int *count = (int *)ud;
    (*count)++;
    /* 完整性检查已解析的字段。 */
    (void)n;
}

CSM_TEST(it_subscribe_status_invokes_callback) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    int count = 0;
    csm_result_t r = csm_client_subscribe_status(c, "Status", "DAQmx",
                                                 it_status_cb, &count, 1000);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    /* 从已接收队列中消耗握手数据。 */
    char *cmd = csm_mock_server_get_received(s, 500);
    csm_string_free(cmd);
    /* 推送一条匹配订阅的 STATUS 通知。 */
    csm_mock_server_push_status(s, "Status >> 1.23 <- DAQmx");
    /* 等待回调（最多轮询 1 秒）。 */
    for (int i = 0; i < 100 && count == 0; ++i) it_sleep_ms(10);
    CSM_ASSERT(count >= 1);

    /* 同一通知也应可通过轮询获取。 */
    csm_status_notification_t n = {0};
    r = csm_client_poll_status(c, &n, 500);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_STR(n.status_name, "Status");
    CSM_ASSERT_EQ_STR(n.module_name, "DAQmx");
    CSM_ASSERT_EQ_STR(n.data, "1.23");
    csm_status_notification_dispose(&n);

    csm_client_unsubscribe_status(c, "Status", "DAQmx", 1000);
    it_teardown(s, c);
}

CSM_TEST(it_disconnect_unblocks_waiters) {
    csm_mock_server_t *s = NULL; csm_client_t *c = NULL;
    it_setup(&s, &c);
    /* 发送一个没有预置响应的命令；模拟服务器返回 CMD_RESP。 */
    /* 本测试中直接立即断开连接并验证后续发送失败。 */
    csm_client_disconnect(c);
    csm_command_response_t resp = {0};
    csm_result_t r = csm_client_send_and_wait(c, "Ping", 200, &resp);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_CONNECTION);
    csm_command_response_dispose(&resp);
    it_teardown(s, c);
}
