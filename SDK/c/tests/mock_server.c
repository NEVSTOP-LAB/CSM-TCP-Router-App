/* mock_server.c - cross-platform implementation of the test mock server. */

#if !defined(_WIN32)
#  ifndef _POSIX_C_SOURCE
#    define _POSIX_C_SOURCE 200809L
#  endif
#  ifndef _DEFAULT_SOURCE
#    define _DEFAULT_SOURCE 1
#  endif
#endif

#include "mock_server.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <winsock2.h>
#  include <ws2tcpip.h>
#  include <windows.h>
#  include <process.h>
#  pragma comment(lib, "Ws2_32.lib")
typedef SOCKET ms_socket_t;
#  define MS_INVALID_SOCKET INVALID_SOCKET
#  define ms_close_socket(s) closesocket(s)
typedef CRITICAL_SECTION ms_mutex_t;
typedef CONDITION_VARIABLE ms_cond_t;
typedef HANDLE ms_thread_t;
static void ms_mutex_init(ms_mutex_t *m){InitializeCriticalSection(m);}
static void ms_mutex_destroy(ms_mutex_t *m){DeleteCriticalSection(m);}
static void ms_mutex_lock(ms_mutex_t *m){EnterCriticalSection(m);}
static void ms_mutex_unlock(ms_mutex_t *m){LeaveCriticalSection(m);}
static void ms_cond_init(ms_cond_t *c){InitializeConditionVariable(c);}
static void ms_cond_destroy(ms_cond_t *c){(void)c;}
static void ms_cond_signal(ms_cond_t *c){WakeConditionVariable(c);}
static int  ms_cond_wait_ms(ms_cond_t *c, ms_mutex_t *m, unsigned int ms){
    return SleepConditionVariableCS(c, m, ms == 0 ? INFINITE : ms) ? 1 : 0;
}
#if 0  /* reserved for future use */
static void ms_sleep_ms(unsigned int ms){Sleep(ms);}
#endif
#else
#  include <arpa/inet.h>
#  include <netinet/in.h>
#  include <pthread.h>
#  include <sys/socket.h>
#  include <sys/select.h>
#  include <sys/time.h>
#  include <time.h>
#  include <unistd.h>
typedef int ms_socket_t;
#  define MS_INVALID_SOCKET (-1)
#  define ms_close_socket(s) close(s)
typedef pthread_mutex_t ms_mutex_t;
typedef pthread_cond_t  ms_cond_t;
typedef pthread_t       ms_thread_t;
static void ms_mutex_init(ms_mutex_t *m){pthread_mutex_init(m,NULL);}
static void ms_mutex_destroy(ms_mutex_t *m){pthread_mutex_destroy(m);}
static void ms_mutex_lock(ms_mutex_t *m){pthread_mutex_lock(m);}
static void ms_mutex_unlock(ms_mutex_t *m){pthread_mutex_unlock(m);}
static void ms_cond_init(ms_cond_t *c){pthread_cond_init(c,NULL);}
static void ms_cond_destroy(ms_cond_t *c){pthread_cond_destroy(c);}
static void ms_cond_signal(ms_cond_t *c){pthread_cond_signal(c);}
static int ms_cond_wait_ms(ms_cond_t *c, ms_mutex_t *m, unsigned int ms){
    if (ms == 0){pthread_cond_wait(c,m);return 1;}
    struct timespec ts; clock_gettime(CLOCK_REALTIME,&ts);
    ts.tv_sec += ms/1000;
    ts.tv_nsec += (long)(ms%1000)*1000000L;
    if (ts.tv_nsec>=1000000000L){ts.tv_sec+=ts.tv_nsec/1000000000L;ts.tv_nsec%=1000000000L;}
    return pthread_cond_timedwait(c,m,&ts) == 0 ? 1 : 0;
}
#if 0  /* reserved for future use */
static void ms_sleep_ms(unsigned int ms){
    struct timespec ts; ts.tv_sec=ms/1000; ts.tv_nsec=(long)(ms%1000)*1000000L;
    nanosleep(&ts,NULL);
}
#endif
#endif

#define MS_MAX_CLIENTS 16
#define MS_HEADER 8
#define MS_VER 0x01

/* --- response map --- */
typedef struct ms_resp {
    struct ms_resp *next;
    char           *cmd;
    csm_packet_type_t type;
    uint8_t        *data;
    size_t          data_len;
} ms_resp_t;

/* --- received command queue --- */
typedef struct ms_msg {
    struct ms_msg *next;
    char          *text;
} ms_msg_t;

