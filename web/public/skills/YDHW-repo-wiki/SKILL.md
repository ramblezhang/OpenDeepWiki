---
name: ydhw-repo-wiki
description: >
  Use when 用户需要了解词典笔/有道硬件技术内容、模块职责、代码链路、接口含义、仓库文档、架构设计、功能入口、日志/现象背后的实现说明，
  即使用户没有明确说“查 wiki”也应触发。典型触发包括 CaptureFrame、Camera、OTA、LTE、MiniApp、DAL、JSAPI、HAL、Buildroot、rootfs、固件、
  Y18、Y15_CV、Y07、Y02-1、X7P、Y09、Y09P、A7、Y15、Y15C，以及“这个技术点怎么实现的/在哪个仓/文档怎么说/帮我了解一下”。
---

# 有道硬件仓库 Wiki 查询 Workflow

## 定位

本 skill 用于“先查有道硬件仓库 Wiki，再回答技术问题”。它不替代本地源码阅读，也不替代 `YDHW-code-map` 的平台/SKU 路由；当用户主要是在问技术背景、模块说明、文档内容、调用链概览、仓库职责或“先帮我了解一下”时，优先使用本 skill。

如果用户明确要求改代码、编译、刷机、ADB 调试、提交 Gerrit，先使用对应专项 skill；但仍可用本 skill 从 Wiki 补充上下文。

## MCP 服务信息

有道硬件 Wiki MCP 是一个 remote MCP server。无论当前 agent 是 OpenCode、Claude Code、Codex 还是 Cursor，安装时都使用同一组服务信息，只是写入位置和命令不同：

```text
name: youdaohw_repo_wiki
type: remote
url: http://10.238.21.156:8090/api/mcp
oauth: false
timeout: 30000
```

如果当前会话已经暴露 `youdaohw_repo_wiki_*` 工具，说明 MCP 已经可用，不需要再安装。

访问控制使用每次业务工具调用中的 `caller_user` 声明参数，不使用 Token、OAuth 或自定义 Authorization Header。身份方案不会要求修改 Codex/OpenCode/Cursor 已有的 MCP 配置；只有 MCP 本身尚未安装时，才沿用原有流程写入服务名和 URL。

### 调用身份

在当前会话第一次调用 Wiki 业务工具前获取一次本机登录用户名，并对后续全部 Wiki 工具调用复用完全相同的值：

1. 原生 Windows 运行 `whoami`。
2. WSL（存在 `WSL_INTEROP` / `WSL_DISTRO_NAME`，或 kernel release 包含 `microsoft`）优先运行 `cmd.exe /c whoami`；失败后运行 `id -un`，再失败可尝试 `whoami`。
3. Linux/macOS 运行 `id -un`；失败后运行 `whoami`。
4. 只去掉命令输出首尾的换行和空白，不拆掉 `DOMAIN\user` 的域前缀，也不改大小写。不要用 `$USER`、`$USERNAME`、Git 作者或提问者名称猜测身份。
5. 每次调用下面列出的七个 Wiki 工具时，都显式传 `caller_user=<本会话用户名>`。`initialize` 和 `tools/list` 不需要该参数。

无法取得用户名时停止业务调用并说明失败命令。服务端返回 `CALLER_USER_REQUIRED`、`CALLER_USER_INVALID`、`USER_NOT_ALLOWED`、`USER_DISABLED`、`SERVICE_NOT_ALLOWED` 或 `ACCESS_CONFIG_INVALID` 时，原样报告错误码并停止；不要尝试其他用户名、别名、Token 或 Header 绕过。该用户名是内网可追责的声明值，不防止用户主动伪造，因此不能替代公网场景的强认证。

### Agent 环境识别

先根据当前运行环境判断用户正在使用哪种 agent：

- **OpenCode**：存在 `opencode` 命令，或配置路径为 `~/.config/opencode/opencode.json` / `~/.config/opencode/opencode.jsonc`，或当前会话工具名已经带有 `youdaohw_repo_wiki_*`。
- **Claude Code**：存在 `claude` 命令，或用户明确提到 Claude Code / Claude。
- **Codex**：存在 `codex` 命令，或用户明确提到 Codex / OpenAI Codex。
- **Cursor**：用户明确提到 Cursor，或当前项目/用户目录存在 Cursor MCP 配置文件。
- **不确定环境**：不要猜配置 schema；只给出上面的 MCP 服务信息，让用户按当前 agent 的 “remote MCP / streamable HTTP MCP server” 配置入口添加。

