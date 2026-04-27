/* csm_tcp_router_client.h - 适用于 CSM-TCP-Router 客户端 SDK 的单头文件公共 C API。
 *
 * 本 SDK 是 Python `csm_tcp_router_client` 模块的 C 对应版本。
 * 它通过 TCP 实现 CSM-TCP-Router 协议 v0，并提供一个
 * 线程安全的同步客户端（`csm_client_t`），支持阻塞调用
 * 以及异步回调/轮询队列两种投递方式。
 *
 * 线帧格式（8 字节头部，大端序）：
 *
 *     | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) | Payload |
 *     +---------------------------- Header (8B) ----------------------------+
 *
 * 快速入门：
 *
 *     csm_client_t *c = csm_client_create();
 *     if (csm_client_connect(c, "localhost", 30007, 5000) == CSM_OK) {
 *         char *modules = NULL;
 *         if (csm_client_list_modules(c, &modules, 5000) == CSM_OK) {
 *             printf("%s\n", modules);
 *             csm_string_free(modules);
 *         }
 *         csm_client_disconnect(c);
 *     }
 *     csm_client_destroy(c);
 *
 * 本库可跨平台运行于 Windows（Winsock2 + Win32 线程）和
 * POSIX 系统（BSD 套接字 + pthreads），通过附带的 CMake / Visual Studio 项目
 * 构建为静态库或共享库。
 */
#ifndef CSM_TCP_ROUTER_CLIENT_H
#define CSM_TCP_ROUTER_CLIENT_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------------- */
/* 版本和 DLL 导出                                                            */
/* ------------------------------------------------------------------------- */

#define CSM_VERSION_MAJOR 0
#define CSM_VERSION_MINOR 1
#define CSM_VERSION_PATCH 0
#define CSM_VERSION_STRING "0.1.0"

#if defined(_WIN32) && defined(CSM_BUILD_SHARED)
#  ifdef CSM_BUILD_LIBRARY
#    define CSM_API __declspec(dllexport)
#  else
#    define CSM_API __declspec(dllimport)
#  endif
#else
#  define CSM_API
#endif

/* ------------------------------------------------------------------------- */
/* 返回码                                                                    */
/* ------------------------------------------------------------------------- */

/** 所有公共 SDK 函数返回的结果码。 */
typedef enum csm_result {
    CSM_OK              =  0, /**< 操作成功。                              */
    CSM_ERR_INVALID     = -1, /**< 无效参数或 NULL 指针。                  */
    CSM_ERR_CONNECTION  = -2, /**< 连接失败或已断开。                      */
    CSM_ERR_TIMEOUT     = -3, /**< 操作超时。                              */
    CSM_ERR_PROTOCOL    = -4, /**< 无效/格式错误的协议帧。                 */
    CSM_ERR_SERVER      = -5, /**< 服务器返回了 ERROR 数据包。             */
    CSM_ERR_NOMEM       = -6, /**< 内存分配失败。                          */
    CSM_ERR_STATE       = -7, /**< 当前状态下操作无效。                    */
    CSM_ERR_IO          = -8  /**< 底层套接字/操作系统 I/O 错误。          */
} csm_result_t;

/** 返回 *code* 对应的静态可读字符串。 */
CSM_API const char *csm_result_str(csm_result_t code);

/* ------------------------------------------------------------------------- */
/* 协议常量                                                                  */
/* ------------------------------------------------------------------------- */

/** 数据包类型字节值（CSM-TCP-Router 协议 v0）。 */
typedef enum csm_packet_type {
    CSM_PT_INFO       = 0x00, /**< 欢迎/再见信息文本。                    */
    CSM_PT_ERROR      = 0x01, /**< 服务器错误："[Error: <code>] <msg>"    */
    CSM_PT_CMD        = 0x02, /**< 命令数据包（客户端 -> 服务器）。       */
    CSM_PT_CMD_RESP   = 0x03, /**< 异步/订阅握手 ACK。                    */
    CSM_PT_RESP       = 0x04, /**< 同步响应载荷。                         */
    CSM_PT_ASYNC_RESP = 0x05, /**< 异步响应："<data> <- <cmd>"。          */
    CSM_PT_STATUS     = 0x06, /**< 状态广播。                             */
    CSM_PT_INTERRUPT  = 0x07  /**< 中断广播。                             */
} csm_packet_type_t;

/** 固定线帧格式头部的字节数。 */
#define CSM_HEADER_SIZE 8