struct csm_mock_server {
    ms_socket_t  listen_sock;
    uint16_t     port;
    int          stop;
    int          thread_started;
    ms_thread_t  accept_thread;

    ms_mutex_t   resp_lock;
    ms_resp_t   *responses;

    ms_mutex_t   recv_lock;
    ms_cond_t    recv_cond;
    ms_msg_t    *msg_head;
    ms_msg_t    *msg_tail;

    ms_mutex_t   client_lock;
    ms_socket_t  clients[MS_MAX_CLIENTS];

    ms_mutex_t   handler_lock;
    int          handler_count;
    ms_cond_t    handler_done;
};

static int ms_wsa_init(void) {
#if defined(_WIN32)
    WSADATA d; return WSAStartup(MAKEWORD(2,2), &d) == 0 ? 0 : -1;
#else
    return 0;
#endif
}
static void ms_wsa_cleanup(void) {
#if defined(_WIN32)
    WSACleanup();
#endif
}

static void ms_pack_be32(uint8_t *b, uint32_t v) {
    b[0]=(uint8_t)((v>>24)&0xFF); b[1]=(uint8_t)((v>>16)&0xFF);
    b[2]=(uint8_t)((v>>8)&0xFF);  b[3]=(uint8_t)(v&0xFF);
}
static uint32_t ms_unpack_be32(const uint8_t *b){
    return ((uint32_t)b[0]<<24)|((uint32_t)b[1]<<16)|((uint32_t)b[2]<<8)|(uint32_t)b[3];
}

/* Encode header + payload into newly-allocated buffer; caller frees. */
static uint8_t *ms_encode(csm_packet_type_t type, const void *data, size_t len, size_t *out_len) {
    uint8_t *buf = (uint8_t *)malloc(MS_HEADER + len);
    if (!buf) return NULL;
    ms_pack_be32(buf, (uint32_t)len);
    buf[4] = MS_VER; buf[5] = (uint8_t)type; buf[6] = 0; buf[7] = 0;
    if (len) memcpy(buf + MS_HEADER, data, len);
    *out_len = MS_HEADER + len;
    return buf;
}

static int ms_send_all(ms_socket_t s, const uint8_t *buf, size_t len) {
    size_t total = 0;
#if defined(MSG_NOSIGNAL)
    int flags = MSG_NOSIGNAL;
#else
    int flags = 0;
#endif
    while (total < len) {
#if defined(_WIN32)
        int n = send(s, (const char *)buf + total, (int)(len - total), 0);
        (void)flags;
#else
        ssize_t n = send(s, buf + total, len - total, flags);
#endif
        if (n <= 0) return -1;
        total += (size_t)n;
    }
    return 0;
}

static int ms_recv_all(ms_socket_t s, uint8_t *buf, size_t len) {
    size_t total = 0;
    while (total < len) {
#if defined(_WIN32)
        int n = recv(s, (char *)buf + total, (int)(len - total), 0);
#else
        ssize_t n = recv(s, buf + total, len - total, 0);
#endif
        if (n <= 0) return -1;
        total += (size_t)n;
    }
    return 0;
}

/* --- Public API --- */

csm_mock_server_t *csm_mock_server_create(void) {
    if (ms_wsa_init() != 0) return NULL;
    csm_mock_server_t *s = (csm_mock_server_t *)calloc(1, sizeof(*s));
    if (!s) { ms_wsa_cleanup(); return NULL; }
    s->listen_sock = MS_INVALID_SOCKET;
    ms_mutex_init(&s->resp_lock);
    ms_mutex_init(&s->recv_lock);
    ms_cond_init(&s->recv_cond);
    ms_mutex_init(&s->client_lock);
    ms_mutex_init(&s->handler_lock);
    ms_cond_init(&s->handler_done);
    for (int i = 0; i < MS_MAX_CLIENTS; ++i) s->clients[i] = MS_INVALID_SOCKET;
    return s;
}

