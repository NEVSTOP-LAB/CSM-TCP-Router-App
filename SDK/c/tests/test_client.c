/* test_client.c - 客户端对象生命周期的单元测试，
 * 无需运行中的模拟服务器。 */
#include "csm_tcp_router_client.h"
#include "test_harness.h"

#include <string.h>

CSM_TEST(test_client_create_destroy) {
    csm_client_t *c = csm_client_create();
    CSM_ASSERT(c != NULL);
    CSM_ASSERT_EQ_INT(csm_client_is_connected(c), 0);
    csm_client_destroy(c);
}

CSM_TEST(test_destroy_null_safe) {
    csm_client_destroy(NULL);
}

CSM_TEST(test_send_when_not_connected_returns_connection_error) {
    csm_client_t *c = csm_client_create();
    CSM_ASSERT(c != NULL);
    csm_command_response_t resp = {0};
    csm_result_t r = csm_client_send_and_wait(c, "Ping", 100, &resp);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_CONNECTION);
    csm_command_response_dispose(&resp);
    csm_client_destroy(c);
}

CSM_TEST(test_invalid_args_rejected) {
    csm_client_t *c = csm_client_create();
    CSM_ASSERT_EQ_INT(csm_client_connect(c, NULL, 1234, 100), CSM_ERR_INVALID);
    CSM_ASSERT_EQ_INT(csm_client_send_and_wait(NULL, "x", 100, NULL), CSM_ERR_INVALID);
    CSM_ASSERT_EQ_INT(csm_client_send_and_wait(c, NULL, 100, NULL), CSM_ERR_INVALID);
    char *out = NULL;
    CSM_ASSERT_EQ_INT(csm_client_list_api(c, NULL, &out, 100), CSM_ERR_INVALID);
    csm_client_destroy(c);
}

CSM_TEST(test_wait_for_server_unreachable_times_out) {
    /* 选择一个不太可能被使用的任意高端口。 */
    csm_result_t r = csm_client_wait_for_server("127.0.0.1", 1, 200, 50);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_TIMEOUT);
}

CSM_TEST(test_connect_unreachable_returns_connection_error) {
    csm_client_t *c = csm_client_create();
    csm_result_t r = csm_client_connect(c, "127.0.0.1", 1, 300);
    /* 根据操作系统，返回 CSM_ERR_CONNECTION（拒绝连接）或 CSM_ERR_TIMEOUT。 */
    CSM_ASSERT(r == CSM_ERR_CONNECTION || r == CSM_ERR_TIMEOUT);
    csm_client_destroy(c);
}
