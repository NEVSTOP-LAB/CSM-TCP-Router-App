/* basic_usage.c - Demonstrates connecting, pinging, listing modules, and
 * sending a synchronous command. Mirrors examples/basic_usage.py. */
#include "csm_tcp_router_client.h"

#include <stdio.h>
#include <stdlib.h>

#define HOST "localhost"
#define PORT 30007

int main(void) {
    /* 1. Wait until the server is ready (optional). */
    printf("Waiting for server ... ");
    fflush(stdout);
    csm_result_t r = csm_client_wait_for_server(HOST, PORT, 30000, 500);
    if (r != CSM_OK) {
        printf("TIMEOUT - server did not start within 30s.\n");
        return 1;
    }
    printf("ready.\n");

    /* 2. Create + connect. */
    csm_client_t *c = csm_client_create();
    if (!c) { fprintf(stderr, "Out of memory\n"); return 1; }

    r = csm_client_connect(c, HOST, PORT, 5000);
    if (r != CSM_OK) {
        fprintf(stderr, "Connection failed: %s\n", csm_result_str(r));
        csm_client_destroy(c);
        return 1;
    }
    printf("Connected to %s:%d\n", HOST, PORT);

    /* 3. Ping. */
    double ms = 0;
    r = csm_client_ping(c, 2000, &ms);
    if (r == CSM_OK) printf("Ping OK  latency=%.1f ms\n", ms);
    else             printf("Ping failed: %s\n", csm_result_str(r));

    /* 4. List CSM modules. */
    char *modules = NULL;
    if (csm_client_list_modules(c, &modules, 5000) == CSM_OK) {
        printf("\nLoaded modules:\n%s\n", modules);
        csm_string_free(modules);
    }

    /* 5. Send a synchronous command (uncomment when wired to a real module).
     *
     *  csm_command_response_t resp = {0};
     *  if (csm_client_send_and_wait(c, "API: Read -@ DAQmx", 5000, &resp) == CSM_OK) {
     *      printf("Sync response: %s\n", (char *)resp.raw);
     *  }
     *  csm_command_response_dispose(&resp);
     */

    /* 6. Disconnect & clean up. */
    csm_client_disconnect(c);
    csm_client_destroy(c);
    printf("Disconnected.\n");
    return 0;
}
