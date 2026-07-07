import type { 
  RepoDocResponse, 
  RepoTreeResponse, 
  RepoBranchesResponse,
  GitBranchesResponse,
  RepositorySubmitRequest, 
  ArchiveRepositorySubmitRequest,
  LocalDirectoryRepositorySubmitRequest,
  RepositoryListResponse,
  RepositoryItemResponse,
  UpdateVisibilityRequest,
  UpdateVisibilityResponse,
  ProcessingLogResponse,
  GitRepoCheckResponse,
  MindMapResponse,
  BranchGenerationTaskResponse,
  BranchGenerationErrorResponse
} from "@/types/repository";
import { ApiError, api, buildApiUrl } from "./api-client";
import { getServerToken } from "./auth-api";

type RepositoryListParams = {
  page?: number;
  pageSize?: number;
  ownerId?: string;
  status?: number;
  keyword?: string;
  language?: string;
  sortBy?: 'createdAt' | 'updatedAt' | 'status';
  sortOrder?: 'asc' | 'desc';
  isPublic?: boolean;
};

const LIST_ALL_PAGE_SIZE = 200;

/**
 * Returns Authorization header for SSR fetches if a JWT cookie is present.
 * On the client side (window exists), getServerToken() returns null so
 * this returns empty headers -- client auth goes through apiClient instead.
 * Async because cookies() is async in Next.js 15+.
 */
async function getSSRAuthHeaders(): Promise<HeadersInit> {
  const token = await getServerToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function encodePathSegments(path: string) {
  return path
    .split("/")
    .map((segment) => encodeURIComponent(segment))
    .join("/");
}

export async function fetchRepoBranches(owner: string, repo: string) {
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/branches`,
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch repository branches");
  }

  return (await response.json()) as RepoBranchesResponse;
}

/**
 * Fetch branches from Git platform API (GitHub/Gitee/GitLab)
 */
export async function fetchGitBranches(gitUrl: string): Promise<GitBranchesResponse> {
  const params = new URLSearchParams();
  params.set("gitUrl", gitUrl);
  
  const url = buildApiUrl(`/api/v1/repositories/branches?${params.toString()}`);

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    return { branches: [], defaultBranch: null, isSupported: false };
  }

  return (await response.json()) as GitBranchesResponse;
}

export async function fetchRepoTree(owner: string, repo: string, branch?: string, lang?: string) {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  
  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/tree${queryString ? `?${queryString}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch repository tree");
  }

  return (await response.json()) as RepoTreeResponse;
}

export async function fetchRepoDoc(owner: string, repo: string, slug: string, branch?: string, lang?: string) {
  const encodedSlug = encodePathSegments(slug);
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  
  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/docs/${encodedSlug}${queryString ? `?${queryString}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch repository doc");
  }

  return (await response.json()) as RepoDocResponse;
}

export async function fetchGraphifyReport(owner: string, repo: string, branch?: string) {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);

  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/graphify/report${queryString ? `?${queryString}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch Graphify report");
  }

  return response.text();
}


/**
 * Submit a repository for wiki generation
 * 自动携带用户 token 进行认证
 */
export async function submitRepository(
  request: RepositorySubmitRequest
): Promise<RepositoryItemResponse> {
  return api.post<RepositoryItemResponse>("/api/v1/repositories/submit", request);
}

export async function submitArchiveRepository(
  request: ArchiveRepositorySubmitRequest
): Promise<RepositoryItemResponse> {
  const formData = new FormData();
  formData.append("orgName", request.orgName);
  formData.append("repoName", request.repoName);
  formData.append("branchName", request.branchName);
  formData.append("languageCode", request.languageCode);
  formData.append("isPublic", String(request.isPublic));
  formData.append("generateSkill", String(request.generateSkill));
  formData.append("archive", request.archive);

  return api.post<RepositoryItemResponse>("/api/v1/repositories/submit-archive", formData);
}

export async function submitLocalDirectoryRepository(
  request: LocalDirectoryRepositorySubmitRequest
): Promise<RepositoryItemResponse> {
  return api.post<RepositoryItemResponse>("/api/v1/repositories/submit-local", request);
}

/**
 * Fetch repository list with optional filters
 */
export async function fetchRepositoryList(params?: RepositoryListParams): Promise<RepositoryListResponse> {
  const searchParams = new URLSearchParams();
  
  // page and pageSize are required by the backend API
  searchParams.set("page", (params?.page ?? 1).toString());
  searchParams.set("pageSize", (params?.pageSize ?? 20).toString());
  if (params?.ownerId) searchParams.set("ownerId", params.ownerId);
  if (params?.status !== undefined) searchParams.set("status", params.status.toString());
  if (params?.keyword) searchParams.set("keyword", params.keyword);
  if (params?.language) searchParams.set("language", params.language);
  if (params?.sortBy) searchParams.set("sortBy", params.sortBy);
  if (params?.sortOrder) searchParams.set("sortOrder", params.sortOrder);
  if (params?.isPublic !== undefined) searchParams.set("isPublic", params.isPublic.toString());

  const queryString = searchParams.toString();
  const url = buildApiUrl(`/api/v1/repositories/list${queryString ? `?${queryString}` : ""}`);

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch repository list");
  }

  return await response.json();
}

