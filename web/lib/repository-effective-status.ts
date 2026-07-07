import type { RepositoryEffectiveStatus, RepositoryStatus } from "@/types/repository";

export function getRepositoryEffectiveStatus(status?: RepositoryEffectiveStatus, fallback?: RepositoryStatus) {
  return status ?? fallback ?? "Unknown";
}

export function getRepositoryEffectiveStatusLabel(status?: RepositoryEffectiveStatus, fallback?: RepositoryStatus) {
  const effectiveStatus = getRepositoryEffectiveStatus(status, fallback);

  switch (effectiveStatus) {
    case "RepositoryFullPending":
      return "全仓待生成";
    case "RepositoryFullProcessing":
      return "全仓生成中";
    case "AllBranchesQueued":
      return "全部分支排队中";
    case "PartialBranchesQueued":
      return "部分分支排队中";
    case "AllBranchesGenerating":
      return "全部分支生成中";
    case "PartialBranchesGenerating":
      return "部分分支生成中";
    case "IncrementalUpdating":
      return "增量更新中";
    case "PartialFailed":
      return "部分失败";
    case "Failed":
      return "生成失败";
    case "Completed":
      return "已完成";
    case "Cancelled":
      return "已取消";
    case "Pending":
      return "待处理";
    case "Processing":
      return "处理中";
    default:
      return "未知";
  }
}

export function getRepositoryEffectiveStatusClassName(status?: RepositoryEffectiveStatus, fallback?: RepositoryStatus) {
  const effectiveStatus = getRepositoryEffectiveStatus(status, fallback);

  switch (effectiveStatus) {
    case "RepositoryFullPending":
    case "AllBranchesQueued":
    case "PartialBranchesQueued":
    case "Pending":
      return "text-yellow-600 bg-yellow-500/10";
    case "RepositoryFullProcessing":
    case "AllBranchesGenerating":
    case "PartialBranchesGenerating":
    case "Processing":
      return "text-blue-600 bg-blue-500/10";
    case "IncrementalUpdating":
      return "text-cyan-600 bg-cyan-500/10";
    case "Completed":
      return "text-green-600 bg-green-500/10";
    case "Failed":
    case "PartialFailed":
      return "text-red-600 bg-red-500/10";
    case "Cancelled":
      return "text-slate-600 bg-slate-500/10";
    default:
      return "text-muted-foreground bg-muted";
  }
}

export function isRepositoryEffectiveStatusActive(status?: RepositoryEffectiveStatus) {
  return status === "RepositoryFullProcessing" ||
    status === "AllBranchesGenerating" ||
    status === "PartialBranchesGenerating" ||
    status === "IncrementalUpdating";
}
