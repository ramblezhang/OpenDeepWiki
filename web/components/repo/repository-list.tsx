"use client";

import { useEffect, useState, useCallback } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useTranslations } from "@/hooks/use-translations";
import {
  fetchAllRepositoryList,
  fetchRepositoryList,
  regenerateRepository,
} from "@/lib/repository-api";
import { RepositoryExplorerView } from "@/components/repo/repository-explorer-view";
import { getRepositoryDisplayPath } from "@/components/repo/repository-explorer-tree";
import type {
  RepositoryEffectiveStatus,
  RepositoryItemResponse,
  RepositoryStatus,
} from "@/types/repository";
import { getRepositorySourceTypeLabelKey } from "@/lib/repository-source";
import {
  Clock,
  Loader2,
  CheckCircle2,
  XCircle,
  ExternalLink,
  RefreshCw,
  GitBranch,
  ChevronLeft,
  ChevronRight,
  LayoutGrid,
  ListTree,
  AlertTriangle,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { buildRepoBasePath } from "@/lib/repo-route";
import {
  getRepositoryEffectiveStatusClassName,
  getRepositoryEffectiveStatusLabel,
  isRepositoryEffectiveStatusActive,
} from "@/lib/repository-effective-status";
import { VisibilityToggle } from "@/components/repo/visibility-toggle";
import { toast } from "sonner";

interface RepositoryListProps {
  ownerId?: string;
  refreshTrigger?: number;
}

const PAGE_SIZE = 20;

const STATUS_CONFIG: Record<RepositoryStatus, {
  icon: typeof Clock;
  className: string;
  labelKey: string;
}> = {
  Pending: {
    icon: Clock,
    className: "text-yellow-500 bg-yellow-500/10",
    labelKey: "pending",
  },
  Processing: {
    icon: Loader2,
    className: "text-blue-500 bg-blue-500/10",
    labelKey: "processing",
  },
  Completed: {
    icon: CheckCircle2,
    className: "text-green-500 bg-green-500/10",
    labelKey: "completed",
  },
  Failed: {
    icon: XCircle,
    className: "text-red-500 bg-red-500/10",
    labelKey: "failed",
  },
};

function StatusBadge({
  status,
  fallbackStatus,
}: {
  status?: RepositoryEffectiveStatus;
  fallbackStatus: RepositoryStatus;
}) {
  const t = useTranslations();
  const isActive = isRepositoryEffectiveStatusActive(status);
  const isQueued = status === "AllBranchesQueued" || status === "PartialBranchesQueued" || status === "RepositoryFullPending";
  const fallbackConfig = STATUS_CONFIG[fallbackStatus];
  const Icon = status === "Failed" || status === "PartialFailed"
    ? XCircle
    : isQueued
      ? Clock
      : isActive
        ? Loader2
        : fallbackConfig.icon;
  const label = status
    ? getRepositoryEffectiveStatusLabel(status, fallbackStatus)
    : t(`home.repository.status.${fallbackConfig.labelKey}`);

  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium",
        status ? getRepositoryEffectiveStatusClassName(status, fallbackStatus) : fallbackConfig.className
      )}
    >
      <Icon
        className={cn("h-3.5 w-3.5", isActive && "animate-spin")}
      />
      {label}
    </span>
  );
}

