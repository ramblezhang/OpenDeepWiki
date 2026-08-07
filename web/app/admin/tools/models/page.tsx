"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { ModelIcon, ProviderIcon } from "@/components/admin/provider-icons";
import { ModelThinkingConfig } from "@/components/admin/model-thinking-config";
import { isGptModelId } from "@/lib/thinking-config";
import { cn } from "@/lib/utils";
import {
  AiModelConfig,
  AiProviderConfig,
  createAiModel,
  deleteAiModel,
  getAiModels,
  getAiProviders,
  updateAiModel,
} from "@/lib/admin-api";
import {
  Activity,
  Brain,
  Code2,
  DollarSign,
  Eye,
  Loader2,
  Plus,
  RefreshCw,
  Search,
  Settings2,
  Sparkles,
  Trash2,
  Wrench,
} from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "@/hooks/use-translations";

const allValue = "all";
const inheritProviderTypeValue = "inherit";

const providerTypes = [
  {
    value: inheritProviderTypeValue,
    labelKey: "admin.models.providerTypes.inherit",
    hintKey: "admin.models.providerTypes.inheritHint",
  },
  {
    value: "OpenAI",
    labelKey: "admin.models.providerTypes.openAI",
    hintKey: "admin.models.providerTypes.openAIHint",
  },
  {
    value: "DeepSeekOpenAI",
    labelKey: "admin.models.providerTypes.deepSeek",
    hintKey: "admin.models.providerTypes.deepSeekHint",
  },
  {
    value: "OpenAIResponses",
    labelKey: "admin.models.providerTypes.responses",
    hintKey: "admin.models.providerTypes.responsesHint",
  },
  {
    value: "Anthropic",
    labelKey: "admin.models.providerTypes.anthropic",
    hintKey: "admin.models.providerTypes.anthropicHint",
  },
  {
    value: "AzureOpenAI",
    labelKey: "admin.models.providerTypes.azure",
    hintKey: "admin.models.providerTypes.azureHint",
  },
];

const modelTypes = [
  { value: "chat", labelKey: "admin.models.modelTypes.chat" },
  { value: "image", labelKey: "admin.models.modelTypes.image" },
  { value: "speech", labelKey: "admin.models.modelTypes.speech" },
  { value: "embedding", labelKey: "admin.models.modelTypes.embedding" },
];

const emptyModelForm = {
  providerId: "",
  modelId: "",
  name: "",
  displayName: "",
  modelType: "chat",
  providerType: inheritProviderTypeValue,
  contextWindow: "",
  maxOutputTokens: "",
  inputTokenPrice: "",
  outputTokenPrice: "",
  cacheHitTokenPrice: "",
  cacheCreationTokenPrice: "",
  supportsThinking: false,
  supportsVision: false,
  supportsTools: true,
  supportsJsonMode: false,
  isDefault: false,
  isActive: true,
  capabilitiesJson: "",
  thinkingConfigJson: "",
  requestOverridesJson: "",
  tagsJson: "",
  description: "",
  sortOrder: "0",
};

function parseOptionalNumber(value: string): number | undefined {
  const trimmed = value.trim();
  return trimmed ? Number(trimmed) : undefined;
}

function formatPrice(value?: number): string {
  if (value === undefined || value === null) return "-";
  return `$${value.toLocaleString(undefined, { maximumFractionDigits: 8 })}`;
}

