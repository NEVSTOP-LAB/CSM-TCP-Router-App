/* csm_tcp_router_client.c - Cross-platform implementation of the
 * CSM-TCP-Router C client SDK.
 *
 * Threading: the receive loop runs on a single background thread.  All
 * public functions are safe to call from any thread; the client serialises
 * concurrent waiters for synchronous (RESP) and command-handshake
 * (CMD_RESP) responses respectively, mirroring the Python SDK.
 *
 * Sockets / threads abstraction:
 *   - Windows: Winsock2 + Win32 CRITICAL_SECTION / CONDITION_VARIABLE / threads.
 *   - POSIX:   BSD sockets + pthreads.
 */

#if !defined(_WIN32)
#  ifndef _POSIX_C_SOURCE
#    define _POSIX_C_SOURCE 200809L
#  endif
#  ifndef _DEFAULT_SOURCE
#    define _DEFAULT_SOURCE 1
#  endif
#endif

#include "csm_tcp_router_client.h"

#include <errno.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

/* CSM_BUILD_LIBRARY is defined by the build system (CMake / MSBuild) when
 * compiling the library, so that csm_tcp_router_client.h decorates the
 * exported symbols with the correct __declspec for shared builds.
 * Defining it unconditionally here would break consumers that compile this
 * .c file directly into their own DLL with a different export contract. */

/* ========================================================================= */
/* Platform abstraction                                                      */
/* ========================================================================= */

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <winsock2.h>
#  include <ws2tcpip.h>
#  include <windows.h>
#  pragma comment(lib, "Ws2_32.lib")

typedef SOCKET csm_socket_t;
#  define CSM_INVALID_SOCKET INVALID_SOCKET
#  define csm_close_socket(s) closesocket(s)
#  define csm_socket_errno()  WSAGetLastError()

typedef CRITICAL_SECTION   csm_mutex_t;
typedef CONDITION_VARIABLE csm_cond_t;
typedef HANDLE             csm_thread_t;

static void csm_mutex_init(csm_mutex_t *m)    { InitializeCriticalSection(m); }
static void csm_mutex_destroy(csm_mutex_t *m) { DeleteCriticalSection(m); }
static void csm_mutex_lock(csm_mutex_t *m)    { EnterCriticalSection(m); }
static void csm_mutex_unlock(csm_mutex_t *m)  { LeaveCriticalSection(m); }

static void csm_cond_init(csm_cond_t *c)      { InitializeConditionVariable(c); }
static void csm_cond_destroy(csm_cond_t *c)   { (void)c; }
static void csm_cond_signal(csm_cond_t *c)    { WakeConditionVariable(c); }
#if 0  /* reserved for future broadcast use */
static void csm_cond_broadcast(csm_cond_t *c) { WakeAllConditionVariable(c); }
#endif

/* Returns 1 on signal, 0 on timeout. */
static int csm_cond_wait_ms(csm_cond_t *c, csm_mutex_t *m, unsigned int ms) {
    BOOL ok = SleepConditionVariableCS(c, m, ms == 0 ? INFINITE : ms);
    if (ok) return 1;
    return 0;
}

static void csm_sleep_ms(unsigned int ms) { Sleep(ms); }

static double csm_monotonic_ms(void) {
    LARGE_INTEGER freq, now;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&now);
    return (double)now.QuadPart * 1000.0 / (double)freq.QuadPart;
}

#else  /* POSIX */
#  include <arpa/inet.h>
#  include <fcntl.h>
#  include <netdb.h>
#  include <netinet/in.h>
#  include <netinet/tcp.h>
#  include <pthread.h>
#  include <sys/select.h>
#  include <sys/socket.h>
#  include <sys/time.h>
#  include <unistd.h>

typedef int csm_socket_t;
#  define CSM_INVALID_SOCKET (-1)
#  define csm_close_socket(s) close(s)
#  define csm_socket_errno()  errno

typedef pthread_mutex_t csm_mutex_t;
typedef pthread_cond_t  csm_cond_t;
typedef pthread_t       csm_thread_t;

static void csm_mutex_init(csm_mutex_t *m)    { pthread_mutex_init(m, NULL); }
static void csm_mutex_destroy(csm_mutex_t *m) { pthread_mutex_destroy(m); }
static void csm_mutex_lock(csm_mutex_t *m)    { pthread_mutex_lock(m); }
static void csm_mutex_unlock(csm_mutex_t *m)  { pthread_mutex_unlock(m); }

static void csm_cond_init(csm_cond_t *c)      { pthread_cond_init(c, NULL); }
static void csm_cond_destroy(csm_cond_t *c)   { pthread_cond_destroy(c); }
static void csm_cond_signal(csm_cond_t *c)    { pthread_cond_signal(c); }
#if 0  /* reserved for future broadcast use */
static void csm_cond_broadcast(csm_cond_t *c) { pthread_cond_broadcast(c); }
#endif

static int csm_cond_wait_ms(csm_cond_t *c, csm_mutex_t *m, unsigned int ms) {
    if (ms == 0) {
        pthread_cond_wait(c, m);
        return 1;
    }
    struct timespec ts;
#  if defined(CLOCK_REALTIME)
    clock_gettime(CLOCK_REALTIME, &ts);
#  else
    struct timeval tv; gettimeofday(&tv, NULL);
    ts.tv_sec  = tv.tv_sec;
    ts.tv_nsec = tv.tv_usec * 1000;
#  endif
    ts.tv_sec  += ms / 1000;
    ts.tv_nsec += (long)(ms % 1000) * 1000000L;
    if (ts.tv_nsec >= 1000000000L) {
        ts.tv_sec  += ts.tv_nsec / 1000000000L;
        ts.tv_nsec  = ts.tv_nsec % 1000000000L;
    }
    int rc = pthread_cond_timedwait(c, m, &ts);
    return rc == 0 ? 1 : 0;
}

static void csm_sleep_ms(unsigned int ms) {
    struct timespec ts;
    ts.tv_sec  = ms / 1000;
    ts.tv_nsec = (long)(ms % 1000) * 1000000L;
    nanosleep(&ts, NULL);
}

static double csm_monotonic_ms(void) {
    struct timespec ts;
#  if defined(CLOCK_MONOTONIC)
    clock_gettime(CLOCK_MONOTONIC, &ts);
#  else
    struct timeval tv; gettimeofday(&tv, NULL);
    ts.tv_sec  = tv.tv_sec;
    ts.tv_nsec = tv.tv_usec * 1000;
#  endif
    return (double)ts.tv_sec * 1000.0 + (double)ts.tv_nsec / 1.0e6;
}
#endif

/* ========================================================================= */
/* WSA bootstrap (Windows only) - reference-counted                          */
/* ========================================================================= */

#if defined(_WIN32)
static csm_mutex_t g_wsa_lock;
static INIT_ONCE   g_wsa_lock_init_once_state = INIT_ONCE_STATIC_INIT;
static int         g_wsa_lock_inited = 0;
static int         g_wsa_refcount    = 0;

static BOOL CALLBACK csm_wsa_lock_init_once_cb(PINIT_ONCE init_once,
                                               PVOID param,
                                               PVOID *context) {
    (void)init_once; (void)param; (void)context;
    csm_mutex_init(&g_wsa_lock);
    g_wsa_lock_inited = 1;
    return TRUE;
}

static void csm_wsa_lock_init_once(void) {
    /* InitOnceExecuteOnce guarantees the callback runs exactly once
     * across all threads in the process, so the critical section is
     * initialised exactly once even under concurrent client creation. */
    InitOnceExecuteOnce(&g_wsa_lock_init_once_state,
                        csm_wsa_lock_init_once_cb, NULL, NULL);
}

