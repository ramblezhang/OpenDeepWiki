import { describe, expect, it } from "vitest";

import { resolveMissingDocRedirectSlug } from "../repo-doc-redirect";

describe("resolveMissingDocRedirectSlug", () => {
  it("redirects an absent branch-specific slug to the target branch default slug", () => {
    const redirectSlug = resolveMissingDocRedirectSlug(
      [
        { title: "Overview", slug: "overview", children: [] },
        { title: "Runtime", slug: "runtime", children: [] },
      ],
      "architecture-overview",
      "overview"
    );

    expect(redirectSlug).toBe("overview");
  });

  it("keeps a valid leaf slug on the target branch", () => {
    const redirectSlug = resolveMissingDocRedirectSlug(
      [{ title: "Overview", slug: "overview", children: [] }],
      "overview",
      "overview"
    );

    expect(redirectSlug).toBeNull();
  });

  it("redirects a directory slug to its first leaf", () => {
    const redirectSlug = resolveMissingDocRedirectSlug(
      [
        {
          title: "Guide",
          slug: "guide",
          children: [{ title: "Start", slug: "guide/start", children: [] }],
        },
      ],
      "guide",
      "overview"
    );

    expect(redirectSlug).toBe("guide/start");
  });
});
