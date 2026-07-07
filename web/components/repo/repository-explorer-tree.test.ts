import { describe, expect, it } from "vitest";
import type { RepositoryItemResponse } from "@/types/repository";
import {
  buildRepositoryTree,
  getRepositoryDisplayPath,
  getRepositoryFolderPath,
  splitRepositoryPath,
} from "./repository-explorer-tree";

function repository(
  orgName: string,
  repoName: string,
  id = `${orgName}/${repoName}`
): RepositoryItemResponse {
  return {
    id,
    orgName,
    repoName,
    gitUrl: "",
    sourceType: 0,
    sourceTypeName: "Git",
    sourceLocation: "",
    status: 2,
    statusName: "Completed",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    isPublic: true,
    generateSkill: true,
    hasPassword: false,
    starCount: 0,
    forkCount: 0,
  };
}

describe("repository explorer tree", () => {
  it("splits orgName and multi-segment repoName into one path", () => {
    const repo = repository("YDHW", "RV1106-SDK/services/camera");

    expect(splitRepositoryPath(repo)).toEqual([
      "YD_HW",
      "RV1106-SDK",
      "services",
      "camera",
    ]);
    expect(getRepositoryFolderPath(repo)).toBe("YD_HW/RV1106-SDK/services");
    expect(getRepositoryDisplayPath(repo)).toBe(
      "YD_HW/RV1106-SDK/services/camera"
    );
  });

  it("splits multi-segment orgName instead of treating it as a flat label", () => {
    const repo = repository("YDHW/RV1106-SDK", "services/camera");

    expect(splitRepositoryPath(repo)).toEqual([
      "YD_HW",
      "RV1106-SDK",
      "services",
      "camera",
    ]);
    expect(getRepositoryFolderPath(repo)).toBe("YD_HW/RV1106-SDK/services");
  });

  it("reuses common parents for repositories under the same prefix", () => {
    const { root, folderPaths } = buildRepositoryTree([
      repository("YDHW/RV1106-SDK", "services/camera", "camera"),
      repository("YDHW/RV1106-SDK", "services/audio", "audio"),
      repository("YDHW/RV1106-SDK", "kernel/drivers", "drivers"),
    ]);

    const ydhw = root.children.get("YD_HW");
    const sdk = ydhw?.children.get("RV1106-SDK");
    const services = sdk?.children.get("services");
    const kernel = sdk?.children.get("kernel");

    expect(ydhw?.repositoryCount).toBe(3);
    expect(sdk?.repositoryCount).toBe(3);
    expect(services?.repositoryCount).toBe(2);
    expect(kernel?.repositoryCount).toBe(1);
    expect(root.children.has("YDHW/RV1106-SDK")).toBe(false);
    expect(root.children.has("YDHW")).toBe(false);
    expect(folderPaths).toEqual(
      expect.arrayContaining([
        "YD_HW",
        "YD_HW/RV1106-SDK",
        "YD_HW/RV1106-SDK/services",
        "YD_HW/RV1106-SDK/kernel",
      ])
    );
  });

  it("keeps existing YD_HW prefixes and non-prefix matches unchanged", () => {
    expect(
      getRepositoryDisplayPath(repository("YD_HW/RV1106-SDK", "sysdrv/cfg"))
    ).toBe("YD_HW/RV1106-SDK/sysdrv/cfg");
    expect(getRepositoryDisplayPath(repository("foo", "bar/YDHW/baz"))).toBe(
      "foo/bar/YDHW/baz"
    );
    expect(getRepositoryDisplayPath(repository("my-YDHW", "repo"))).toBe(
      "my-YDHW/repo"
    );
  });
});
