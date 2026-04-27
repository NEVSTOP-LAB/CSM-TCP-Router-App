/* csm_tcp_router_client.h - Single-header public C API for the CSM-TCP-Router
 * client SDK.
 *
 * This SDK is the C counterpart of the Python `csm_tcp_router_client` module.
 * It implements the CSM-TCP-Router protocol v0 over TCP and exposes a
 * thread-safe synchronous client (`csm_client_t`) with both blocking calls
 * and asynchronous callback / polling-queue delivery.
 *
 * Wire format (8-byte header, big-endian):
 *
 *     | Data Length (4B) | Version (1B=0x01) | Type (1B) | FLAG1 (1B) | FLAG2 (1B) | Payload |
 *     +---------------------------- Header (8B) ----------------------------+
 *
 * Quickstart:
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
 * The library is portable across Windows (Winsock2 + Win32 threads) and
 * POSIX systems (BSD sockets + pthreads), and is built as either a static
 * or shared library by the bundled CMake / Visual Studio projects.
 */
#ifndef CSM_TCP_ROUTER_CLIENT_H
#define CSM_TCP_ROUTER_CLIENT_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------------- */
/* Versioning and DLL export                                                 */
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
/* Return codes                                                              */
/* ------------------------------------------------------------------------- */

/** Result codes returned by every public SDK function. */
typedef enum csm_result {
    CSM_OK              =  0, /**< Operation succeeded.                    */
    CSM_ERR_INVALID     = -1, /**< Invalid argument or NULL pointer.       */
    CSM_ERR_CONNECTION  = -2, /**< Connection failed or was lost.          */
    CSM_ERR_TIMEOUT     = -3, /**< Operation exceeded its timeout.         */
    CSM_ERR_PROTOCOL    = -4, /**< Invalid / malformed protocol frame.     */
    CSM_ERR_SERVER      = -5, /**< Server returned an ERROR packet.        */
    CSM_ERR_NOMEM       = -6, /**< Memory allocation failure.              */
    CSM_ERR_STATE       = -7, /**< Operation invalid in current state.     */
    CSM_ERR_IO          = -8  /**< Underlying socket / OS I/O error.       */
} csm_result_t;

/** Return a static, human-readable string for *code*. */
CSM_API const char *csm_result_str(csm_result_t code);

/* ------------------------------------------------------------------------- */
/* Protocol constants                                                        */
/* ------------------------------------------------------------------------- */

/** Packet type byte values (CSM-TCP-Router protocol v0). */
typedef enum csm_packet_type {
    CSM_PT_INFO       = 0x00, /**< Welcome / goodbye informational text. */
    CSM_PT_ERROR      = 0x01, /**< Server error: "[Error: <code>] <msg>" */
    CSM_PT_CMD        = 0x02, /**< Command packet (client -> server).    */
    CSM_PT_CMD_RESP   = 0x03, /**< Async / subscribe handshake ACK.      */
    CSM_PT_RESP       = 0x04, /**< Synchronous response payload.         */
    CSM_PT_ASYNC_RESP = 0x05, /**< Async response: "<data> <- <cmd>".    */
    CSM_PT_STATUS     = 0x06, /**< Status broadcast.                     */
    CSM_PT_INTERRUPT  = 0x07  /**< Interrupt broadcast.                  */
} csm_packet_type_t;

/** Number of bytes in the fixed wire-format header. */
#define CSM_HEADER_SIZE 8

/** Protocol version byte sent in every outgoing packet. */
#define CSM_PROTOCOL_VERSION 0x01

/* ------------------------------------------------------------------------- */
/* Public data models                                                        */
/* ------------------------------------------------------------------------- */

/** A decoded packet (header fields + heap-allocated body). */
typedef struct csm_packet {
    csm_packet_type_t type;
    uint8_t           version;
    uint8_t           flag1;
    uint8_t           flag2;
    uint8_t          *data;     /**< Owned payload buffer (or NULL).      */
    size_t            data_len; /**< Length of `data` in bytes.           */
} csm_packet_t;