static int csm_wsa_startup(void) {
    csm_wsa_lock_init_once();
    csm_mutex_lock(&g_wsa_lock);
    if (g_wsa_refcount == 0) {
        WSADATA d;
        if (WSAStartup(MAKEWORD(2, 2), &d) != 0) {
            csm_mutex_unlock(&g_wsa_lock);
            return -1;
        }
    }
    g_wsa_refcount++;
    csm_mutex_unlock(&g_wsa_lock);
    return 0;
}

static void csm_wsa_cleanup(void) {
    if (!g_wsa_lock_inited) return;
    csm_mutex_lock(&g_wsa_lock);
    if (g_wsa_refcount > 0) {
        g_wsa_refcount--;
        if (g_wsa_refcount == 0) WSACleanup();
    }
    csm_mutex_unlock(&g_wsa_lock);
}
#else
static int  csm_wsa_startup(void) { return 0; }
static void csm_wsa_cleanup(void) {}
#endif

/* ========================================================================= */
/* Result code helpers                                                       */
/* ========================================================================= */

const char *csm_result_str(csm_result_t code) {
    switch (code) {
        case CSM_OK:             return "OK";
        case CSM_ERR_INVALID:    return "Invalid argument";
        case CSM_ERR_CONNECTION: return "Connection error";
        case CSM_ERR_TIMEOUT:    return "Timeout";
        case CSM_ERR_PROTOCOL:   return "Protocol error";
        case CSM_ERR_SERVER:     return "Server error";
        case CSM_ERR_NOMEM:      return "Out of memory";
        case CSM_ERR_STATE:      return "Invalid state";
        case CSM_ERR_IO:         return "I/O error";
    }
    return "Unknown";
}

/* ========================================================================= */
/* Memory helpers                                                            */
/* ========================================================================= */

static char *csm_strdup_n(const char *s, size_t n) {
    char *out = (char *)malloc(n + 1);
    if (!out) return NULL;
    if (n) memcpy(out, s, n);
    out[n] = '\0';
    return out;
}

static char *csm_strdup_str(const char *s) {
    return csm_strdup_n(s ? s : "", s ? strlen(s) : 0);
}

void csm_string_free(char *s) { free(s); }

void csm_command_response_dispose(csm_command_response_t *resp) {
    if (!resp) return;
    free(resp->raw);
    resp->raw = NULL;
    resp->raw_len = 0;
}

void csm_async_response_dispose(csm_async_response_t *resp) {
    if (!resp) return;
    free(resp->raw);
    free(resp->original_command);
    resp->raw = NULL;
    resp->original_command = NULL;
    resp->raw_len = 0;
}

void csm_status_notification_dispose(csm_status_notification_t *n) {
    if (!n) return;
    free(n->raw);
    free(n->status_name);
    free(n->data);
    free(n->module_name);
    n->raw = NULL;
    n->status_name = NULL;
    n->data = NULL;
    n->module_name = NULL;
    n->raw_len = 0;
}

void csm_packet_dispose(csm_packet_t *pkt) {
    if (!pkt) return;
    free(pkt->data);
    pkt->data = NULL;
    pkt->data_len = 0;
}

/* ========================================================================= */
/* Protocol codec                                                            */
/* ========================================================================= */

static void csm_pack_be32(uint8_t *buf, uint32_t v) {
    buf[0] = (uint8_t)((v >> 24) & 0xFF);
    buf[1] = (uint8_t)((v >> 16) & 0xFF);
    buf[2] = (uint8_t)((v >>  8) & 0xFF);
    buf[3] = (uint8_t)( v        & 0xFF);
}

static uint32_t csm_unpack_be32(const uint8_t *buf) {
    return ((uint32_t)buf[0] << 24) |
           ((uint32_t)buf[1] << 16) |
           ((uint32_t)buf[2] <<  8) |
            (uint32_t)buf[3];
}

csm_result_t csm_encode_packet(const void       *data,
                               size_t            data_len,
                               csm_packet_type_t type,
                               uint8_t           flag1,
                               uint8_t           flag2,
                               uint8_t          *out_buf,
                               size_t            out_buf_size,
                               size_t           *out_len) {
    if (!out_buf || (data_len > 0 && !data)) return CSM_ERR_INVALID;
    if (out_buf_size < CSM_HEADER_SIZE + data_len) return CSM_ERR_INVALID;

    csm_pack_be32(out_buf, (uint32_t)data_len);
    out_buf[4] = CSM_PROTOCOL_VERSION;
    out_buf[5] = (uint8_t)type;
    out_buf[6] = flag1;
    out_buf[7] = flag2;
    if (data_len) memcpy(out_buf + CSM_HEADER_SIZE, data, data_len);
    if (out_len) *out_len = CSM_HEADER_SIZE + data_len;
    return CSM_OK;
}

csm_result_t csm_decode_header(const uint8_t *header_bytes,
                               size_t         header_len,
                               uint32_t      *out_data_len,
                               uint8_t       *out_version,
                               uint8_t       *out_type,
                               uint8_t       *out_flag1,
                               uint8_t       *out_flag2) {
    if (!header_bytes || header_len != CSM_HEADER_SIZE) return CSM_ERR_PROTOCOL;
    if (out_data_len) *out_data_len = csm_unpack_be32(header_bytes);
    if (out_version)  *out_version  = header_bytes[4];
    if (out_type)     *out_type     = header_bytes[5];
    if (out_flag1)    *out_flag1    = header_bytes[6];
    if (out_flag2)    *out_flag2    = header_bytes[7];
    return CSM_OK;
}

csm_result_t csm_parse_packet(const uint8_t *header_bytes,
                              size_t         header_len,
                              const uint8_t *body,
                              size_t         body_len,
                              csm_packet_t  *out_packet) {
    if (!out_packet) return CSM_ERR_INVALID;
    uint32_t data_len = 0;
    uint8_t  version = 0, type_byte = 0, flag1 = 0, flag2 = 0;
    csm_result_t r = csm_decode_header(header_bytes, header_len, &data_len,
                                       &version, &type_byte, &flag1, &flag2);
    if (r != CSM_OK) return r;
    if ((size_t)data_len != body_len) return CSM_ERR_PROTOCOL;

    /* Forward-compatible: unknown type bytes are mapped to INFO. */
    csm_packet_type_t pt;
    switch (type_byte) {
        case CSM_PT_INFO:
        case CSM_PT_ERROR:
        case CSM_PT_CMD:
        case CSM_PT_CMD_RESP:
        case CSM_PT_RESP:
        case CSM_PT_ASYNC_RESP:
        case CSM_PT_STATUS:
        case CSM_PT_INTERRUPT:
            pt = (csm_packet_type_t)type_byte;
            break;
        default:
            pt = CSM_PT_INFO;
            break;
    }

    out_packet->type     = pt;
    out_packet->version  = version;
    out_packet->flag1    = flag1;
    out_packet->flag2    = flag2;
    out_packet->data_len = body_len;
    out_packet->data     = NULL;
    if (body_len > 0) {
        out_packet->data = (uint8_t *)malloc(body_len);
        if (!out_packet->data) return CSM_ERR_NOMEM;
        memcpy(out_packet->data, body, body_len);
    }
    return CSM_OK;
}

/* ========================================================================= */
/* Internal: server-error parsing                                            */
/* ========================================================================= */

