import { describe, expect, it } from "vitest";

import { isLocalGitWorktreeDirtyError } from "@/lib/admin-api";
import { ApiError } from "@/lib/api-client";

describe("isLocalGitWorktreeDirtyError", () => {
  it("recognizes the structured local Git dirty response", () => {
    const error = new ApiError("Conflict", 409, {
      success: false,
      errorCode: "LOCAL_GIT_WORKTREE_DIRTY",
      error: "worktree dirty",
    });

    expect(isLocalGitWorktreeDirtyError(error)).toBe(true);
  });

  it("does not turn unrelated failures into the dirty-worktree toast", () => {
    expect(isLocalGitWorktreeDirtyError(new ApiError("Conflict", 409, { errorCode: "OTHER" }))).toBe(false);
    expect(isLocalGitWorktreeDirtyError(new ApiError("Server error", 500, {
      errorCode: "LOCAL_GIT_WORKTREE_DIRTY",
    }))).toBe(false);
    expect(isLocalGitWorktreeDirtyError(new Error("LOCAL_GIT_WORKTREE_DIRTY"))).toBe(false);
  });
});