### OpenCode 安装

OpenCode 是本 skill 的默认支持环境。未安装时运行：

```bash
opencode mcp add youdaohw_repo_wiki --url http://10.238.21.156:8090/api/mcp
```

然后检查 `~/.config/opencode/opencode.json` 或 `~/.config/opencode/opencode.jsonc`，确保配置项为：

```jsonc
"mcp": {
  "youdaohw_repo_wiki": {
    "type": "remote",
    "url": "http://10.238.21.156:8090/api/mcp",
    "enabled": true,
    "oauth": false,
    "timeout": 30000
  }
}
```

如果配置里存在旧的 `opendeepwiki`，将其改名为 `youdaohw_repo_wiki`，不要保留两个指向同一服务的重复 MCP。

### Claude Code 安装

如果当前环境是 Claude Code，优先使用 Claude Code 自带 MCP 命令安装 remote HTTP MCP：

```bash
claude mcp add --transport http youdaohw_repo_wiki http://10.238.21.156:8090/api/mcp
```

然后运行：

```bash
claude mcp list
```

如果本机 Claude Code 版本不支持 `--transport http` 或命令参数不同，先运行 `claude mcp add --help`，按帮助中的 remote / HTTP MCP server 写法添加同一个 `name` 和 `url`。不要把它配置成 stdio server。

### Codex 安装

如果当前环境是 Codex，先检查 Codex CLI 是否提供 MCP 子命令：

```bash
codex mcp --help
```

如果支持 remote URL 添加，按 Codex CLI 帮助添加：

```text
name: youdaohw_repo_wiki
url: http://10.238.21.156:8090/api/mcp
transport/type: remote/http/streamable-http（按 Codex 当前版本命名）
```

如果当前 Codex 版本只支持 `~/.codex/config.toml`，在 Codex 官方 schema 支持 remote MCP 的前提下添加等价配置；如果 schema 只支持 stdio MCP，不要硬写不可用配置，直接说明“当前 Codex 版本可能不支持 remote MCP，需要升级或使用支持 remote MCP 的版本”。

### Cursor 安装

如果当前环境是 Cursor，在 Cursor 的 MCP 配置入口添加 remote MCP server。常见配置形态如下，具体字段名以 Cursor 当前版本为准：

```jsonc
{
  "mcpServers": {
    "youdaohw_repo_wiki": {
      "type": "remote",
      "url": "http://10.238.21.156:8090/api/mcp"
    }
  }
}
```

如果 Cursor 版本要求 `transport` 字段，则选择 HTTP / streamable HTTP，不要选择 stdio。

## 使用流程

1. **确认 MCP 工具是否可用**

   如果当前会话已经暴露以下 `youdaohw_repo_wiki_*` 工具，直接进入查询，不必先跑命令检查：

   - `youdaohw_repo_wiki_search_repositories`
   - `youdaohw_repo_wiki_search_docs`
   - `youdaohw_repo_wiki_read_doc`
   - `youdaohw_repo_wiki_list_repositories`
   - `youdaohw_repo_wiki_get_repo_structure`
   - `youdaohw_repo_wiki_read_file`
   - `youdaohw_repo_wiki_search_doc`

   如果工具不可用，再检查 MCP 是否已安装。先识别当前 agent 类型，再使用对应安装方式。

   OpenCode 环境先运行：

   ```bash
   opencode mcp list
   ```

   判断标准：
   - 列表中存在 `youdaohw_repo_wiki` 且状态为 `connected`：直接进入查询。
   - 不存在 `youdaohw_repo_wiki`，或只有旧名称 `opendeepwiki`：安装/修正 MCP。
   - 存在但不是 `connected`：先尝试重连或提示用户重启 OpenCode 会话；如果当前任务能通过直接 MCP 工具调用继续，则继续查询并说明状态。

