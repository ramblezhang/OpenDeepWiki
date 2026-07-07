import type { RepositoryItemResponse } from "@/types/repository";

export type TreeNode = {
  name: string;
  path: string;
  children: Map<string, TreeNode>;
  repositoryCount: number;
};

export const ROOT_PATH = "";

function normalizeRepositoryPathSegment(segment: string) {
  return segment === "YDHW" ? "YD_HW" : segment;
}

export function splitRepositoryPath(repository: RepositoryItemResponse) {
  return `${repository.orgName}/${repository.repoName}`
    .split("/")
    .map((segment) => segment.trim())
    .filter(Boolean)
    .map((segment, index) =>
      index === 0 ? normalizeRepositoryPathSegment(segment) : segment
    );
}

export function getRepositoryFolderPath(repository: RepositoryItemResponse) {
  const segments = splitRepositoryPath(repository);
  return segments.slice(0, -1).join("/");
}

export function getRepositoryDisplayPath(repository: RepositoryItemResponse) {
  return splitRepositoryPath(repository).join("/");
}

function createNode(name: string, path: string): TreeNode {
  return {
    name,
    path,
    children: new Map(),
    repositoryCount: 0,
  };
}

export function buildRepositoryTree(repositories: RepositoryItemResponse[]) {
  const root = createNode("Repositories", ROOT_PATH);
  const folderPaths = new Set<string>();

  for (const repository of repositories) {
    const segments = splitRepositoryPath(repository);
    const folderSegments = segments.slice(0, -1);
    let current = root;

    current.repositoryCount += 1;
    folderSegments.forEach((segment, index) => {
      const path = folderSegments.slice(0, index + 1).join("/");
      let child = current.children.get(segment);

      if (!child) {
        child = createNode(segment, path);
        current.children.set(segment, child);
      }

      child.repositoryCount += 1;
      folderPaths.add(path);
      current = child;
    });
  }

  return {
    root,
    folderPaths: Array.from(folderPaths),
  };
}

export function sortTreeNodes(nodes: Iterable<TreeNode>) {
  return Array.from(nodes).sort((a, b) => a.name.localeCompare(b.name));
}
