import type { RepositorySourceTypeName, RepositorySourceTypeValue } from "@/lib/repository-source";
import type {
  RepositoryActiveOperation,
  RepositoryBlockingFailure,
  RepositoryEffectiveStatus,
  RepositoryStatusCounts,
} from "@/types/repository";
import { getApiProxyUrl } from "./env";
import { getToken } from "./auth-api";
import { ApiError, api } from "./api-client";

function buildApiUrl(path: string) {
  const baseUrl = getApiProxyUrl();
  if (!baseUrl) {
    return path;
  }
  const trimmedBase = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  return `${trimmedBase}${path}`;
}

async function fetchWithAuth(url: string, options: RequestInit = {}) {
  const token = getToken();
  const headers: HeadersInit = {
    "Content-Type": "application/json",
    ...(options.headers || {}),
  };

  if (token) {
    (headers as Record<string, string>)["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(url, { ...options, headers });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || error.error || error.errorMessage || `Request failed: ${response.status}`);
  }

  return response.json();
}

// ==================== Statistics API ====================

export interface DailyRepositoryStatistic {
  date: string;
  processedCount: number;
  submittedCount: number;
}

export interface DailyUserStatistic {
  date: string;
  newUserCount: number;
}

export interface DashboardStatistics {
  repositoryStats: DailyRepositoryStatistic[];
  userStats: DailyUserStatistic[];
}

export interface DailyTokenUsage {
  date: string;
  inputTokens: number;
  outputTokens: number;
  cachedInputTokens: number;
  cacheCreationInputTokens: number;
  totalTokens: number;
  inputCacheHitRate: number;
  inputCost: number;
  outputCost: number;
  totalCost: number;
}

export interface TokenUsageStatistics {
  dailyUsages: DailyTokenUsage[];
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCachedInputTokens: number;
  totalCacheCreationInputTokens: number;
  totalTokens: number;
  inputCacheHitRate: number;
  totalInputCost: number;
  totalOutputCost: number;
  totalCost: number;
}

export interface McpDailyUsage {
  date: string;
  requestCount: number;
  successCount: number;
  errorCount: number;
  inputTokens: number;
  outputTokens: number;
}

export interface McpUsageStatistics {
  dailyUsages: McpDailyUsage[];
  totalRequests: number;
  totalSuccessful: number;
  totalErrors: number;
  totalInputTokens: number;
  totalOutputTokens: number;
}

export async function getDashboardStatistics(days: number = 7): Promise<DashboardStatistics> {
  const url = buildApiUrl(`/api/admin/statistics/dashboard?days=${days}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getTokenUsageStatistics(days: number = 7): Promise<TokenUsageStatistics> {
  const url = buildApiUrl(`/api/admin/statistics/token-usage?days=${days}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getMcpUsageStatistics(days: number = 7): Promise<McpUsageStatistics> {
  const url = buildApiUrl(`/api/admin/statistics/mcp-usage?days=${days}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

// ==================== MCP Provider API ====================

export interface McpProviderRequest {
  name: string;
  description?: string;
  serverUrl: string;
  transportType: string;
  requiresApiKey: boolean;
  apiKeyObtainUrl?: string;
  systemApiKey?: string;
  modelConfigId?: string;
  isActive: boolean;
  sortOrder: number;
  iconUrl?: string;
  maxRequestsPerDay: number;
}

export interface McpProvider {
  id: string;
  name: string;
  description?: string;
  serverUrl: string;
  transportType: string;
  requiresApiKey: boolean;
  apiKeyObtainUrl?: string;
  hasSystemApiKey: boolean;
  modelConfigId?: string;
  modelConfigName?: string;
  isActive: boolean;
  sortOrder: number;
  iconUrl?: string;
  maxRequestsPerDay: number;
  createdAt: string;
}

export interface McpUsageLog {
  id: string;
  userId?: string;
  userName?: string;
  mcpProviderId?: string;
  mcpProviderName?: string;
  toolName: string;
  requestSummary?: string;
  responseStatus: number;
  durationMs: number;
  inputTokens: number;
  outputTokens: number;
  ipAddress?: string;
  errorMessage?: string;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface McpUsageLogFilter {
  mcpProviderId?: string;
  userId?: string;
  toolName?: string;
  page?: number;
  pageSize?: number;
}

export async function getMcpProviders(): Promise<McpProvider[]> {
  const url = buildApiUrl("/api/admin/mcp-providers");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createMcpProvider(data: McpProviderRequest): Promise<McpProvider> {
  const url = buildApiUrl("/api/admin/mcp-providers");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateMcpProvider(id: string, data: McpProviderRequest): Promise<void> {
  const url = buildApiUrl(`/api/admin/mcp-providers/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteMcpProvider(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/mcp-providers/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getMcpUsageLogs(filter: McpUsageLogFilter): Promise<PagedResult<McpUsageLog>> {
  const params = new URLSearchParams();
  if (filter.mcpProviderId) params.append("mcpProviderId", filter.mcpProviderId);
  if (filter.userId) params.append("userId", filter.userId);
  if (filter.toolName) params.append("toolName", filter.toolName);
  params.append("page", (filter.page ?? 1).toString());
  params.append("pageSize", (filter.pageSize ?? 20).toString());

  const url = buildApiUrl(`/api/admin/mcp-providers/usage-logs?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

// ==================== Repository API ====================

export interface AdminRepository {
  id: string;
  gitUrl: string;
  sourceType: RepositorySourceTypeValue;
  sourceTypeName: RepositorySourceTypeName;
  sourceLocation: string;
  repoName: string;
  orgName: string;
  isPublic: boolean;
  generateSkill: boolean;
  status: number;
  statusText: string;
  effectiveStatus?: RepositoryEffectiveStatus;
  effectiveStatusReason?: string;
  statusCounts?: RepositoryStatusCounts;
  activeOperations?: RepositoryActiveOperation[];
  blockingFailures?: RepositoryBlockingFailure[];
  scanDepthMode: "Auto" | "Manual";
  scanPlan?: AdminRepositoryScanPlan;
  starCount: number;
  forkCount: number;
  bookmarkCount: number;
  viewCount: number;
  branchGenerationActiveCount: number;
  branchGenerationFailedCount: number;
  ownerUserId?: string;
  ownerUserName?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface RepositoryListResponse {
  items: AdminRepository[];
  total: number;
  page: number;
  pageSize: number;
}

export async function getRepositories(
  page: number = 1,
  pageSize: number = 20,
  search?: string,
  status?: number
): Promise<RepositoryListResponse> {
  const params = new URLSearchParams({
    page: page.toString(),
    pageSize: pageSize.toString(),
  });
  if (search) params.append("search", search);
  if (status !== undefined) params.append("status", status.toString());

  const url = buildApiUrl(`/api/admin/repositories?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getRepository(id: string): Promise<AdminRepository> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateRepository(id: string, data: {
  isPublic?: boolean;
  authAccount?: string;
  authPassword?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteRepository(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function updateRepositoryStatus(id: string, status: number): Promise<void> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/status`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ status }),
  });
}

// Sync individual repository statistics
export interface SyncStatsResult {
  success: boolean;
  message?: string;
  starCount: number;
  forkCount: number;
}

export async function syncRepositoryStats(id: string): Promise<SyncStatsResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/sync-stats`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result.data;
}

// Batch sync statistics
export interface BatchSyncItemResult {
  id: string;
  repoName: string;
  success: boolean;
  message?: string;
  starCount: number;
  forkCount: number;
}

export interface BatchSyncStatsResult {
  totalCount: number;
  successCount: number;
  failedCount: number;
  results: BatchSyncItemResult[];
}

export async function batchSyncRepositoryStats(ids: string[]): Promise<BatchSyncStatsResult> {
  const url = buildApiUrl("/api/admin/repositories/batch/sync-stats");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ ids }),
  });
  return result.data;
}

// Batch delete repositories
export interface BatchDeleteResult {
  totalCount: number;
  successCount: number;
  failedCount: number;
  failedIds: string[];
}

export async function batchDeleteRepositories(ids: string[]): Promise<BatchDeleteResult> {
  const url = buildApiUrl("/api/admin/repositories/batch/delete");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ ids }),
  });
  return result.data;
}

export interface AdminBranchLanguage {
  id: string;
  languageCode: string;
  isDefault: boolean;
  catalogCount: number;
  documentCount: number;
  createdAt: string;
}

export interface AdminRepositoryBranch {
  id: string;
  name: string;
  lastCommitId?: string;
  lastProcessedAt?: string;
  generationStatus?: string;
  lastGenerationTaskId?: string;
  lastGenerationError?: string;
  lastGenerationStartedAt?: string;
  lastGenerationCompletedAt?: string;
  languages: AdminBranchLanguage[];
}

export interface AdminIncrementalTask {
  taskId: string;
  branchId: string;
  branchName?: string;
  status: string;
  priority: number;
  isManualTrigger: boolean;
  retryCount: number;
  previousCommitId?: string;
  targetCommitId?: string;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface AdminRepositoryManagement {
  repositoryId: string;
  orgName: string;
  repoName: string;
  status: number;
  statusText: string;
  effectiveStatus?: RepositoryEffectiveStatus;
  effectiveStatusReason?: string;
  statusCounts?: RepositoryStatusCounts;
  activeOperations?: RepositoryActiveOperation[];
  blockingFailures?: RepositoryBlockingFailure[];
  branches: AdminRepositoryBranch[];
  recentIncrementalTasks: AdminIncrementalTask[];
  recentBranchGenerationTasks: AdminBranchGenerationTask[];
  scanPlan?: AdminRepositoryScanPlan;
}

export interface AdminRepositoryOperationResult {
  success: boolean;
  message: string;
}

export interface AdminBranchGenerationTask {
  taskId: string;
  repositoryId: string;
  branchId: string;
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

export interface AdminRepositoryScanPlan {
  source: string;
  mode: "Auto" | "Manual";
  directoryTreeDepth: number;
  fileListDepth: number;
  maxTreeNodes: number;
  maxFilesPerDirectory: number;
  maxTotalFiles: number;
  extraExcludedDirs: string[];
  profileHash?: string;
  reason?: string;
  confidence?: number;
  updatedAt?: string;
}

export interface UpdateRepositoryScanPlanPayload {
  mode: "Auto" | "Manual";
  directoryTreeDepth?: number;
  fileListDepth?: number;
  maxTreeNodes?: number;
  maxFilesPerDirectory?: number;
  maxTotalFiles?: number;
  extraExcludedDirs?: string[];
}

export interface AdminGraphifyArtifact {
  id: string;
  repositoryId: string;
  repositoryBranchId: string;
  branchName: string;
  status: number;
  statusName: string;
  commitId?: string;
  errorMessage?: string;
  createdAt: string;
  updatedAt?: string;
  startedAt?: string;
  completedAt?: string;
}

export interface RegenerateRepositoryDocumentPayload {
  branchId: string;
  languageCode: string;
  documentPath: string;
}

export interface UpdateRepositoryDocumentContentPayload {
  branchId: string;
  languageCode: string;
  documentPath: string;
  content: string;
}

export async function getRepositoryManagement(id: string): Promise<AdminRepositoryManagement> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/management`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function regenerateRepository(id: string): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/regenerate`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result.data;
}

export async function enqueueBranchFullGeneration(
  repositoryId: string,
  branchId: string
): Promise<AdminBranchGenerationTask> {
  const url = buildApiUrl(`/api/v1/repositories/${repositoryId}/branches/${branchId}/generation-tasks/full`);
  return fetchWithAuth(url, { method: "POST" });
}

export async function retryBranchGenerationTask(taskId: string): Promise<AdminBranchGenerationTask> {
  const url = buildApiUrl(`/api/v1/branch-generation-tasks/${taskId}/retry`);
  return fetchWithAuth(url, { method: "POST" });
}

export async function cancelBranchGenerationTask(taskId: string): Promise<AdminBranchGenerationTask> {
  const url = buildApiUrl(`/api/v1/branch-generation-tasks/${taskId}/cancel`);
  return fetchWithAuth(url, { method: "POST" });
}

export async function getRepositoryScanPlan(id: string): Promise<AdminRepositoryScanPlan> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/scan-plan`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export interface AdminRepositoryScanPlanOperationResult {
  success: boolean;
  message?: string;
  data: AdminRepositoryScanPlan;
}

export async function updateRepositoryScanPlan(
  id: string,
  data: UpdateRepositoryScanPlanPayload
): Promise<AdminRepositoryScanPlan> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/scan-plan`);
  const result = await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function reevaluateRepositoryScanPlan(id: string): Promise<AdminRepositoryScanPlanOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/scan-plan/reevaluate`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result; // return the full result containing success, message, and data
}

export async function getRepositoryGraphifyArtifacts(id: string): Promise<AdminGraphifyArtifact[]> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/graphify`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function generateRepositoryGraphify(
  id: string,
  branchId?: string
): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/graphify/generate`);
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ branchId }),
  });
  return result.data;
}

export async function regenerateRepositoryDocument(
  id: string,
  data: RegenerateRepositoryDocumentPayload
): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/documents/regenerate`);
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateRepositoryDocumentContent(
  id: string,
  data: UpdateRepositoryDocumentContentPayload
): Promise<AdminRepositoryOperationResult> {
  const url = buildApiUrl(`/api/admin/repositories/${id}/documents/content`);
  const result = await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
  return result.data;
}

export interface IncrementalUpdateTriggerResult {
  success: boolean;
  taskId: string;
  status: string;
  message: string;
}

export interface IncrementalUpdateTaskStatus {
  success: boolean;
  taskId: string;
  repositoryId: string;
  repositoryName?: string;
  branchId: string;
  branchName?: string;
  status: string;
  priority: number;
  isManualTrigger: boolean;
  previousCommitId?: string;
  targetCommitId?: string;
  retryCount: number;
  errorMessage?: string;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}

export interface IncrementalUpdateRetryResult {
  success: boolean;
  taskId: string;
  status: string;
  retryCount: number;
  message: string;
}

interface IncrementalUpdateErrorResponse {
  success: false;
  errorCode?: string;
  error?: string;
  details?: string;
}

export function isLocalGitWorktreeDirtyError(error: unknown): boolean {
  if (!(error instanceof ApiError) || error.status !== 409 || !error.data || typeof error.data !== "object") {
    return false;
  }

  return (error.data as Partial<IncrementalUpdateErrorResponse>).errorCode === "LOCAL_GIT_WORKTREE_DIRTY";
}

export async function triggerRepositoryIncrementalUpdate(
  repositoryId: string,
  branchId: string
): Promise<IncrementalUpdateTriggerResult> {
  return api.post<IncrementalUpdateTriggerResult>(
    `/api/v1/repositories/${repositoryId}/branches/${branchId}/incremental-update`
  );
}

export async function getIncrementalUpdateTask(
  taskId: string
): Promise<IncrementalUpdateTaskStatus> {
  const url = buildApiUrl(`/api/v1/incremental-updates/${taskId}`);
  return fetchWithAuth(url);
}

export async function retryIncrementalUpdateTask(
  taskId: string
): Promise<IncrementalUpdateRetryResult> {
  const url = buildApiUrl(`/api/v1/incremental-updates/${taskId}/retry`);
  return fetchWithAuth(url, { method: "POST" });
}

// ==================== User API ====================

export interface AdminUser {
  id: string;
  name: string;
  email?: string;
  avatar?: string;
  status: number;
  roles: string[];
  createdAt: string;
  updatedAt?: string;
}

export interface UserListResponse {
  items: AdminUser[];
  total: number;
  page: number;
  pageSize: number;
}

export async function getUsers(
  page: number = 1,
  pageSize: number = 20,
  search?: string,
  roleId?: string
): Promise<UserListResponse> {
  const params = new URLSearchParams({
    page: page.toString(),
    pageSize: pageSize.toString(),
  });
  if (search) params.append("search", search);
  if (roleId) params.append("roleId", roleId);

  const url = buildApiUrl(`/api/admin/users?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getUser(id: string): Promise<AdminUser> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createUser(data: {
  name: string;
  email: string;
  password: string;
  roleIds?: string[];
}): Promise<AdminUser> {
  const url = buildApiUrl("/api/admin/users");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateUser(id: string, data: {
  name?: string;
  email?: string;
  avatar?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteUser(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function updateUserStatus(id: string, status: number): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/status`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ status }),
  });
}

