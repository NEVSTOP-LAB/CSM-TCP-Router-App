/* test_main.c - Runner for the C SDK test suite. */
#include "test_harness.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

jmp_buf csm_test_jmp;
int     csm_test_failed     = 0;
int     csm_test_assertions = 0;

/* --- test_protocol.c --- */
CSM_TEST_EXTERN(test_header_size_constant);
CSM_TEST_EXTERN(test_encode_decode_roundtrip);
CSM_TEST_EXTERN(test_encode_empty_payload);
CSM_TEST_EXTERN(test_encode_buffer_too_small);
CSM_TEST_EXTERN(test_decode_header_bad_size);
CSM_TEST_EXTERN(test_parse_packet_unknown_type_maps_to_info);
CSM_TEST_EXTERN(test_parse_packet_length_mismatch);
CSM_TEST_EXTERN(test_result_str_known);

/* --- test_client.c --- */
CSM_TEST_EXTERN(test_client_create_destroy);
CSM_TEST_EXTERN(test_destroy_null_safe);
CSM_TEST_EXTERN(test_send_when_not_connected_returns_connection_error);
CSM_TEST_EXTERN(test_invalid_args_rejected);
CSM_TEST_EXTERN(test_wait_for_server_unreachable_times_out);
CSM_TEST_EXTERN(test_connect_unreachable_returns_connection_error);

/* --- test_integration.c --- */
CSM_TEST_EXTERN(it_ping);
CSM_TEST_EXTERN(it_list_modules);
CSM_TEST_EXTERN(it_list_api_includes_module_name);
CSM_TEST_EXTERN(it_send_and_wait_custom_response);
CSM_TEST_EXTERN(it_server_error_propagated);
CSM_TEST_EXTERN(it_post_async_handshake);
CSM_TEST_EXTERN(it_subscribe_status_invokes_callback);
CSM_TEST_EXTERN(it_disconnect_unblocks_waiters);

static const csm_test_t TESTS[] = {
    {"test_header_size_constant",                       test_header_size_constant},
    {"test_encode_decode_roundtrip",                    test_encode_decode_roundtrip},
    {"test_encode_empty_payload",                       test_encode_empty_payload},
    {"test_encode_buffer_too_small",                    test_encode_buffer_too_small},
    {"test_decode_header_bad_size",                     test_decode_header_bad_size},
    {"test_parse_packet_unknown_type_maps_to_info",     test_parse_packet_unknown_type_maps_to_info},
    {"test_parse_packet_length_mismatch",               test_parse_packet_length_mismatch},
    {"test_result_str_known",                           test_result_str_known},
    {"test_client_create_destroy",                      test_client_create_destroy},
    {"test_destroy_null_safe",                          test_destroy_null_safe},
    {"test_send_when_not_connected_returns_connection_error", test_send_when_not_connected_returns_connection_error},
    {"test_invalid_args_rejected",                      test_invalid_args_rejected},
    {"test_wait_for_server_unreachable_times_out",      test_wait_for_server_unreachable_times_out},
    {"test_connect_unreachable_returns_connection_error", test_connect_unreachable_returns_connection_error},
    {"it_ping",                                         it_ping},
    {"it_list_modules",                                 it_list_modules},
    {"it_list_api_includes_module_name",                it_list_api_includes_module_name},
    {"it_send_and_wait_custom_response",                it_send_and_wait_custom_response},
    {"it_server_error_propagated",                      it_server_error_propagated},
    {"it_post_async_handshake",                         it_post_async_handshake},
    {"it_subscribe_status_invokes_callback",            it_subscribe_status_invokes_callback},
    {"it_disconnect_unblocks_waiters",                  it_disconnect_unblocks_waiters},
};

int main(int argc, char **argv) {
    const char *only = (argc > 1) ? argv[1] : NULL;
    /* `volatile` ensures these survive the longjmp performed by failing
     * assertions inside individual test bodies. */
    volatile int passed = 0, failed = 0, skipped = 0;
    volatile int total_assertions = 0;
    size_t n = sizeof(TESTS) / sizeof(TESTS[0]);
    volatile size_t i = 0;
    for (; i < n; ++i) {
        if (only && strcmp(only, TESTS[i].name) != 0) { skipped++; continue; }
        printf("[RUN ] %s\n", TESTS[i].name);
        csm_test_failed = 0;
        int before = csm_test_assertions;
        if (setjmp(csm_test_jmp) == 0) {
            TESTS[i].fn();
        }
        int delta = csm_test_assertions - before;
        total_assertions += delta;
        if (csm_test_failed) {
            printf("[FAIL] %s (%d asserts)\n", TESTS[i].name, delta);
            failed++;
        } else {
            printf("[ OK ] %s (%d asserts)\n", TESTS[i].name, delta);
            passed++;
        }
    }
    printf("\nResults: %d passed, %d failed, %d skipped (%d assertions)\n",
           passed, failed, skipped, total_assertions);
    return failed == 0 ? 0 : 1;
}
