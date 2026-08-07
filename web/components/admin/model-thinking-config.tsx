"use client";

import { useMemo, useState } from "react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { useTranslations } from "@/hooks/use-translations";
import {
  getThinkingLevel,
  getThinkingLevelOptions,
  isGptModelId,
  parseThinkingConfigJson,
  type ThinkingConfigObject,
  type ThinkingLevel,
  updateThinkingConfigJson,
} from "@/lib/thinking-config";
import { cn } from "@/lib/utils";

type ModelThinkingConfigProps = {
  modelId?: string;
  supportsThinking: boolean;
  value: string;
  onChange: (value: string) => void;
  textareaClassName?: string;
};

function getCurrentReasoningConfig(value: string): {
  config?: ThinkingConfigObject;
  error?: string;
} {
  const parsed = parseThinkingConfigJson(value);
  return parsed.error ? { error: parsed.error } : { config: parsed.config };
}

export function ModelThinkingConfig({
  modelId,
  supportsThinking,
  value,
  onChange,
  textareaClassName,
}: ModelThinkingConfigProps) {
  const t = useTranslations();
  const isGpt = isGptModelId(modelId);
  const parsed = useMemo(() => getCurrentReasoningConfig(value), [value]);
  const options = useMemo(
    () => getThinkingLevelOptions(modelId, parsed.config),
    [modelId, parsed.config]
  );
  const selectedLevel = useMemo(
    () => (parsed.config ? getThinkingLevel(modelId, parsed.config) : undefined),
    [modelId, parsed.config]
  );
  const [advancedOpen, setAdvancedOpen] = useState(Boolean(parsed.error));

  const currentValueIsCustom =
    selectedLevel === undefined &&
    parsed.config &&
    typeof parsed.config.bodyParams === "object" &&
    parsed.config.bodyParams !== null &&
    !Array.isArray(parsed.config.bodyParams) &&
    typeof (parsed.config.bodyParams as Record<string, unknown>).reasoning_effort === "string";
  const customValue = currentValueIsCustom
    ? (parsed.config?.bodyParams as Record<string, unknown>).reasoning_effort as string
    : undefined;

  if (!isGpt) {
    return (
      <Textarea
        placeholder={t("admin.thinkingConfig.jsonPlaceholder")}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={cn("font-mono text-xs", textareaClassName)}
      />
    );
  }

  const handleLevelChange = (nextLevel: string) => {
    if (!options.includes(nextLevel as ThinkingLevel)) return;

    const updated = updateThinkingConfigJson(value, nextLevel as ThinkingLevel);
    if (updated.error) return;
    onChange(updated.value ?? "");
  };

  return (
    <div className="space-y-3 md:col-span-2">
      <div className="space-y-2">
        <label className="text-sm font-medium">{t("admin.thinkingConfig.label")}</label>
        <Select
          value={selectedLevel ?? ""}
          onValueChange={handleLevelChange}
          disabled={!supportsThinking || Boolean(parsed.error)}
        >
          <SelectTrigger className="w-full">
            <SelectValue placeholder={t("admin.thinkingConfig.placeholder")} />
          </SelectTrigger>
          <SelectContent>
            {options.map((level) => (
              <SelectItem key={level} value={level}>
                {level}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {!supportsThinking && (
          <p className="text-xs text-muted-foreground">{t("admin.thinkingConfig.thinkingDisabled")}</p>
        )}
        {!parsed.error && !supportsThinking && (
          <p className="text-xs text-muted-foreground">{t("admin.thinkingConfig.editJsonHint")}</p>
        )}
        {!parsed.error && supportsThinking && !selectedLevel && customValue && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            {t("admin.thinkingConfig.unsupportedValue", { value: customValue })}
          </p>
        )}
        {!parsed.error && supportsThinking && (
          <p className="text-xs text-muted-foreground">{t("admin.thinkingConfig.modelSupportHint")}</p>
        )}
        {parsed.error && (
          <p className="text-xs text-destructive">
            {t("admin.thinkingConfig.invalidJson", { error: parsed.error })}
          </p>
        )}
      </div>

      <details
        open={advancedOpen || Boolean(parsed.error)}
        onToggle={(event) => setAdvancedOpen(event.currentTarget.open)}
        className="rounded-lg border border-dashed px-3 py-2"
      >
        <summary className="cursor-pointer text-sm font-medium">
          {t("admin.thinkingConfig.advancedJson")}
        </summary>
        <div className="mt-3 space-y-2">
          <p className="text-xs text-muted-foreground">{t("admin.thinkingConfig.editJsonHint")}</p>
          <Textarea
            placeholder={t("admin.thinkingConfig.jsonPlaceholder")}
            value={value}
            onChange={(event) => onChange(event.target.value)}
            className={cn("min-h-24 font-mono text-xs", textareaClassName)}
          />
        </div>
      </details>
    </div>
  );
}