/* Parse a packet payload of the form "[Error: <code>] <message>" into out_err. */
static void csm_parse_server_error(const uint8_t      *data,
                                   size_t              len,
                                   csm_server_error_t *out_err) {
    out_err->code[0] = '\0';
    out_err->message[0] = '\0';

    /* Copy into a NUL-terminated stack buffer (capped). */
    char buf[1024];
    size_t copy_len = len < sizeof(buf) - 1 ? len : sizeof(buf) - 1;
    if (copy_len) memcpy(buf, data, copy_len);
    buf[copy_len] = '\0';

    /* Trim trailing whitespace. */
    while (copy_len > 0 && (buf[copy_len - 1] == ' ' ||
                            buf[copy_len - 1] == '\r' ||
                            buf[copy_len - 1] == '\n' ||
                            buf[copy_len - 1] == '\t')) {
        buf[--copy_len] = '\0';
    }

    const char *prefix = "[Error:";
    size_t prefix_len = strlen(prefix);
    const char *msg = buf;
    if (copy_len >= prefix_len && strncmp(buf, prefix, prefix_len) == 0) {
        char *end = strchr(buf, ']');
        if (end) {
            size_t code_len = (size_t)(end - (buf + prefix_len));
            /* Trim leading/trailing spaces from code. */
            const char *cs = buf + prefix_len;
            while (code_len && *cs == ' ') { cs++; code_len--; }
            while (code_len && cs[code_len - 1] == ' ') code_len--;
            if (code_len >= sizeof(out_err->code))
                code_len = sizeof(out_err->code) - 1;
            memcpy(out_err->code, cs, code_len);
            out_err->code[code_len] = '\0';
            msg = end + 1;
            while (*msg == ' ') msg++;
        }
    }

    size_t msg_len = strlen(msg);
    if (msg_len >= sizeof(out_err->message))
        msg_len = sizeof(out_err->message) - 1;
    memcpy(out_err->message, msg, msg_len);
    out_err->message[msg_len] = '\0';
}

/* ========================================================================= */
/* Internal: bounded queues                                                  */
/* ========================================================================= */

/* Generic queue node. Items hold either a packet (for resp/cmd_resp), or
 * a notification / async response (for the polling queues), or a sentinel
 * (signaled via `is_disconnect`). */
typedef struct csm_queue_node {
    struct csm_queue_node *next;
    void                  *item; /* type depends on queue */
    int                    is_disconnect;
    int                    is_server_error;
    csm_server_error_t     server_error;
} csm_queue_node_t;

typedef struct csm_queue {
    csm_queue_node_t *head;
    csm_queue_node_t *tail;
    csm_mutex_t       lock;
    csm_cond_t        cond;
} csm_queue_t;

static void csm_queue_init(csm_queue_t *q) {
    q->head = q->tail = NULL;
    csm_mutex_init(&q->lock);
    csm_cond_init(&q->cond);
}

static void csm_queue_destroy_with(csm_queue_t *q,
                                   void (*free_item)(void *)) {
    csm_queue_node_t *n = q->head;
    while (n) {
        csm_queue_node_t *next = n->next;
        if (n->item && free_item) free_item(n->item);
        free(n);
        n = next;
    }
    q->head = q->tail = NULL;
    csm_cond_destroy(&q->cond);
    csm_mutex_destroy(&q->lock);
}

/* Push an item; takes ownership of *item* on success. */
static int csm_queue_push(csm_queue_t *q, void *item,
                          int is_disconnect, int is_server_error,
                          const csm_server_error_t *err) {
    csm_queue_node_t *n = (csm_queue_node_t *)calloc(1, sizeof(*n));
    if (!n) return -1;
    n->item = item;
    n->is_disconnect = is_disconnect;
    n->is_server_error = is_server_error;
    if (err) n->server_error = *err;

    csm_mutex_lock(&q->lock);
    if (q->tail) q->tail->next = n;
    else         q->head = n;
    q->tail = n;
    csm_cond_signal(&q->cond);
    csm_mutex_unlock(&q->lock);
    return 0;
}

/* Pop one item, blocking up to *timeout_ms*. Returns CSM_OK with *out_item
 * set (and ownership transferred), CSM_ERR_TIMEOUT, CSM_ERR_CONNECTION
 * (sentinel), or CSM_ERR_SERVER (with *out_err* populated). */
static csm_result_t csm_queue_pop(csm_queue_t        *q,
                                  unsigned int        timeout_ms,
                                  void              **out_item,
                                  csm_server_error_t *out_err) {
    if (out_item) *out_item = NULL;
    double deadline = csm_monotonic_ms() + (double)timeout_ms;
    csm_mutex_lock(&q->lock);
    while (q->head == NULL) {
        double remaining = deadline - csm_monotonic_ms();
        if (remaining <= 0) {
            csm_mutex_unlock(&q->lock);
            return CSM_ERR_TIMEOUT;
        }
        unsigned int wait_ms = (unsigned int)remaining;
        if (wait_ms == 0) wait_ms = 1;
        csm_cond_wait_ms(&q->cond, &q->lock, wait_ms);
    }
    csm_queue_node_t *n = q->head;
    q->head = n->next;
    if (q->head == NULL) q->tail = NULL;
    csm_mutex_unlock(&q->lock);

    csm_result_t result = CSM_OK;
    if (n->is_disconnect) {
        result = CSM_ERR_CONNECTION;
    } else if (n->is_server_error) {
        if (out_err) *out_err = n->server_error;
        result = CSM_ERR_SERVER;
    } else if (out_item) {
        *out_item = n->item;
        n->item = NULL;
    }
    if (n->item) {
        /* Item not consumed (e.g. caller passed NULL out_item). Leak-safe
         * default is to free as bytes via the disposer set by the caller's
         * queue-specific wrapper; here we just drop it. */
        free(n->item);
    }
    free(n);
    return result;
}

/* ========================================================================= */
/* Subscription / async-callback registries                                  */
/* ========================================================================= */

typedef struct csm_status_sub {
    struct csm_status_sub *next;
    char                  *status_name;
    char                  *module_name;
    csm_status_callback_fn callback;
    void                  *user_data;
} csm_status_sub_t;

typedef struct csm_async_sub {
    struct csm_async_sub *next;
    char                 *original_command;
    csm_async_callback_fn callback;
    void                 *user_data;
} csm_async_sub_t;

/* ========================================================================= */
/* Client                                                                    */
/* ========================================================================= */

struct csm_client {
    csm_socket_t  sock;
    csm_thread_t  recv_thread;
    int           recv_thread_running;
    int           connected;        /* set under state_lock */
    int           stop_flag;        /* set to request shutdown */

    csm_mutex_t   state_lock;       /* protects connected/stop_flag/sock */
    csm_mutex_t   send_lock;        /* serialises sendall() */

    csm_mutex_t   resp_lock;        /* at most one in-flight RESP waiter */
    csm_mutex_t   cmd_resp_lock;    /* at most one in-flight CMD_RESP waiter */

    csm_queue_t   resp_queue;       /* items: csm_packet_t* */
    csm_queue_t   cmd_resp_queue;   /* items: csm_packet_t* (or NULL) */
    csm_queue_t   status_queue;     /* items: csm_status_notification_t* */
    csm_queue_t   async_queue;      /* items: csm_async_response_t* */

    csm_mutex_t   sub_lock;         /* protects subscription registries */
    csm_status_sub_t *status_subs;
    csm_async_sub_t  *async_subs;

    csm_mutex_t   err_lock;
    int           has_server_error;
    csm_server_error_t last_server_error;
};

/* --- helpers --- */

static void csm_packet_free_void(void *p) {
    csm_packet_t *pkt = (csm_packet_t *)p;
    if (!pkt) return;
    csm_packet_dispose(pkt);
    free(pkt);
}

static void csm_status_notif_free_void(void *p) {
    csm_status_notification_t *n = (csm_status_notification_t *)p;
    if (!n) return;
    csm_status_notification_dispose(n);
    free(n);
}

static void csm_async_resp_free_void(void *p) {
    csm_async_response_t *r = (csm_async_response_t *)p;
    if (!r) return;
    csm_async_response_dispose(r);
    free(r);
}

/* Set client.sock under state_lock; closes any old one. */
static void csm_set_socket_locked(csm_client_t *c, csm_socket_t s) {
    if (c->sock != CSM_INVALID_SOCKET) csm_close_socket(c->sock);
    c->sock = s;
}

/* Remember the most-recent server error so callers can fetch it after
 * receiving a CSM_ERR_SERVER. */
static void csm_record_server_error(csm_client_t *c,
                                    const csm_server_error_t *err) {
    csm_mutex_lock(&c->err_lock);
    c->has_server_error  = 1;
    c->last_server_error = *err;
    csm_mutex_unlock(&c->err_lock);
}