export async function updateUserRoles(id: string, roleIds: string[]): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/roles`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify({ roleIds }),
  });
}

export async function resetUserPassword(id: string, newPassword: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/users/${id}/reset-password`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ newPassword }),
  });
}

// ==================== Role API ====================

export interface AdminRole {
  id: string;
  name: string;
  description?: string;
  isSystem: boolean;
  userCount: number;
  createdAt: string;
}

export async function getRoles(): Promise<AdminRole[]> {
  const url = buildApiUrl("/api/admin/roles");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getRole(id: string): Promise<AdminRole> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createRole(data: {
  name: string;
  description?: string;
}): Promise<AdminRole> {
  const url = buildApiUrl("/api/admin/roles");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateRole(id: string, data: {
  name?: string;
  description?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteRole(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/roles/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Tools API - MCP ====================

export interface McpConfig {
  id: string;
  name: string;
  description?: string;
  serverUrl: string;
  apiKey?: string;
  isActive: boolean;
  sortOrder: number;
  createdAt: string;
}

export async function getMcpConfigs(): Promise<McpConfig[]> {
  const url = buildApiUrl("/api/admin/tools/mcps");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createMcpConfig(data: {
  name: string;
  description?: string;
  serverUrl: string;
  apiKey?: string;
  isActive?: boolean;
  sortOrder?: number;
}): Promise<McpConfig> {
  const url = buildApiUrl("/api/admin/tools/mcps");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateMcpConfig(id: string, data: {
  name?: string;
  description?: string;
  serverUrl?: string;
  apiKey?: string;
  isActive?: boolean;
  sortOrder?: number;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/mcps/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteMcpConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/mcps/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Tools API - Skill (Agent Skills Standard) ====================

export interface SkillFileInfo {
  fileName: string;
  relativePath: string;
  size: number;
  lastModified: string;
}

export interface SkillConfig {
  id: string;
  name: string;
  description: string;
  license?: string;
  compatibility?: string;
  allowedTools?: string;
  folderPath: string;
  isActive: boolean;
  sortOrder: number;
  author?: string;
  version: string;
  source: string;
  sourceUrl?: string;
  hasScripts: boolean;
  hasReferences: boolean;
  hasAssets: boolean;
  skillMdSize: number;
  totalSize: number;
  createdAt: string;
}

export interface SkillDetail extends SkillConfig {
  skillMdContent: string;
  scripts: SkillFileInfo[];
  references: SkillFileInfo[];
  assets: SkillFileInfo[];
}

export async function getSkillConfigs(): Promise<SkillConfig[]> {
  const url = buildApiUrl("/api/admin/tools/skills");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getSkillDetail(id: string): Promise<SkillDetail> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function uploadSkill(file: File): Promise<SkillConfig> {
  const token = getToken();
  const formData = new FormData();
  formData.append("file", file);

  const url = buildApiUrl("/api/admin/tools/skills/upload");
  const headers: HeadersInit = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const response = await fetch(url, {
    method: "POST",
    headers,
    body: formData,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.message || `Upload failed: ${response.status}`);
  }

  const result = await response.json();
  return result.data;
}

export async function updateSkillConfig(id: string, data: {
  isActive?: boolean;
  sortOrder?: number;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteSkillConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getSkillFileContent(id: string, filePath: string): Promise<string> {
  const url = buildApiUrl(`/api/admin/tools/skills/${id}/files/${filePath}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function refreshSkills(): Promise<void> {
  const url = buildApiUrl("/api/admin/tools/skills/refresh");
  await fetchWithAuth(url, { method: "POST" });
}

// ==================== Tools API - Model ====================

export interface ModelConfig {
  id: string;
  name: string;
  aiProviderId?: string;
  aiProviderName?: string;
  provider: string;
  modelId: string;
  isDefault: boolean;
  isActive: boolean;
  description?: string;
  createdAt: string;
}

export interface AiProviderConfig {
  id: string;
  name: string;
  displayName?: string;
  providerType: string;
  baseUrl: string;
  hasApiKey: boolean;
  authType: string;
  isBuiltIn: boolean;
  isActive: boolean;
  supportsModelDiscovery: boolean;
  modelsEndpoint?: string;
  defaultModelId?: string;
  systemProxyUrl?: string;
  oauthConfigJson?: string;
  channelConfigJson?: string;
  accountsJson?: string;
  requestOverridesJson?: string;
  iconUrl?: string;
  description?: string;
  sortOrder: number;
  createdAt: string;
}

export interface AiProviderConfigRequest {
  name: string;
  displayName?: string;
  providerType: string;
  baseUrl: string;
  apiKey?: string;
  authType: string;
  isBuiltIn?: boolean;
  isActive?: boolean;
  supportsModelDiscovery?: boolean;
  modelsEndpoint?: string;
  defaultModelId?: string;
  systemProxyUrl?: string;
  oauthConfigJson?: string;
  channelConfigJson?: string;
  accountsJson?: string;
  requestOverridesJson?: string;
  iconUrl?: string;
  description?: string;
  sortOrder?: number;
}

export interface AiModelConfig {
  id: string;
  providerId: string;
  providerName?: string;
  modelId: string;
  name: string;
  displayName?: string;
  modelType: string;
  providerType?: string;
  contextWindow?: number;
  maxOutputTokens?: number;
  inputTokenPrice?: number;
  outputTokenPrice?: number;
  cacheHitTokenPrice?: number;
  cacheCreationTokenPrice?: number;
  supportsThinking: boolean;
  supportsVision: boolean;
  supportsTools: boolean;
  supportsJsonMode: boolean;
  isDefault: boolean;
  isActive: boolean;
  capabilitiesJson?: string;
  thinkingConfigJson?: string;
  requestOverridesJson?: string;
  tagsJson?: string;
  description?: string;
  sortOrder: number;
  createdAt: string;
}

export type AiModelConfigRequest = Omit<AiModelConfig, "id" | "providerName" | "createdAt">;

export interface AiProviderConnectivityTestRequest {
  modelId?: string;
  providerType?: string;
  baseUrl?: string;
  apiKey?: string;
}

export interface AiProviderConnectivityTestResult {
  success: boolean;
  message: string;
  providerType: string;
  modelId: string;
  statusCode?: number;
  latencyMs: number;
}

export async function getModelConfigs(): Promise<ModelConfig[]> {
  const url = buildApiUrl("/api/admin/tools/models");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getAiProviders(): Promise<AiProviderConfig[]> {
  const url = buildApiUrl("/api/admin/tools/ai-providers");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createAiProvider(data: AiProviderConfigRequest): Promise<AiProviderConfig> {
  const url = buildApiUrl("/api/admin/tools/ai-providers");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateAiProvider(id: string, data: AiProviderConfigRequest): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/ai-providers/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteAiProvider(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/ai-providers/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getAiModels(providerId?: string): Promise<AiModelConfig[]> {
  const params = providerId ? `?providerId=${encodeURIComponent(providerId)}` : "";
  const url = buildApiUrl(`/api/admin/tools/ai-models${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createAiModel(data: AiModelConfigRequest): Promise<AiModelConfig> {
  const url = buildApiUrl("/api/admin/tools/ai-models");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateAiModel(id: string, data: AiModelConfigRequest): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/ai-models/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteAiModel(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/ai-models/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function discoverAiModels(providerId: string): Promise<AiModelConfig[]> {
  const url = buildApiUrl(`/api/admin/tools/ai-providers/${providerId}/discover-models`);
  const result = await fetchWithAuth(url, { method: "POST" });
  return result.data;
}

export async function testAiProviderConnectivity(
  providerId: string,
  request: AiProviderConnectivityTestRequest
): Promise<AiProviderConnectivityTestResult> {
  const url = buildApiUrl(`/api/admin/tools/ai-providers/${providerId}/test-connectivity`);
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(request),
  });
  return result.data;
}

export async function createModelConfig(data: {
  name: string;
  aiProviderId?: string;
  provider?: string;
  modelId: string;
  isDefault?: boolean;
  isActive?: boolean;
  description?: string;
}): Promise<ModelConfig> {
  const url = buildApiUrl("/api/admin/tools/models");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateModelConfig(id: string, data: {
  name?: string;
  aiProviderId?: string;
  provider?: string;
  modelId?: string;
  isDefault?: boolean;
  isActive?: boolean;
  description?: string;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/models/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteModelConfig(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/tools/models/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== Chat Provider API ====================

export interface ChatProviderStatus {
  platform: string;
  displayName: string;
  isEnabled: boolean;
  isRegistered: boolean;
  webhookUrl?: string;
  messageInterval: number;
  maxRetryCount: number;
}

export interface ChatProviderConfig {
  platform: string;
  displayName: string;
  isEnabled: boolean;
  configData: string;
  webhookUrl?: string;
  messageInterval: number;
  maxRetryCount: number;
}

export async function getChatProviderConfigs(): Promise<ChatProviderStatus[]> {
  const url = buildApiUrl("/api/chat/admin/providers");
  const result = await fetchWithAuth(url);
  return Array.isArray(result) ? result : result.data ?? [];
}

export async function getChatProviderConfig(platform: string): Promise<ChatProviderConfig> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}`);
  const result = await fetchWithAuth(url);
  return result.data ?? result;
}

export async function saveChatProviderConfig(data: ChatProviderConfig): Promise<void> {
  const url = buildApiUrl("/api/chat/admin/providers");
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
}

export async function deleteChatProviderConfig(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function enableChatProvider(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/enable`);
  await fetchWithAuth(url, { method: "POST" });
}

export async function disableChatProvider(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/disable`);
  await fetchWithAuth(url, { method: "POST" });
}

export async function reloadChatProviderConfig(platform: string): Promise<void> {
  const url = buildApiUrl(`/api/chat/admin/providers/${platform}/reload`);
  await fetchWithAuth(url, { method: "POST" });
}

// ==================== Settings API ====================

export interface SystemSetting {
  id: string;
  key: string;
  value?: string;
  description?: string;
  category: string;
}

export async function getSettings(category?: string): Promise<SystemSetting[]> {
  const params = category ? `?category=${category}` : "";
  const url = buildApiUrl(`/api/admin/settings${params}`);
  const result = await fetchWithAuth(url);
  return result.data || [];
}

export async function getSetting(key: string): Promise<SystemSetting> {
  const url = buildApiUrl(`/api/admin/settings/${key}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateSettings(settings: { key: string; value: string }[]): Promise<void> {
  const url = buildApiUrl("/api/admin/settings");
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(settings),
  });
}

export interface ProviderModel {
  id: string;
  displayName: string;
}

export async function listProviderModels(
  providerId: string
): Promise<ProviderModel[]> {
  const url = buildApiUrl("/api/admin/settings/list-provider-models");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ providerId }),
  });
  return result.data?.models || [];
}


// ==================== Department API ====================

export interface AdminDepartment {
  id: string;
  name: string;
  parentId?: string;
  parentName?: string;
  description?: string;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  children?: AdminDepartment[];
}

export async function getDepartments(): Promise<AdminDepartment[]> {
  const url = buildApiUrl("/api/admin/departments");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getDepartmentTree(): Promise<AdminDepartment[]> {
  const url = buildApiUrl("/api/admin/departments/tree");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getDepartment(id: string): Promise<AdminDepartment> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function createDepartment(data: {
  name: string;
  parentId?: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}): Promise<AdminDepartment> {
  const url = buildApiUrl("/api/admin/departments");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function updateDepartment(id: string, data: {
  name?: string;
  parentId?: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
}

export async function deleteDepartment(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}


// ==================== Department Users & Repositories API ====================

export interface DepartmentUser {
  id: string;
  userId: string;
  userName: string;
  email?: string;
  avatar?: string;
  isManager: boolean;
  createdAt: string;
}

export interface DepartmentRepository {
  id: string;
  repositoryId: string;
  repoName: string;
  orgName: string;
  gitUrl?: string;
  status: number;
  assigneeUserName?: string;
  createdAt: string;
}

export async function getDepartmentUsers(departmentId: string): Promise<DepartmentUser[]> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function addUserToDepartment(departmentId: string, userId: string, isManager: boolean = false): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ userId, isManager }),
  });
}

export async function removeUserFromDepartment(departmentId: string, userId: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/users/${userId}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getDepartmentRepositories(departmentId: string): Promise<DepartmentRepository[]> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function assignRepositoryToDepartment(
  departmentId: string,
  repositoryId: string,
  assigneeUserId: string
): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories`);
  await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ repositoryId, assigneeUserId }),
  });
}

export async function removeRepositoryFromDepartment(departmentId: string, repositoryId: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/departments/${departmentId}/repositories/${repositoryId}`);
  await fetchWithAuth(url, { method: "DELETE" });
}


// ==================== Chat Assistant Config API ====================

export interface SelectableItem {
  id: string;
  name: string;
  description?: string;
  isActive: boolean;
  isSelected: boolean;
}

export interface SelectableModelItem extends SelectableItem {
  aiProviderId?: string;
  aiProviderName?: string;
  aiProviderType?: string;
  aiProviderIsActive: boolean;
  modelId: string;
  modelName?: string;
  modelDisplayName?: string;
  contextWindow?: number;
  supportsThinking: boolean;
  supportsVision: boolean;
  supportsTools: boolean;
  supportsJsonMode: boolean;
}

export interface ChatAssistantConfig {
  id: string;
  isEnabled: boolean;
  enabledModelIds: string[];
  enabledMcpIds: string[];
  enabledSkillIds: string[];
  defaultModelId?: string;
  enableImageUpload: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface ChatAssistantConfigOptions {
  config: ChatAssistantConfig;
  availableModels: SelectableModelItem[];
  availableMcps: SelectableItem[];
  availableSkills: SelectableItem[];
}

export interface UpdateChatAssistantConfigRequest {
  isEnabled: boolean;
  enabledModelIds: string[];
  enabledMcpIds: string[];
  enabledSkillIds: string[];
  defaultModelId?: string;
  enableImageUpload?: boolean;
}

export async function getChatAssistantConfig(): Promise<ChatAssistantConfigOptions> {
  const url = buildApiUrl("/api/admin/chat-assistant/config");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function updateChatAssistantConfig(
  data: UpdateChatAssistantConfigRequest
): Promise<ChatAssistantConfig> {
  const url = buildApiUrl("/api/admin/chat-assistant/config");
  const result = await fetchWithAuth(url, {
    method: "PUT",
    body: JSON.stringify(data),
  });
  return result.data;
}

// ==================== GitHub Import API ====================

export interface GitHubInstallation {
  id: string;
  installationId: number;
  accountLogin: string;
  accountType: string;
  accountId: number;
  avatarUrl?: string;
  departmentId?: string;
  departmentName?: string;
  createdAt: string;
}

export interface GitHubStatus {
  configured: boolean;
  appName?: string;
  installations: GitHubInstallation[];
}

export interface GitHubConfig {
  hasAppId: boolean;
  hasPrivateKey: boolean;
  appId?: string;
  appName?: string;
  source: string;
}

export interface GitHubRepo {
  id: number;
  fullName: string;
  name: string;
  owner: string;
  private: boolean;
  description?: string;
  language?: string;
  stargazersCount: number;
  forksCount: number;
  defaultBranch: string;
  cloneUrl: string;
  htmlUrl: string;
  alreadyImported: boolean;
}

export interface GitHubRepoList {
  totalCount: number;
  repositories: GitHubRepo[];
  page: number;
  perPage: number;
}

export interface BatchImportResult {
  totalRequested: number;
  imported: number;
  skipped: number;
  skippedRepos: string[];
  importedRepos: string[];
}

export async function getGitHubStatus(): Promise<GitHubStatus> {
  const url = buildApiUrl("/api/admin/github/status");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function getGitHubInstallUrl(): Promise<{ url: string; appName: string }> {
  const url = buildApiUrl("/api/admin/github/install-url");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function storeGitHubInstallation(installationId: number): Promise<GitHubInstallation> {
  const url = buildApiUrl("/api/admin/github/installations");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify({ installationId }),
  });
  return result.data;
}

export async function disconnectGitHubInstallation(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/github/installations/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}

export async function getInstallationRepos(
  installationId: number,
  page: number = 1,
  perPage: number = 30
): Promise<GitHubRepoList> {
  const params = new URLSearchParams({
    page: page.toString(),
    perPage: perPage.toString(),
  });
  const url = buildApiUrl(`/api/admin/github/installations/${installationId}/repos?${params}`);
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function batchImportRepos(request: {
  installationId: number;
  departmentId: string;
  languageCode: string;
  generateSkill: boolean;
  repos: {
    fullName: string;
    name: string;
    owner: string;
    cloneUrl: string;
    defaultBranch: string;
    private: boolean;
    language?: string;
    stargazersCount: number;
    forksCount: number;
  }[];
}): Promise<BatchImportResult> {
  const url = buildApiUrl("/api/admin/github/batch-import");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(request),
  });
  return result.data;
}

export async function getGitHubConfig(): Promise<GitHubConfig> {
  const url = buildApiUrl("/api/admin/github/config");
  const result = await fetchWithAuth(url);
  return result.data;
}

export async function saveGitHubConfig(data: {
  appId: string;
  appName: string;
  privateKey: string;
}): Promise<GitHubConfig> {
  const url = buildApiUrl("/api/admin/github/config");
  const result = await fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
  return result.data;
}

export async function resetGitHubConfig(): Promise<void> {
  const url = buildApiUrl("/api/admin/github/config");
  await fetchWithAuth(url, { method: "DELETE" });
}

// ==================== API Key API ====================

export interface ApiKeyCreateResult {
  id: string;
  name: string;
  keyPrefix: string;
  scope: string;
  expiresAt?: string;
  plainTextKey: string;
}

export interface ApiKeyListItem {
  id: string;
  name: string;
  keyPrefix: string;
  userId: string;
  userEmail?: string;
  scope: string;
  expiresAt?: string;
  lastUsedAt?: string;
  createdAt: string;
}

export async function getApiKeys(): Promise<ApiKeyListItem[]> {
  const url = buildApiUrl("/api/admin/api-keys");
  return fetchWithAuth(url);
}

export async function createApiKey(data: {
  name: string;
  userId: string;
  scope?: string;
  expiresInDays?: number;
}): Promise<ApiKeyCreateResult> {
  const url = buildApiUrl("/api/admin/api-keys");
  return fetchWithAuth(url, {
    method: "POST",
    body: JSON.stringify(data),
  });
}

export async function revokeApiKey(id: string): Promise<void> {
  const url = buildApiUrl(`/api/admin/api-keys/${id}`);
  await fetchWithAuth(url, { method: "DELETE" });
}
