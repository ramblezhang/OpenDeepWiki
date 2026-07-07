"use client";

import { useMemo, useState } from "react";
import type { ReactNode } from "react";
import { Card } from "@/components/ui/card";
import type { RepositoryItemResponse } from "@/types/repository";
import { cn } from "@/lib/utils";
import {
  ROOT_PATH,
  buildRepositoryTree,
  getRepositoryFolderPath,
  sortTreeNodes,
  type TreeNode,
} from "./repository-explorer-tree";
import {
  ChevronDown,
  ChevronRight,
  Folder,
  FolderOpen,
  GitBranch,
} from "lucide-react";

interface RepositoryExplorerViewProps {
  repositories: RepositoryItemResponse[];
  renderRepository: (repository: RepositoryItemResponse) => ReactNode;
  emptyMessage: string;
  labels: {
    treeTitle: string;
    allRepositories: string;
    repositoryCount: (count: number) => string;
    emptyFolder: string;
    expandFolder: string;
    collapseFolder: string;
  };
  className?: string;
  contentClassName?: string;
}

function TreeRow({
  node,
  depth,
  selectedPath,
  expandedPaths,
  labels,
  onSelect,
  onToggle,
}: {
  node: TreeNode;
  depth: number;
  selectedPath: string;
  expandedPaths: Set<string>;
  labels: RepositoryExplorerViewProps["labels"];
  onSelect: (path: string) => void;
  onToggle: (path: string) => void;
}) {
  const hasChildren = node.children.size > 0;
  const isExpanded = expandedPaths.has(node.path);
  const isSelected = selectedPath === node.path;
  const FolderIcon = isExpanded ? FolderOpen : Folder;

  return (
    <div>
      <div
        className={cn(
          "flex min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors",
          isSelected
            ? "bg-primary/10 text-primary"
            : "text-muted-foreground hover:bg-secondary/60 hover:text-foreground"
        )}
        style={{ paddingLeft: `${8 + depth * 18}px` }}
      >
        <button
          type="button"
          className="flex h-5 w-5 shrink-0 items-center justify-center rounded-sm hover:bg-background/80"
          onClick={() => onToggle(node.path)}
          aria-label={isExpanded ? labels.collapseFolder : labels.expandFolder}
        >
          {hasChildren ? (
            isExpanded ? (
              <ChevronDown className="h-4 w-4" />
            ) : (
              <ChevronRight className="h-4 w-4" />
            )
          ) : (
            <span className="h-4 w-4" />
          )}
        </button>
        <button
          type="button"
          className="flex min-w-0 flex-1 items-center gap-2 text-left"
          onClick={() => onSelect(node.path)}
        >
          <FolderIcon
            className={cn(
              "h-4 w-4 shrink-0",
              isSelected ? "text-primary" : "text-muted-foreground"
            )}
          />
          <span className="truncate font-medium">{node.name}</span>
          <span className="ml-auto shrink-0 rounded-full bg-secondary px-1.5 py-0.5 text-[11px] text-muted-foreground">
            {node.repositoryCount}
          </span>
        </button>
      </div>
      {hasChildren && isExpanded && (
        <div className="mt-1 space-y-1">
          {sortTreeNodes(node.children.values()).map((child) => (
            <TreeRow
              key={child.path}
              node={child}
              depth={depth + 1}
              selectedPath={selectedPath}
              expandedPaths={expandedPaths}
              labels={labels}
              onSelect={onSelect}
              onToggle={onToggle}
            />
          ))}
        </div>
      )}
    </div>
  );
}