/* --- subscription registries (under sub_lock) --- */

static csm_status_sub_t *csm_find_status_sub(csm_client_t *c,
                                             const char   *status_name,
                                             const char   *module_name) {
    csm_status_sub_t *s = c->status_subs;
    while (s) {
        if (strcmp(s->status_name, status_name) == 0 &&
            strcmp(s->module_name, module_name) == 0) {
            return s;
        }
        s = s->next;
    }
    return NULL;
}

static csm_result_t csm_register_status_sub(csm_client_t          *c,
                                            const char            *status_name,
                                            const char            *module_name,
                                            csm_status_callback_fn callback,
                                            void                  *user_data) {
    csm_mutex_lock(&c->sub_lock);
    csm_status_sub_t *existing = csm_find_status_sub(c, status_name, module_name);
    if (existing) {
        existing->callback = callback;
        existing->user_data = user_data;
        csm_mutex_unlock(&c->sub_lock);
        return CSM_OK;
    }
    csm_status_sub_t *s = (csm_status_sub_t *)calloc(1, sizeof(*s));
    if (!s) { csm_mutex_unlock(&c->sub_lock); return CSM_ERR_NOMEM; }
    s->status_name = csm_strdup_str(status_name);
    s->module_name = csm_strdup_str(module_name);
    if (!s->status_name || !s->module_name) {
        free(s->status_name); free(s->module_name); free(s);
        csm_mutex_unlock(&c->sub_lock);
        return CSM_ERR_NOMEM;
    }
    s->callback = callback;
    s->user_data = user_data;
    s->next = c->status_subs;
    c->status_subs = s;
    csm_mutex_unlock(&c->sub_lock);
    return CSM_OK;
}

static void csm_remove_status_sub(csm_client_t *c,
                                  const char   *status_name,
                                  const char   *module_name) {
    csm_mutex_lock(&c->sub_lock);
    csm_status_sub_t **pp = &c->status_subs;
    while (*pp) {
        csm_status_sub_t *s = *pp;
        if (strcmp(s->status_name, status_name) == 0 &&
            strcmp(s->module_name, module_name) == 0) {
            *pp = s->next;
            free(s->status_name);
            free(s->module_name);
            free(s);
            break;
        }
        pp = &s->next;
    }
    csm_mutex_unlock(&c->sub_lock);
}

static csm_result_t csm_register_async_sub(csm_client_t          *c,
                                           const char            *original_command,
                                           csm_async_callback_fn  callback,
                                           void                  *user_data) {
    csm_mutex_lock(&c->sub_lock);
    csm_async_sub_t *s = c->async_subs;
    while (s) {
        if (strcmp(s->original_command, original_command) == 0) {
            s->callback = callback;
            s->user_data = user_data;
            csm_mutex_unlock(&c->sub_lock);
            return CSM_OK;
        }
        s = s->next;
    }
    s = (csm_async_sub_t *)calloc(1, sizeof(*s));
    if (!s) { csm_mutex_unlock(&c->sub_lock); return CSM_ERR_NOMEM; }
    s->original_command = csm_strdup_str(original_command);
    if (!s->original_command) { free(s); csm_mutex_unlock(&c->sub_lock); return CSM_ERR_NOMEM; }
    s->callback = callback;
    s->user_data = user_data;
    s->next = c->async_subs;
    c->async_subs = s;
    csm_mutex_unlock(&c->sub_lock);
    return CSM_OK;
}

static void csm_remove_async_sub(csm_client_t *c, const char *original_command) {
    csm_mutex_lock(&c->sub_lock);
    csm_async_sub_t **pp = &c->async_subs;
    while (*pp) {
        csm_async_sub_t *s = *pp;
        if (strcmp(s->original_command, original_command) == 0) {
            *pp = s->next;
            free(s->original_command);
            free(s);
            break;
        }
        pp = &s->next;
    }
    csm_mutex_unlock(&c->sub_lock);
}

static void csm_free_all_subs(csm_client_t *c) {
    csm_mutex_lock(&c->sub_lock);
    csm_status_sub_t *s = c->status_subs;
    while (s) { csm_status_sub_t *n = s->next; free(s->status_name); free(s->module_name); free(s); s = n; }
    c->status_subs = NULL;
    csm_async_sub_t *a = c->async_subs;
    while (a) { csm_async_sub_t *n = a->next; free(a->original_command); free(a); a = n; }
    c->async_subs = NULL;
    csm_mutex_unlock(&c->sub_lock);
}

/* --- recv helpers --- */

/* Read exactly *size* bytes from sock; returns 0 on success, -1 on EOF/err. */
static int csm_recv_all(csm_socket_t sock, uint8_t *buf, size_t size) {
    size_t total = 0;
    while (total < size) {
#if defined(_WIN32)
        int n = recv(sock, (char *)buf + total, (int)(size - total), 0);
#else
        ssize_t n = recv(sock, buf + total, size - total, 0);
#endif
        if (n <= 0) return -1;
        total += (size_t)n;
    }
    return 0;
}

/* --- Parsing helpers for ASYNC_RESP / STATUS payloads --- */

static void csm_async_resp_free_void(void *p);
static void csm_status_notif_free_void(void *p);

/* Build an csm_async_response_t from raw payload data. */
static csm_async_response_t *csm_make_async_response(const uint8_t *data,
                                                    size_t         len) {
    csm_async_response_t *r = (csm_async_response_t *)calloc(1, sizeof(*r));
    if (!r) return NULL;
    /* Server format: "<response-data> <- <original-command>". */
    const char  *sep   = " <- ";
    const size_t seplen = 4;
    size_t split = (size_t)-1;
    if (len >= seplen) {
        for (size_t i = 0; i + seplen <= len; ++i) {
            if (memcmp(data + i, sep, seplen) == 0) { split = i; break; }
        }
    }
    if (split != (size_t)-1) {
        r->raw = csm_strdup_n((const char *)data, split);
        r->raw_len = split;
        r->original_command = csm_strdup_n((const char *)data + split + seplen,
                                           len - split - seplen);
    } else {
        r->raw = csm_strdup_n((const char *)data, len);
        r->raw_len = len;
        r->original_command = csm_strdup_str("");
    }
    if (!r->raw || !r->original_command) {
        csm_async_resp_free_void(r);
        return NULL;
    }
    return r;
}

/* Build a csm_status_notification_t from raw payload data. */
static csm_status_notification_t *csm_make_status_notif(csm_packet_type_t pt,
                                                       const uint8_t   *data,
                                                       size_t           len) {
    csm_status_notification_t *n = (csm_status_notification_t *)calloc(1, sizeof(*n));
    if (!n) return NULL;
    n->packet_type = pt;
    n->raw = (char *)malloc(len + 1);
    if (!n->raw) { free(n); return NULL; }
    if (len) memcpy(n->raw, data, len);
    n->raw[len] = '\0';
    n->raw_len = len;

    /* Find rightmost " <- " separator (rsplit by 1). */
    const char *raw_str = n->raw;
    const char *left = raw_str;
    size_t left_len = len;
    const char *module_start = NULL;
    size_t module_len = 0;
    if (len >= 4) {
        for (size_t i = len - 4 + 1; i-- > 0; ) {
            if (memcmp(raw_str + i, " <- ", 4) == 0) {
                left_len = i;
                module_start = raw_str + i + 4;
                module_len = len - i - 4;
                break;
            }
        }
    }

    /* Trim whitespace from module. */
    while (module_len && (*module_start == ' ' || *module_start == '\t')) {
        module_start++; module_len--;
    }
    while (module_len && (module_start[module_len - 1] == ' ' ||
                          module_start[module_len - 1] == '\t' ||
                          module_start[module_len - 1] == '\r' ||
                          module_start[module_len - 1] == '\n')) {
        module_len--;
    }

    /* Split left by " >> " into status_name + data. */
    const char *status_start = NULL;
    size_t status_len = 0;
    const char *data_start = left;
    size_t data_len_local = left_len;
    if (left_len >= 4) {
        for (size_t i = 0; i + 4 <= left_len; ++i) {
            if (memcmp(left + i, " >> ", 4) == 0) {
                status_start = left;
                status_len = i;
                data_start = left + i + 4;
                data_len_local = left_len - i - 4;
                break;
            }
        }
    }

    /* Trim status_name and data. */
    while (status_len && (*status_start == ' ' || *status_start == '\t')) { status_start++; status_len--; }
    while (status_len && (status_start[status_len - 1] == ' ' || status_start[status_len - 1] == '\t')) status_len--;
    while (data_len_local && (*data_start == ' ' || *data_start == '\t')) { data_start++; data_len_local--; }
    while (data_len_local && (data_start[data_len_local - 1] == ' ' ||
                              data_start[data_len_local - 1] == '\t' ||
                              data_start[data_len_local - 1] == '\r' ||
                              data_start[data_len_local - 1] == '\n')) data_len_local--;

    n->status_name = csm_strdup_n(status_start ? status_start : "", status_len);
    n->data        = csm_strdup_n(data_start, data_len_local);
    n->module_name = csm_strdup_n(module_start ? module_start : "", module_len);
    if (!n->status_name || !n->data || !n->module_name) {
        csm_status_notif_free_void(n);
        return NULL;
    }
    return n;
}