function RepositoryCard({ 
  repo, 
  onVisibilityChange,
  onRetry,
  isRetrying,
}: { 
  repo: RepositoryItemResponse;
  onVisibilityChange: (repoId: string, newIsPublic: boolean) => void;
  onRetry: (repo: RepositoryItemResponse) => void;
  isRetrying: boolean;
}) {
  const t = useTranslations();
  const createdDate = new Date(repo.createdAt).toLocaleDateString();
  const branchFullProcessing = repo.statusCounts?.branchFullProcessing ?? 0;
  const branchFullPending = repo.statusCounts?.branchFullPending ?? 0;
  const incrementalActive = (repo.statusCounts?.incrementalPending ?? 0) + (repo.statusCounts?.incrementalProcessing ?? 0);

  // 生成正确编码的Wiki导航URL
  // 使用encodeURIComponent处理特殊字符，确保URL安全
  const wikiUrl = buildRepoBasePath(repo.orgName, repo.repoName);

  const handleVisibilityChange = (newIsPublic: boolean) => {
    onVisibilityChange(repo.id, newIsPublic);
  };

  return (
    <Card className="transition-shadow hover:shadow-md">
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <GitBranch className="h-4 w-4 text-muted-foreground shrink-0" />
              <h3 className="font-medium truncate">
                {getRepositoryDisplayPath(repo)}
              </h3>
            </div>
            <div className="mt-2">
              <span className="inline-flex rounded-full bg-secondary px-2 py-1 text-xs text-muted-foreground">
                {t(`home.repository.${getRepositorySourceTypeLabelKey(repo.sourceType, repo.sourceTypeName)}`)}
              </span>
            </div>
            <p className="mt-1 text-sm text-muted-foreground truncate">
              {repo.sourceLocation || repo.gitUrl}
            </p>
            <p className="mt-2 text-xs text-muted-foreground">
              {t("home.repository.createdAt")}: {createdDate}
            </p>
            {((repo.branchGenerationActiveCount ?? 0) > 0 ||
              (repo.branchGenerationFailedCount ?? 0) > 0 ||
              incrementalActive > 0) && (
              <div className="mt-2 flex flex-wrap gap-1.5">
                {branchFullProcessing > 0 && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-blue-500/10 px-2 py-1 text-xs text-blue-600">
                    <Loader2 className="h-3 w-3 animate-spin" />
                    branch generating {branchFullProcessing}
                  </span>
                )}
                {branchFullPending > 0 && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-yellow-500/10 px-2 py-1 text-xs text-yellow-600">
                    <Clock className="h-3 w-3" />
                    branch queued {branchFullPending}
                  </span>
                )}
                {(repo.branchGenerationFailedCount ?? 0) > 0 && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-red-500/10 px-2 py-1 text-xs text-red-600">
                    <AlertTriangle className="h-3 w-3" />
                    branch failed {repo.branchGenerationFailedCount}
                  </span>
                )}
                {incrementalActive > 0 && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-cyan-500/10 px-2 py-1 text-xs text-cyan-600">
                    <RefreshCw className="h-3 w-3 animate-spin" />
                    incremental {incrementalActive}
                  </span>
                )}
              </div>
            )}
          </div>
          <div className="flex flex-col items-end gap-2 shrink-0">
            <StatusBadge status={repo.effectiveStatus} fallbackStatus={repo.statusName} />
            <VisibilityToggle
              repositoryId={repo.id}
              isPublic={repo.isPublic}
              hasPassword={repo.hasPassword}
              onVisibilityChange={handleVisibilityChange}
            />
            {repo.statusName === "Completed" && (
              <Button variant="outline" size="sm" asChild>
                <Link href={wikiUrl}>
                  <ExternalLink className="mr-1.5 h-3.5 w-3.5" />
                  {t("home.repository.viewWiki")}
                </Link>
              </Button>
            )}
            {repo.statusName === "Failed" && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => onRetry(repo)}
                disabled={isRetrying}
              >
                {isRetrying ? (
                  <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                ) : (
                  <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
                )}
                {isRetrying
                  ? (t("home.repository.status.regenerating") || t("home.repository.retry"))
                  : t("home.repository.retry")}
              </Button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function RepositoryListSkeleton() {
  return (
    <div className="space-y-4">
      {[1, 2, 3].map((i) => (
        <Card key={i}>
          <CardContent className="p-4">
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 space-y-2">
                <Skeleton className="h-5 w-48" />
                <Skeleton className="h-4 w-64" />
                <Skeleton className="h-3 w-32" />
              </div>
              <Skeleton className="h-6 w-20 rounded-full" />
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

export function RepositoryList({ ownerId, refreshTrigger }: RepositoryListProps) {
  const t = useTranslations();
  const [repositories, setRepositories] = useState<RepositoryItemResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [retryingRepoId, setRetryingRepoId] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<"tree" | "list">("tree");
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);

  const isTreeView = viewMode === "tree";
  const totalPages = isTreeView ? 1 : Math.ceil(total / PAGE_SIZE);

  const loadRepositories = useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);
      const params = { ownerId };
      const response = isTreeView
        ? await fetchAllRepositoryList(params)
        : await fetchRepositoryList({
            ...params,
            page,
            pageSize: PAGE_SIZE,
          });
      setRepositories(response.items);
      setTotal(response.total);
    } catch (err) {
      setError("Failed to load repositories");
      console.error("Failed to fetch repositories:", err);
    } finally {
      setIsLoading(false);
    }
  }, [isTreeView, ownerId, page]);

  // 处理可见性变化，更新本地状态
  const handleVisibilityChange = useCallback((repoId: string, newIsPublic: boolean) => {
    setRepositories((prev) =>
      prev.map((repo) =>
        repo.id === repoId ? { ...repo, isPublic: newIsPublic } : repo
      )
    );
  }, []);

  const handleRetry = useCallback(async (repo: RepositoryItemResponse) => {
    setRetryingRepoId(repo.id);
    try {
      const result = await regenerateRepository(repo.orgName, repo.repoName);
      if (!result.success) {
        toast.error(result.errorMessage || t("home.actions.actionError"));
        return;
      }

      setRepositories((prev) =>
        prev.map((item) =>
          item.id === repo.id ? { ...item, statusName: "Pending" } : item
        )
      );
      void loadRepositories();
    } catch (err) {
      console.error("Failed to regenerate repository:", err);
      toast.error(t("home.actions.actionError"));
    } finally {
      setRetryingRepoId((current) => (current === repo.id ? null : current));
    }
  }, [loadRepositories, t]);

  useEffect(() => {
    loadRepositories();
  }, [loadRepositories, refreshTrigger]);

  useEffect(() => {
    setPage(1);
  }, [ownerId, viewMode]);

  // Auto-refresh for pending/processing repositories
  useEffect(() => {
    const hasPendingOrProcessing = repositories.some(
      (r) => r.statusName === "Pending" || r.statusName === "Processing"
    );

    if (hasPendingOrProcessing) {
      const interval = setInterval(loadRepositories, 10000); // Refresh every 10 seconds
      return () => clearInterval(interval);
    }
  }, [repositories, loadRepositories]);

  if (isLoading && repositories.length === 0) {
    return (
      <Card className="w-full">
        <CardHeader>
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          <RepositoryListSkeleton />
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card className="w-full">
        <CardHeader>
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <XCircle className="h-12 w-12 text-destructive mb-4" />
            <p className="text-muted-foreground">{error}</p>
            <Button
              variant="outline"
              className="mt-4"
              onClick={loadRepositories}
            >
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("home.repository.retry")}
            </Button>
          </div>
        </CardContent>
      </Card>
    );
  }

  const pagination =
    totalPages > 1 ? (
      <div className="flex items-center justify-center gap-4 pt-2">
        <Button
          variant="outline"
          size="sm"
          onClick={() => setPage((current) => Math.max(1, current - 1))}
          disabled={page === 1 || isLoading}
        >
          <ChevronLeft className="mr-1 h-4 w-4" />
          {t("home.bookmarks.previous")}
        </Button>
        <span className="text-sm text-muted-foreground">
          {t("home.bookmarks.pageInfo")
            .replace("{current}", page.toString())
            .replace("{total}", totalPages.toString())}
        </span>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
          disabled={page === totalPages || isLoading}
        >
          {t("home.bookmarks.next")}
          <ChevronRight className="ml-1 h-4 w-4" />
        </Button>
      </div>
    ) : null;

  return (
    <Card className="w-full">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <CardTitle>{t("home.repository.listTitle")}</CardTitle>
          <div className="flex w-full items-center gap-2 sm:w-auto">
            <div className="grid flex-1 grid-cols-2 rounded-lg border bg-muted/30 p-1 sm:flex sm:flex-none">
              <Button
                variant={viewMode === "tree" ? "secondary" : "ghost"}
                size="sm"
                className="gap-1.5"
                onClick={() => setViewMode("tree")}
              >
                <ListTree className="h-4 w-4" />
                {t("home.repository.view.tree")}
              </Button>
              <Button
                variant={viewMode === "list" ? "secondary" : "ghost"}
                size="sm"
                className="gap-1.5"
                onClick={() => setViewMode("list")}
              >
                <LayoutGrid className="h-4 w-4" />
                {t("home.repository.view.grid")}
              </Button>
            </div>
            <Button
              variant="ghost"
              size="icon"
              className="shrink-0"
              onClick={loadRepositories}
              disabled={isLoading}
            >
              <RefreshCw
                className={cn("h-4 w-4", isLoading && "animate-spin")}
              />
            </Button>
          </div>
        </div>
        {repositories.length > 0 && (
          <CardDescription>
            {total} {total === 1 ? "repository" : "repositories"}
          </CardDescription>
        )}
      </CardHeader>
      <CardContent>
        {repositories.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-center">
            <GitBranch className="h-12 w-12 text-muted-foreground mb-4" />
            <p className="text-muted-foreground">
              {t("home.repository.noRepositories")}
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            {viewMode === "tree" ? (
              <RepositoryExplorerView
                repositories={repositories}
                emptyMessage={t("home.repository.noRepositories")}
                contentClassName="lg:grid-cols-1 xl:grid-cols-2"
                labels={{
                  treeTitle: t("home.repository.tree.title"),
                  allRepositories: t("home.repository.tree.all"),
                  repositoryCount: (count) =>
                    t("home.repository.tree.count").replace("{count}", count.toString()),
                  emptyFolder: t("home.repository.tree.emptyFolder"),
                  expandFolder: t("home.repository.tree.expandFolder"),
                  collapseFolder: t("home.repository.tree.collapseFolder"),
                }}
                renderRepository={(repo) => (
                  <RepositoryCard
                    repo={repo}
                    onVisibilityChange={handleVisibilityChange}
                    onRetry={handleRetry}
                    isRetrying={retryingRepoId === repo.id}
                  />
                )}
              />
            ) : (
              repositories.map((repo) => (
                <RepositoryCard
                  key={repo.id}
                  repo={repo}
                  onVisibilityChange={handleVisibilityChange}
                  onRetry={handleRetry}
                  isRetrying={retryingRepoId === repo.id}
                />
              ))
            )}
            {pagination}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