function formatCompact(value?: number): string {
  if (!value) return "-";
  return new Intl.NumberFormat("en", { notation: "compact" }).format(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function getModelIconKey(model: AiModelConfig): string | undefined {
  if (!model.capabilitiesJson) return undefined;

  try {
    const capabilities = JSON.parse(model.capabilitiesJson) as unknown;
    if (!isRecord(capabilities) || !isRecord(capabilities.openCowork)) return undefined;

    const icon = capabilities.openCowork.icon;
    return typeof icon === "string" ? icon : undefined;
  } catch {
    return undefined;
  }
}

function getModelLabel(model: AiModelConfig): string {
  return model.displayName || model.name || model.modelId;
}

function getProviderLabel(provider?: AiProviderConfig): string {
  return provider?.displayName || provider?.name || "";
}

function isOpenAIResponsesModelId(modelId?: string): boolean {
  const match = modelId?.trim().toLowerCase().match(/^gpt-(\d+)/);
  return match ? Number(match[1]) >= 5 : false;
}

function normalizeModelProviderType(modelId: string, providerType: string): string | undefined {
  if (providerType === inheritProviderTypeValue && isOpenAIResponsesModelId(modelId)) {
    return "OpenAIResponses";
  }

  return providerType === inheritProviderTypeValue ? undefined : providerType;
}

function getEffectiveProviderType(model: AiModelConfig, provider?: AiProviderConfig): string {
  return model.providerType || (isOpenAIResponsesModelId(model.modelId)
    ? "OpenAIResponses"
    : provider?.providerType || "OpenAI");
}

function getModelCapabilities(model: AiModelConfig) {
  return [
    { key: "tools", labelKey: "admin.models.capabilities.tools", active: model.supportsTools, icon: Wrench },
    { key: "vision", labelKey: "admin.models.capabilities.vision", active: model.supportsVision, icon: Eye },
    { key: "thinking", labelKey: "admin.models.capabilities.thinking", active: model.supportsThinking, icon: Brain },
    { key: "json", labelKey: "admin.models.capabilities.json", active: model.supportsJsonMode, icon: Code2 },
  ];
}

export default function AdminAiModelsPage() {
  const t = useTranslations();
  const [providers, setProviders] = useState<AiProviderConfig[]>([]);
  const [models, setModels] = useState<AiModelConfig[]>([]);
  const [selectedProviderId, setSelectedProviderId] = useState(allValue);
  const [selectedType, setSelectedType] = useState(allValue);
  const [searchQuery, setSearchQuery] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [modelDialog, setModelDialog] = useState<AiModelConfig | null | "new">(null);
  const [modelForm, setModelForm] = useState(emptyModelForm);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const [providerData, modelData] = await Promise.all([
        getAiProviders(),
        getAiModels(),
      ]);
      setProviders(providerData);
      setModels(modelData);
    } catch (error) {
      console.error(error);
      toast.error(t("admin.models.toasts.loadFailed"));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const providerById = useMemo(() => {
    return new Map(providers.map((provider) => [provider.id, provider]));
  }, [providers]);

  const getProviderTypeLabel = useCallback(
    (type?: string) => {
      const item = providerTypes.find((candidate) => candidate.value === type);
      return item ? t(item.labelKey) : "";
    },
    [t]
  );

  const filteredModels = useMemo(() => {
    const query = searchQuery.trim().toLowerCase();
    return models.filter((model) => {
      if (selectedProviderId !== allValue && model.providerId !== selectedProviderId) {
        return false;
      }

      if (selectedType !== allValue && model.modelType !== selectedType) {
        return false;
      }

      if (!query) return true;

      const provider = providerById.get(model.providerId);
      const haystack = [
        model.modelId,
        model.name,
        model.displayName,
        model.modelType,
        model.providerType,
        provider?.name,
        provider?.displayName,
      ].filter(Boolean).join(" ").toLowerCase();

      return haystack.includes(query);
    });
  }, [models, providerById, searchQuery, selectedProviderId, selectedType]);

  const activeModelCount = models.filter((model) => model.isActive).length;
  const thinkingModelCount = models.filter((model) => model.supportsThinking).length;
  const visionModelCount = models.filter((model) => model.supportsVision).length;
  const pricedModelCount = models.filter(
    (model) =>
      model.inputTokenPrice !== undefined ||
      model.outputTokenPrice !== undefined ||
      model.cacheHitTokenPrice !== undefined ||
      model.cacheCreationTokenPrice !== undefined
  ).length;
  const statCards = [
    {
      label: t("admin.models.stats.total"),
      value: models.length,
      detail: t("admin.models.stats.totalDetail", { count: providers.length }),
      icon: Sparkles,
    },
    {
      label: t("admin.models.stats.active"),
      value: activeModelCount,
      detail: t("admin.models.stats.activeDetail", { count: models.length - activeModelCount }),
      icon: Activity,
    },
    {
      label: t("admin.models.stats.thinking"),
      value: thinkingModelCount,
      detail: t("admin.models.stats.thinkingDetail"),
      icon: Brain,
    },
    {
      label: t("admin.models.stats.vision"),
      value: visionModelCount,
      detail: t("admin.models.stats.visionDetail"),
      icon: Eye,
    },
    {
      label: t("admin.models.stats.priced"),
      value: pricedModelCount,
      detail: t("admin.models.stats.pricedDetail"),
      icon: DollarSign,
    },
  ];
  const hasActiveFilters =
    searchQuery.trim().length > 0 ||
    selectedProviderId !== allValue ||
    selectedType !== allValue;

  const openModelDialog = (model?: AiModelConfig) => {
    setModelDialog(model ?? "new");
    setModelForm(
      model
        ? {
            providerId: model.providerId,
            modelId: model.modelId,
            name: model.name,
            displayName: model.displayName ?? "",
            modelType: model.modelType,
            providerType: model.providerType || (isOpenAIResponsesModelId(model.modelId)
              ? "OpenAIResponses"
              : inheritProviderTypeValue),
            contextWindow: model.contextWindow?.toString() ?? "",
            maxOutputTokens: model.maxOutputTokens?.toString() ?? "",
            inputTokenPrice: model.inputTokenPrice?.toString() ?? "",
            outputTokenPrice: model.outputTokenPrice?.toString() ?? "",
            cacheHitTokenPrice: model.cacheHitTokenPrice?.toString() ?? "",
            cacheCreationTokenPrice: model.cacheCreationTokenPrice?.toString() ?? "",
            supportsThinking: model.supportsThinking,
            supportsVision: model.supportsVision,
            supportsTools: model.supportsTools,
            supportsJsonMode: model.supportsJsonMode,
            isDefault: model.isDefault,
            isActive: model.isActive,
            capabilitiesJson: model.capabilitiesJson ?? "",
            thinkingConfigJson: model.thinkingConfigJson ?? "",
            requestOverridesJson: model.requestOverridesJson ?? "",
            tagsJson: model.tagsJson ?? "",
            description: model.description ?? "",
            sortOrder: model.sortOrder?.toString() ?? "0",
          }
        : {
            ...emptyModelForm,
            providerId:
              selectedProviderId === allValue
                ? providers.find((provider) => provider.isActive)?.id ?? providers[0]?.id ?? ""
                : selectedProviderId,
          }
    );
  };

  const saveModel = async () => {
    if (!modelForm.providerId || !modelForm.modelId) {
      toast.error(t("admin.models.toasts.selectProviderAndModel"));
      return;
    }

    setSaving(true);
    try {
      const payload = {
        providerId: modelForm.providerId,
        modelId: modelForm.modelId,
        name: modelForm.name || modelForm.displayName || modelForm.modelId,
        displayName: modelForm.displayName || undefined,
        modelType: modelForm.modelType,
        providerType: normalizeModelProviderType(modelForm.modelId, modelForm.providerType),
        contextWindow: parseOptionalNumber(modelForm.contextWindow),
        maxOutputTokens: parseOptionalNumber(modelForm.maxOutputTokens),
        inputTokenPrice: parseOptionalNumber(modelForm.inputTokenPrice),
        outputTokenPrice: parseOptionalNumber(modelForm.outputTokenPrice),
        cacheHitTokenPrice: parseOptionalNumber(modelForm.cacheHitTokenPrice),
        cacheCreationTokenPrice: parseOptionalNumber(modelForm.cacheCreationTokenPrice),
        supportsThinking: modelForm.supportsThinking,
        supportsVision: modelForm.supportsVision,
        supportsTools: modelForm.supportsTools,
        supportsJsonMode: modelForm.supportsJsonMode,
        isDefault: modelForm.isDefault,
        isActive: modelForm.isActive,
        capabilitiesJson: modelForm.capabilitiesJson || undefined,
        thinkingConfigJson: modelForm.thinkingConfigJson || undefined,
        requestOverridesJson: modelForm.requestOverridesJson || undefined,
        tagsJson: modelForm.tagsJson || undefined,
        description: modelForm.description || undefined,
        sortOrder: Number(modelForm.sortOrder || 0),
      };

      if (modelDialog && modelDialog !== "new") {
        await updateAiModel(modelDialog.id, payload);
      } else {
        await createAiModel(payload);
      }

      toast.success(t("admin.models.toasts.saveSuccess"));
      setModelDialog(null);
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.models.toasts.saveFailed"));
    } finally {
      setSaving(false);
    }
  };

  const removeModel = async (model: AiModelConfig) => {
    if (!window.confirm(t("admin.models.confirmDelete", { name: getModelLabel(model) }))) return;

    try {
      await deleteAiModel(model.id);
      toast.success(t("admin.models.toasts.deleteSuccess"));
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.models.toasts.deleteFailed"));
    }
  };

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <section className="space-y-4">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
          <div className="min-w-0 space-y-3">
            <Badge variant="outline" className="w-fit bg-muted/40 text-xs">
              {t("admin.models.registryBadge")}
            </Badge>
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">{t("admin.models.title")}</h1>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">
                {t("admin.models.subtitle")}
              </p>
            </div>
          </div>
          <div className="flex shrink-0 gap-2">
            <Button variant="outline" onClick={fetchData}>
              <RefreshCw className="mr-2 h-4 w-4" />
              {t("admin.models.refresh")}
            </Button>
            <Button onClick={() => openModelDialog()}>
              <Plus className="mr-2 h-4 w-4" />
              {t("admin.models.create")}
            </Button>
          </div>
        </div>

        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {statCards.map(({ label, value, detail, icon: Icon }) => (
            <div
              key={label}
              className="rounded-lg border border-border/70 bg-card/70 p-4 shadow-sm transition-colors hover:bg-card"
            >
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-xs text-muted-foreground">{label}</p>
                  <p className="mt-2 text-2xl font-semibold tracking-tight">{value}</p>
                </div>
                <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
                  <Icon className="h-4 w-4" />
                </span>
              </div>
              <p className="mt-3 truncate text-xs text-muted-foreground">{detail}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="space-y-4">
        <div className="rounded-lg border border-border/70 bg-card/70 p-3 shadow-sm">
          <div className="grid gap-3 xl:grid-cols-[minmax(240px,1fr)_240px_180px]">
            <div className="relative min-w-0">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                placeholder={t("admin.models.filters.searchPlaceholder")}
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
                className="pl-9"
              />
            </div>
            <Select value={selectedProviderId} onValueChange={setSelectedProviderId}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder={t("admin.models.filters.providerPlaceholder")} />
              </SelectTrigger>
              <SelectContent className="max-h-80">
                <SelectItem value={allValue}>{t("admin.models.filters.allProviders")}</SelectItem>
                {providers.map((provider) => (
                  <SelectItem key={provider.id} value={provider.id}>
                    <span className="flex items-center gap-2">
                      <ProviderIcon builtinId={provider.name} iconUrl={provider.iconUrl} size={14} />
                      {getProviderLabel(provider)}
                    </span>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={selectedType} onValueChange={setSelectedType}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder={t("admin.models.filters.typePlaceholder")} />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={allValue}>{t("admin.models.filters.allTypes")}</SelectItem>
                {modelTypes.map((type) => (
                  <SelectItem key={type.value} value={type.value}>{t(type.labelKey)}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
          <p>
            {t("admin.models.summary.showing", { filtered: filteredModels.length, total: models.length })}
          </p>
          {hasActiveFilters && (
            <p className="text-xs">{t("admin.models.summary.filteredHint")}</p>
          )}
        </div>

        {filteredModels.length === 0 ? (
          <div className="flex min-h-64 flex-col items-center justify-center gap-2 rounded-lg border border-dashed border-border/80 bg-muted/20 p-8 text-center">
            <Sparkles className="h-10 w-10 text-muted-foreground" />
            <p className="font-medium">{t("admin.models.empty.title")}</p>
            <p className="text-sm text-muted-foreground">{t("admin.models.empty.description")}</p>
          </div>
        ) : (
          <div className="grid gap-4 lg:grid-cols-2 2xl:grid-cols-3">
            {filteredModels.map((model) => {
              const provider = providerById.get(model.providerId);
              const capabilities = getModelCapabilities(model);
              const effectiveProviderType = getEffectiveProviderType(model, provider);

              return (
                <article
                  key={model.id}
                  className={cn(
                    "group flex min-h-[304px] flex-col rounded-lg border border-border/70 bg-card/80 p-4 shadow-sm transition-all hover:-translate-y-0.5 hover:border-primary/30 hover:shadow-md",
                    !model.isActive && "opacity-75"
                  )}
                >
                  <div className="flex min-w-0 items-start gap-3">
                    <div className="flex size-11 shrink-0 items-center justify-center rounded-lg bg-muted/60 ring-1 ring-border/70">
                      <ModelIcon
                        icon={getModelIconKey(model)}
                        modelId={model.modelId}
                        providerBuiltinId={provider?.name}
                        size={23}
                      />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex min-w-0 flex-wrap items-center gap-2">
                        <h3 className="min-w-0 truncate text-base font-semibold leading-6">
                          {getModelLabel(model)}
                        </h3>
                        {model.isDefault && <Badge className="shrink-0">{t("admin.models.status.default")}</Badge>}
                        <Badge
                          variant={model.isActive ? "default" : "secondary"}
                          className="shrink-0"
                        >
                          {model.isActive ? t("admin.models.status.enabled") : t("admin.models.status.disabled")}
                        </Badge>
                      </div>
                      <p className="mt-1 truncate font-mono text-xs text-muted-foreground">
                        {model.modelId}
                      </p>
                      <div className="mt-2 flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
                        <ProviderIcon builtinId={provider?.name} iconUrl={provider?.iconUrl} size={14} />
                        <span className="truncate">{getProviderLabel(provider)}</span>
                      </div>
                    </div>
                    <div className="flex shrink-0 gap-1">
                      <Button
                        variant="outline"
                        size="icon"
                        className="size-8"
                        aria-label={t("admin.models.actions.edit", { name: getModelLabel(model) })}
                        onClick={() => openModelDialog(model)}
                      >
                        <Settings2 className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="size-8"
                        aria-label={t("admin.models.actions.delete", { name: getModelLabel(model) })}
                        onClick={() => removeModel(model)}
                      >
                        <Trash2 className="h-4 w-4 text-red-500" />
                      </Button>
                    </div>
                  </div>

                  <div className="mt-4 grid grid-cols-2 gap-x-5 gap-y-3 border-y border-border/70 py-3 text-xs lg:grid-cols-3">
                    <div className="min-w-0">
                      <p className="text-muted-foreground">{t("admin.models.metrics.context")}</p>
                      <p className="mt-1 truncate font-semibold">{formatCompact(model.contextWindow)}</p>
                    </div>
                    <div className="min-w-0">
                      <p className="text-muted-foreground">{t("admin.models.metrics.maxOutput")}</p>
                      <p className="mt-1 truncate font-semibold">{formatCompact(model.maxOutputTokens)}</p>
                    </div>
                    <div className="min-w-0">
                      <p className="text-muted-foreground">{t("admin.models.metrics.inputPerM")}</p>
                      <p className="mt-1 truncate font-semibold">{formatPrice(model.inputTokenPrice)}</p>
                    </div>
                    <div className="min-w-0">
                      <p className="text-muted-foreground">{t("admin.models.metrics.outputPerM")}</p>
                      <p className="mt-1 truncate font-semibold">{formatPrice(model.outputTokenPrice)}</p>
                    </div>
                    <div className="min-w-0">
                      <p className="text-muted-foreground">Cache hit / 1M</p>
                      <p className="mt-1 truncate font-semibold">{formatPrice(model.cacheHitTokenPrice)}</p>
                    </div>
                    <div className="min-w-0">
                      <p className="text-muted-foreground">Cache create / 1M</p>
                      <p className="mt-1 truncate font-semibold">{formatPrice(model.cacheCreationTokenPrice)}</p>
                    </div>
                  </div>

                  <div className="mt-4 flex flex-wrap gap-1.5">
                    <Badge variant="outline" className="bg-background/70">
                      {t(modelTypes.find((type) => type.value === model.modelType)?.labelKey ?? "admin.models.modelTypes.chat")}
                    </Badge>
                    <Badge variant="outline" className="bg-background/70">
                      {getProviderTypeLabel(effectiveProviderType)}
                    </Badge>
                    {capabilities.map(({ key, labelKey, active, icon: Icon }) => (
                      <Badge
                        key={key}
                        variant="outline"
                        className={cn(
                          "gap-1 bg-background/70",
                          active
                            ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"
                            : "text-muted-foreground"
                        )}
                      >
                        <Icon className="h-3 w-3" />
                        {t(labelKey)}
                      </Badge>
                    ))}
                  </div>

                  <p className="mt-3 line-clamp-2 text-xs leading-5 text-muted-foreground">
                    {model.description || t("admin.models.descriptionFallback")}
                  </p>
                </article>
              );
            })}
          </div>
        )}
      </section>

      <Dialog open={!!modelDialog} onOpenChange={() => setModelDialog(null)}>
        <DialogContent className="sm:max-w-4xl">
          <DialogHeader>
            <DialogTitle>{modelDialog === "new" ? t("admin.models.dialog.newTitle") : t("admin.models.dialog.editTitle")}</DialogTitle>
          </DialogHeader>
          <div className="max-h-[72vh] space-y-5 overflow-y-auto pr-1">
            <section className="rounded-2xl border bg-muted/20 p-4">
              <div className="mb-4 flex items-center gap-2">
                <Activity className="h-4 w-4 text-muted-foreground" />
                <h3 className="font-medium">{t("admin.models.dialog.basicInfo")}</h3>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <label className="space-y-2 md:col-span-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.providerLabel")}</span>
                  <Select
                    value={modelForm.providerId || undefined}
                    onValueChange={(value) => setModelForm({ ...modelForm, providerId: value })}
                  >
                    <SelectTrigger>
                      <SelectValue placeholder={t("admin.models.dialog.providerSelectPlaceholder")} />
                    </SelectTrigger>
                    <SelectContent className="max-h-80">
                      {providers.map((provider) => (
                        <SelectItem key={provider.id} value={provider.id}>
                          <span className="flex items-center gap-2">
                            <ProviderIcon builtinId={provider.name} iconUrl={provider.iconUrl} size={14} />
                            {getProviderLabel(provider)}
                          </span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </label>
                <label className="space-y-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.modelIdLabel")}</span>
                  <Input
                    placeholder={t("admin.models.dialog.modelIdPlaceholder")}
                    value={modelForm.modelId}
                    onChange={(event) => {
                      const modelId = event.target.value;
                      setModelForm({
                        ...modelForm,
                        modelId,
                        providerType: modelForm.providerType === inheritProviderTypeValue &&
                          isOpenAIResponsesModelId(modelId)
                          ? "OpenAIResponses"
                          : modelForm.providerType,
                      });
                    }}
                  />
                </label>
                <label className="space-y-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.displayNameLabel")}</span>
                  <Input
                    placeholder={t("admin.models.dialog.displayNamePlaceholder")}
                    value={modelForm.displayName}
                    onChange={(event) =>
                      setModelForm({
                        ...modelForm,
                        displayName: event.target.value,
                        name: event.target.value || modelForm.modelId,
                      })
                    }
                  />
                </label>
                <label className="space-y-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.modelTypeLabel")}</span>
                  <Select
                    value={modelForm.modelType}
                    onValueChange={(value) => setModelForm({ ...modelForm, modelType: value })}
                  >
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {modelTypes.map((type) => (
                        <SelectItem key={type.value} value={type.value}>{t(type.labelKey)}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </label>
                <label className="space-y-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.providerTypeLabel")}</span>
                  <Select
                    value={modelForm.providerType}
                    onValueChange={(value) => setModelForm({ ...modelForm, providerType: value })}
                  >
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {providerTypes.map((type) => (
                        <SelectItem key={type.value} value={type.value}>
                          {type.value === inheritProviderTypeValue
                            ? t("admin.models.providerTypes.inheritWithCurrent", { type: getProviderTypeLabel(providerById.get(modelForm.providerId)?.providerType) || t("admin.models.providerTypes.openAI") })
                            : t(type.labelKey)}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </label>
                <label className="space-y-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.sortOrderLabel")}</span>
                  <Input
                    type="number"
                    value={modelForm.sortOrder}
                    onChange={(event) => setModelForm({ ...modelForm, sortOrder: event.target.value })}
                  />
                </label>
                <label className="space-y-2 md:col-span-2">
                  <span className="text-sm font-medium">{t("admin.models.dialog.descriptionLabel")}</span>
                  <Textarea
                    placeholder={t("admin.models.dialog.descriptionPlaceholder")}
                    value={modelForm.description}
                    onChange={(event) => setModelForm({ ...modelForm, description: event.target.value })}
                  />
                </label>
              </div>
            </section>

            <section className="rounded-2xl border bg-background p-4">
              <div className="mb-4 flex items-center gap-2">
                <DollarSign className="h-4 w-4 text-muted-foreground" />
                <h3 className="font-medium">{t("admin.models.dialog.pricingTitle")}</h3>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                <Input
                  placeholder={t("admin.models.dialog.contextWindowPlaceholder")}
                  value={modelForm.contextWindow}
                  onChange={(event) => setModelForm({ ...modelForm, contextWindow: event.target.value })}
                />
                <Input
                  placeholder={t("admin.models.dialog.maxOutputTokensPlaceholder")}
                  value={modelForm.maxOutputTokens}
                  onChange={(event) => setModelForm({ ...modelForm, maxOutputTokens: event.target.value })}
                />
                <Input
                  placeholder={t("admin.models.dialog.inputPricePlaceholder")}
                  value={modelForm.inputTokenPrice}
                  onChange={(event) => setModelForm({ ...modelForm, inputTokenPrice: event.target.value })}
                />
                <Input
                  placeholder={t("admin.models.dialog.outputPricePlaceholder")}
                  value={modelForm.outputTokenPrice}
                  onChange={(event) => setModelForm({ ...modelForm, outputTokenPrice: event.target.value })}
                />
                <Input
                  placeholder="Cache hit price / 1M"
                  value={modelForm.cacheHitTokenPrice}
                  onChange={(event) => setModelForm({ ...modelForm, cacheHitTokenPrice: event.target.value })}
                />
                <Input
                  placeholder="Cache create price / 1M"
                  value={modelForm.cacheCreationTokenPrice}
                  onChange={(event) => setModelForm({ ...modelForm, cacheCreationTokenPrice: event.target.value })}
                />
              </div>
              <div className="mt-4 grid gap-3 text-sm md:grid-cols-3">
                {[
                  ["supportsTools", t("admin.models.capabilities.tools")],
                  ["supportsVision", t("admin.models.capabilities.vision")],
                  ["supportsThinking", t("admin.models.capabilities.thinking")],
                  ["supportsJsonMode", t("admin.models.capabilities.json")],
                  ["isDefault", t("admin.models.status.defaultModel")],
                  ["isActive", t("admin.models.status.enabledModel")],
                ].map(([key, label]) => (
                  <label key={key} className="flex items-center justify-between rounded-xl border bg-muted/20 px-3 py-2">
                    <span>{label}</span>
                    <Switch
                      checked={Boolean(modelForm[key as keyof typeof modelForm])}
                      onCheckedChange={(checked) => setModelForm({ ...modelForm, [key]: checked })}
                    />
                  </label>
                ))}
              </div>
            </section>

            {isGptModelId(modelForm.modelId) && (
              <section className="rounded-2xl border bg-background p-4">
                <ModelThinkingConfig
                  modelId={modelForm.modelId}
                  supportsThinking={modelForm.supportsThinking}
                  value={modelForm.thinkingConfigJson}
                  onChange={(thinkingConfigJson) =>
                    setModelForm({ ...modelForm, thinkingConfigJson })
                  }
                  textareaClassName="min-h-32"
                />
              </section>
            )}

            <section className="rounded-2xl border bg-background px-4">
              <Accordion type="single" collapsible>
                <AccordionItem value="advanced">
                  <AccordionTrigger className="hover:no-underline">
                    <div className="flex items-center gap-2">
                      <Settings2 className="h-4 w-4 text-muted-foreground" />
                      <span>{t("admin.models.dialog.advancedJson")}</span>
                    </div>
                  </AccordionTrigger>
                  <AccordionContent>
                    <div className="grid gap-4 md:grid-cols-2">
                      <Textarea
                        placeholder="Capabilities JSON"
                        value={modelForm.capabilitiesJson}
                        onChange={(event) => setModelForm({ ...modelForm, capabilitiesJson: event.target.value })}
                        className="min-h-32 font-mono text-xs"
                      />
                      {!isGptModelId(modelForm.modelId) && (
                        <ModelThinkingConfig
                          modelId={modelForm.modelId}
                          supportsThinking={modelForm.supportsThinking}
                          value={modelForm.thinkingConfigJson}
                          onChange={(thinkingConfigJson) =>
                            setModelForm({ ...modelForm, thinkingConfigJson })
                          }
                          textareaClassName="min-h-32"
                        />
                      )}
                      <Textarea
                        placeholder="Request Overrides JSON"
                        value={modelForm.requestOverridesJson}
                        onChange={(event) => setModelForm({ ...modelForm, requestOverridesJson: event.target.value })}
                        className="min-h-32 font-mono text-xs"
                      />
                      <Textarea
                        placeholder="Tags JSON"
                        value={modelForm.tagsJson}
                        onChange={(event) => setModelForm({ ...modelForm, tagsJson: event.target.value })}
                        className="min-h-32 font-mono text-xs"
                      />
                    </div>
                  </AccordionContent>
                </AccordionItem>
              </Accordion>
            </section>
          </div>
          <DialogFooter>
              <Button variant="outline" onClick={() => setModelDialog(null)}>
              {t("admin.models.dialog.cancel")}
            </Button>
            <Button onClick={saveModel} disabled={saving}>
              {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              {t("admin.models.dialog.save")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