/** 每个发出数据包中发送的协议版本字节。 */
#define CSM_PROTOCOL_VERSION 0x01

/* ------------------------------------------------------------------------- */
/* 公共数据模型                                                               */
/* ------------------------------------------------------------------------- */

/** 已解码的数据包（头部字段 + 堆分配的主体）。 */
typedef struct csm_packet {
    csm_packet_type_t type;
    uint8_t           version;
    uint8_t           flag1;
    uint8_t           flag2;
    uint8_t          *data;     /**< 拥有的载荷缓冲区（或 NULL）。        */
    size_t            data_len; /**< `data` 的字节长度。                  */
} csm_packet_t;

/** 成功的同步响应。 */
typedef struct csm_command_response {
    uint8_t *raw;     /**< NUL 终止的 UTF-8 载荷（已拥有）。             */
    size_t   raw_len; /**< `raw` 的字节长度（不含 NUL）。                */
} csm_command_response_t;

/** ASYNC_RESP 数据包：载荷 + 服务器回显的原始命令。
 *
 * 服务器格式：``"<response-data> <- <original-command>"``。 */
typedef struct csm_async_response {
    char  *raw;              /**< 响应载荷（已拥有，NUL 终止）。          */
    size_t raw_len;
    char  *original_command; /**< 回显的命令文本（已拥有）。              */
} csm_async_response_t;

/** STATUS 或 INTERRUPT 广播。
 *
 * 服务器格式：``"<status-name> >> <data> <- <module>"``。 */
typedef struct csm_status_notification {
    csm_packet_type_t packet_type; /**< CSM_PT_STATUS 或 CSM_PT_INTERRUPT */
    char  *raw;                    /**< 完整载荷（已拥有）。              */
    size_t raw_len;
    char  *status_name;            /**< 已拥有，NUL 终止。                */
    char  *data;                   /**< 已拥有，NUL 终止。                */
    char  *module_name;            /**< 已拥有，NUL 终止。                */
} csm_status_notification_t;

/** 释放通过出参（例如由 `csm_client_list_modules` 返回）分配的字符串。
 * 传入 NULL 时安全。 */
CSM_API void csm_string_free(char *s);

/** 释放 `csm_command_response_t` 的堆成员（不释放结构体本身）。 */
CSM_API void csm_command_response_dispose(csm_command_response_t *resp);

/** 释放 `csm_async_response_t` 的堆成员（不释放结构体本身）。 */
CSM_API void csm_async_response_dispose(csm_async_response_t *resp);

/** 释放 `csm_status_notification_t` 的堆成员（不释放结构体本身）。 */
CSM_API void csm_status_notification_dispose(csm_status_notification_t *n);

/** 释放 `csm_packet_t` 的堆成员（不释放结构体本身）。 */
CSM_API void csm_packet_dispose(csm_packet_t *pkt);

/* ------------------------------------------------------------------------- */
/* 服务器错误信息                                                             */
/* ------------------------------------------------------------------------- */

/** 函数最近返回 CSM_ERR_SERVER 时的相关信息。 */
typedef struct csm_server_error {
    char code[32];     /**< NUL 终止的 CSM 错误码（可为空）。             */
    char message[256]; /**< NUL 终止的错误消息（已截断）。                */
} csm_server_error_t;

/* ------------------------------------------------------------------------- */
/* 协议编解码（暴露用于高级用途/测试）                                        */
/* ------------------------------------------------------------------------- */

/** 将 *data*（`data_len` 字节）编码为完整的线帧格式数据包。
 *
 * 调用者必须传入至少 `CSM_HEADER_SIZE + data_len` 字节的 `out_buf`。
 * 成功时 `*out_len` 将被设置为写入的字节数。
 */
CSM_API csm_result_t csm_encode_packet(const void       *data,
                                       size_t            data_len,
                                       csm_packet_type_t type,
                                       uint8_t           flag1,
                                       uint8_t           flag2,
                                       uint8_t          *out_buf,
                                       size_t            out_buf_size,
                                       size_t           *out_len);

/** 将 8 字节头部解码为各组成字段。 */
CSM_API csm_result_t csm_decode_header(const uint8_t *header_bytes,
                                       size_t         header_len,
                                       uint32_t      *out_data_len,
                                       uint8_t       *out_version,
                                       uint8_t       *out_type,
                                       uint8_t       *out_flag1,
                                       uint8_t       *out_flag2);