/* --- Receive thread --- */

static void csm_dispatch_packet(csm_client_t *c, csm_packet_t *pkt) {
    /* On RESP / CMD_RESP / ERROR we transfer ownership of the packet
     * (or err sentinel) into a queue. On STATUS / ASYNC_RESP / INTERRUPT
     * we build a higher-level object and dispose of the raw packet. */
    switch (pkt->type) {
        case CSM_PT_RESP: {
            csm_packet_t *heap = (csm_packet_t *)malloc(sizeof(*heap));
            if (!heap) { csm_packet_free_void(pkt); return; }
            *heap = *pkt;
            /* Push to resp queue; queue-node owns it. */
            if (csm_queue_push(&c->resp_queue, heap, 0, 0, NULL) != 0) {
                csm_packet_free_void(heap);
            }
            free(pkt);
            return;
        }
        case CSM_PT_CMD_RESP: {
            csm_packet_t *heap = (csm_packet_t *)malloc(sizeof(*heap));
            if (!heap) { csm_packet_free_void(pkt); return; }
            *heap = *pkt;
            if (csm_queue_push(&c->cmd_resp_queue, heap, 0, 0, NULL) != 0) {
                csm_packet_free_void(heap);
            }
            free(pkt);
            return;
        }
        case CSM_PT_ERROR: {
            csm_server_error_t err;
            csm_parse_server_error(pkt->data, pkt->data_len, &err);
            csm_record_server_error(c, &err);
            csm_queue_push(&c->resp_queue, NULL, 0, 1, &err);
            csm_queue_push(&c->cmd_resp_queue, NULL, 0, 1, &err);
            csm_packet_free_void(pkt);
            return;
        }
        case CSM_PT_ASYNC_RESP: {
            csm_async_response_t *r = csm_make_async_response(pkt->data, pkt->data_len);
            if (r) {
                /* Look up callback under sub_lock. */
                csm_mutex_lock(&c->sub_lock);
                csm_async_callback_fn cb = NULL; void *ud = NULL;
                csm_async_sub_t *s = c->async_subs;
                while (s) {
                    if (strcmp(s->original_command, r->original_command) == 0) {
                        cb = s->callback; ud = s->user_data; break;
                    }
                    s = s->next;
                }
                csm_mutex_unlock(&c->sub_lock);
                if (cb) cb(r, ud);
                /* Push a copy onto polling queue so callback users and
                 * polling users are independent. */
                csm_async_response_t *queued = (csm_async_response_t *)calloc(1, sizeof(*queued));
                if (queued) {
                    queued->raw = csm_strdup_n(r->raw, r->raw_len);
                    queued->raw_len = r->raw_len;
                    queued->original_command = csm_strdup_str(r->original_command);
                    if (queued->raw && queued->original_command) {
                        if (csm_queue_push(&c->async_queue, queued, 0, 0, NULL) != 0)
                            csm_async_resp_free_void(queued);
                    } else {
                        csm_async_resp_free_void(queued);
                    }
                }
                csm_async_resp_free_void(r);
            }
            csm_packet_free_void(pkt);
            return;
        }
        case CSM_PT_STATUS:
        case CSM_PT_INTERRUPT: {
            csm_status_notification_t *n = csm_make_status_notif(pkt->type, pkt->data, pkt->data_len);
            if (n) {
                csm_mutex_lock(&c->sub_lock);
                csm_status_callback_fn cb = NULL; void *ud = NULL;
                csm_status_sub_t *s = c->status_subs;
                while (s) {
                    if (strcmp(s->status_name, n->status_name) == 0 &&
                        strcmp(s->module_name, n->module_name) == 0) {
                        cb = s->callback; ud = s->user_data; break;
                    }
                    s = s->next;
                }
                csm_mutex_unlock(&c->sub_lock);
                if (cb) cb(n, ud);
                /* Push a copy onto polling queue. */
                csm_status_notification_t *q = (csm_status_notification_t *)calloc(1, sizeof(*q));
                if (q) {
                    q->packet_type = n->packet_type;
                    q->raw = csm_strdup_n(n->raw, n->raw_len);
                    q->raw_len = n->raw_len;
                    q->status_name = csm_strdup_str(n->status_name);
                    q->data = csm_strdup_str(n->data);
                    q->module_name = csm_strdup_str(n->module_name);
                    if (q->raw && q->status_name && q->data && q->module_name) {
                        if (csm_queue_push(&c->status_queue, q, 0, 0, NULL) != 0)
                            csm_status_notif_free_void(q);
                    } else {
                        csm_status_notif_free_void(q);
                    }
                }
                csm_status_notif_free_void(n);
            }
            csm_packet_free_void(pkt);
            return;
        }
        case CSM_PT_INFO:
        case CSM_PT_CMD:
        default:
            /* INFO is silently discarded; CMD never sent by server. */
            csm_packet_free_void(pkt);
            return;
    }
}

#if defined(_WIN32)
static unsigned __stdcall csm_recv_thread_main(void *arg)
#else
static void *csm_recv_thread_main(void *arg)
#endif
{
    csm_client_t *c = (csm_client_t *)arg;
    uint8_t header[CSM_HEADER_SIZE];
    for (;;) {
        /* Snapshot stop_flag and sock under state_lock. csm_client_disconnect()
         * mutates both fields under the same lock, so a torn read or a stale
         * sock value cannot occur and TSAN/UBSan stay quiet. */
        csm_mutex_lock(&c->state_lock);
        int stop_flag    = c->stop_flag;
        csm_socket_t sock = c->sock;
        csm_mutex_unlock(&c->state_lock);

        if (stop_flag) break;
        if (sock == CSM_INVALID_SOCKET) break;
        if (csm_recv_all(sock, header, CSM_HEADER_SIZE) != 0) break;
        uint32_t data_len = csm_unpack_be32(header);
        uint8_t  *body = NULL;
        if (data_len > 0) {
            body = (uint8_t *)malloc(data_len);
            if (!body) break;
            if (csm_recv_all(sock, body, data_len) != 0) {
                free(body);
                break;
            }
        }
        csm_packet_t parsed = {0};
        csm_result_t r = csm_parse_packet(header, CSM_HEADER_SIZE, body, data_len, &parsed);
        free(body);
        if (r != CSM_OK) {
            /* Skip corrupt frame; keep loop alive. */
            continue;
        }
        /* Allocate heap copy to pass ownership to dispatch. */
        csm_packet_t *heap_pkt = (csm_packet_t *)malloc(sizeof(*heap_pkt));
        if (!heap_pkt) {
            csm_packet_dispose(&parsed);
            continue;
        }
        *heap_pkt = parsed;
        csm_dispatch_packet(c, heap_pkt);
    }

    /* Notify any blocked waiters that the connection is gone. */
    csm_queue_push(&c->resp_queue, NULL, 1, 0, NULL);
    csm_queue_push(&c->cmd_resp_queue, NULL, 1, 0, NULL);
    csm_queue_push(&c->status_queue, NULL, 1, 0, NULL);
    csm_queue_push(&c->async_queue, NULL, 1, 0, NULL);

    csm_mutex_lock(&c->state_lock);
    c->connected = 0;
    csm_mutex_unlock(&c->state_lock);
#if defined(_WIN32)
    return 0;
#else
    return NULL;
#endif
}

