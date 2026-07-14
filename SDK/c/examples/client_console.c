/* client_console.c - 交互式客户端控制台示例。
 *
 * 连接到正在运行的 CSM-TCP-Router 服务器，从 stdin 读取用户输入的
 * 命令，并通过 SDK 转发。同样的命令集、提示符和输出格式也在
 * Python（examples/client_console.py）和 C#（examples/ClientConsole）
 * SDK 示例中实现，因此三种语言的行为一致。
 *
 * 用法:
 *     client_console [host] [port]
 */
#include "csm_tcp_router_client.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DEFAULT_HOST "localhost"
#define DEFAULT_PORT 30007
#define LINE_BUFFER_SIZE 4096

static const char *HELP_TEXT =
    "Available commands:\n"
    "  help                 Show this help text\n"
    "  quit / exit          Disconnect and exit\n"
    "  ping                 Measure round-trip latency\n"
    "  list                 List CSM modules loaded on the server\n"
    "  api <module>         List the API of a module\n"
    "  state <module>       List the states of a module\n"
    "  mhelp <module>       Server-side Help for a module\n"
    "  send <command>       Send a synchronous command and print the response\n"
    "  post <command>       Send an asynchronous command (-> suffix)\n"
    "  nopost <command>     Send a no-reply asynchronous command (->|)\n"
    "  sub <status>@<mod>   Subscribe to a status broadcast\n"
    "  unsub <status>@<mod> Unsubscribe from a status broadcast";

static void on_status(const csm_status_notification_t *n, void *ud) {
    (void)ud;
    printf("\n[STATUS] %s@%s: %s\n",
           n->status_name ? n->status_name : "",
           n->module_name ? n->module_name : "",
           n->data ? n->data : "");
}

static void on_async(const csm_async_response_t *r, void *ud) {
    (void)ud;
    printf("\n[ASYNC] %s  (cmd=%s)\n",
           r->raw ? r->raw : "",
           r->original_command ? r->original_command : "");
}

/* 修剪 *s* 的前后空白字符（包括换行）。原地修改。返回 *s*。 */
static char *trim(char *s) {
    if (!s) return s;
    char *end;
    while (*s == ' ' || *s == '\t' || *s == '\r' || *s == '\n') s++;
    end = s + strlen(s);
    while (end > s && (end[-1] == ' ' || end[-1] == '\t' ||
                       end[-1] == '\r' || end[-1] == '\n')) {
        end--;
    }
    *end = '\0';
    return s;
}

/* 不区分大小写地比较两个 NUL 终止字符串。 */
static int ieq(const char *a, const char *b) {
    while (*a && *b) {
        char ca = *a, cb = *b;
        if (ca >= 'A' && ca <= 'Z') ca = (char)(ca - 'A' + 'a');
        if (cb >= 'A' && cb <= 'Z') cb = (char)(cb - 'A' + 'a');
        if (ca != cb) return 0;
        a++; b++;
    }
    return *a == '\0' && *b == '\0';
}

/* 在 *line* 上拆分 "<status>@<module>"，将指针存入 out_status/out_module
 * （指向 *line* 内的位置，*line* 会被原地修改）。失败时返回 0。 */
static int split_status_module(char *line, char **out_status, char **out_module) {
    char *at = strchr(line, '@');
    if (!at) return 0;
    *at = '\0';
    char *status = trim(line);
    char *module = trim(at + 1);
    if (*status == '\0' || *module == '\0') return 0;
    *out_status = status;
    *out_module = module;
    return 1;
}

/* 打印来自服务器的最近一次错误（如果有）。 */
static void print_error(csm_client_t *c, csm_result_t r) {
    csm_server_error_t err;
    if (r == CSM_ERR_SERVER && csm_client_last_server_error(c, &err) == CSM_OK) {
        if (err.code[0]) {
            printf("Error: [%s] %s\n", err.code, err.message);
        } else {
            printf("Error: %s\n", err.message);
        }
    } else {
        printf("Error: %s\n", csm_result_str(r));
    }
}

/* 调用一个返回字符串的 SDK 辅助函数并打印结果。 */
static void print_string_command(csm_client_t *c, csm_result_t r, char *text) {
    if (r == CSM_OK) {
        printf("%s\n", text ? text : "");
        csm_string_free(text);
    } else {
        print_error(c, r);
    }
}

