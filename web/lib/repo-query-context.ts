export const REPO_BRANCH_HEADER = "x-opendeepwiki-repo-branch";
export const REPO_LANGUAGE_HEADER = "x-opendeepwiki-repo-lang";

type HeaderReader = Pick<Headers, "get">;
type HeaderWriter = Pick<Headers, "delete" | "set">;

function normalizeQueryValue(value: string | null | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

export function applyRepoQueryHeaders(headers: HeaderWriter, searchParams: URLSearchParams) {
  const branch = normalizeQueryValue(searchParams.get("branch"));
  const lang = normalizeQueryValue(searchParams.get("lang"));

  if (branch) {
    headers.set(REPO_BRANCH_HEADER, branch);
  } else {
    headers.delete(REPO_BRANCH_HEADER);
  }

  if (lang) {
    headers.set(REPO_LANGUAGE_HEADER, lang);
  } else {
    headers.delete(REPO_LANGUAGE_HEADER);
  }
}

export function getRepoQueryFromHeaders(headers: HeaderReader) {
  return {
    branch: normalizeQueryValue(headers.get(REPO_BRANCH_HEADER)),
    lang: normalizeQueryValue(headers.get(REPO_LANGUAGE_HEADER)),
  };
}
