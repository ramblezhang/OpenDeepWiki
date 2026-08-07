import { describe, expect, it } from "vitest";

import {
  getThinkingLevelOptions,
  THINKING_LEVELS,
  updateThinkingConfigJson,
} from "../thinking-config";

describe("thinking-config", () => {
  it("uses the GPT-5.6 levels without minimal", () => {
    expect(getThinkingLevelOptions("gpt-5.6")).toEqual([
      "default",
      "none",
      "low",
      "medium",
      "high",
      "xhigh",
      "max",
    ]);
    expect(getThinkingLevelOptions("gpt-5.6-codex")).not.toContain("minimal");
  });

  it("prioritizes legal reasoningEffortLevels metadata", () => {
    expect(
      getThinkingLevelOptions("gpt-5.6", {
        reasoningEffortLevels: ["high", "minimal", "xhigh", "unsupported"],
      })
    ).toEqual(["default", "high", "xhigh"]);
  });

  it("exposes the standard English enum values for other GPT models", () => {
    expect(getThinkingLevelOptions("gpt-5")).toEqual([...THINKING_LEVELS]);
    expect(getThinkingLevelOptions("gpt-4.1")).toEqual([...THINKING_LEVELS]);
  });

  it("writes xhigh to both locations while preserving other JSON fields", () => {
    const result = updateThinkingConfigJson(
      JSON.stringify({
        keep: "value",
        reasoningEffortLevels: ["low", "medium", "high", "xhigh"],
        bodyParams: {
          keepBodyParam: true,
        },
      }),
      "xhigh"
    );

    expect(result.error).toBeUndefined();
    expect(JSON.parse(result.value ?? "{}")).toEqual({
      keep: "value",
      reasoningEffortLevels: ["low", "medium", "high", "xhigh"],
      bodyParams: {
        keepBodyParam: true,
        reasoning_effort: "xhigh",
      },
      defaultReasoningEffort: "xhigh",
    });
  });

  it("clears explicit effort for default while preserving other fields", () => {
    const result = updateThinkingConfigJson(
      JSON.stringify({
        forceTemperature: 1,
        defaultReasoningEffort: "high",
        bodyParams: {
          reasoning_effort: "high",
          temperature: 0.2,
        },
      }),
      "default"
    );

    expect(result.error).toBeUndefined();
    expect(JSON.parse(result.value ?? "{}")).toEqual({
      forceTemperature: 1,
      bodyParams: {
        temperature: 0.2,
      },
    });
  });

  it("returns an error without overwriting invalid JSON", () => {
    const invalidJson = '{"bodyParams":';
    const result = updateThinkingConfigJson(invalidJson, "xhigh");

    expect(result.error).toBeTruthy();
    expect(result.value).toBeUndefined();
  });
});
