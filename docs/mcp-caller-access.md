# MCP 声明用户名准入与使用统计

本部署方案面向公司内网。客户端 skill 获取本机登录用户名，并在每次 MCP 业务工具调用中传入 `caller_user`；服务端用 YAML 白名单判断是否放行，并将用户、工具、结果和耗时写入现有 `McpUsageLogs`。它不使用 Token、OAuth 或自定义 Authorization Header，也不要求已安装用户修改 Agent MCP 配置。

`caller_user` 是声明式身份，只解决非恶意场景下的登记、权限和统计，不证明请求者真实身份。不要把这一方案暴露到公网或替代强认证。

## 白名单

先从示例生成仅保存在部署机 `data/` 下的真实名单：

```bash
cp docs/mcp-access.yaml.example data/mcp-access.yaml
chmod 600 data/mcp-access.yaml
# 编辑 users，删除示例项并登记真实用户
```

格式如下：

```yaml
version: 1
users:
  zhangsan:
    enabled: true
    aliases:
      - zhangsan
      - YOUDAO\zhangsan
      - zhangsan@youdao.com
    services:
      - wiki
      - datasheet
```

- YAML key 是统计使用的 canonical user。
- `aliases` 登记 Linux/macOS、Windows/WSL 或邮箱环境可能返回的名字；匹配不区分大小写。
- `enabled: false` 停用用户。
- `services` 必须包含 `wiki` 才能调用 OpenDeepWiki MCP。
- 名单按文件修改时间和大小热重载。更新时先写临时文件再原子替换，避免读到半份 YAML。
- `data/mcp-access.yaml` 包含内部人员信息，不要提交 Git。

## 环境变量

Compose 已映射以下配置：

```text
MCP_ACCESS_FILE=/data/mcp-access.yaml
MCP_REQUIRE_CALLER_USER=false
MCP_USAGE_RETENTION_DAYS=180
MCP_USAGE_FALLBACK_FILE=/data/mcp-usage-fallback.jsonl
MCP_USAGE_FALLBACK_MAX_BYTES=10485760
MCP_USAGE_FALLBACK_BACKUP_COUNT=12
FORWARDED_HEADERS_TRUSTED_NETWORK=192.168.0.0/20
```

`MCP_REQUIRE_CALLER_USER` 在进程启动时读取，修改后需要重启后端：

- `false`：兼容迁移模式。缺少 `caller_user` 的旧调用允许执行，记为 `legacy_anonymous`；提供了用户名的调用仍严格检查白名单，未知用户不会放行。
- `true`：严格模式。所有业务 `tools/call` 都必须提供用户名，缺少时返回 `CALLER_USER_REQUIRED`。

`initialize` 和 `tools/list` 始终不要求用户名，因此客户端仍可完成连接和工具发现。
严格模式下，`tools/list` 返回的所有业务工具 schema 会同时把 `caller_user` 标为 required；兼容模式下仍保持 optional，供旧客户端继续调用。

`FORWARDED_HEADERS_TRUSTED_NETWORK` 必须与 Web 反向代理所在的 Compose bridge 网段一致。只有该网段发送的最后一跳 `X-Forwarded-For` 会被信任，其他来源伪造的转发头会被忽略。部署前可用 `docker network inspect multica_selfhost_default` 核对实际 Subnet；不一致时通过环境变量覆盖，不能扩大成整个公司网段或 `0.0.0.0/0`。

## 日志可靠性与保留

正常使用日志写入数据库。数据库瞬时失败时会重试三次；仍失败则把同一个带唯一 ID 的事件写入 `mcp-usage-fallback.jsonl`，业务工具保持原来的成功或失败结果，不会被统计故障覆盖。后台任务每小时自动重放回退事件，按 ID 去重；存在待重放文件时 `/health` 返回 `503 degraded` 并给出待处理文件数和字节数。无法解析的行会连同原文件隔离为 `.invalid-*`，`quarantinedFiles` 会持续非零，需人工检查并留档或修复后才能恢复健康状态。

原始 MCP 使用日志和每日汇总默认保留 180 天，后台以硬删除方式清理；`MCP_USAGE_RETENTION_DAYS=0` 可禁用自动清理。持久化回退文件每 10 MiB 轮转，默认最多 12 份。回退文件和数据库都包含内部使用信息，权限必须为 `0600`。

Compose 后端以 `OPENDEEPWIKI_UID:GID` 指定的非 root 用户运行并丢弃 Linux capabilities。本机部署在 `.env` 中设置实际 UID/GID；从旧 root 容器迁移时，必须在停服和备份后，把 `data/` 中旧容器创建的文件调整为该 UID/GID 可读写，再重建服务。数据库、WAL、SHM 和备份文件统一执行 `chmod 600`，白名单同样保持 `0600`。非 root 容器不能写入镜像内默认的 `/app/logs`，因此 Compose 通过 `Logging__LogDirectory=/data/logs` 将滚动错误日志一并放在可写数据卷上。

Web 容器是唯一对外的 Wiki/MCP 入口。它在 Next.js 处理请求前用 TCP socket 看到的 `remoteAddress` 覆盖客户端传入的 `Forwarded`/`X-Forwarded-*` 身份头；后端只信任 Compose 网桥中的这一代理跳。不要绕过 Web 容器对外分发 `18081` 后端地址，否则无法保证记录与 Web 入口使用同一 IP 口径。

## 上线顺序

1. 创建覆盖所有批准用户及跨系统 alias 的真实白名单。
2. 备份数据库，确认 `data/` 对 Compose 配置的非 root UID/GID 可读写，再保持 `MCP_REQUIRE_CALLER_USER=false` 部署新版后端并完成数据库幂等升级。
3. 发布带 `caller_user` 的新版 `YDHW-repo-wiki` skill。身份参数由每次工具调用携带，不增加 Header，也不修改已有 Agent 配置。
4. 在管理后台 MCP 使用日志中确认目标用户已有 `authorized_success`，并观察最近 30 天用户汇总和 `legacy_anonymous` 数量。
5. 当旧调用降为零或达到可接受水平后，将 `MCP_REQUIRE_CALLER_USER=true` 并重启后端。
6. 分别验证白名单用户成功、未知用户拒绝、缺少参数拒绝、管理页面统计、真实客户端 IP，以及 `/health` 中 `mcpUsageLogging.healthy=true`。

回退严格模式只需把开关恢复为 `false` 并重启；不要删除白名单或使用日志。白名单内容调整无需重启。

## 记录内容与错误码

每个业务调用记录一行数据库日志，包括原始提交用户名、canonical user、身份类型、工具名、结果、响应状态、耗时、时间、IP 和 User-Agent。查询正文和完整工具参数不进入这份使用日志。

稳定错误码包括：

- `CALLER_USER_REQUIRED`
- `CALLER_USER_INVALID`
- `USER_NOT_ALLOWED`
- `USER_DISABLED`
- `SERVICE_NOT_ALLOWED`
- `ACCESS_CONFIG_INVALID`

客户端遇到这些错误后应原样报告并停止，不尝试其他用户名、别名、Token 或 Header 绕过。