static void ms_handle_command(csm_mock_server_t *s, ms_socket_t conn,
                              const char *cmd) {
    /* Look up custom response. */
    ms_mutex_lock(&s->resp_lock);
    ms_resp_t *r = s->responses;
    while (r) {
        if (strcmp(r->cmd, cmd) == 0) {
            size_t out_len = 0;
            uint8_t *wire = ms_encode(r->type, r->data, r->data_len, &out_len);
            ms_mutex_unlock(&s->resp_lock);
            if (wire) { ms_send_all(conn, wire, out_len); free(wire); }
            return;
        }
        r = r->next;
    }
    ms_mutex_unlock(&s->resp_lock);

    /* Built-in defaults. */
    size_t out_len = 0;
    uint8_t *wire = NULL;
    if (strcmp(cmd, "Ping") == 0) {
        wire = ms_encode(CSM_PT_RESP, "Pong", 4, &out_len);
    } else if (strcmp(cmd, "List") == 0) {
        const char *txt = "AI\nDIO\nSystem";
        wire = ms_encode(CSM_PT_RESP, txt, strlen(txt), &out_len);
    } else if (strncmp(cmd, "List API ", 9) == 0) {
        char buf[256];
        snprintf(buf, sizeof(buf), "API: Start -> %s\nAPI: Stop -> %s",
                 cmd + 9, cmd + 9);
        wire = ms_encode(CSM_PT_RESP, buf, strlen(buf), &out_len);
    } else if (strncmp(cmd, "List State ", 11) == 0) {
        char buf[256];
        snprintf(buf, sizeof(buf), "Idle <- %s\nRunning <- %s",
                 cmd + 11, cmd + 11);
        wire = ms_encode(CSM_PT_RESP, buf, strlen(buf), &out_len);
    } else if (strstr(cmd, "-><register>") || strstr(cmd, "-><unregister>")) {
        wire = ms_encode(CSM_PT_CMD_RESP, "", 0, &out_len);
    } else {
        /* Generic async handshake. */
        wire = ms_encode(CSM_PT_CMD_RESP, "", 0, &out_len);
    }
    if (wire) { ms_send_all(conn, wire, out_len); free(wire); }
}

#if defined(_WIN32)
static unsigned __stdcall
#else
static void *
#endif
ms_client_thread(void *arg) {
    typedef struct { csm_mock_server_t *s; ms_socket_t conn; } ms_ctx_t;
    ms_ctx_t *ctx = (ms_ctx_t *)arg;
    csm_mock_server_t *s = ctx->s;
    ms_socket_t conn = ctx->conn;
    free(ctx);

    /* Send welcome INFO. */
    size_t wlen = 0;
    uint8_t *welcome = ms_encode(CSM_PT_INFO, "Welcome to mock server", 22, &wlen);
    if (welcome) { ms_send_all(conn, welcome, wlen); free(welcome); }

    while (!s->stop) {
        uint8_t hdr[MS_HEADER];
        if (ms_recv_all(conn, hdr, MS_HEADER) != 0) break;
        uint32_t data_len = ms_unpack_be32(hdr);
        uint8_t *body = NULL;
        if (data_len) {
            body = (uint8_t *)malloc(data_len);
            if (!body) break;
            if (ms_recv_all(conn, body, data_len) != 0) { free(body); break; }
        }
        if (hdr[5] == CSM_PT_CMD) {
            char *cmd = (char *)malloc(data_len + 1);
            if (cmd) {
                if (data_len) memcpy(cmd, body, data_len);
                cmd[data_len] = '\0';

                /* Trim trailing whitespace. */
                size_t L = strlen(cmd);
                while (L && (cmd[L-1]==' '||cmd[L-1]=='\r'||cmd[L-1]=='\n'||cmd[L-1]=='\t'))
                    cmd[--L] = '\0';

                /* Handle the command first (using the local copy). */
                ms_handle_command(s, conn, cmd);

                /* Then enqueue a copy for the test to inspect. */
                ms_msg_t *m = (ms_msg_t *)calloc(1, sizeof(*m));
                if (m) {
                    m->text = (char *)malloc(strlen(cmd) + 1);
                    if (m->text) {
                        strcpy(m->text, cmd);
                        ms_mutex_lock(&s->recv_lock);
                        if (s->msg_tail) s->msg_tail->next = m;
                        else             s->msg_head = m;
                        s->msg_tail = m;
                        ms_cond_signal(&s->recv_cond);
                        ms_mutex_unlock(&s->recv_lock);
                    } else {
                        free(m);
                    }
                }
                free(cmd);
            }
        }
        free(body);
    }

    /* Remove from clients list. */
    ms_mutex_lock(&s->client_lock);
    for (int i = 0; i < MS_MAX_CLIENTS; ++i) {
        if (s->clients[i] == conn) { s->clients[i] = MS_INVALID_SOCKET; break; }
    }
    ms_mutex_unlock(&s->client_lock);
    ms_close_socket(conn);

    ms_mutex_lock(&s->handler_lock);
    s->handler_count--;
    if (s->handler_count == 0) ms_cond_signal(&s->handler_done);
    ms_mutex_unlock(&s->handler_lock);
#if defined(_WIN32)
    return 0;
#else
    return NULL;
#endif
}