/** A successful synchronous response. */
typedef struct csm_command_response {
    uint8_t *raw;     /**< NUL-terminated UTF-8 payload (owned).         */
    size_t   raw_len; /**< Length of `raw` in bytes (excluding NUL).     */
} csm_command_response_t;

/** An ASYNC_RESP packet: payload + the original command echoed by the server.
 *
 * Server format: ``"<response-data> <- <original-command>"``. */
typedef struct csm_async_response {
    char  *raw;              /**< Response payload (owned, NUL-terminated). */
    size_t raw_len;
    char  *original_command; /**< Echoed command text (owned).              */
} csm_async_response_t;

/** A STATUS or INTERRUPT broadcast.
 *
 * Server format: ``"<status-name> >> <data> <- <module>"``. */
typedef struct csm_status_notification {
    csm_packet_type_t packet_type; /**< CSM_PT_STATUS or CSM_PT_INTERRUPT */
    char  *raw;                    /**< Full payload (owned).             */
    size_t raw_len;
    char  *status_name;            /**< Owned, NUL-terminated.            */
    char  *data;                   /**< Owned, NUL-terminated.            */
    char  *module_name;            /**< Owned, NUL-terminated.            */
} csm_status_notification_t;

/** Free a string previously returned via an out-parameter (e.g. by
 * `csm_client_list_modules`). Safe to call with NULL. */
CSM_API void csm_string_free(char *s);

/** Free heap members of a `csm_command_response_t` (does not free the struct). */
CSM_API void csm_command_response_dispose(csm_command_response_t *resp);

/** Free heap members of a `csm_async_response_t` (does not free the struct). */
CSM_API void csm_async_response_dispose(csm_async_response_t *resp);

/** Free heap members of a `csm_status_notification_t` (does not free struct). */
CSM_API void csm_status_notification_dispose(csm_status_notification_t *n);

/** Free heap members of a `csm_packet_t` (does not free the struct). */
CSM_API void csm_packet_dispose(csm_packet_t *pkt);

/* ------------------------------------------------------------------------- */
/* Server error info                                                         */
/* ------------------------------------------------------------------------- */

/** Information about the most recent CSM_ERR_SERVER returned by a function. */
typedef struct csm_server_error {
    char code[32];     /**< NUL-terminated CSM error code (may be empty). */
    char message[256]; /**< NUL-terminated error message (truncated).     */
} csm_server_error_t;

/* ------------------------------------------------------------------------- */
/* Protocol codec (exposed for advanced use / testing)                       */
/* ------------------------------------------------------------------------- */

/** Encode *data* (`data_len` bytes) into a complete wire-format packet.
 *
 * The caller must pass `out_buf` with at least `CSM_HEADER_SIZE + data_len`
 * bytes. On success, `*out_len` is set to the number of bytes written.
 */
CSM_API csm_result_t csm_encode_packet(const void       *data,
                                       size_t            data_len,
                                       csm_packet_type_t type,
                                       uint8_t           flag1,
                                       uint8_t           flag2,
                                       uint8_t          *out_buf,
                                       size_t            out_buf_size,
                                       size_t           *out_len);

/** Decode an 8-byte header into its constituent fields. */
CSM_API csm_result_t csm_decode_header(const uint8_t *header_bytes,
                                       size_t         header_len,
                                       uint32_t      *out_data_len,
                                       uint8_t       *out_version,
                                       uint8_t       *out_type,
                                       uint8_t       *out_flag1,
                                       uint8_t       *out_flag2);

/** Build a `csm_packet_t` from raw header + body. The returned packet
 * **owns** a copy of the body; release it with `csm_packet_dispose`.
 *
 * Unknown packet type bytes are mapped to `CSM_PT_INFO` for forward
 * compatibility (the server may introduce new types in future revisions).
 */
CSM_API csm_result_t csm_parse_packet(const uint8_t *header_bytes,
                                      size_t         header_len,
                                      const uint8_t *body,
                                      size_t         body_len,
                                      csm_packet_t  *out_packet);