/* --- Lifecycle --- */

csm_client_t *csm_client_create(void) {
    if (csm_wsa_startup() != 0) return NULL;
    csm_client_t *c = (csm_client_t *)calloc(1, sizeof(*c));
    if (!c) { csm_wsa_cleanup(); return NULL; }
    c->sock = CSM_INVALID_SOCKET;
    csm_mutex_init(&c->state_lock);
    csm_mutex_init(&c->send_lock);
    csm_mutex_init(&c->resp_lock);
    csm_mutex_init(&c->cmd_resp_lock);
    csm_mutex_init(&c->sub_lock);
    csm_mutex_init(&c->err_lock);
    csm_queue_init(&c->resp_queue);
    csm_queue_init(&c->cmd_resp_queue);
    csm_queue_init(&c->status_queue);
    csm_queue_init(&c->async_queue);
    return c;
}

void csm_client_destroy(csm_client_t *client) {
    if (!client) return;
    csm_client_disconnect(client);
    csm_queue_destroy_with(&client->resp_queue, csm_packet_free_void);
    csm_queue_destroy_with(&client->cmd_resp_queue, csm_packet_free_void);
    csm_queue_destroy_with(&client->status_queue, csm_status_notif_free_void);
    csm_queue_destroy_with(&client->async_queue, csm_async_resp_free_void);
    csm_free_all_subs(client);
    csm_mutex_destroy(&client->state_lock);
    csm_mutex_destroy(&client->send_lock);
    csm_mutex_destroy(&client->resp_lock);
    csm_mutex_destroy(&client->cmd_resp_lock);
    csm_mutex_destroy(&client->sub_lock);
    csm_mutex_destroy(&client->err_lock);
    free(client);
    csm_wsa_cleanup();
}

/* Resolve host and connect with a timeout. Returns CSM_OK or
 * CSM_ERR_CONNECTION / CSM_ERR_TIMEOUT. */
static csm_result_t csm_do_connect(const char *host, uint16_t port,
                                   unsigned int timeout_ms,
                                   csm_socket_t *out_sock) {
    char port_str[16];
    snprintf(port_str, sizeof(port_str), "%u", (unsigned)port);

    struct addrinfo hints, *res = NULL;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family   = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_protocol = IPPROTO_TCP;
    if (getaddrinfo(host, port_str, &hints, &res) != 0 || !res) {
        return CSM_ERR_CONNECTION;
    }

    csm_result_t result = CSM_ERR_CONNECTION;
    csm_socket_t sock = CSM_INVALID_SOCKET;
    for (struct addrinfo *ai = res; ai; ai = ai->ai_next) {
        sock = socket(ai->ai_family, ai->ai_socktype, ai->ai_protocol);
        if (sock == CSM_INVALID_SOCKET) continue;

        /* Switch to non-blocking for connect-with-timeout. */
#if defined(_WIN32)
        u_long mode = 1;
        ioctlsocket(sock, FIONBIO, &mode);
#else
        int flags = fcntl(sock, F_GETFL, 0);
        if (flags == -1 || fcntl(sock, F_SETFL, flags | O_NONBLOCK) == -1) {
            csm_close_socket(sock);
            sock = CSM_INVALID_SOCKET;
            continue;
        }
#endif

        int cr = connect(sock, ai->ai_addr, (int)ai->ai_addrlen);
        if (cr == 0) {
            result = CSM_OK;
        } else {
#if defined(_WIN32)
            int err = WSAGetLastError();
            int in_progress = (err == WSAEWOULDBLOCK);
#else
            int in_progress = (errno == EINPROGRESS);
#endif
            if (in_progress) {
                fd_set wfds;
                FD_ZERO(&wfds);
                FD_SET(sock, &wfds);
                struct timeval tv;
                tv.tv_sec  = timeout_ms / 1000;
                tv.tv_usec = (long)(timeout_ms % 1000) * 1000;
                int sel = select((int)(sock + 1), NULL, &wfds, NULL,
                                 timeout_ms > 0 ? &tv : NULL);
                if (sel > 0) {
                    int       so_err = 0;
                    socklen_t sl = sizeof(so_err);
                    if (getsockopt(sock, SOL_SOCKET, SO_ERROR,
                                   (char *)&so_err, &sl) == 0 && so_err == 0) {
                        result = CSM_OK;
                    }
                } else if (sel == 0) {
                    result = CSM_ERR_TIMEOUT;
                }
            }
        }

        if (result == CSM_OK) {
            /* Switch back to blocking for the recv loop. */
#if defined(_WIN32)
            u_long mode2 = 0;
            ioctlsocket(sock, FIONBIO, &mode2);
#else
            int flags2 = fcntl(sock, F_GETFL, 0);
            fcntl(sock, F_SETFL, flags2 & ~O_NONBLOCK);
#endif
            *out_sock = sock;
            break;
        }
        csm_close_socket(sock);
        sock = CSM_INVALID_SOCKET;
    }

    freeaddrinfo(res);
    return result;
}

csm_result_t csm_client_connect(csm_client_t *client,
                                const char   *host,
                                uint16_t      port,
                                unsigned int  connect_timeout_ms) {
    if (!client || !host) return CSM_ERR_INVALID;

    csm_mutex_lock(&client->state_lock);
    if (client->connected) {
        csm_mutex_unlock(&client->state_lock);
        return CSM_ERR_STATE;
    }
    csm_mutex_unlock(&client->state_lock);

    csm_socket_t sock = CSM_INVALID_SOCKET;
    csm_result_t r = csm_do_connect(host, port,
                                    connect_timeout_ms ? connect_timeout_ms : 5000,
                                    &sock);
    if (r != CSM_OK) return r;

    csm_mutex_lock(&client->state_lock);
    csm_set_socket_locked(client, sock);
    client->connected = 1;
    client->stop_flag = 0;
    csm_mutex_unlock(&client->state_lock);

    csm_mutex_lock(&client->err_lock);
    client->has_server_error = 0;
    csm_mutex_unlock(&client->err_lock);

#if defined(_WIN32)
    client->recv_thread = (HANDLE)_beginthreadex(NULL, 0, csm_recv_thread_main,
                                                  client, 0, NULL);
    if (client->recv_thread == NULL) {
        csm_mutex_lock(&client->state_lock);
        csm_set_socket_locked(client, CSM_INVALID_SOCKET);
        client->connected = 0;
        csm_mutex_unlock(&client->state_lock);
        return CSM_ERR_IO;
    }
#else
    if (pthread_create(&client->recv_thread, NULL, csm_recv_thread_main, client) != 0) {
        csm_mutex_lock(&client->state_lock);
        csm_set_socket_locked(client, CSM_INVALID_SOCKET);
        client->connected = 0;
        csm_mutex_unlock(&client->state_lock);
        return CSM_ERR_IO;
    }
#endif
    client->recv_thread_running = 1;
    return CSM_OK;
}