#if defined(_WIN32)
static unsigned __stdcall
#else
static void *
#endif
ms_accept_thread(void *arg) {
    csm_mock_server_t *s = (csm_mock_server_t *)arg;
    while (!s->stop) {
        fd_set rfds; FD_ZERO(&rfds); FD_SET(s->listen_sock, &rfds);
        struct timeval tv; tv.tv_sec = 0; tv.tv_usec = 200 * 1000;
        int sel = select((int)(s->listen_sock + 1), &rfds, NULL, NULL, &tv);
        if (sel <= 0) continue;
        struct sockaddr_in addr; socklen_t alen = sizeof(addr);
        ms_socket_t conn = accept(s->listen_sock, (struct sockaddr *)&addr, &alen);
        if (conn == MS_INVALID_SOCKET) continue;
        ms_mutex_lock(&s->client_lock);
        for (int i = 0; i < MS_MAX_CLIENTS; ++i) {
            if (s->clients[i] == MS_INVALID_SOCKET) { s->clients[i] = conn; break; }
        }
        ms_mutex_unlock(&s->client_lock);

        typedef struct { csm_mock_server_t *s; ms_socket_t conn; } ms_ctx_t;
        ms_ctx_t *ctx = (ms_ctx_t *)malloc(sizeof(*ctx));
        if (!ctx) { ms_close_socket(conn); continue; }
        ctx->s = s; ctx->conn = conn;

        ms_mutex_lock(&s->handler_lock);
        s->handler_count++;
        ms_mutex_unlock(&s->handler_lock);

#if defined(_WIN32)
        HANDLE t = (HANDLE)_beginthreadex(NULL, 0, ms_client_thread, ctx, 0, NULL);
        if (t) CloseHandle(t);
        else {
            free(ctx); ms_close_socket(conn);
            ms_mutex_lock(&s->handler_lock); s->handler_count--; ms_mutex_unlock(&s->handler_lock);
        }
#else
        pthread_t t;
        if (pthread_create(&t, NULL, ms_client_thread, ctx) == 0) {
            pthread_detach(t);
        } else {
            free(ctx); ms_close_socket(conn);
            ms_mutex_lock(&s->handler_lock); s->handler_count--; ms_mutex_unlock(&s->handler_lock);
        }
#endif
    }
#if defined(_WIN32)
    return 0;
#else
    return NULL;
#endif
}

int csm_mock_server_start(csm_mock_server_t *s) {
    if (!s) return -1;
    s->listen_sock = socket(AF_INET, SOCK_STREAM, 0);
    if (s->listen_sock == MS_INVALID_SOCKET) return -1;
    int yes = 1;
    setsockopt(s->listen_sock, SOL_SOCKET, SO_REUSEADDR, (const char *)&yes, sizeof(yes));
    struct sockaddr_in a; memset(&a, 0, sizeof(a));
    a.sin_family = AF_INET;
    a.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    a.sin_port = 0;
    if (bind(s->listen_sock, (struct sockaddr *)&a, sizeof(a)) != 0) {
        ms_close_socket(s->listen_sock); s->listen_sock = MS_INVALID_SOCKET; return -1;
    }
    socklen_t alen = sizeof(a);
    getsockname(s->listen_sock, (struct sockaddr *)&a, &alen);
    s->port = ntohs(a.sin_port);
    if (listen(s->listen_sock, 8) != 0) {
        ms_close_socket(s->listen_sock); s->listen_sock = MS_INVALID_SOCKET; return -1;
    }
    s->stop = 0;
#if defined(_WIN32)
    s->accept_thread = (HANDLE)_beginthreadex(NULL, 0, ms_accept_thread, s, 0, NULL);
    if (s->accept_thread == NULL) return -1;
#else
    if (pthread_create(&s->accept_thread, NULL, ms_accept_thread, s) != 0) return -1;
#endif
    s->thread_started = 1;
    return 0;
}