export async function fetchAllRepositoryList(
  params?: Omit<RepositoryListParams, "page" | "pageSize">
): Promise<RepositoryListResponse> {
  const firstPage = await fetchRepositoryList({
    ...params,
    page: 1,
    pageSize: LIST_ALL_PAGE_SIZE,
  });

  if (firstPage.items.length >= firstPage.total) {
    return firstPage;
  }

  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / LIST_ALL_PAGE_SIZE);

  for (let page = 2; page <= totalPages; page += 1) {
    const response = await fetchRepositoryList({
      ...params,
      page,
      pageSize: LIST_ALL_PAGE_SIZE,
    });

    if (response.items.length === 0) {
      break;
    }

    items.push(...response.items);
  }

  return {
    ...firstPage,
    items,
  };
}


/**
 * Update repository visibility (public/private)
 * 自动携带用户 token 进行认证
 */
export async function updateRepositoryVisibility(
  request: UpdateVisibilityRequest
): Promise<UpdateVisibilityResponse> {
  return api.post<UpdateVisibilityResponse>("/api/v1/repositories/visibility", request);
}

/**
 * Fetch repository status (client-side polling)
 */
export async function fetchRepoStatus(owner: string, repo: string, branch?: string, lang?: string): Promise<RepoTreeResponse> {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);
  const query = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/tree${query ? `?${query}` : ""}`,
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch repository status");
  }

  return (await response.json()) as RepoTreeResponse;
}


/**
 * Fetch repository processing logs
 */
export async function fetchProcessingLogs(
  owner: string,
  repo: string,
  since?: Date,
  limit: number = 100,
  branchId?: string,
  taskId?: string
): Promise<ProcessingLogResponse> {
  const params = new URLSearchParams();
  if (since) {
    params.set("since", since.toISOString());
  }
  params.set("limit", limit.toString());
  if (branchId) params.set("branchId", branchId);
  if (taskId) params.set("taskId", taskId);

  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/processing-logs${queryString ? `?${queryString}` : ""}`
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch processing logs");
  }

  return (await response.json()) as ProcessingLogResponse;
}


/**
 * Check if a GitHub repository exists
 */
export async function checkGitHubRepo(
  owner: string,
  repo: string
): Promise<GitRepoCheckResponse> {
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/check`
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    return {
      exists: false,
      name: null,
      description: null,
      defaultBranch: null,
      starCount: 0,
      forkCount: 0,
      language: null,
      avatarUrl: null,
      isPrivate: false,
      gitUrl: null,
    };
  }

  return (await response.json()) as GitRepoCheckResponse;
}

/**
 * Regenerate repository documentation
 * 重新生成仓库文档
 */
export async function regenerateRepository(
  owner: string,
  repo: string
): Promise<{ success: boolean; errorMessage?: string }> {
  return api.post<{ success: boolean; errorMessage?: string }>(
    "/api/v1/repositories/regenerate",
    { owner, repo }
  );
}

/**
 * Fetch repository mind map
 * 获取仓库项目架构思维导图
 */
export async function fetchMindMap(
  owner: string,
  repo: string,
  branch?: string,
  lang?: string
): Promise<MindMapResponse> {
  const params = new URLSearchParams();
  if (branch) params.set("branch", branch);
  if (lang) params.set("lang", lang);

  const queryString = params.toString();
  const url = buildApiUrl(
    `/api/v1/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/mindmap${queryString ? `?${queryString}` : ""}`
  );

  const response = await fetch(url, { cache: "no-store", headers: await getSSRAuthHeaders() });

  if (!response.ok) {
    throw new Error("Failed to fetch mind map");
  }

  return (await response.json()) as MindMapResponse;
}

export async function enqueueBranchFullGeneration(
  repositoryId: string,
  branchId: string
): Promise<BranchGenerationTaskResponse | BranchGenerationErrorResponse> {
  return postBranchGenerationAction(`/api/v1/repositories/${repositoryId}/branches/${branchId}/generation-tasks/full`);
}

export async function getBranchGenerationTask(
  taskId: string
): Promise<BranchGenerationTaskResponse | BranchGenerationErrorResponse> {
  return api.get<BranchGenerationTaskResponse>(`/api/v1/branch-generation-tasks/${taskId}`)
    .catch(toBranchGenerationError);
}

export async function retryBranchGenerationTask(
  taskId: string
): Promise<BranchGenerationTaskResponse | BranchGenerationErrorResponse> {
  return postBranchGenerationAction(`/api/v1/branch-generation-tasks/${taskId}/retry`);
}

export async function cancelBranchGenerationTask(
  taskId: string
): Promise<BranchGenerationTaskResponse | BranchGenerationErrorResponse> {
  return postBranchGenerationAction(`/api/v1/branch-generation-tasks/${taskId}/cancel`);
}

async function postBranchGenerationAction(path: string): Promise<BranchGenerationTaskResponse | BranchGenerationErrorResponse> {
  return api.post<BranchGenerationTaskResponse>(path).catch(toBranchGenerationError);
}

function toBranchGenerationError(error: unknown): BranchGenerationErrorResponse {
  if (error instanceof ApiError && error.data && typeof error.data === "object") {
    const data = error.data as Partial<BranchGenerationErrorResponse>;
    if (data.success === false && data.errorCode && data.error) {
      return data as BranchGenerationErrorResponse;
    }
  }

  return {
    success: false,
    errorCode: "BRANCH_GENERATION_REQUEST_FAILED",
    error: error instanceof Error ? error.message : "Branch generation request failed",
  };
}
