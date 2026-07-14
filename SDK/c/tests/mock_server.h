/* mock_server.h - 用于测试的进程内 TCP 服务器，模拟 CSM-TCP-Router。
 *
 * 对应 Python `tests/conftest.py` 中的 MockServer 夹具。
 */
#ifndef CSM_MOCK_SERVER_H
#define CSM_MOCK_SERVER_H

#include "csm_tcp_router_client.h"

#include <stdint.h>

typedef struct csm_mock_server csm_mock_server_t;

/** 创建一个已停止的模拟服务器，绑定到 127.0.0.1；
 * 实际端口由操作系统在 csm_mock_server_start() 中分配。 */
csm_mock_server_t *csm_mock_server_create(void);

/** 释放（运行中或已停止的）模拟服务器。 */
void csm_mock_server_destroy(csm_mock_server_t *s);

/** 绑定到 127.0.0.1 的临时端口，并启动接受连接线程。 */
int csm_mock_server_start(csm_mock_server_t *s);

/** 停止接受连接线程并关闭所有客户端连接。 */
void csm_mock_server_stop(csm_mock_server_t *s);

/** 返回服务器正在监听的端口（start() 之后有效）。 */
uint16_t csm_mock_server_port(const csm_mock_server_t *s);

/** 为精确匹配的命令字符串注册自定义 RESP 回复。 */
void csm_mock_server_set_response(csm_mock_server_t *s,
                                  const char *cmd_text,
                                  const char *resp_text);

/** 为精确匹配的命令字符串注册 ERROR 回复。 */
void csm_mock_server_set_error_response(csm_mock_server_t *s,
                                        const char *cmd_text,
                                        const char *error_text);

/** 向所有当前连接的客户端推送 STATUS 数据包。 */
void csm_mock_server_push_status(csm_mock_server_t *s, const char *payload);

/** 弹出下一条已接收的命令，最多阻塞 *timeout_ms* 毫秒。返回的
 * 字符串由调用者拥有，必须使用 csm_string_free 释放。
 * 超时时返回 NULL。 */
char *csm_mock_server_get_received(csm_mock_server_t *s, unsigned int timeout_ms);

#endif /* CSM_MOCK_SERVER_H */