/* ------------------------------------------------------------------------- */
/* Callback signatures                                                       */
/* ------------------------------------------------------------------------- */

/** Status/interrupt notification callback.
 *
 * Invoked from the receive thread. Must be fast and non-blocking. The
 * `notif` pointer and its members are valid only for the duration of the
 * call; copy any data you need before returning.
 */
typedef void (*csm_status_callback_fn)(const csm_status_notification_t *notif,
                                       void *user_data);

/** Async response callback. Same threading rules as `csm_status_callback_fn`. */
typedef void (*csm_async_callback_fn)(const csm_async_response_t *resp,
                                      void *user_data);

/* ------------------------------------------------------------------------- */
/* Client lifecycle                                                          */
/* ------------------------------------------------------------------------- */

/** Opaque thread-safe client handle. */
typedef struct csm_client csm_client_t;

/** Create a new client instance. Returns NULL on allocation failure. */
CSM_API csm_client_t *csm_client_create(void);

/** Disconnect (if connected) and free all resources held by *client*. */
CSM_API void csm_client_destroy(csm_client_t *client);

/** Open a TCP connection and start the background receive thread.
 *
 * @param connect_timeout_ms  Connect timeout in milliseconds.
 * @return CSM_OK or CSM_ERR_CONNECTION / CSM_ERR_STATE / CSM_ERR_INVALID.
 */
CSM_API csm_result_t csm_client_connect(csm_client_t *client,
                                        const char   *host,
                                        uint16_t      port,
                                        unsigned int  connect_timeout_ms);

/** Close the connection and stop the receive thread. Safe to call when not
 * connected; any blocked callers receive CSM_ERR_CONNECTION immediately. */
CSM_API csm_result_t csm_client_disconnect(csm_client_t *client);

/** Return non-zero while the underlying socket is open. */
CSM_API int csm_client_is_connected(const csm_client_t *client);

/** Poll until *host*:*port* accepts a connection or *timeout_ms* elapses.
 *
 * @return CSM_OK when the server is reachable; CSM_ERR_TIMEOUT otherwise. */
CSM_API csm_result_t csm_client_wait_for_server(const char  *host,
                                                uint16_t     port,
                                                unsigned int timeout_ms,
                                                unsigned int retry_interval_ms);

/* ------------------------------------------------------------------------- */
/* Core command methods                                                      */
/* ------------------------------------------------------------------------- */

/** Send a synchronous command and block until the response arrives.
 *
 * On CSM_OK the caller owns `*out_resp` and must release it via
 * `csm_command_response_dispose`. */
CSM_API csm_result_t csm_client_send_and_wait(csm_client_t           *client,
                                              const char             *command,
                                              unsigned int            timeout_ms,
                                              csm_command_response_t *out_resp);

/** Send an asynchronous command (`->` suffix) and block until the
 * `cmd-resp` handshake arrives. The eventual `async-resp` is delivered to
 * any callback registered via `csm_client_register_async_callback` and to
 * the polling queue (`csm_client_poll_async_response`). */
CSM_API csm_result_t csm_client_post(csm_client_t *client,
                                     const char   *command,
                                     unsigned int  timeout_ms);

/** Send an async no-reply command (`->|` suffix) and block until the
 * `cmd-resp` handshake arrives. */
CSM_API csm_result_t csm_client_post_no_reply(csm_client_t *client,
                                              const char   *command,
                                              unsigned int  timeout_ms);

/** Send a `Ping` and measure round-trip latency.
 *
 * @param out_elapsed_ms  Set to round-trip time in milliseconds on success.
 * @return CSM_OK or one of the regular error codes.
 */
CSM_API csm_result_t csm_client_ping(csm_client_t *client,
                                     unsigned int  timeout_ms,
                                     double       *out_elapsed_ms);

/* ------------------------------------------------------------------------- */
/* Router management helpers                                                 */
/* ------------------------------------------------------------------------- */

/** Run `List` and return the response text. Caller frees `*out_text` via
 * `csm_string_free`. */