void csm_mock_server_stop(csm_mock_server_t *s) {
    if (!s) return;
    s->stop = 1;
    if (s->listen_sock != MS_INVALID_SOCKET) {
#if defined(_WIN32)
        shutdown(s->listen_sock, SD_BOTH);
#else
        shutdown(s->listen_sock, SHUT_RDWR);
#endif
        ms_close_socket(s->listen_sock);
        s->listen_sock = MS_INVALID_SOCKET;
    }
    /* Close client sockets to wake handlers. */
    ms_mutex_lock(&s->client_lock);
    for (int i = 0; i < MS_MAX_CLIENTS; ++i) {
        if (s->clients[i] != MS_INVALID_SOCKET) {
#if defined(_WIN32)
            shutdown(s->clients[i], SD_BOTH);
#else
            shutdown(s->clients[i], SHUT_RDWR);
#endif
            ms_close_socket(s->clients[i]);
            s->clients[i] = MS_INVALID_SOCKET;
        }
    }
    ms_mutex_unlock(&s->client_lock);

    if (s->thread_started) {
#if defined(_WIN32)
        WaitForSingleObject(s->accept_thread, 2000);
        CloseHandle(s->accept_thread);
#else
        pthread_join(s->accept_thread, NULL);
#endif
        s->thread_started = 0;
    }
    /* Wait for handlers to finish (best-effort, brief). */
    ms_mutex_lock(&s->handler_lock);
    int waited = 0;
    while (s->handler_count > 0 && waited < 20) {
        ms_cond_wait_ms(&s->handler_done, &s->handler_lock, 100);
        waited++;
    }
    ms_mutex_unlock(&s->handler_lock);
}

void csm_mock_server_destroy(csm_mock_server_t *s) {
    if (!s) return;
    csm_mock_server_stop(s);
    ms_mutex_lock(&s->resp_lock);
    ms_resp_t *r = s->responses;
    while (r) { ms_resp_t *n = r->next; free(r->cmd); free(r->data); free(r); r = n; }
    s->responses = NULL;
    ms_mutex_unlock(&s->resp_lock);

    ms_mutex_lock(&s->recv_lock);
    ms_msg_t *m = s->msg_head;
    while (m) { ms_msg_t *n = m->next; free(m->text); free(m); m = n; }
    s->msg_head = s->msg_tail = NULL;
    ms_mutex_unlock(&s->recv_lock);

    ms_mutex_destroy(&s->resp_lock);
    ms_mutex_destroy(&s->recv_lock);
    ms_cond_destroy(&s->recv_cond);
    ms_mutex_destroy(&s->client_lock);
    ms_mutex_destroy(&s->handler_lock);
    ms_cond_destroy(&s->handler_done);
    free(s);
    ms_wsa_cleanup();
}

uint16_t csm_mock_server_port(const csm_mock_server_t *s) {
    return s ? s->port : 0;
}

static void ms_set_response_typed(csm_mock_server_t *s, const char *cmd_text,
                                  csm_packet_type_t type, const char *data) {
    ms_resp_t *r = (ms_resp_t *)calloc(1, sizeof(*r));
    if (!r) return;
    r->cmd = (char *)malloc(strlen(cmd_text) + 1);
    if (!r->cmd) {
        free(r);
        return;
    }
    strcpy(r->cmd, cmd_text);
    r->type = type;
    size_t dl = strlen(data);
    if (dl > 0) {
        r->data = (uint8_t *)malloc(dl);
        if (!r->data) {
            free(r->cmd);
            free(r);
            return;
        }
        memcpy(r->data, data, dl);
    }
    r->data_len = dl;
    ms_mutex_lock(&s->resp_lock);
    r->next = s->responses;
    s->responses = r;
    ms_mutex_unlock(&s->resp_lock);
}

void csm_mock_server_set_response(csm_mock_server_t *s, const char *cmd, const char *resp) {
    ms_set_response_typed(s, cmd, CSM_PT_RESP, resp);
}
void csm_mock_server_set_error_response(csm_mock_server_t *s, const char *cmd, const char *err) {
    ms_set_response_typed(s, cmd, CSM_PT_ERROR, err);
}

void csm_mock_server_push_status(csm_mock_server_t *s, const char *payload) {
    if (!s || !payload) return;
    size_t len = 0;
    uint8_t *wire = ms_encode(CSM_PT_STATUS, payload, strlen(payload), &len);
    if (!wire) return;
    ms_mutex_lock(&s->client_lock);
    for (int i = 0; i < MS_MAX_CLIENTS; ++i) {
        if (s->clients[i] != MS_INVALID_SOCKET) {
            ms_send_all(s->clients[i], wire, len);
        }
    }
    ms_mutex_unlock(&s->client_lock);
    free(wire);
}

char *csm_mock_server_get_received(csm_mock_server_t *s, unsigned int timeout_ms) {
    if (!s) return NULL;
    ms_mutex_lock(&s->recv_lock);
    if (!s->msg_head) {
        ms_cond_wait_ms(&s->recv_cond, &s->recv_lock, timeout_ms);
    }
    char *out = NULL;
    if (s->msg_head) {
        ms_msg_t *m = s->msg_head;
        s->msg_head = m->next;
        if (!s->msg_head) s->msg_tail = NULL;
        out = m->text;
        free(m);
    }
    ms_mutex_unlock(&s->recv_lock);
    return out;
}
