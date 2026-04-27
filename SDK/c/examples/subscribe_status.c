/* subscribe_status.c - Demonstrates real-time status subscription with a
 * callback, mirroring examples/subscribe_status.py. */

#if !defined(_WIN32)
#  ifndef _POSIX_C_SOURCE
#    define _POSIX_C_SOURCE 200809L
#  endif
#endif

#include "csm_tcp_router_client.h"

#include <stdio.h>
#include <stdlib.h>

#if defined(_WIN32)
#  include <windows.h>
static void sleep_ms(unsigned int ms){ Sleep(ms); }
#else
#  include <time.h>
static void sleep_ms(unsigned int ms){
    struct timespec ts; ts.tv_sec=ms/1000; ts.tv_nsec=(long)(ms%1000)*1000000L;
    nanosleep(&ts, NULL);
}
#endif

#define HOST "localhost"
#define PORT 30007

static void on_status(const csm_status_notification_t *n, void *ud) {
    (void)ud;
    printf("[%s @ %s] %s\n", n->status_name, n->module_name, n->data);
}

int main(int argc, char **argv) {
    const char *status_name = (argc > 1) ? argv[1] : "Status";
    const char *module_name = (argc > 2) ? argv[2] : "DAQmx";

    csm_client_t *c = csm_client_create();
    if (!c) return 1;

    csm_result_t r = csm_client_connect(c, HOST, PORT, 5000);
    if (r != CSM_OK) {
        fprintf(stderr, "Connection failed: %s\n", csm_result_str(r));
        csm_client_destroy(c);
        return 1;
    }

    r = csm_client_subscribe_status(c, status_name, module_name,
                                    on_status, NULL, 5000);
    if (r != CSM_OK) {
        fprintf(stderr, "Subscribe failed: %s\n", csm_result_str(r));
        csm_client_disconnect(c);
        csm_client_destroy(c);
        return 1;
    }

    printf("Subscribed to %s@%s. Listening for 30s ...\n",
           status_name, module_name);
    sleep_ms(30000);

    csm_client_unsubscribe_status(c, status_name, module_name, 5000);
    csm_client_disconnect(c);
    csm_client_destroy(c);
    return 0;
}