/** 从原始头部 + 主体构建 `csm_packet_t`。返回的数据包
 * **拥有** 主体的副本；使用 `csm_packet_dispose` 释放。
 *
 * 未知数据包类型字节将被映射到 `CSM_PT_INFO` 以实现前向
 * 兼容性（服务器可能在未来版本中引入新类型）。
 */
CSM_API csm_result_t csm_parse_packet(const uint8_t *header_bytes,
                                      size_t         header_len,
                                      const uint8_t *body,
                                      size_t         body_len,
                                      csm_packet_t  *out_packet);

/* ------------------------------------------------------------------------- */
/* 回调签名                                                                  */
/* ------------------------------------------------------------------------- */

/** 状态/中断通知回调。
 *
 * 从接收线程调用。必须快速且非阻塞。`notif`
 * 指针及其成员仅在调用期间有效；
 * 请在返回前复制所需数据。
 */
typedef void (*csm_status_callback_fn)(const csm_status_notification_t *notif,
                                       void *user_data);

/** 异步响应回调。与 `csm_status_callback_fn` 使用相同的线程规则。 */
typedef void (*csm_async_callback_fn)(const csm_async_response_t *resp,
                                      void *user_data);

/* ------------------------------------------------------------------------- */
/* 客户端生命周期                                                             */
/* ------------------------------------------------------------------------- */

/** 不透明的线程安全客户端句柄。 */
typedef struct csm_client csm_client_t;

/** 创建新的客户端实例。分配失败时返回 NULL。 */
CSM_API csm_client_t *csm_client_create(void);

/** 断开连接（如已连接）并释放 *client* 持有的所有资源。 */
CSM_API void csm_client_destroy(csm_client_t *client);

/** 打开 TCP 连接并启动后台接收线程。
 *
 * @param connect_timeout_ms  连接超时时间（毫秒）。
 * @return CSM_OK 或 CSM_ERR_CONNECTION / CSM_ERR_STATE / CSM_ERR_INVALID。
 */
CSM_API csm_result_t csm_client_connect(csm_client_t *client,
                                        const char   *host,
                                        uint16_t      port,
                                        unsigned int  connect_timeout_ms);

/** 关闭连接并停止接收线程。未连接时调用安全；
 * 任何被阻塞的调用者将立即收到 CSM_ERR_CONNECTION。 */
CSM_API csm_result_t csm_client_disconnect(csm_client_t *client);

/** 底层套接字打开时返回非零值。 */
CSM_API int csm_client_is_connected(const csm_client_t *client);

/** 轮询直到 *host*:*port* 接受连接或 *timeout_ms* 超时。
 *
 * @return 服务器可达时返回 CSM_OK；否则返回 CSM_ERR_TIMEOUT。 */
CSM_API csm_result_t csm_client_wait_for_server(const char  *host,
                                                uint16_t     port,
                                                unsigned int timeout_ms,
                                                unsigned int retry_interval_ms);

/* ------------------------------------------------------------------------- */
/* 核心命令方法                                                               */
/* ------------------------------------------------------------------------- */

/** 发送同步命令并阻塞直到响应到达。
 *
 * 返回 CSM_OK 时调用者拥有 `*out_resp`，必须通过
 * `csm_command_response_dispose` 释放。 */
CSM_API csm_result_t csm_client_send_and_wait(csm_client_t           *client,
                                              const char             *command,
                                              unsigned int            timeout_ms,
                                              csm_command_response_t *out_resp);

/** 发送异步命令（`->` 后缀）并阻塞直到
 * `cmd-resp` 握手到达。最终的 `async-resp` 将被投递到
 * 通过 `csm_client_register_async_callback` 注册的回调
 * 以及轮询队列（`csm_client_poll_async_response`）。 */
CSM_API csm_result_t csm_client_post(csm_client_t *client,
                                     const char   *command,
                                     unsigned int  timeout_ms);

/** 发送异步无回复命令（`->|` 后缀）并阻塞直到
 * `cmd-resp` 握手到达。 */
CSM_API csm_result_t csm_client_post_no_reply(csm_client_t *client,
                                              const char   *command,
                                              unsigned int  timeout_ms);

/** 发送 `Ping` 并测量往返延迟。
 *
 * @param out_elapsed_ms  成功时设置为往返时间（毫秒）。
 * @return CSM_OK 或常规错误码之一。
 */
CSM_API csm_result_t csm_client_ping(csm_client_t *client,
                                     unsigned int  timeout_ms,
                                     double       *out_elapsed_ms);