export function RepositoryExplorerView({
  repositories,
  renderRepository,
  emptyMessage,
  labels,
  className,
  contentClassName,
}: RepositoryExplorerViewProps) {
  const { root, folderPaths } = useMemo(
    () => buildRepositoryTree(repositories),
    [repositories]
  );
  const [selectedPath, setSelectedPath] = useState(ROOT_PATH);
  const [collapsedPaths, setCollapsedPaths] = useState<Set<string>>(
    () => new Set()
  );
  const effectiveSelectedPath =
    selectedPath === ROOT_PATH || folderPaths.includes(selectedPath)
      ? selectedPath
      : ROOT_PATH;
  const expandedPaths = useMemo(
    () => new Set(folderPaths.filter((path) => !collapsedPaths.has(path))),
    [collapsedPaths, folderPaths]
  );

  const selectedRepositories = useMemo(() => {
    if (effectiveSelectedPath === ROOT_PATH) {
      return repositories;
    }

    return repositories.filter((repository) => {
      const folderPath = getRepositoryFolderPath(repository);
      return (
        folderPath === effectiveSelectedPath ||
        folderPath.startsWith(`${effectiveSelectedPath}/`)
      );
    });
  }, [repositories, effectiveSelectedPath]);

  const breadcrumbSegments = effectiveSelectedPath
    ? effectiveSelectedPath.split("/")
    : [];

  const handleToggle = (path: string) => {
    setCollapsedPaths((current) => {
      const next = new Set(current);
      if (next.has(path)) {
        next.delete(path);
      } else {
        next.add(path);
      }
      return next;
    });
  };

  if (repositories.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-center">
        <GitBranch className="mb-4 h-12 w-12 text-muted-foreground" />
        <p className="text-muted-foreground">{emptyMessage}</p>
      </div>
    );
  }

  return (
    <Card
      className={cn(
        "grid overflow-hidden rounded-lg border bg-background shadow-sm md:grid-cols-[280px_minmax(0,1fr)]",
        className
      )}
    >
      <aside className="border-b bg-muted/20 p-3 md:border-b-0 md:border-r">
        <div className="mb-3 flex items-center justify-between gap-3 px-1">
          <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            {labels.treeTitle}
          </div>
          <span className="rounded-full bg-background px-2 py-0.5 text-xs text-muted-foreground">
            {repositories.length}
          </span>
        </div>
        <div className="max-h-72 space-y-1 overflow-y-auto pr-1 md:max-h-[640px]">
          <button
            type="button"
            className={cn(
              "mb-1 flex w-full min-w-0 items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors",
              effectiveSelectedPath === ROOT_PATH
                ? "bg-primary/10 text-primary"
                : "text-muted-foreground hover:bg-secondary/60 hover:text-foreground"
            )}
            onClick={() => setSelectedPath(ROOT_PATH)}
          >
            <GitBranch className="h-4 w-4 shrink-0" />
            <span className="truncate font-medium">{labels.allRepositories}</span>
            <span className="ml-auto shrink-0 rounded-full bg-secondary px-1.5 py-0.5 text-[11px] text-muted-foreground">
              {repositories.length}
            </span>
          </button>
          {sortTreeNodes(root.children.values()).map((node) => (
            <TreeRow
              key={node.path}
              node={node}
              depth={0}
              selectedPath={effectiveSelectedPath}
              expandedPaths={expandedPaths}
              labels={labels}
              onSelect={setSelectedPath}
              onToggle={handleToggle}
            />
          ))}
        </div>
      </aside>

      <section className="min-w-0 bg-muted/5 p-4 md:p-5">
        <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0">
            <div className="flex min-w-0 flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
              <button
                type="button"
                className="hover:text-foreground"
                onClick={() => setSelectedPath(ROOT_PATH)}
              >
                {labels.allRepositories}
              </button>
              {breadcrumbSegments.map((segment, index) => {
                const path = breadcrumbSegments.slice(0, index + 1).join("/");
                return (
                  <span key={path} className="flex min-w-0 items-center gap-1.5">
                    <ChevronRight className="h-3.5 w-3.5 shrink-0" />
                    <button
                      type="button"
                      className={cn(
                        "max-w-[160px] truncate hover:text-foreground",
                        index === breadcrumbSegments.length - 1 &&
                          "font-medium text-foreground"
                      )}
                      onClick={() => setSelectedPath(path)}
                    >
                      {segment}
                    </button>
                  </span>
                );
              })}
            </div>
          </div>
          <div className="text-sm text-muted-foreground">
            {labels.repositoryCount(selectedRepositories.length)}
          </div>
        </div>

        {selectedRepositories.length === 0 ? (
          <div className="flex min-h-48 flex-col items-center justify-center rounded-lg border border-dashed bg-background/60 p-8 text-center">
            <FolderOpen className="mb-3 h-10 w-10 text-muted-foreground" />
            <p className="text-sm text-muted-foreground">
              {labels.emptyFolder}
            </p>
          </div>
        ) : (
          <div
            className={cn(
              "grid auto-rows-fr grid-cols-1 gap-4 xl:grid-cols-2 2xl:grid-cols-3",
              contentClassName
            )}
          >
            {selectedRepositories.map((repository) => (
              <div key={repository.id} className="h-full min-w-0">
                {renderRepository(repository)}
              </div>
            ))}
          </div>
        )}
      </section>
    </Card>
  );
}
