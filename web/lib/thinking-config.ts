export const THINKING_LEVELS = [
  "default",
  "none",
  "minimal",
  "low",
  "medium",
  "high",
  "xhigh",
  "max",
] as const;

export type ThinkingLevel = (typeof THINKING_LEVELS)[number];

export type ThinkingConfigObject = Record<string, unknown>;

export type ParsedThinkingConfig =
  | {
      config: ThinkingConfigObject;
      error?: undefined;
    }
  | {
      config?: undefined;
      error: string;
    };

const GENERIC_GPT_LEVELS: readonly ThinkingLevel[] = [
  "default",
  "none",
  "minimal",
  "low",
  "medium",
  "high",
  "xhigh",
  "max",
];

const GPT_56_LEVELS: readonly ThinkingLevel[] = [
  "default",
  "none",
  "low",
  "medium",
  "high",
  "xhigh",
  "max",
];

export function isGptModelId(modelId?: string): boolean {
  return modelId?.trim().toLowerCase().startsWith("gpt-") ?? false;
}

export function isGpt56ModelId(modelId?: string): boolean {
  return modelId?.trim().toLowerCase().startsWith("gpt-5.6") ?? false;
}

export function isThinkingLevel(value: unknown): value is ThinkingLevel {
  return typeof value === "string" && (THINKING_LEVELS as readonly string[]).includes(value);
}

export function parseThinkingConfigJson(value: string): ParsedThinkingConfig {
  if (!value.trim()) {
    return { config: {} };
  }

  try {
    const parsed: unknown = JSON.parse(value);
    if (!isRecord(parsed)) {
      return { error: "Thinking config must be a JSON object." };
    }
    if (parsed.bodyParams !== undefined && !isRecord(parsed.bodyParams)) {
      return { error: "bodyParams must be a JSON object." };
    }

    return { config: parsed };
  } catch (error) {
    return {
      error: error instanceof Error ? error.message : "Invalid JSON.",
    };
  }
}

export function isRecord(value: unknown): value is ThinkingConfigObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function configuredReasoningLevels(config: ThinkingConfigObject): ThinkingLevel[] | undefined {
  if (!Array.isArray(config.reasoningEffortLevels)) {
    return undefined;
  }

  const levels = config.reasoningEffortLevels.filter(isThinkingLevel);
  return levels.length > 0 ? levels : undefined;
}

export function getThinkingLevelOptions(
  modelId: string | undefined,
  config: ThinkingConfigObject = {}
): ThinkingLevel[] {
  const modelLevels = isGpt56ModelId(modelId) ? GPT_56_LEVELS : GENERIC_GPT_LEVELS;
  const configuredLevels = configuredReasoningLevels(config);

  if (!configuredLevels) {
    return [...modelLevels];
  }

  // `default` is a UI-only choice that clears the explicit effort. Keep it
  // available even when a provider's metadata only lists API values.
  const levels = ["default", ...configuredLevels] as ThinkingLevel[];
  return levels.filter((level, index) => levels.indexOf(level) === index && modelLevels.includes(level));
}

function readConfiguredReasoningEffort(config: ThinkingConfigObject): unknown {
  const bodyParams = config.bodyParams;
  if (isRecord(bodyParams) && bodyParams.reasoning_effort !== undefined) {
    return bodyParams.reasoning_effort;
  }

  return config.defaultReasoningEffort;
}

export function getThinkingLevel(
  modelId: string | undefined,
  config: ThinkingConfigObject
): ThinkingLevel | undefined {
  const configured = readConfiguredReasoningEffort(config);
  if (configured === undefined) return "default";

  const options = getThinkingLevelOptions(modelId, config);
  return isThinkingLevel(configured) && options.includes(configured) ? configured : undefined;
}

export type ThinkingConfigUpdateResult =
  | { value: string; error?: undefined }
  | { value?: undefined; error: string };

export function updateThinkingConfigJson(
  value: string,
  level: ThinkingLevel
): ThinkingConfigUpdateResult {
  const parsed = parseThinkingConfigJson(value);
  if (parsed.error || !parsed.config) {
    return { error: parsed.error ?? "Invalid Thinking config JSON." };
  }

  const config = parsed.config;
  let bodyParams: ThinkingConfigObject | undefined;
  if (config.bodyParams !== undefined) {
    if (!isRecord(config.bodyParams)) {
      return { error: "bodyParams must be a JSON object." };
    }
    bodyParams = config.bodyParams;
  }

  if (level === "default") {
    if (bodyParams) {
      delete bodyParams.reasoning_effort;
      if (Object.keys(bodyParams).length === 0) {
        delete config.bodyParams;
      }
    }
    delete config.defaultReasoningEffort;
  } else {
    const nextBodyParams = bodyParams ?? {};
    nextBodyParams.reasoning_effort = level;
    config.bodyParams = nextBodyParams;
    config.defaultReasoningEffort = level;
  }

  return {
    value: Object.keys(config).length === 0 ? "" : JSON.stringify(config, null, 2),
  };
}
