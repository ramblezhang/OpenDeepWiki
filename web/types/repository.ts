import type { RepositorySourceTypeName, RepositorySourceTypeValue } from "@/lib/repository-source";

export interface RepoTreeNode {
  title: string;
  slug: string;
  children: RepoTreeNode[];
}

export interface RepoTreeResponse {
  owner: string;
  repo: string;
  defaultSlug: string;
  nodes: RepoTreeNode[];
  status: number;
  statusName: RepositoryStatus;
  effectiveStatus?: RepositoryEffectiveStatus;
  effectiveStatusReason?: string;
  statusCounts?: RepositoryStatusCounts;
  activeOperations?: RepositoryActiveOperation[];
  blockingFailures?: RepositoryBlockingFailure[];
  exists: boolean;
  currentBranch: string;
  currentLanguage: string;
  hasGraphifyArtifact?: boolean;
  graphifyStatus?: number | null;
  graphifyStatusName?: string | null;
}

export interface RepoBranchesResponse {
  repositoryId: string;
  branches: BranchItem[];
  languages: string[];
  defaultBranch: string;
  defaultLanguage: string;
}

export interface BranchItem {
  name: string;
  id?: string;
  generationStatus?: string;
  lastGenerationTaskId?: string;
  lastGenerationError?: string;
  lastGenerationStartedAt?: string;
  lastGenerationCompletedAt?: string;
  languages: string[];
}

// Git platform branches response (from GitHub/Gitee/GitLab API)
export interface GitBranchesResponse {
  branches: GitBranchItem[];
  defaultBranch: string | null;
  isSupported: boolean;
}

export interface GitBranchItem {
  name: string;
  isDefault: boolean;
}

export type RepositorySourceType = RepositorySourceTypeName;

export interface RepoDocResponse {
  exists: boolean;
  slug: string;
  content: string;
  sourceFiles: string[];
  gitUrl: string | null;
  branch: string | null;
}

export interface RepoHeading {
  id: string;
  text: string;
  level: number;
}

// Repository submission and list types
export type RepositoryStatus = "Pending" | "Processing" | "Completed" | "Failed";
export type RepositoryEffectiveStatus =
  | "RepositoryFullPending"
  | "RepositoryFullProcessing"
  | "AllBranchesQueued"
  | "PartialBranchesQueued"
  | "AllBranchesGenerating"
  | "PartialBranchesGenerating"
  | "IncrementalUpdating"
  | "Failed"
  | "PartialFailed"
  | "Completed"
  | "Cancelled"
  | "Unknown";

export interface RepositoryStatusCounts {
  totalBranches: number;
  completedBranches: number;
  failedBranches: number;
  branchFullPending: number;
  branchFullProcessing: number;
  incrementalPending: number;
  incrementalProcessing: number;
}

export interface RepositoryActiveOperation {
  type: string;
  repositoryId?: string;
  branchId?: string;
  branchName?: string;
  taskId?: string;
  status: string;
  createdAt: string;
  startedAt?: string;
}

export interface RepositoryBlockingFailure {
  type: string;
  branchId?: string;
  branchName?: string;
  taskId?: string;
  reason: string;
  message?: string;
}

export interface RepositorySubmitRequest {
  gitUrl: string;
  repoName: string;
  orgName: string;
  authAccount?: string;
  authPassword?: string;
  branchName: string;
  languageCode: string;
  isPublic: boolean;
  generateSkill: boolean;
}

export interface ArchiveRepositorySubmitRequest {
  repoName: string;
  orgName: string;
  branchName: string;
  languageCode: string;
  isPublic: boolean;
  generateSkill: boolean;
  archive: File;
}

export interface LocalDirectoryRepositorySubmitRequest {
  repoName: string;
  orgName: string;
  localPath: string;
  branchName: string;
  languageCode: string;
  isPublic: boolean;
  generateSkill: boolean;
}

