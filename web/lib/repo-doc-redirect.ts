import type { RepoTreeNode } from "@/types/repository";

function findNodeBySlug(nodes: RepoTreeNode[], slug: string): RepoTreeNode | null {
  for (const node of nodes) {
    if (node.slug === slug) {
      return node;
    }

    const match = findNodeBySlug(node.children ?? [], slug);
    if (match) {
      return match;
    }
  }

  return null;
}

function findFirstLeafSlug(node: RepoTreeNode): string | null {
  const children = node.children ?? [];
  if (children.length === 0) {
    return node.slug;
  }

  for (const child of children) {
    const slug = findFirstLeafSlug(child);
    if (slug) {
      return slug;
    }
  }

  return null;
}

export function resolveMissingDocRedirectSlug(
  nodes: RepoTreeNode[],
  requestedSlug: string,
  defaultSlug?: string | null
) {
  const node = findNodeBySlug(nodes, requestedSlug);

  if (node && (node.children ?? []).length > 0) {
    const leafSlug = findFirstLeafSlug(node);
    return leafSlug && leafSlug !== requestedSlug ? leafSlug : null;
  }

  if (!node && defaultSlug && defaultSlug !== requestedSlug) {
    return defaultSlug;
  }

  return null;
}