2. **安装或修正 MCP**

   如果未安装，按上面的 Agent 专属安装章节处理。能直接执行安装命令的环境，应主动帮用户安装；不能确定配置 schema 的环境，不要猜写配置，给出准确 MCP 服务信息和需要用户在对应 agent 设置页添加的字段。

   OpenCode 安装后再次运行：

   ```bash
   opencode mcp list
   ```

   看到 `youdaohw_repo_wiki connected` 后再继续。Claude Code / Codex / Cursor 也要使用各自的 MCP list/status 界面确认 server 已连接。若未连接，报告具体错误，不要假装已查询 Wiki。

3. **选择查询工具**

   MCP 可用后，先按“调用身份”取得本会话用户名，再按问题类型选择工具；下面每个业务调用都必须携带同一个 `caller_user`：

   - 不确定用户问题属于哪个仓库：先用 `youdaohw_repo_wiki_search_repositories`，再用 `youdaohw_repo_wiki_search_docs`。
   - 用户已经给出 owner/repo 或仓库名：直接用 `youdaohw_repo_wiki_search_docs`，并传入明确仓库范围。
   - `youdaohw_repo_wiki_search_docs` 返回了准确文档 path，且需要给出可靠细节：继续用 `youdaohw_repo_wiki_read_doc` 读取全文。
   - 用户只要仓库列表或确认某仓是否纳入 Wiki：用 `youdaohw_repo_wiki_list_repositories`。
   - 用户要看某仓目录结构：用 `youdaohw_repo_wiki_get_repo_structure`。
   - 用户要确认某个具体文件内容，且已知仓库内相对路径：用 `youdaohw_repo_wiki_read_file`。

   优先使用中文 `language: "zh"`；如果中文结果少或用户要求英文，再扩大语言范围。

4. **回答要求**

   回答必须区分三类信息：

   - **Wiki 证据**：来自 MCP 查询结果或 `youdaohw_repo_wiki_read_doc` 文档内容。
   - **推断**：基于文档标题、snippet、仓库名、路径做出的判断。
   - **未确认项**：Wiki 未命中、结果冲突、需要读源码或设备日志才能确认的部分。

   技术回答建议结构：

   ```markdown
   结论：...

   Wiki 命中：
   - 仓库：owner/repo
   - 文档：title
   - path：...

   关键内容：
   - ...

   需要继续确认：
   - ...
   ```

   不要输出大段 Wiki 原文。只摘取必要结论、关键文件名、函数名、状态名和路径。

## 典型查询策略

### 用户问“某功能在哪个仓/入口在哪”

1. 用用户原话调用 `youdaohw_repo_wiki_search_repositories`。
2. 对 top 3 仓库调用 `youdaohw_repo_wiki_search_docs`，或让 `youdaohw_repo_wiki_search_docs` 自动路由。
3. 若结果中有明确入口文档，调用 `youdaohw_repo_wiki_read_doc`。
4. 输出仓库、文档 path、关键源码文件/函数、置信度。

### 用户问“某技术链路怎么工作”

1. 用完整问题调用 `youdaohw_repo_wiki_search_docs`，不指定仓库。
2. 从结果中选择最相关的 1-3 篇文档，必要时 `youdaohw_repo_wiki_read_doc`。
3. 按“组件 -> 调用关系 -> 数据/状态流 -> 异常点”解释。

### 用户问“某报错/日志是什么意思”

1. 先用日志关键词、模块名、SKU 调用 `youdaohw_repo_wiki_search_docs`。
2. 如果 Wiki 命中少，再用更宽泛的模块名调用 `youdaohw_repo_wiki_search_repositories`。
3. 明确说明 Wiki 是否直接解释该日志；未直接命中时，给出基于相关文档的推断和下一步源码/设备日志验证建议。

## 与其他 YDHW skills 的配合

- 需要进入真实代码路径、判断 SKU/平台工程根：切换或并用 `YDHW-code-map`。
- 需要实际调试设备、拉日志、Camera/LTE/NAND/关机压测：切换或并用 `dictpen-debug`、`adb-auth`、`nand-badblock`。
- 需要编译固件、rootfs、services、OTA 包：切换或并用 `YDHW-build-firmware`。
- 需要提交代码：使用 `YDHW-gerrit-submit`。

本 skill 的职责是尽快把“仓库 Wiki 已整理的信息”带入上下文，减少盲搜和凭经验猜测。