export interface RepositoryItemResponse {
  id: string;
  orgName: string;
  repoName: string;
  gitUrl: string;
  sourceType: RepositorySourceTypeValue;
  sourceTypeName: RepositorySourceType;
  sourceLocation: string;
  status: number;
  statusName: RepositoryStatus;
  isPublic: boolean;
  generateSkill: boolean;
  hasPassword: boolean;  // 新增：是否设置了密码，用于判断是否可设为私有
  createdAt: string;
  updatedAt?: string;
  starCount?: number;
  forkCount?: number;
  primaryLanguage?: string;
  branchGenerationActiveCount?: number;
  branchGenerationFailedCount?: number;
  effectiveStatus?: RepositoryEffectiveStatus;
  effectiveStatusReason?: string;
  statusCounts?: RepositoryStatusCounts;
  activeOperations?: RepositoryActiveOperation[];
  blockingFailures?: RepositoryBlockingFailure[];
}

export interface RepositoryListResponse {
  items: RepositoryItemResponse[];
  total: number;
}

// Visibility update types for private repository management
export interface UpdateVisibilityRequest {
  repositoryId: string;
  isPublic: boolean;
}

export interface UpdateVisibilityResponse {
  id: string;
  isPublic: boolean;
  success: boolean;
  errorMessage?: string;
}

// Processing log types
export type ProcessingStep = "Workspace" | "Catalog" | "Content" | "Translation" | "MindMap" | "Complete" | "Graphify";

// 思维导图状态
export type MindMapStatus = "Pending" | "Processing" | "Completed" | "Failed";

// 思维导图状态数字到字符串的映射
export const MindMapStatusMap: Record<number, MindMapStatus> = {
  0: "Pending",
  1: "Processing",
  2: "Completed",
  3: "Failed",
};

// 思维导图响应
export interface MindMapResponse {
  owner: string;
  repo: string;
  branch: string;
  language: string;
  status: number;
  statusName: MindMapStatus;
  content: string | null;
}

// 思维导图节点（解析后的结构）
export interface MindMapNode {
  title: string;
  filePath?: string;
  level: number;
  children: MindMapNode[];
}

// 步骤数字到字符串的映射
export const ProcessingStepMap: Record<number, ProcessingStep> = {
  0: "Workspace",
  1: "Catalog",
  2: "Content",
  3: "Translation",
  4: "MindMap",
  5: "Complete",
  6: "Graphify",
};

export interface ProcessingLogItem {
  id: string;
  branchId?: string;
  generationTaskId?: string;
  step: number;
  stepName: ProcessingStep;
  message: string;
  isAiOutput: boolean;
  toolName?: string;
  createdAt: string;
}

export interface ProcessingLogResponse {
  status: number;
  statusName: RepositoryStatus;
  currentStep: number;
  currentStepName: ProcessingStep;
  totalDocuments: number;
  completedDocuments: number;
  startedAt: string | null;
  logs: ProcessingLogItem[];
}

// GitHub repo check response
export interface GitRepoCheckResponse {
  exists: boolean;
  name: string | null;
  description: string | null;
  defaultBranch: string | null;
  starCount: number;
  forkCount: number;
  language: string | null;
  avatarUrl: string | null;
  isPrivate: boolean;
  gitUrl: string | null;
}

// Branch generation task responses
export interface BranchGenerationTaskResponse {
  success: boolean;
  taskId: string;
  repositoryId: string;
  branchId: string;
  repositoryName?: string;
  branchName?: string;
  status: string;
  mode: string;
  priority: number;
  isManualTrigger: boolean;
  retryCount: number;
  errorMessage?: string;
  requestedBy?: string;
  targetCommitId?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface BranchGenerationLockResponse {
  repositoryId: string;
  ownerType: string;
  ownerId: string;
  scope: string;
  acquiredAt: string;
}

export interface BranchGenerationErrorResponse {
  success: boolean;
  errorCode: string;
  error: string;
  activeTask?: BranchGenerationTaskResponse;
  activeLock?: BranchGenerationLockResponse;
}
