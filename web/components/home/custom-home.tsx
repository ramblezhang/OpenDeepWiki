"use client";

import { useState } from "react";
import { Download, FileText, Search, Server } from "lucide-react";
import { AppLayout } from "@/components/app-layout";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PublicRepositoryList } from "@/components/repo/public-repository-list";
import { useScrollPosition } from "@/hooks/use-scroll-position";
import { useTranslations } from "@/hooks/use-translations";
import { cn } from "@/lib/utils";

const MCP_SERVER_NAME = "youdaohw_repo_wiki";
const MCP_SERVER_URL = "http://10.238.21.156:8090/api/mcp";
const SKILL_DOWNLOAD_URL = "/skills/YDHW-repo-wiki.zip";

export function CustomHome() {
  const t = useTranslations();
  const [keyword, setKeyword] = useState("");
  const { isScrolled } = useScrollPosition(140);

  return (
    <AppLayout
      activeItem={t("sidebar.explore")}
      hideSidebar
      hideAuthControls
      searchBox={{
        value: keyword,
        onChange: setKeyword,
        visible: isScrolled,
      }}
    >
      <main className="flex min-w-0 flex-1 flex-col overflow-x-hidden">
        <section className="min-w-0 border-b bg-background">
          <div className="mx-auto w-full min-w-0 max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
            <div className="mb-5 min-w-0">
              <h1 className="text-2xl font-semibold tracking-normal text-foreground sm:text-3xl">
                YDHW Repo Wiki
              </h1>
            </div>
            <div
              className={cn(
                "w-full min-w-0 transition-all duration-250 ease-in-out",
                isScrolled
                  ? "opacity-0 -translate-y-2 pointer-events-none"
                  : "opacity-100 translate-y-0 pointer-events-auto"
              )}
            >
              <label className="sr-only" htmlFor="home-repository-search">
                {t("home.custom.searchLabel")}
              </label>
              <div className="relative min-w-0">
                <Search className="pointer-events-none absolute left-4 top-1/2 h-5 w-5 -translate-y-1/2 text-muted-foreground" />
                <Input
                  id="home-repository-search"
                  value={keyword}
                  onChange={(e) => setKeyword(e.target.value)}
                  placeholder={t("home.searchPlaceholder")}
                  maxLength={100}
                  className="h-12 rounded-lg border-border bg-secondary/40 pl-12 text-base shadow-sm focus-visible:ring-2 focus-visible:ring-primary/20"
                />
              </div>
            </div>
          </div>
        </section>

        <section className="border-b bg-secondary/20">
          <div className="mx-auto grid w-full min-w-0 max-w-7xl gap-4 px-4 py-5 sm:px-6 lg:grid-cols-2 lg:px-8">
            <article className="min-w-0 rounded-lg border bg-background p-4">
              <div className="flex min-w-0 items-center gap-2">
                <Server className="h-4 w-4 shrink-0 text-primary" />
                <h3 className="truncate text-sm font-semibold">MCP 安装（需配套 SKILL）</h3>
              </div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                请安装本页提供的新版 YDHW Repo Wiki SKILL。下方命令只登记 remote MCP 的服务名和 URL，
                单独执行不会为业务调用补充本机登录用户名；兼容期只会记为 legacy_anonymous，严格模式下会被拒绝。
              </p>
              <div className="mt-3 space-y-2">
                <div className="rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
                  <div className="break-all">name: {MCP_SERVER_NAME}</div>
                  <div className="break-all">url: {MCP_SERVER_URL}</div>
                  <div>type: remote / http</div>
                </div>
                {[
                  ["Codex", `codex mcp add ${MCP_SERVER_NAME} --url ${MCP_SERVER_URL}`],
                  ["OpenCode", `opencode mcp add ${MCP_SERVER_NAME} --url ${MCP_SERVER_URL}`],
                  ["Claude Code", `claude mcp add --transport http ${MCP_SERVER_NAME} ${MCP_SERVER_URL}`],
                ].map(([label, command]) => (
                  <div key={label} className="min-w-0">
                    <div className="mb-1 text-xs font-medium text-muted-foreground">
                      {label}
                    </div>
                    <pre className="overflow-x-auto rounded-md bg-muted px-3 py-2 text-xs leading-5">
                      <code>{command}</code>
                    </pre>
                  </div>
                ))}
              </div>
            </article>

            <article className="min-w-0 rounded-lg border bg-background p-4">
              <div className="flex min-w-0 items-center gap-2">
                <FileText className="h-4 w-4 shrink-0 text-primary" />
                <h3 className="truncate text-sm font-semibold">YDHW Repo Wiki SKILL</h3>
              </div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                先查有道硬件仓库 Wiki，再回答词典笔/有道硬件技术背景、模块职责、代码链路、接口含义、仓库文档和实现说明。
              </p>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                适用于 CaptureFrame、Camera、OTA、LTE、MiniApp、DAL、JSAPI、HAL、Buildroot、rootfs、固件及 Y18、Y15_CV、Y07、Y02-1、X7P 等场景。
              </p>
              <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                <Button asChild className="w-full gap-2 sm:w-auto">
                  <a href={SKILL_DOWNLOAD_URL} download>
                    <Download className="h-4 w-4" />
                    下载 SKILL
                  </a>
                </Button>
                <Button asChild variant="secondary" className="w-full sm:w-auto">
                  <a href="/skills/YDHW-repo-wiki/SKILL.md" target="_blank" rel="noreferrer">
                    查看 SKILL.md
                  </a>
                </Button>
              </div>
            </article>
          </div>
        </section>

        <section className="mx-auto w-full min-w-0 max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
          <PublicRepositoryList keyword={keyword} />
        </section>
      </main>
    </AppLayout>
  );
}
