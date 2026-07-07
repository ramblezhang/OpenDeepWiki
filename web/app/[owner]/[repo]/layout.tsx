import React from "react";
import type { Metadata } from "next";
import { headers } from "next/headers";
import { fetchRepoTree, fetchRepoBranches, checkGitHubRepo, fetchProcessingLogs } from "@/lib/repository-api";
import { RepoShell } from "@/components/repo/repo-shell";
import { RepositoryProcessingStatus } from "@/components/repo/repository-processing-status";
import { RepositoryNotFound } from "@/components/repo/repository-not-found";
import { decodeRouteSegment } from "@/lib/repo-route";
import { getRepoQueryFromHeaders } from "@/lib/repo-query-context";
import { indexableMetadata, noIndexMetadata, repoCanonicalPath, repoTitle, SITE_DESCRIPTION } from "@/lib/repo-seo";
import RouteProviders from "@/app/route-providers";

// 禁用缓存
export const dynamic = "force-dynamic";

interface RepoLayoutProps {
  children: React.ReactNode;
  params: Promise<{
    owner: string;
    repo: string;
  }>;
}

async function getTreeData(owner: string, repo: string, branch?: string, lang?: string) {
  try {
    const tree = await fetchRepoTree(owner, repo, branch, lang);
    return tree;
  } catch {
    return null;
  }
}

async function getBranchesData(owner: string, repo: string) {
  try {
    const branches = await fetchRepoBranches(owner, repo);
    return branches;
  } catch {
    return null;
  }
}

async function getGitHubInfo(owner: string, repo: string) {
  try {
    return await checkGitHubRepo(owner, repo);
  } catch {
    return null;
  }
}

async function getProcessingStatus(owner: string, repo: string) {
  try {
    const logs = await fetchProcessingLogs(owner, repo, undefined, 1);
    if (logs.statusName === "Pending" || logs.statusName === "Processing" || logs.statusName === "Failed") {
      return logs.statusName;
    }
  } catch {
    return null;
  }

  return null;
}

export async function generateMetadata({ params }: Pick<RepoLayoutProps, "params">): Promise<Metadata> {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const title = `${repoTitle(decodedOwner, decodedRepo)} Wiki`;
  const description = `AI-generated documentation and code knowledge base for ${repoTitle(decodedOwner, decodedRepo)}.`;
  const canonicalPath = repoCanonicalPath(decodedOwner, decodedRepo);
  const { branch, lang } = getRepoQueryFromHeaders(await headers());
  const tree = await getTreeData(decodedOwner, decodedRepo, branch, lang);

  if (!tree?.exists || tree.statusName !== "Completed" || tree.nodes.length === 0) {
    return noIndexMetadata(title, description || SITE_DESCRIPTION, canonicalPath);
  }

  return indexableMetadata({
    title,
    description,
    canonicalPath,
  });
}

export default async function RepoLayout({ children, params }: RepoLayoutProps) {
  const { owner, repo } = await params;
  const decodedOwner = decodeRouteSegment(owner);
  const decodedRepo = decodeRouteSegment(repo);
  const { branch, lang } = getRepoQueryFromHeaders(await headers());
  
  const tree = await getTreeData(decodedOwner, decodedRepo, branch, lang);

  let content: React.ReactNode;

  // API请求失败或仓库不存在时，先查本地处理状态，再检查GitHub
  if (!tree || !tree.exists) {
    const processingStatus = await getProcessingStatus(decodedOwner, decodedRepo);
    if (processingStatus) {
      content = (
        <RepositoryProcessingStatus
          owner={decodedOwner}
          repo={decodedRepo}
          branch={branch}
          lang={lang}
          status={processingStatus}
        />
      );
    } else {
      const gitHubInfo = await getGitHubInfo(decodedOwner, decodedRepo);
      content = <RepositoryNotFound owner={decodedOwner} repo={decodedRepo} gitHubInfo={gitHubInfo} />;
    }
  }
  // 仓库正在处理中或等待处理
  else if (tree.statusName === "Pending" || tree.statusName === "Processing" || tree.statusName === "Failed") {
    content = (
      <RepositoryProcessingStatus
        owner={decodedOwner}
        repo={decodedRepo}
        branch={branch}
        lang={lang}
        status={tree.statusName}
        effectiveStatus={tree.effectiveStatus}
        effectiveStatusReason={tree.effectiveStatusReason}
        statusCounts={tree.statusCounts}
      />
    );
  }
  // 仓库已完成但没有文档
  else if (tree.nodes.length === 0) {
    content = (
      <RepositoryProcessingStatus
        owner={decodedOwner}
        repo={decodedRepo}
        branch={branch}
        lang={lang}
        status="Completed"
        effectiveStatus={tree.effectiveStatus}
        effectiveStatusReason={tree.effectiveStatusReason}
        statusCounts={tree.statusCounts}
      />
    );
  }
  else {
    // 获取分支和语言数据
    const branches = await getBranchesData(decodedOwner, decodedRepo);

    return (
      <RepoShell
        owner={decodedOwner}
        repo={decodedRepo}
        initialNodes={tree.nodes}
        initialBranches={branches ?? undefined}
        initialBranch={tree.currentBranch}
        initialLanguage={tree.currentLanguage}
        initialHasGraphifyArtifact={tree.hasGraphifyArtifact}
      >
        {children}
      </RepoShell>
    );
  }

  // For non-ready states, wrap content in RouteProviders
  return (
    <RouteProviders>
      {content}
    </RouteProviders>
  );
}