/* 处理一行输入。返回 0 表示退出，否则返回 1。 */
static int dispatch(csm_client_t *c, char *line) {
    line = trim(line);
    if (*line == '\0') return 1;

    /* 将命令字与参数拆分（参数保留空格）。 */
    char *cmd = line;
    char *arg = strpbrk(line, " \t");
    if (arg) {
        *arg++ = '\0';
        arg = trim(arg);
    } else {
        arg = (char *)"";
    }

    if (ieq(cmd, "quit") || ieq(cmd, "exit")) {
        return 0;
    }
    if (ieq(cmd, "help")) {
        printf("%s\n", HELP_TEXT);
        return 1;
    }
    if (ieq(cmd, "ping")) {
        double ms = 0;
        csm_result_t r = csm_client_ping(c, 2000, &ms);
        if (r == CSM_OK) printf("Ping OK  latency=%.1f ms\n", ms);
        else             printf("Ping failed.\n");
        return 1;
    }
    if (ieq(cmd, "list")) {
        char *text = NULL;
        print_string_command(c, csm_client_list_modules(c, &text, 5000), text);
        return 1;
    }
    if (ieq(cmd, "api")) {
        if (*arg == '\0') { printf("Error: usage: api <module>\n"); return 1; }
        char *text = NULL;
        print_string_command(c, csm_client_list_api(c, arg, &text, 5000), text);
        return 1;
    }
    if (ieq(cmd, "state")) {
        if (*arg == '\0') { printf("Error: usage: state <module>\n"); return 1; }
        char *text = NULL;
        print_string_command(c, csm_client_list_states(c, arg, &text, 5000), text);
        return 1;
    }
    if (ieq(cmd, "mhelp")) {
        if (*arg == '\0') { printf("Error: usage: mhelp <module>\n"); return 1; }
        char *text = NULL;
        print_string_command(c, csm_client_help(c, arg, &text, 5000), text);
        return 1;
    }
    if (ieq(cmd, "send")) {
        if (*arg == '\0') { printf("Error: usage: send <command>\n"); return 1; }
        csm_command_response_t resp = {0};
        csm_result_t r = csm_client_send_and_wait(c, arg, 5000, &resp);
        if (r == CSM_OK) {
            printf("Response: %s\n", resp.raw ? (const char *)resp.raw : "");
            csm_command_response_dispose(&resp);
        } else {
            print_error(c, r);
        }
        return 1;
    }
    if (ieq(cmd, "post")) {
        if (*arg == '\0') { printf("Error: usage: post <command>\n"); return 1; }
        csm_client_register_async_callback(c, arg, on_async, NULL);
        csm_result_t r = csm_client_post(c, arg, 5000);
        if (r == CSM_OK) printf("Async command sent.\n");
        else             print_error(c, r);
        return 1;
    }
    if (ieq(cmd, "nopost")) {
        if (*arg == '\0') { printf("Error: usage: nopost <command>\n"); return 1; }
        csm_result_t r = csm_client_post_no_reply(c, arg, 5000);
        if (r == CSM_OK) printf("No-reply command sent.\n");
        else             print_error(c, r);
        return 1;
    }
    if (ieq(cmd, "sub")) {
        char *status = NULL, *module = NULL;
        if (!split_status_module(arg, &status, &module)) {
            printf("Error: expected '<status>@<module>'\n");
            return 1;
        }
        csm_result_t r = csm_client_subscribe_status(c, status, module,
                                                     on_status, NULL, 5000);
        if (r == CSM_OK) printf("Subscribed to %s@%s\n", status, module);
        else             print_error(c, r);
        return 1;
    }
    if (ieq(cmd, "unsub")) {
        char *status = NULL, *module = NULL;
        if (!split_status_module(arg, &status, &module)) {
            printf("Error: expected '<status>@<module>'\n");
            return 1;
        }
        csm_result_t r = csm_client_unsubscribe_status(c, status, module, 5000);
        if (r == CSM_OK) printf("Unsubscribed from %s@%s\n", status, module);
        else             print_error(c, r);
        return 1;
    }

    printf("Error: unknown command '%s'.  Type 'help' for the command list.\n",
           cmd);
    return 1;
}

int main(int argc, char **argv) {
    const char *host = (argc > 1) ? argv[1] : DEFAULT_HOST;
    int         port = (argc > 2) ? atoi(argv[2]) : DEFAULT_PORT;
    if (port <= 0 || port > 65535) {
        fprintf(stderr, "Error: invalid port '%s'\n", argv[2]);
        return 1;
    }

    printf("CSM-TCP-Router Client Console\n");
    printf("Connecting to %s:%d ...\n", host, port);

    csm_client_t *c = csm_client_create();
    if (!c) {
        fprintf(stderr, "Out of memory\n");
        return 1;
    }

    csm_result_t r = csm_client_connect(c, host, (uint16_t)port, 5000);
    if (r != CSM_OK) {
        printf("Error: %s\n", csm_result_str(r));
        csm_client_destroy(c);
        return 1;
    }

    printf("Connected to %s:%d.  Type 'help' for commands, 'quit' to exit.\n",
           host, port);

    char line[LINE_BUFFER_SIZE];
    for (;;) {
        printf("csm> ");
        fflush(stdout);
        if (!fgets(line, (int)sizeof(line), stdin)) {
            printf("\n");
            break;
        }
        if (!dispatch(c, line)) break;
    }

    csm_client_disconnect(c);
    csm_client_destroy(c);
    printf("Disconnected.\n");
    return 0;
}
