"use client";

import { useState, useEffect, useCallback } from "react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { useTranslations } from "@/hooks/use-translations";
import { useAuth } from "@/contexts/auth-context";
import { getChatProviderConfigs } from "@/lib/admin-api";
import {
  Copy,
  Check,
  ExternalLink,
  MessageSquare,
  Server,
  CheckCircle,
  XCircle,
  Loader2,
} from "lucide-react";
import Link from "next/link";

interface IntegrationsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function IntegrationsDialog({ open, onOpenChange }: IntegrationsDialogProps) {
  const t = useTranslations();
  const { user } = useAuth();
  const isAdmin = user?.roles?.includes("Admin") ?? false;

  const [slackLoading, setSlackLoading] = useState(false);
  const [slackConnected, setSlackConnected] = useState(false);
  const [mcpUrlCopied, setMcpUrlCopied] = useState(false);
  const [configCopied, setConfigCopied] = useState(false);

  const mcpUrl = typeof window !== "undefined" ? `${window.location.origin}/api/mcp` : "/api/mcp";

  const claudeDesktopConfig = JSON.stringify(
    {
      mcpServers: {
        deepwiki: {
          url: mcpUrl,
        },
      },
    },
    null,
    2
  );

  useEffect(() => {
    if (!open) return;

    let cancelled = false;
    void Promise.resolve()
      .then(() => {
        if (cancelled) return null;
        setSlackLoading(true);
        setSlackConnected(false);
        return getChatProviderConfigs();
      })
      .then((providers) => {
        if (cancelled || !providers) return;
        const slack = providers.find((p) => p.platform === "slack");
        setSlackConnected(slack ? slack.isEnabled && slack.isRegistered : false);
      })
      .catch(() => {
        if (!cancelled) setSlackConnected(false);
      })
      .finally(() => {
        if (!cancelled) setSlackLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [open]);

  const copyToClipboard = useCallback(async (text: string, type: "url" | "config") => {
    try {
      await navigator.clipboard.writeText(text);
      if (type === "url") {
        setMcpUrlCopied(true);
        setTimeout(() => setMcpUrlCopied(false), 2000);
      } else {
        setConfigCopied(true);
        setTimeout(() => setConfigCopied(false), 2000);
      }
    } catch {
      // Clipboard API not available
    }
  }, []);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{t("home.integrations.title")}</DialogTitle>
          <DialogDescription>{t("home.integrations.description")}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4 pt-2">
          {/* Slack Integration Section */}
          <div className="rounded-lg border p-4 space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <MessageSquare className="h-5 w-5 text-muted-foreground" />
                <h3 className="font-medium">{t("home.integrations.slack.title")}</h3>
              </div>
              <div className="flex items-center gap-1.5">
                {slackLoading ? (
                  <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
                ) : slackConnected ? (
                  <>
                    <CheckCircle className="h-4 w-4 text-green-500" />
                    <span className="text-sm text-green-500">{t("home.integrations.slack.connected")}</span>
                  </>
                ) : (
                  <>
                    <XCircle className="h-4 w-4 text-muted-foreground" />
                    <span className="text-sm text-muted-foreground">{t("home.integrations.slack.notConnected")}</span>
                  </>
                )}
              </div>
            </div>
            <div className="text-sm text-muted-foreground">
              {isAdmin ? (
                <Link
                  href="/admin/chat-providers"
                  className="inline-flex items-center gap-1 text-primary hover:underline"
                  onClick={() => onOpenChange(false)}
                >
                  {t("home.integrations.slack.configure")}
                  <ExternalLink className="h-3 w-3" />
                </Link>
              ) : (
                <p>{t("home.integrations.slack.contactAdmin")}</p>
              )}
            </div>
          </div>

          {/* MCP Server Section */}
          <div className="rounded-lg border p-4 space-y-3">
            <div className="flex items-center gap-2">
              <Server className="h-5 w-5 text-muted-foreground" />
              <h3 className="font-medium">{t("home.integrations.mcp.title")}</h3>
            </div>
            <p className="text-sm text-muted-foreground">{t("home.integrations.mcp.description")}</p>

            <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm dark:border-amber-800 dark:bg-amber-950/30">
              <p className="font-medium text-amber-900 dark:text-amber-200">
                {t("common.mcp.skillRequiredTitle")}
              </p>
              <p className="mt-1 leading-5 text-amber-800 dark:text-amber-300">
                {t("common.mcp.skillRequiredDesc")}{" "}
                <a
                  href="/skills/YDHW-repo-wiki.zip"
                  download
                  className="font-medium underline underline-offset-4"
                >
                  {t("common.mcp.downloadSkill")}
                </a>
              </p>
            </div>

            {/* MCP URL */}
            <div className="space-y-1.5">
              <label className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                {t("home.integrations.mcp.serverUrl")}
              </label>
              <div className="flex items-center gap-2">
                <code className="flex-1 rounded-md bg-muted px-3 py-2 text-sm font-mono break-all">
                  {mcpUrl}
                </code>
                <Button
                  variant="outline"
                  size="sm"
                  className="shrink-0 gap-1.5"
                  onClick={() => copyToClipboard(mcpUrl, "url")}
                >
                  {mcpUrlCopied ? (
                    <>
                      <Check className="h-3.5 w-3.5" />
                      {t("home.integrations.mcp.copied")}
                    </>
                  ) : (
                    <>
                      <Copy className="h-3.5 w-3.5" />
                      {t("home.integrations.mcp.copyUrl")}
                    </>
                  )}
                </Button>
              </div>
            </div>

            {/* Claude Desktop Config */}
            <div className="space-y-1.5">
              <p className="text-sm text-muted-foreground">
                {t("home.integrations.mcp.claudeDesktopHint")}
              </p>
              <div className="relative">
                <pre className="rounded-md bg-muted px-3 py-2 text-xs font-mono overflow-x-auto">
                  {claudeDesktopConfig}
                </pre>
                <Button
                  variant="outline"
                  size="sm"
                  className="absolute top-1.5 right-1.5 h-7 gap-1 text-xs"
                  onClick={() => copyToClipboard(claudeDesktopConfig, "config")}
                >
                  {configCopied ? (
                    <>
                      <Check className="h-3 w-3" />
                      {t("home.integrations.mcp.copied")}
                    </>
                  ) : (
                    <>
                      <Copy className="h-3 w-3" />
                      {t("home.integrations.mcp.copyConfig")}
                    </>
                  )}
                </Button>
              </div>
            </div>

            {/* Claude.ai Hint */}
            <p className="text-sm text-muted-foreground">
              {t("home.integrations.mcp.claudeAiHint")}
            </p>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
