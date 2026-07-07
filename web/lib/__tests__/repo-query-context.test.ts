import { describe, expect, it } from "vitest";

import { applyRepoQueryHeaders, getRepoQueryFromHeaders } from "../repo-query-context";

describe("repo-query-context", () => {
  it("preserves slash-bearing branch values for server layout fetches", () => {
    const headers = new Headers();
    const searchParams = new URLSearchParams({
      branch: "feature/nested-docs",
      lang: "zh",
    });

    applyRepoQueryHeaders(headers, searchParams);

    expect(getRepoQueryFromHeaders(headers)).toEqual({
      branch: "feature/nested-docs",
      lang: "zh",
    });
  });

  it("clears stale query headers when branch and language are absent", () => {
    const headers = new Headers({
      "x-opendeepwiki-repo-branch": "feature/old",
      "x-opendeepwiki-repo-lang": "zh",
    });

    applyRepoQueryHeaders(headers, new URLSearchParams());

    expect(getRepoQueryFromHeaders(headers)).toEqual({
      branch: undefined,
      lang: undefined,
    });
  });
});
