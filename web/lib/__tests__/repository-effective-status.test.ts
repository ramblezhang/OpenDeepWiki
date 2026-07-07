import { describe, expect, it } from "vitest";
import {
  getRepositoryEffectiveStatus,
  getRepositoryEffectiveStatusLabel,
  isRepositoryEffectiveStatusActive,
} from "../repository-effective-status";

describe("repository-effective-status", () => {
  it("prefers effective status over raw repository status", () => {
    expect(getRepositoryEffectiveStatus("PartialFailed", "Completed")).toBe("PartialFailed");
    expect(getRepositoryEffectiveStatusLabel("PartialFailed", "Completed")).toBe("部分失败");
  });

  it("falls back to the raw repository status for old API responses", () => {
    expect(getRepositoryEffectiveStatus(undefined, "Processing")).toBe("Processing");
    expect(getRepositoryEffectiveStatusLabel(undefined, "Processing")).toBe("处理中");
  });

  it("marks branch and incremental display statuses as active", () => {
    expect(isRepositoryEffectiveStatusActive("PartialBranchesGenerating")).toBe(true);
    expect(isRepositoryEffectiveStatusActive("IncrementalUpdating")).toBe(true);
    expect(isRepositoryEffectiveStatusActive("AllBranchesQueued")).toBe(false);
    expect(isRepositoryEffectiveStatusActive("PartialFailed")).toBe(false);
  });

  it("labels queued branch statuses separately from generation statuses", () => {
    expect(getRepositoryEffectiveStatusLabel("AllBranchesQueued")).toBe("全部分支排队中");
    expect(getRepositoryEffectiveStatusLabel("PartialBranchesQueued")).toBe("部分分支排队中");
    expect(getRepositoryEffectiveStatusLabel("AllBranchesGenerating")).toBe("全部分支生成中");
  });
});