/* ------------------------------------------------------------------------- */
/* 路由器管理辅助函数                                                         */
/* ------------------------------------------------------------------------- */

/** 执行 `List` 并返回响应文本。调用者通过
 * `csm_string_free` 释放 `*out_text`。 */
CSM_API csm_result_t csm_client_list_modules(csm_client_t *client,
                                             char        **out_text,
                                             unsigned int  timeout_ms);

/** 执行 `List API <module>`。调用者释放 `*out_text`。 */
CSM_API csm_result_t csm_client_list_api(csm_client_t *client,
                                         const char   *module,
                                         char        **out_text,
                                         unsigned int  timeout_ms);

/** 执行 `List State <module>`。调用者释放 `*out_text`。 */
CSM_API csm_result_t csm_client_list_states(csm_client_t *client,
                                            const char   *module,
                                            char        **out_text,
                                            unsigned int  timeout_ms);

/** 执行 `Help <module>`。调用者释放 `*out_text`。 */
CSM_API csm_result_t csm_client_help(csm_client_t *client,
                                     const char   *module,
                                     char        **out_text,
                                     unsigned int  timeout_ms);

/* ------------------------------------------------------------------------- */
/* 状态/中断订阅                                                              */
/* ------------------------------------------------------------------------- */

/** 订阅 CSM 模块的状态广播。
 *
 * 发送 ``"<status_name>@<module_name> -><register>"`` 并阻塞直到
 * `cmd-resp` 握手到达。`callback`（若非 NULL）将从
 * 接收线程对每个通知调用；通知同时也会入队以供
 * 通过 `csm_client_poll_status` 轮询。
 */
CSM_API csm_result_t csm_client_subscribe_status(csm_client_t          *client,
                                                 const char            *status_name,
                                                 const char            *module_name,
                                                 csm_status_callback_fn callback,
                                                 void                  *user_data,
                                                 unsigned int           timeout_ms);

/** 取消状态订阅。 */
CSM_API csm_result_t csm_client_unsubscribe_status(csm_client_t *client,
                                                   const char   *status_name,
                                                   const char   *module_name,
                                                   unsigned int  timeout_ms);

/** 为匹配 *original_command* 的 `async-resp` 数据包注册回调。 */
CSM_API csm_result_t csm_client_register_async_callback(csm_client_t          *client,
                                                        const char            *original_command,
                                                        csm_async_callback_fn  callback,
                                                        void                  *user_data);

/** 移除之前注册的异步回调。 */
CSM_API csm_result_t csm_client_unregister_async_callback(csm_client_t *client,
                                                          const char   *original_command);

/* ------------------------------------------------------------------------- */
/* 轮询队列（回调的替代方案）                                                 */
/* ------------------------------------------------------------------------- */

/** 从轮询队列弹出下一个状态/中断通知。
 *
 * @param timeout_ms  0 = 非阻塞；>0 = 最多阻塞 N 毫秒。
 * @return 返回 CSM_OK 且 `*out_notif` 已填充（调用者通过
 *         `csm_status_notification_dispose` 释放）；若队列在
 *         超时内为空则返回 CSM_ERR_TIMEOUT；若断开连接则返回 CSM_ERR_CONNECTION。
 */
CSM_API csm_result_t csm_client_poll_status(csm_client_t              *client,
                                            csm_status_notification_t *out_notif,
                                            unsigned int               timeout_ms);

/** 从轮询队列弹出下一个异步响应。 */
CSM_API csm_result_t csm_client_poll_async_response(csm_client_t         *client,
                                                    csm_async_response_t *out_resp,
                                                    unsigned int          timeout_ms);

/* ------------------------------------------------------------------------- */
/* 最后一次服务器错误                                                         */
/* ------------------------------------------------------------------------- */

/** 获取 *client* 观察到的最后一次 CSM_ERR_SERVER 的信息。
 *
 * 返回 CSM_OK 并用最近捕获的服务器错误码/消息填充 *out_err*；
 * 否则（该客户端从未观察到服务器错误）返回 CSM_ERR_STATE。
 * 存储的错误将无限期保留，直到下一次 CSM_ERR_SERVER 将其覆盖，
 * 因此可以在操作失败后立即调用此函数，
 * 而无需担心被中间无关的成功操作清除。
 */
CSM_API csm_result_t csm_client_last_server_error(const csm_client_t *client,
                                                  csm_server_error_t *out_err);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* CSM_TCP_ROUTER_CLIENT_H */
