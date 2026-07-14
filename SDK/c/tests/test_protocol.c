/* test_protocol.c - 协议编解码的单元测试。 */
#include "csm_tcp_router_client.h"
#include "test_harness.h"

#include <string.h>

CSM_TEST(test_header_size_constant) {
    CSM_ASSERT_EQ_INT(CSM_HEADER_SIZE, 8);
    CSM_ASSERT_EQ_INT(CSM_PROTOCOL_VERSION, 0x01);
}

CSM_TEST(test_encode_decode_roundtrip) {
    const char *payload = "Hello";
    uint8_t buf[64];
    size_t out_len = 0;
    csm_result_t r = csm_encode_packet(payload, 5, CSM_PT_CMD, 0, 0,
                                       buf, sizeof(buf), &out_len);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_INT(out_len, 8 + 5);

    /* 头部字节：大端序长度、版本、类型、flag1、flag2。 */
    CSM_ASSERT_EQ_INT(buf[0], 0);
    CSM_ASSERT_EQ_INT(buf[1], 0);
    CSM_ASSERT_EQ_INT(buf[2], 0);
    CSM_ASSERT_EQ_INT(buf[3], 5);
    CSM_ASSERT_EQ_INT(buf[4], CSM_PROTOCOL_VERSION);
    CSM_ASSERT_EQ_INT(buf[5], CSM_PT_CMD);
    CSM_ASSERT(memcmp(buf + 8, "Hello", 5) == 0);

    uint32_t len; uint8_t ver, type, f1, f2;
    r = csm_decode_header(buf, 8, &len, &ver, &type, &f1, &f2);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_INT(len, 5);
    CSM_ASSERT_EQ_INT(ver, 1);
    CSM_ASSERT_EQ_INT(type, CSM_PT_CMD);
}

CSM_TEST(test_encode_empty_payload) {
    uint8_t buf[16];
    size_t out_len = 0;
    csm_result_t r = csm_encode_packet(NULL, 0, CSM_PT_INFO, 0, 0,
                                       buf, sizeof(buf), &out_len);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_INT(out_len, 8);
}

CSM_TEST(test_encode_buffer_too_small) {
    uint8_t buf[4];
    size_t out_len = 0;
    csm_result_t r = csm_encode_packet("X", 1, CSM_PT_CMD, 0, 0,
                                       buf, sizeof(buf), &out_len);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_INVALID);
}

CSM_TEST(test_decode_header_bad_size) {
    uint8_t hdr[4] = {0};
    csm_result_t r = csm_decode_header(hdr, 4, NULL, NULL, NULL, NULL, NULL);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_PROTOCOL);
}

CSM_TEST(test_parse_packet_unknown_type_maps_to_info) {
    uint8_t hdr[8] = {0,0,0,3, 0x01, 0xFE /* unknown */, 0, 0};
    uint8_t body[3] = {'a','b','c'};
    csm_packet_t pkt = {0};
    csm_result_t r = csm_parse_packet(hdr, 8, body, 3, &pkt);
    CSM_ASSERT_EQ_INT(r, CSM_OK);
    CSM_ASSERT_EQ_INT(pkt.type, CSM_PT_INFO);
    CSM_ASSERT_EQ_INT(pkt.data_len, 3);
    CSM_ASSERT(memcmp(pkt.data, "abc", 3) == 0);
    csm_packet_dispose(&pkt);
}

CSM_TEST(test_parse_packet_length_mismatch) {
    uint8_t hdr[8] = {0,0,0,5, 0x01, CSM_PT_RESP, 0, 0};
    uint8_t body[3] = {'a','b','c'};
    csm_packet_t pkt = {0};
    csm_result_t r = csm_parse_packet(hdr, 8, body, 3, &pkt);
    CSM_ASSERT_EQ_INT(r, CSM_ERR_PROTOCOL);
}

CSM_TEST(test_result_str_known) {
    CSM_ASSERT(strcmp(csm_result_str(CSM_OK), "OK") == 0);
    CSM_ASSERT(strstr(csm_result_str(CSM_ERR_TIMEOUT), "imeout") != NULL);
}
