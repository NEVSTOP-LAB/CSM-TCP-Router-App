/* mock_server.h - In-process TCP server emulating a CSM-TCP-Router for tests.
 *
 * Mirrors the Python `tests/conftest.py` MockServer fixture.
 */
#ifndef CSM_MOCK_SERVER_H
#define CSM_MOCK_SERVER_H

#include "csm_tcp_router_client.h"

#include <stdint.h>

typedef struct csm_mock_server csm_mock_server_t;

/** Create a stopped mock server bound to 127.0.0.1; the actual port is
 * assigned by the OS in csm_mock_server_start(). */
csm_mock_server_t *csm_mock_server_create(void);

/** Free a (running or stopped) mock server. */
void csm_mock_server_destroy(csm_mock_server_t *s);

/** Bind to 127.0.0.1, an ephemeral port, and start the accept thread. */
int csm_mock_server_start(csm_mock_server_t *s);

/** Stop the accept thread and close all client connections. */
void csm_mock_server_stop(csm_mock_server_t *s);

/** Return the port the server is listening on (valid after start()). */
uint16_t csm_mock_server_port(const csm_mock_server_t *s);

/** Register a custom RESP reply for an exact command string. */
void csm_mock_server_set_response(csm_mock_server_t *s,
                                  const char *cmd_text,
                                  const char *resp_text);

/** Register an ERROR reply for an exact command string. */
void csm_mock_server_set_error_response(csm_mock_server_t *s,
                                        const char *cmd_text,
                                        const char *error_text);

/** Push a STATUS packet to all currently connected clients. */
void csm_mock_server_push_status(csm_mock_server_t *s, const char *payload);

/** Pop the next received command, blocking up to *timeout_ms*. The returned
 * string is owned by the caller and must be freed with csm_string_free.
 * Returns NULL on timeout. */
char *csm_mock_server_get_received(csm_mock_server_t *s, unsigned int timeout_ms);

#endif /* CSM_MOCK_SERVER_H */