csm_result_t csm_client_disconnect(csm_client_t *client) {
    if (!client) return CSM_ERR_INVALID;
    csm_mutex_lock(&client->state_lock);
    int was_connected = client->connected;
    client->stop_flag = 1;
    client->connected = 0;
    csm_socket_t s = client->sock;
    client->sock = CSM_INVALID_SOCKET;
    csm_mutex_unlock(&client->state_lock);

    /* Wake any blocked waiters before tearing down the socket. */
    csm_queue_push(&client->resp_queue, NULL, 1, 0, NULL);
    csm_queue_push(&client->cmd_resp_queue, NULL, 1, 0, NULL);
    csm_queue_push(&client->status_queue, NULL, 1, 0, NULL);
    csm_queue_push(&client->async_queue, NULL, 1, 0, NULL);

    if (s != CSM_INVALID_SOCKET) {
#if defined(_WIN32)
        shutdown(s, SD_BOTH);
#else
        shutdown(s, SHUT_RDWR);
#endif
        csm_close_socket(s);
    }

    if (client->recv_thread_running) {
#if defined(_WIN32)
        WaitForSingleObject(client->recv_thread, 2000);
        CloseHandle(client->recv_thread);
#else
        pthread_join(client->recv_thread, NULL);
#endif
        client->recv_thread_running = 0;
    }
    return was_connected ? CSM_OK : CSM_OK;
}

int csm_client_is_connected(const csm_client_t *client) {
    if (!client) return 0;
    /* Casting away const to take the lock; logically this is a read. */
    csm_client_t *mc = (csm_client_t *)client;
    csm_mutex_lock(&mc->state_lock);
    int v = mc->connected;
    csm_mutex_unlock(&mc->state_lock);
    return v;
}

csm_result_t csm_client_wait_for_server(const char *host,
                                        uint16_t    port,
                                        unsigned int timeout_ms,
                                        unsigned int retry_interval_ms) {
    if (!host) return CSM_ERR_INVALID;
    if (csm_wsa_startup() != 0) return CSM_ERR_IO;
    double deadline = csm_monotonic_ms() + (double)timeout_ms;
    csm_result_t result = CSM_ERR_TIMEOUT;
    while (csm_monotonic_ms() < deadline) {
        csm_socket_t s = CSM_INVALID_SOCKET;
        csm_result_t r = csm_do_connect(host, port, 1000, &s);
        if (r == CSM_OK) {
            csm_close_socket(s);
            result = CSM_OK;
            break;
        }
        csm_sleep_ms(retry_interval_ms ? retry_interval_ms : 500);
    }
    csm_wsa_cleanup();
    return result;
}

/* --- Send helpers --- */

static csm_result_t csm_send_raw(csm_client_t *client,
                                 const uint8_t *data, size_t len) {
    csm_mutex_lock(&client->state_lock);
    if (!client->connected) {
        csm_mutex_unlock(&client->state_lock);
        return CSM_ERR_CONNECTION;
    }
    csm_socket_t sock = client->sock;
    csm_mutex_unlock(&client->state_lock);

    csm_mutex_lock(&client->send_lock);
    size_t total = 0;
#if defined(MSG_NOSIGNAL)
    int send_flags = MSG_NOSIGNAL;
#else
    int send_flags = 0;
#endif
    while (total < len) {
#if defined(_WIN32)
        int n = send(sock, (const char *)data + total, (int)(len - total), 0);
        (void)send_flags;
#else
        ssize_t n = send(sock, data + total, len - total, send_flags);
#endif
        if (n <= 0) {
            csm_mutex_unlock(&client->send_lock);
            csm_mutex_lock(&client->state_lock);
            client->stop_flag = 1;
            csm_mutex_unlock(&client->state_lock);
            return CSM_ERR_CONNECTION;
        }
        total += (size_t)n;
    }
    csm_mutex_unlock(&client->send_lock);
    return CSM_OK;
}

/* Pack and send a CMD packet. */
static csm_result_t csm_send_cmd(csm_client_t *client, const char *command) {
    size_t len = strlen(command);
    uint8_t *buf = (uint8_t *)malloc(CSM_HEADER_SIZE + len);
    if (!buf) return CSM_ERR_NOMEM;
    size_t out_len = 0;
    csm_result_t r = csm_encode_packet(command, len, CSM_PT_CMD, 0, 0,
                                       buf, CSM_HEADER_SIZE + len, &out_len);
    if (r == CSM_OK) r = csm_send_raw(client, buf, out_len);
    free(buf);
    return r;
}

/* --- Wait helpers --- */

static csm_result_t csm_wait_for_resp(csm_client_t           *client,
                                      unsigned int            timeout_ms,
                                      csm_command_response_t *out_resp) {
    void *item = NULL;
    csm_server_error_t err = {0};
    csm_result_t r = csm_queue_pop(&client->resp_queue, timeout_ms, &item, &err);
    if (r == CSM_ERR_SERVER) {
        csm_record_server_error(client, &err);
        return CSM_ERR_SERVER;
    }
    if (r != CSM_OK) return r;
    csm_packet_t *pkt = (csm_packet_t *)item;
    if (out_resp) {
        out_resp->raw_len = pkt->data_len;
        out_resp->raw = (uint8_t *)malloc(pkt->data_len + 1);
        if (!out_resp->raw) { csm_packet_free_void(pkt); return CSM_ERR_NOMEM; }
        if (pkt->data_len) memcpy(out_resp->raw, pkt->data, pkt->data_len);
        out_resp->raw[pkt->data_len] = 0;
    }
    csm_packet_free_void(pkt);
    return CSM_OK;
}

static csm_result_t csm_wait_for_cmd_resp(csm_client_t *client,
                                          unsigned int  timeout_ms) {
    void *item = NULL;
    csm_server_error_t err = {0};
    csm_result_t r = csm_queue_pop(&client->cmd_resp_queue, timeout_ms, &item, &err);
    if (r == CSM_ERR_SERVER) {
        csm_record_server_error(client, &err);
        return CSM_ERR_SERVER;
    }
    if (r != CSM_OK) return r;
    /* Discard handshake payload. */
    csm_packet_t *pkt = (csm_packet_t *)item;
    csm_packet_free_void(pkt);
    return CSM_OK;
}

/* --- Public command API --- */

csm_result_t csm_client_send_and_wait(csm_client_t           *client,
                                      const char             *command,
                                      unsigned int            timeout_ms,
                                      csm_command_response_t *out_resp) {
    if (!client || !command) return CSM_ERR_INVALID;
    if (out_resp) { out_resp->raw = NULL; out_resp->raw_len = 0; }

    csm_mutex_lock(&client->resp_lock);
    csm_result_t r = csm_send_cmd(client, command);
    if (r == CSM_OK) r = csm_wait_for_resp(client, timeout_ms ? timeout_ms : 5000, out_resp);
    csm_mutex_unlock(&client->resp_lock);
    return r;
}

csm_result_t csm_client_post(csm_client_t *client, const char *command,
                             unsigned int timeout_ms) {
    if (!client || !command) return CSM_ERR_INVALID;
    csm_mutex_lock(&client->cmd_resp_lock);
    csm_result_t r = csm_send_cmd(client, command);
    if (r == CSM_OK) r = csm_wait_for_cmd_resp(client, timeout_ms ? timeout_ms : 5000);
    csm_mutex_unlock(&client->cmd_resp_lock);
    return r;
}

csm_result_t csm_client_post_no_reply(csm_client_t *client, const char *command,
                                      unsigned int timeout_ms) {
    return csm_client_post(client, command, timeout_ms);
}

csm_result_t csm_client_ping(csm_client_t *client, unsigned int timeout_ms,
                             double *out_elapsed_ms) {
    if (out_elapsed_ms) *out_elapsed_ms = 0.0;
    csm_command_response_t resp = {0};
    double t0 = csm_monotonic_ms();
    csm_result_t r = csm_client_send_and_wait(client, "Ping",
                                              timeout_ms ? timeout_ms : 2000, &resp);
    csm_command_response_dispose(&resp);
    if (r != CSM_OK) return r;
    if (out_elapsed_ms) *out_elapsed_ms = csm_monotonic_ms() - t0;
    return CSM_OK;
}