CSM_API csm_result_t csm_client_list_modules(csm_client_t *client,
                                             char        **out_text,
                                             unsigned int  timeout_ms);

/** Run `List API <module>`. Caller frees `*out_text`. */
CSM_API csm_result_t csm_client_list_api(csm_client_t *client,
                                         const char   *module,
                                         char        **out_text,
                                         unsigned int  timeout_ms);

/** Run `List State <module>`. Caller frees `*out_text`. */
CSM_API csm_result_t csm_client_list_states(csm_client_t *client,
                                            const char   *module,
                                            char        **out_text,
                                            unsigned int  timeout_ms);

/** Run `Help <module>`. Caller frees `*out_text`. */
CSM_API csm_result_t csm_client_help(csm_client_t *client,
                                     const char   *module,
                                     char        **out_text,
                                     unsigned int  timeout_ms);

/* ------------------------------------------------------------------------- */
/* Status / interrupt subscriptions                                          */
/* ------------------------------------------------------------------------- */

/** Subscribe to a CSM module's status broadcast.
 *
 * Sends ``"<status_name>@<module_name> -><register>"`` and blocks until the
 * `cmd-resp` handshake arrives. `callback` (if non-NULL) is invoked from the
 * receive thread for each notification; notifications are also enqueued for
 * polling via `csm_client_poll_status`.
 */
CSM_API csm_result_t csm_client_subscribe_status(csm_client_t          *client,
                                                 const char            *status_name,
                                                 const char            *module_name,
                                                 csm_status_callback_fn callback,
                                                 void                  *user_data,
                                                 unsigned int           timeout_ms);

/** Cancel a status subscription. */
CSM_API csm_result_t csm_client_unsubscribe_status(csm_client_t *client,
                                                   const char   *status_name,
                                                   const char   *module_name,
                                                   unsigned int  timeout_ms);

/** Register a callback for `async-resp` packets matching *original_command*. */
CSM_API csm_result_t csm_client_register_async_callback(csm_client_t          *client,
                                                        const char            *original_command,
                                                        csm_async_callback_fn  callback,
                                                        void                  *user_data);

/** Remove a previously registered async callback. */
CSM_API csm_result_t csm_client_unregister_async_callback(csm_client_t *client,
                                                          const char   *original_command);

/* ------------------------------------------------------------------------- */
/* Polling queues (alternative to callbacks)                                 */
/* ------------------------------------------------------------------------- */

/** Pop the next status/interrupt notification from the polling queue.
 *
 * @param timeout_ms  0 = non-blocking; >0 = block up to N ms.
 * @return CSM_OK with `*out_notif` populated (caller disposes via
 *         `csm_status_notification_dispose`); CSM_ERR_TIMEOUT if the queue
 *         is empty within the timeout; CSM_ERR_CONNECTION if disconnected.
 */
CSM_API csm_result_t csm_client_poll_status(csm_client_t              *client,
                                            csm_status_notification_t *out_notif,
                                            unsigned int               timeout_ms);

/** Pop the next async response from the polling queue. */
CSM_API csm_result_t csm_client_poll_async_response(csm_client_t         *client,
                                                    csm_async_response_t *out_resp,
                                                    unsigned int          timeout_ms);

/* ------------------------------------------------------------------------- */
/* Last server error                                                         */
/* ------------------------------------------------------------------------- */

/** Retrieve information about the last CSM_ERR_SERVER observed by *client*.
 *
 * Returns CSM_OK and fills *out_err* with the most recently captured
 * server-error code/message; otherwise (no server error has ever been
 * observed for this client) returns CSM_ERR_STATE. The stored error is
 * kept indefinitely until the next CSM_ERR_SERVER overwrites it, so it
 * is safe to call this immediately after a failing operation without
 * worrying about it being cleared by an unrelated success in between.
 */
CSM_API csm_result_t csm_client_last_server_error(const csm_client_t *client,
                                                  csm_server_error_t *out_err);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* CSM_TCP_ROUTER_CLIENT_H */