/* Shared helper: send a fixed-text command and return the response text. */
static csm_result_t csm_send_text_query(csm_client_t *client,
                                        const char   *command,
                                        char        **out_text,
                                        unsigned int  timeout_ms) {
    if (!out_text) return CSM_ERR_INVALID;
    *out_text = NULL;
    csm_command_response_t resp = {0};
    csm_result_t r = csm_client_send_and_wait(client, command, timeout_ms, &resp);
    if (r == CSM_OK) {
        *out_text = (char *)resp.raw; /* transfer ownership; was NUL-terminated */
        resp.raw = NULL;
    } else {
        csm_command_response_dispose(&resp);
    }
    return r;
}

csm_result_t csm_client_list_modules(csm_client_t *client, char **out_text,
                                     unsigned int timeout_ms) {
    return csm_send_text_query(client, "List", out_text, timeout_ms);
}

/* Build a "<prefix> <module>" command and send. */
static csm_result_t csm_send_text_query_2(csm_client_t *client,
                                          const char   *prefix,
                                          const char   *module,
                                          char        **out_text,
                                          unsigned int  timeout_ms) {
    if (!module) return CSM_ERR_INVALID;
    size_t plen = strlen(prefix);
    size_t mlen = strlen(module);
    char *cmd = (char *)malloc(plen + 1 + mlen + 1);
    if (!cmd) return CSM_ERR_NOMEM;
    memcpy(cmd, prefix, plen);
    cmd[plen] = ' ';
    memcpy(cmd + plen + 1, module, mlen);
    cmd[plen + 1 + mlen] = '\0';
    csm_result_t r = csm_send_text_query(client, cmd, out_text, timeout_ms);
    free(cmd);
    return r;
}

csm_result_t csm_client_list_api(csm_client_t *client, const char *module,
                                 char **out_text, unsigned int timeout_ms) {
    return csm_send_text_query_2(client, "List API", module, out_text, timeout_ms);
}

csm_result_t csm_client_list_states(csm_client_t *client, const char *module,
                                    char **out_text, unsigned int timeout_ms) {
    return csm_send_text_query_2(client, "List State", module, out_text, timeout_ms);
}

csm_result_t csm_client_help(csm_client_t *client, const char *module,
                             char **out_text, unsigned int timeout_ms) {
    return csm_send_text_query_2(client, "Help", module, out_text, timeout_ms);
}

/* --- Subscriptions --- */

csm_result_t csm_client_subscribe_status(csm_client_t          *client,
                                         const char            *status_name,
                                         const char            *module_name,
                                         csm_status_callback_fn callback,
                                         void                  *user_data,
                                         unsigned int           timeout_ms) {
    if (!client || !status_name || !module_name) return CSM_ERR_INVALID;

    /* Register first to eliminate the race where a STATUS arrives before
     * the callback is stored. */
    csm_result_t r = csm_register_status_sub(client, status_name, module_name,
                                             callback, user_data);
    if (r != CSM_OK) return r;

    /* Build "<status>@<module> -><register>". */
    size_t s_len = strlen(status_name);
    size_t m_len = strlen(module_name);
    const char *suffix = " -><register>";
    size_t suf_len = strlen(suffix);
    char *cmd = (char *)malloc(s_len + 1 + m_len + suf_len + 1);
    if (!cmd) { csm_remove_status_sub(client, status_name, module_name); return CSM_ERR_NOMEM; }
    memcpy(cmd, status_name, s_len);
    cmd[s_len] = '@';
    memcpy(cmd + s_len + 1, module_name, m_len);
    memcpy(cmd + s_len + 1 + m_len, suffix, suf_len);
    cmd[s_len + 1 + m_len + suf_len] = '\0';

    csm_mutex_lock(&client->cmd_resp_lock);
    r = csm_send_cmd(client, cmd);
    if (r == CSM_OK) r = csm_wait_for_cmd_resp(client, timeout_ms ? timeout_ms : 5000);
    csm_mutex_unlock(&client->cmd_resp_lock);
    free(cmd);

    if (r != CSM_OK) csm_remove_status_sub(client, status_name, module_name);
    return r;
}

csm_result_t csm_client_unsubscribe_status(csm_client_t *client,
                                           const char   *status_name,
                                           const char   *module_name,
                                           unsigned int  timeout_ms) {
    if (!client || !status_name || !module_name) return CSM_ERR_INVALID;
    size_t s_len = strlen(status_name);
    size_t m_len = strlen(module_name);
    const char *suffix = " -><unregister>";
    size_t suf_len = strlen(suffix);
    char *cmd = (char *)malloc(s_len + 1 + m_len + suf_len + 1);
    if (!cmd) return CSM_ERR_NOMEM;
    memcpy(cmd, status_name, s_len);
    cmd[s_len] = '@';
    memcpy(cmd + s_len + 1, module_name, m_len);
    memcpy(cmd + s_len + 1 + m_len, suffix, suf_len);
    cmd[s_len + 1 + m_len + suf_len] = '\0';

    csm_mutex_lock(&client->cmd_resp_lock);
    csm_result_t r = csm_send_cmd(client, cmd);
    if (r == CSM_OK) r = csm_wait_for_cmd_resp(client, timeout_ms ? timeout_ms : 5000);
    csm_mutex_unlock(&client->cmd_resp_lock);
    free(cmd);

    csm_remove_status_sub(client, status_name, module_name);
    return r;
}

csm_result_t csm_client_register_async_callback(csm_client_t          *client,
                                                const char            *original_command,
                                                csm_async_callback_fn  callback,
                                                void                  *user_data) {
    if (!client || !original_command || !callback) return CSM_ERR_INVALID;
    return csm_register_async_sub(client, original_command, callback, user_data);
}

csm_result_t csm_client_unregister_async_callback(csm_client_t *client,
                                                  const char   *original_command) {
    if (!client || !original_command) return CSM_ERR_INVALID;
    csm_remove_async_sub(client, original_command);
    return CSM_OK;
}

/* --- Polling queues --- */

csm_result_t csm_client_poll_status(csm_client_t              *client,
                                    csm_status_notification_t *out_notif,
                                    unsigned int               timeout_ms) {
    if (!client || !out_notif) return CSM_ERR_INVALID;
    memset(out_notif, 0, sizeof(*out_notif));
    void *item = NULL;
    csm_result_t r = csm_queue_pop(&client->status_queue, timeout_ms, &item, NULL);
    if (r != CSM_OK) return r;
    csm_status_notification_t *src = (csm_status_notification_t *)item;
    /* Move ownership of fields from src to out_notif. */
    *out_notif = *src;
    free(src);
    return CSM_OK;
}

csm_result_t csm_client_poll_async_response(csm_client_t         *client,
                                            csm_async_response_t *out_resp,
                                            unsigned int          timeout_ms) {
    if (!client || !out_resp) return CSM_ERR_INVALID;
    memset(out_resp, 0, sizeof(*out_resp));
    void *item = NULL;
    csm_result_t r = csm_queue_pop(&client->async_queue, timeout_ms, &item, NULL);
    if (r != CSM_OK) return r;
    csm_async_response_t *src = (csm_async_response_t *)item;
    *out_resp = *src;
    free(src);
    return CSM_OK;
}

csm_result_t csm_client_last_server_error(const csm_client_t *client,
                                          csm_server_error_t *out_err) {
    if (!client || !out_err) return CSM_ERR_INVALID;
    csm_client_t *mc = (csm_client_t *)client;
    csm_mutex_lock(&mc->err_lock);
    csm_result_t r = mc->has_server_error ? CSM_OK : CSM_ERR_STATE;
    if (r == CSM_OK) *out_err = mc->last_server_error;
    csm_mutex_unlock(&mc->err_lock);
    return r;
}
