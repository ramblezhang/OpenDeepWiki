"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
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
import { useTranslations } from "@/hooks/use-translations";
import { cn } from "@/lib/utils";
import {
  AiModelConfig,
  AiProviderConfig,
  type AiProviderConfigRequest,
  createAiModel,
  createAiProvider,
  deleteAiProvider,
  discoverAiModels,
  getAiModels,
  getAiProviders,
  testAiProviderConnectivity,
  updateAiProvider,
  updateAiModel,
} from "@/lib/admin-api";
import {
  Bot,
  CheckCircle2,
  ExternalLink,
  Eye,
  EyeOff,
  KeyRound,
  Layers3,
  Loader2,
  Pencil,
  Plus,
  RefreshCw,
  Search,
  Server,
  Trash2,
} from "lucide-react";
import { toast } from "sonner";

type ProviderTypeDefinition = {
  value: string;
  labelKey: string;
  hintKey: string;
};

type LocalizedProviderType = {
  value: string;
  label: string;
  hint: string;
};

type AuthTypeDefinition = {
  value: string;
  labelKey: string;
};

type LocalizedAuthType = {
  value: string;
  label: string;
};

const providerTypeDefinitions: ProviderTypeDefinition[] = [
  {
    value: "OpenAI",
    labelKey: "admin.aiProviders.providerTypes.openAI",
    hintKey: "admin.aiProviders.providerTypes.openAIHint",
  },
  {
    value: "DeepSeekOpenAI",
    labelKey: "admin.aiProviders.providerTypes.deepSeek",
    hintKey: "admin.aiProviders.providerTypes.deepSeekHint",
  },
  {
    value: "OpenAIResponses",
    labelKey: "admin.aiProviders.providerTypes.responses",
    hintKey: "admin.aiProviders.providerTypes.responsesHint",
  },
  {
    value: "Anthropic",
    labelKey: "admin.aiProviders.providerTypes.anthropic",
    hintKey: "admin.aiProviders.providerTypes.anthropicHint",
  },
  {
    value: "AzureOpenAI",
    labelKey: "admin.aiProviders.providerTypes.azure",
    hintKey: "admin.aiProviders.providerTypes.azureHint",
  },
];

const inheritProviderTypeValue = "inherit";
const modelTypes = ["chat", "image", "speech", "embedding"];

const authTypeDefinitions: AuthTypeDefinition[] = [
  { value: "ApiKey", labelKey: "admin.aiProviders.authTypes.apiKey" },
  { value: "OAuth", labelKey: "admin.aiProviders.authTypes.oauth" },
  { value: "Channel", labelKey: "admin.aiProviders.authTypes.channel" },
  { value: "None", labelKey: "admin.aiProviders.authTypes.none" },
];

const emptyProviderForm = {
  name: "",
  displayName: "",
  providerType: "OpenAI",
  baseUrl: "",
  apiKey: "",
  authType: "ApiKey",
  isBuiltIn: false,
  isActive: true,
  supportsModelDiscovery: true,
  modelsEndpoint: "",
  defaultModelId: "",
  systemProxyUrl: "",
  oauthConfigJson: "",
  channelConfigJson: "",
  accountsJson: "",
  requestOverridesJson: "",
  iconUrl: "",
  description: "",
  sortOrder: "0",
};

type ProviderForm = typeof emptyProviderForm;

const emptyCustomProviderForm = {
  name: "",
  providerType: "OpenAI",
  baseUrl: "",
};

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

type ModelForm = typeof emptyModelForm;

function getProviderTitle(provider?: AiProviderConfig | null): string {
  return provider?.displayName || provider?.name || "";
}

function getProviderTypeLabel(type: string, providerTypes: LocalizedProviderType[]): string {
  return providerTypes.find((item) => item.value === type)?.label ?? type;
}

function isOpenAIResponsesModelId(modelId?: string): boolean {
  const match = modelId?.trim().toLowerCase().match(/^gpt-(\d+)/);
  return match ? Number(match[1]) >= 5 : false;
}

function getModelProviderType(model: AiModelConfig, provider: AiProviderConfig): string {
  return model.providerType || (isOpenAIResponsesModelId(model.modelId)
    ? "OpenAIResponses"
    : provider.providerType);
}

function getAuthTypeLabel(type: string, authTypes: LocalizedAuthType[]): string {
  return authTypes.find((item) => item.value === type)?.label ?? type;
}

function sortProviders(providers: AiProviderConfig[]): AiProviderConfig[] {
  return [...providers].sort((left, right) => {
    if (left.isActive !== right.isActive) return left.isActive ? -1 : 1;
    if (left.sortOrder !== right.sortOrder) return left.sortOrder - right.sortOrder;
    return getProviderTitle(left).localeCompare(getProviderTitle(right));
  });
}

function toProviderForm(provider: AiProviderConfig): ProviderForm {
  return {
    name: provider.name,
    displayName: provider.displayName ?? "",
    providerType: provider.providerType,
    baseUrl: provider.baseUrl,
    apiKey: "",
    authType: provider.authType,
    isBuiltIn: provider.isBuiltIn,
    isActive: provider.isActive,
    supportsModelDiscovery: provider.supportsModelDiscovery,
    modelsEndpoint: provider.modelsEndpoint ?? "",
    defaultModelId: provider.defaultModelId ?? "",
    systemProxyUrl: provider.systemProxyUrl ?? "",
    oauthConfigJson: provider.oauthConfigJson ?? "",
    channelConfigJson: provider.channelConfigJson ?? "",
    accountsJson: provider.accountsJson ?? "",
    requestOverridesJson: provider.requestOverridesJson ?? "",
    iconUrl: provider.iconUrl ?? "",
    description: provider.description ?? "",
    sortOrder: provider.sortOrder?.toString() ?? "0",
  };
}

function buildProviderPayload(form: ProviderForm, apiKeyTouched: boolean): AiProviderConfigRequest {
  return {
    name: form.name.trim(),
    displayName: form.displayName.trim() || undefined,
    providerType: form.providerType,
    baseUrl: form.baseUrl.trim(),
    apiKey: apiKeyTouched ? form.apiKey : undefined,
    authType: form.authType,
    isBuiltIn: form.isBuiltIn,
    isActive: form.isActive,
    supportsModelDiscovery: form.supportsModelDiscovery,
    modelsEndpoint: form.modelsEndpoint.trim() || undefined,
    defaultModelId: form.defaultModelId.trim() || undefined,
    systemProxyUrl: form.systemProxyUrl.trim() || undefined,
    oauthConfigJson: form.oauthConfigJson.trim() || undefined,
    channelConfigJson: form.channelConfigJson.trim() || undefined,
    accountsJson: form.accountsJson.trim() || undefined,
    requestOverridesJson: form.requestOverridesJson.trim() || undefined,
    iconUrl: form.iconUrl.trim() || undefined,
    description: form.description.trim() || undefined,
    sortOrder: Number(form.sortOrder || 0),
  };
}

function parseOpenCoworkDescription(description?: string) {
  return {
    homepage: description?.match(/Homepage:\s*(https?:\/\/\S+)/i)?.[1],
    apiKeyUrl: description?.match(/API key:\s*(https?:\/\/\S+)/i)?.[1],
    requiresApiKey:
      description?.match(/Requires API key:\s*(true|false)/i)?.[1]?.toLowerCase() === "true",
  };
}

function getProviderModelStats(models: AiModelConfig[], providerId: string) {
  const providerModels = models.filter((model) => model.providerId === providerId);
  return {
    total: providerModels.length,
    active: providerModels.filter((model) => model.isActive).length,
    defaults: providerModels.filter((model) => model.isDefault).length,
  };
}

function getModelTitle(model: AiModelConfig): string {
  return model.displayName || model.name || model.modelId;
}

function formatContextWindow(value?: number): string {
  if (!value) return "-";
  if (value >= 1_000_000) return `${Number((value / 1_000_000).toFixed(1))}M context`;
  if (value >= 1_000) return `${Math.round(value / 1_000)}K context`;
  return `${value} context`;
}

function formatModelPrice(value?: number): string {
  if (value === undefined || value === null) return "-";
  return `$${Number(value.toFixed(4))}`;
}

function parseOptionalNumber(value: string): number | undefined {
  const trimmed = value.trim();
  if (!trimmed) return undefined;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function normalizeModelProviderType(modelId: string, providerType: string): string | undefined {
  if (providerType === inheritProviderTypeValue && isOpenAIResponsesModelId(modelId)) {
    return "OpenAIResponses";
  }

  return providerType === inheritProviderTypeValue ? undefined : providerType;
}

function toModelForm(model: AiModelConfig, provider: AiProviderConfig): ModelForm {
  return {
    providerId: provider.id,
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
  };
}

function buildModelFormPayload(form: ModelForm) {
  return {
    providerId: form.providerId,
    modelId: form.modelId.trim(),
    name: form.name.trim() || form.displayName.trim() || form.modelId.trim(),
    displayName: form.displayName.trim() || undefined,
    modelType: form.modelType,
    providerType: normalizeModelProviderType(form.modelId, form.providerType),
    contextWindow: parseOptionalNumber(form.contextWindow),
    maxOutputTokens: parseOptionalNumber(form.maxOutputTokens),
    inputTokenPrice: parseOptionalNumber(form.inputTokenPrice),
    outputTokenPrice: parseOptionalNumber(form.outputTokenPrice),
    cacheHitTokenPrice: parseOptionalNumber(form.cacheHitTokenPrice),
    cacheCreationTokenPrice: parseOptionalNumber(form.cacheCreationTokenPrice),
    supportsThinking: form.supportsThinking,
    supportsVision: form.supportsVision,
    supportsTools: form.supportsTools,
    supportsJsonMode: form.supportsJsonMode,
    isDefault: form.isDefault,
    isActive: form.isActive,
    capabilitiesJson: form.capabilitiesJson.trim() || undefined,
    thinkingConfigJson: form.thinkingConfigJson.trim() || undefined,
    requestOverridesJson: form.requestOverridesJson.trim() || undefined,
    tagsJson: form.tagsJson.trim() || undefined,
    description: form.description.trim() || undefined,
    sortOrder: Number(form.sortOrder || 0),
  };
}

function buildModelPayload(model: AiModelConfig, overrides: Partial<AiModelConfig> = {}) {
  const next = { ...model, ...overrides };
  return {
    providerId: next.providerId,
    modelId: next.modelId,
    name: next.name,
    displayName: next.displayName,
    modelType: next.modelType,
    providerType: next.providerType || (isOpenAIResponsesModelId(next.modelId) ? "OpenAIResponses" : undefined),
    contextWindow: next.contextWindow,
    maxOutputTokens: next.maxOutputTokens,
    inputTokenPrice: next.inputTokenPrice,
    outputTokenPrice: next.outputTokenPrice,
    cacheHitTokenPrice: next.cacheHitTokenPrice,
    cacheCreationTokenPrice: next.cacheCreationTokenPrice,
    supportsThinking: next.supportsThinking,
    supportsVision: next.supportsVision,
    supportsTools: next.supportsTools,
    supportsJsonMode: next.supportsJsonMode,
    isDefault: next.isDefault,
    isActive: next.isActive,
    capabilitiesJson: next.capabilitiesJson,
    thinkingConfigJson: next.thinkingConfigJson,
    requestOverridesJson: next.requestOverridesJson,
    tagsJson: next.tagsJson,
    description: next.description,
    sortOrder: next.sortOrder,
  };
}

export default function AdminAiProvidersPage() {
  const t = useTranslations();
  const providerTypes = useMemo<LocalizedProviderType[]>(
    () =>
      providerTypeDefinitions.map((item) => ({
        value: item.value,
        label: t(item.labelKey),
        hint: t(item.hintKey),
      })),
    [t]
  );
  const modelProviderTypes = useMemo<LocalizedProviderType[]>(
    () => [
      {
        value: inheritProviderTypeValue,
        label: t("admin.aiProviders.providerTypes.inherit"),
        hint: t("admin.aiProviders.providerTypes.inheritHint"),
      },
      ...providerTypes,
    ],
    [providerTypes, t]
  );
  const authTypes = useMemo<LocalizedAuthType[]>(
    () =>
      authTypeDefinitions.map((item) => ({
        value: item.value,
        label: t(item.labelKey),
      })),
    [t]
  );

  const [providers, setProviders] = useState<AiProviderConfig[]>([]);
  const [models, setModels] = useState<AiModelConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [addDialogOpen, setAddDialogOpen] = useState(false);
  const [modelDialog, setModelDialog] = useState<AiModelConfig | "new" | null>(null);
  const [selectedProviderId, setSelectedProviderId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [modelSearchQuery, setModelSearchQuery] = useState("");
  const [showApiKey, setShowApiKey] = useState(false);
  const [apiKeyTouched, setApiKeyTouched] = useState(false);
  const [discoveringProviderId, setDiscoveringProviderId] = useState<string | null>(null);
  const [checkingProviderId, setCheckingProviderId] = useState<string | null>(null);
  const [providerForm, setProviderForm] = useState<ProviderForm>(emptyProviderForm);
  const [modelForm, setModelForm] = useState<ModelForm>(emptyModelForm);
  const [customProviderForm, setCustomProviderForm] = useState(emptyCustomProviderForm);

  const fetchData = useCallback(async () => {
    setLoading(true);
    try {
      const [providerData, modelData] = await Promise.all([
        getAiProviders(),
        getAiModels(),
      ]);
      const sortedProviders = sortProviders(providerData);
      setProviders(sortedProviders);
      setModels(modelData);
      setSelectedProviderId((current) => {
        if (current && sortedProviders.some((provider) => provider.id === current)) {
          return current;
        }
        return sortedProviders.find((provider) => provider.isActive)?.id
          ?? sortedProviders[0]?.id
          ?? null;
      });
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.loadFailed"));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const selectedProvider = useMemo(
    () => providers.find((provider) => provider.id === selectedProviderId) ?? null,
    [providers, selectedProviderId]
  );

  useEffect(() => {
    if (!selectedProvider) {
      setProviderForm(emptyProviderForm);
      return;
    }

    setProviderForm(toProviderForm(selectedProvider));
    setApiKeyTouched(false);
    setShowApiKey(false);
    setModelSearchQuery("");
    setModelDialog(null);
    setModelForm(emptyModelForm);
  }, [selectedProvider]);

  const filteredProviders = useMemo(() => {
    const query = searchQuery.trim().toLowerCase();
    if (!query) return providers;

    return providers.filter((provider) => {
      const haystack = [
        provider.name,
        provider.displayName,
        provider.providerType,
        provider.authType,
        provider.baseUrl,
      ].filter(Boolean).join(" ").toLowerCase();

      return haystack.includes(query);
    });
  }, [providers, searchQuery]);

  const activeProviders = filteredProviders.filter((provider) => provider.isActive);
  const inactiveProviders = filteredProviders.filter((provider) => !provider.isActive);
  const selectedStats = selectedProvider
    ? getProviderModelStats(models, selectedProvider.id)
    : { total: 0, active: 0, defaults: 0 };
  const selectedMetadata = parseOpenCoworkDescription(selectedProvider?.description);
  const selectedProviderModels = useMemo(() => {
    if (!selectedProvider) return [];

    return models
      .filter((model) => model.providerId === selectedProvider.id)
      .sort((left, right) => {
        if (left.isActive !== right.isActive) return left.isActive ? -1 : 1;
        if (left.sortOrder !== right.sortOrder) return left.sortOrder - right.sortOrder;
        return getModelTitle(left).localeCompare(getModelTitle(right), "zh-CN");
      });
  }, [models, selectedProvider]);
  const filteredSelectedModels = useMemo(() => {
    const query = modelSearchQuery.trim().toLowerCase();
    if (!query) return selectedProviderModels;

    return selectedProviderModels.filter((model) => {
      const haystack = [
        model.modelId,
        model.name,
        model.displayName,
        model.modelType,
        model.description,
      ].filter(Boolean).join(" ").toLowerCase();

      return haystack.includes(query);
    });
  }, [modelSearchQuery, selectedProviderModels]);

  const updateForm = <K extends keyof ProviderForm>(key: K, value: ProviderForm[K]) => {
    setProviderForm((current) => ({ ...current, [key]: value }));
  };

  const updateModelForm = <K extends keyof ModelForm>(key: K, value: ModelForm[K]) => {
    setModelForm((current) => ({ ...current, [key]: value }));
  };

  const saveProvider = async () => {
    if (!selectedProvider) return;
    if (!providerForm.name.trim()) {
      toast.error(t("admin.aiProviders.toasts.providerNameRequired"));
      return;
    }

    setSaving(true);
    try {
      await updateAiProvider(
        selectedProvider.id,
        buildProviderPayload(providerForm, apiKeyTouched)
      );
      toast.success(t("admin.aiProviders.toasts.providerSaved"));
      setApiKeyTouched(false);
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.providerSaveFailed"));
    } finally {
      setSaving(false);
    }
  };

  const createCustomProvider = async () => {
    const name = customProviderForm.name.trim();
    if (!name) {
      toast.error(t("admin.aiProviders.toasts.providerNameRequired"));
      return;
    }

    const duplicate = providers.some((provider) =>
      [provider.name, provider.displayName]
        .filter(Boolean)
        .some((value) => value!.trim().toLowerCase() === name.toLowerCase())
    );
    if (duplicate) {
      toast.error(t("admin.aiProviders.toasts.providerNameDuplicate"));
      return;
    }

    setSaving(true);
    try {
      const created = await createAiProvider({
        name,
        displayName: name,
        providerType: customProviderForm.providerType,
        baseUrl: customProviderForm.baseUrl.trim(),
        authType: "ApiKey",
        isBuiltIn: false,
        isActive: false,
        supportsModelDiscovery: true,
        sortOrder: providers.length * 10 + 10,
      });

      toast.success(t("admin.aiProviders.toasts.customProviderCreated"));
      setCustomProviderForm(emptyCustomProviderForm);
      setAddDialogOpen(false);
      setSelectedProviderId(created.id);
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.createProviderFailed"));
    } finally {
      setSaving(false);
    }
  };

  const openModelDialog = (model?: AiModelConfig) => {
    if (!selectedProvider) return;

    setModelDialog(model ?? "new");
    setModelForm(model
      ? toModelForm(model, selectedProvider)
      : {
          ...emptyModelForm,
          providerId: selectedProvider.id,
          providerType: inheritProviderTypeValue,
          sortOrder: String(selectedProviderModels.length * 10 + 10),
        });
  };

  const saveModel = async () => {
    if (!selectedProvider) return;
    if (!modelForm.modelId.trim()) {
      toast.error(t("admin.aiProviders.toasts.modelIdRequired"));
      return;
    }

    setSaving(true);
    try {
      const payload = buildModelFormPayload(modelForm);
      if (modelDialog && modelDialog !== "new") {
        await updateAiModel(modelDialog.id, payload);
      } else {
        await createAiModel(payload);
      }

      toast.success(t("admin.aiProviders.toasts.modelSaved"));
      setModelDialog(null);
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(error instanceof Error ? error.message : t("admin.aiProviders.toasts.modelSaveFailed"));
    } finally {
      setSaving(false);
    }
  };

  const removeSelectedProvider = async () => {
    if (!selectedProvider) return;
    if (selectedProvider.isBuiltIn) {
      toast.error(t("admin.aiProviders.toasts.builtinDeleteBlocked"));
      return;
    }

    if (!window.confirm(t("admin.aiProviders.confirmDeleteProvider", {
      name: getProviderTitle(selectedProvider) || t("admin.aiProviders.providerCard.unselected"),
    }))) return;

    try {
      await deleteAiProvider(selectedProvider.id);
      toast.success(t("admin.aiProviders.toasts.providerDeleted"));
      setSelectedProviderId(null);
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.providerDeleteFailed"));
    }
  };

  const handleDiscoverModels = async (provider: AiProviderConfig) => {
    setDiscoveringProviderId(provider.id);
    try {
      const discovered = await discoverAiModels(provider.id);
      const existingIds = new Set(
        models
          .filter((model) => model.providerId === provider.id)
          .map((model) => model.modelId.toLowerCase())
      );
      const missing = discovered.filter(
        (model) => !existingIds.has(model.modelId.toLowerCase())
      );

      await Promise.all(
        missing.map((model) => createAiModel({
          providerId: provider.id,
          modelId: model.modelId,
          name: model.name || model.modelId,
          displayName: model.displayName,
          modelType: model.modelType || "chat",
          providerType: model.providerType || (isOpenAIResponsesModelId(model.modelId)
            ? "OpenAIResponses"
            : provider.providerType),
          contextWindow: model.contextWindow,
          maxOutputTokens: model.maxOutputTokens,
          inputTokenPrice: model.inputTokenPrice,
          outputTokenPrice: model.outputTokenPrice,
          cacheHitTokenPrice: model.cacheHitTokenPrice,
          cacheCreationTokenPrice: model.cacheCreationTokenPrice,
          supportsThinking: model.supportsThinking,
          supportsVision: model.supportsVision,
          supportsTools: model.supportsTools ?? true,
          supportsJsonMode: model.supportsJsonMode,
          isDefault: false,
          isActive: true,
          capabilitiesJson: model.capabilitiesJson,
          thinkingConfigJson: model.thinkingConfigJson,
          requestOverridesJson: model.requestOverridesJson,
          tagsJson: model.tagsJson,
          description: model.description,
          sortOrder: 0,
        }))
      );

      toast.success(t("admin.aiProviders.toasts.modelsDiscovered", {
        discovered: discovered.length,
        created: missing.length,
      }));
      await fetchData();
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.discoverFailed"));
    } finally {
      setDiscoveringProviderId(null);
    }
  };

  const handleTestConnectivity = async (provider: AiProviderConfig) => {
    const modelId = providerForm.defaultModelId || selectedProviderModels[0]?.modelId;
    if (!modelId) {
      toast.error(t("admin.aiProviders.toasts.selectModelForCheck"));
      return;
    }

    setCheckingProviderId(provider.id);
    try {
      const result = await testAiProviderConnectivity(provider.id, {
        modelId,
        providerType: providerForm.providerType,
        baseUrl: providerForm.baseUrl,
        apiKey: apiKeyTouched ? providerForm.apiKey : undefined,
      });

      toast.success(`${result.message} (${result.latencyMs}ms)`);
    } catch (error) {
      console.error(error);
      toast.error(error instanceof Error ? error.message : t("admin.aiProviders.toasts.connectivityFailed"));
    } finally {
      setCheckingProviderId(null);
    }
  };

  const toggleModelActive = async (model: AiModelConfig, isActive: boolean) => {
    try {
      await updateAiModel(model.id, buildModelPayload(model, { isActive }));
      setModels((current) =>
        current.map((item) => (item.id === model.id ? { ...item, isActive } : item))
      );
      toast.success(
        isActive ? t("admin.aiProviders.toasts.modelEnabled") : t("admin.aiProviders.toasts.modelDisabled")
      );
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.modelStatusUpdateFailed"));
    }
  };

  const setAllSelectedModelsActive = async (isActive: boolean) => {
    const targets = selectedProviderModels.filter((model) => model.isActive !== isActive);
    if (targets.length === 0) return;

    try {
      await Promise.all(
        targets.map((model) => updateAiModel(model.id, buildModelPayload(model, { isActive })))
      );
      const targetIds = new Set(targets.map((model) => model.id));
      setModels((current) =>
        current.map((item) => (targetIds.has(item.id) ? { ...item, isActive } : item))
      );
      toast.success(
        isActive ? t("admin.aiProviders.toasts.allModelsEnabled") : t("admin.aiProviders.toasts.allModelsDisabled")
      );
    } catch (error) {
      console.error(error);
      toast.error(t("admin.aiProviders.toasts.bulkModelUpdateFailed"));
    }
  };

  const renderProviderList = (title: string, items: AiProviderConfig[], muted = false) => (
    <div className="space-y-1">
      <div className="flex items-center justify-between px-3 pb-1 pt-3 text-[11px] font-medium text-[#7f7f86]">
        <span>{title}</span>
        <span>{items.length}</span>
      </div>
      {items.length === 0 ? (
        <div className="mx-2 rounded-lg border border-dashed border-[#333] px-3 py-4 text-center text-xs text-[#777]">
          {t("admin.aiProviders.providerList.empty")}
        </div>
      ) : (
        items.map((provider, index) => {
          const isSelected = selectedProviderId === provider.id;

          return (
            <button
              key={provider.id}
              type="button"
              onClick={() => setSelectedProviderId(provider.id)}
              className={cn(
                "ai-provider-nav-item group flex h-7 w-full items-center gap-2 rounded-md px-2 text-left text-sm transition-colors",
                isSelected
                  ? "is-selected bg-[#303030] text-[#f5f5f5]"
                  : "text-[#b9b9c0] hover:bg-[#252525] hover:text-[#f1f1f1]",
                muted && !isSelected && "text-[#7c7c84]"
              )}
              style={{ animationDelay: `${Math.min(index * 24, 240)}ms` }}
            >
              <span className="flex size-5 shrink-0 items-center justify-center">
                <ProviderIcon
                  builtinId={provider.name}
                  iconUrl={provider.iconUrl}
                  size={16}
                  className={muted ? "opacity-55" : undefined}
                />
              </span>
              <span className="min-w-0 flex-1 truncate">{getProviderTitle(provider)}</span>
              {provider.isActive && (
                <span className="ai-provider-status-dot size-1.5 shrink-0 rounded-full bg-emerald-400" />
              )}
            </button>
          );
        })
      )}
    </div>
  );

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div
      className="ai-provider-workbench relative -m-6 overflow-hidden bg-[#171717] text-[#f4f4f5]"
      style={{ height: "calc(100dvh - 3.5rem)", minHeight: 0 }}
    >
      <style>{`
        .ai-provider-workbench {
          --ai-border: rgba(255, 255, 255, 0.08);
          --ai-line: rgba(255, 255, 255, 0.055);
          --ai-glow: rgba(52, 211, 153, 0.18);
          isolation: isolate;
          background:
            radial-gradient(circle at 12% -8%, rgba(52, 211, 153, 0.14), transparent 28%),
            radial-gradient(circle at 92% 12%, rgba(148, 163, 184, 0.12), transparent 24%),
            linear-gradient(135deg, #141414 0%, #181818 46%, #101010 100%);
        }

        .ai-provider-workbench::before,
        .ai-provider-workbench::after {
          content: "";
          position: absolute;
          inset: 0;
          pointer-events: none;
          z-index: 0;
        }

        .ai-provider-workbench::before {
          background:
            linear-gradient(115deg, transparent 0%, rgba(255,255,255,0.035) 34%, transparent 52%),
            linear-gradient(to right, transparent, rgba(255,255,255,0.035), transparent);
          transform: translateX(-120%);
          animation: aiWorkbenchSweep 3.8s cubic-bezier(.22,1,.36,1) .15s both;
        }

        .ai-provider-workbench::after {
          opacity: 0.34;
          background-image:
            linear-gradient(var(--ai-line) 1px, transparent 1px),
            linear-gradient(90deg, var(--ai-line) 1px, transparent 1px);
          background-size: 38px 38px;
          mask-image: radial-gradient(circle at 58% 42%, black, transparent 72%);
        }

        .ai-provider-grid {
          position: relative;
          z-index: 1;
          animation: aiWorkbenchIn .42s cubic-bezier(.22,1,.36,1) both;
        }

        .ai-provider-sidebar,
        .ai-provider-main {
          backdrop-filter: blur(18px);
        }

        .ai-provider-sidebar {
          height: 100%;
          max-height: 100%;
          min-height: 0;
          overflow: hidden;
          box-shadow: inset -1px 0 0 rgba(255,255,255,0.03);
        }

        .ai-provider-sidebar-scroll {
          flex: 1 1 0;
          height: 0;
          min-height: 0;
          overflow-x: hidden;
          overflow-y: scroll;
          overscroll-behavior: contain;
          scrollbar-gutter: stable;
        }

        .ai-provider-sidebar-scroll::-webkit-scrollbar {
          width: 10px;
        }

        .ai-provider-sidebar-scroll::-webkit-scrollbar-track {
          background: rgba(255,255,255,0.035);
          border-left: 1px solid rgba(255,255,255,0.04);
        }

        .ai-provider-sidebar-scroll::-webkit-scrollbar-thumb {
          background: linear-gradient(180deg, rgba(255,255,255,0.22), rgba(255,255,255,0.09));
          border: 2px solid #181818;
          border-radius: 999px;
        }

        .ai-provider-sidebar-scroll::-webkit-scrollbar-thumb:hover {
          background: linear-gradient(180deg, rgba(255,255,255,0.32), rgba(255,255,255,0.15));
        }

        .ai-provider-title-icon,
        .ai-provider-detail-icon,
        .ai-provider-model-icon {
          position: relative;
          overflow: hidden;
        }

        .ai-provider-title-icon::after,
        .ai-provider-detail-icon::after,
        .ai-provider-model-icon::after {
          content: "";
          position: absolute;
          inset: -30%;
          background: radial-gradient(circle, rgba(255,255,255,.22), transparent 46%);
          opacity: 0;
          transform: scale(.65);
          transition: opacity .22s ease, transform .22s ease;
        }

        .ai-provider-title-icon:hover::after,
        .ai-provider-detail-icon:hover::after,
        .ai-provider-model-row:hover .ai-provider-model-icon::after {
          opacity: 1;
          transform: scale(1);
        }

        .ai-provider-nav-item {
          position: relative;
          overflow: hidden;
          opacity: 0;
          transform: translateX(-10px);
          animation: aiNavItemIn .34s cubic-bezier(.22,1,.36,1) both;
        }

        .ai-provider-nav-item::before {
          content: "";
          position: absolute;
          inset: 0;
          border-radius: inherit;
          background: linear-gradient(90deg, rgba(255,255,255,.08), transparent 42%);
          opacity: 0;
          transform: translateX(-40%);
          transition: opacity .2s ease, transform .28s ease;
        }

        .ai-provider-nav-item:hover::before,
        .ai-provider-nav-item.is-selected::before {
          opacity: 1;
          transform: translateX(0);
        }

        .ai-provider-nav-item.is-selected {
          box-shadow:
            inset 0 0 0 1px rgba(255,255,255,0.055),
            0 10px 24px rgba(0,0,0,0.18);
        }

        .ai-provider-status-dot {
          box-shadow: 0 0 0 0 rgba(52, 211, 153, .42);
          animation: aiStatusPulse 2.2s ease-out infinite;
        }

        .ai-provider-topbar {
          position: relative;
        }

        .ai-provider-topbar::after {
          content: "";
          position: absolute;
          left: 0;
          right: 0;
          bottom: -1px;
          height: 1px;
          background: linear-gradient(90deg, transparent, rgba(255,255,255,.18), transparent);
          opacity: .75;
        }

        .ai-provider-panel {
          position: relative;
          overflow: hidden;
          animation: aiPanelIn .42s cubic-bezier(.22,1,.36,1) both;
        }

        .ai-provider-panel::before {
          content: "";
          position: absolute;
          inset: 0;
          border-radius: inherit;
          pointer-events: none;
          background:
            radial-gradient(circle at 12% 0%, rgba(52,211,153,.09), transparent 32%),
            linear-gradient(180deg, rgba(255,255,255,.035), transparent 46%);
          opacity: .86;
        }

        .ai-provider-panel > * {
          position: relative;
          z-index: 1;
        }

        .ai-provider-model-panel {
          animation-delay: .07s;
        }

        .ai-provider-advanced-panel {
          animation-delay: .13s;
        }

        .ai-provider-model-row {
          position: relative;
          opacity: 0;
          transform: translateY(8px);
          animation: aiModelRowIn .28s cubic-bezier(.22,1,.36,1) both;
          transition: background-color .18s ease, border-color .18s ease, transform .18s ease;
        }

        .ai-provider-model-row::before {
          content: "";
          position: absolute;
          inset: 6px -8px;
          border-radius: 14px;
          background: linear-gradient(90deg, rgba(52,211,153,.08), rgba(255,255,255,.04), transparent);
          opacity: 0;
          transform: scaleX(.96);
          transition: opacity .18s ease, transform .18s ease;
          z-index: 0;
        }

        .ai-provider-model-row:hover {
          border-color: rgba(255,255,255,.13);
          transform: translateX(2px);
        }

        .ai-provider-model-row:hover::before {
          opacity: 1;
          transform: scaleX(1);
        }

        .ai-provider-model-row > * {
          position: relative;
          z-index: 1;
        }

        .ai-provider-action {
          transition: transform .18s ease, border-color .18s ease, background-color .18s ease, color .18s ease;
        }

        .ai-provider-action:hover {
          transform: translateY(-1px);
          border-color: rgba(255,255,255,.16);
        }

        .ai-provider-primary-action {
          box-shadow: 0 8px 24px rgba(255,255,255,.08);
          transition: transform .18s ease, box-shadow .18s ease, background-color .18s ease;
        }

        .ai-provider-primary-action:hover {
          transform: translateY(-1px);
          box-shadow: 0 12px 34px rgba(255,255,255,.12);
        }

        @keyframes aiWorkbenchIn {
          from { opacity: 0; transform: translateY(10px) scale(.992); }
          to { opacity: 1; transform: translateY(0) scale(1); }
        }

        @keyframes aiWorkbenchSweep {
          0% { transform: translateX(-120%); opacity: 0; }
          24% { opacity: 1; }
          100% { transform: translateX(120%); opacity: 0; }
        }

        @keyframes aiNavItemIn {
          to { opacity: 1; transform: translateX(0); }
        }

        @keyframes aiPanelIn {
          from { opacity: 0; transform: translateY(12px); }
          to { opacity: 1; transform: translateY(0); }
        }

        @keyframes aiModelRowIn {
          to { opacity: 1; transform: translateY(0); }
        }

        @keyframes aiStatusPulse {
          0% { box-shadow: 0 0 0 0 rgba(52,211,153,.38); }
          72% { box-shadow: 0 0 0 7px rgba(52,211,153,0); }
          100% { box-shadow: 0 0 0 0 rgba(52,211,153,0); }
        }

        @media (prefers-reduced-motion: reduce) {
          .ai-provider-workbench::before,
          .ai-provider-grid,
          .ai-provider-nav-item,
          .ai-provider-status-dot,
          .ai-provider-panel,
          .ai-provider-model-row {
            animation: none !important;
          }

          .ai-provider-nav-item,
          .ai-provider-model-row {
            opacity: 1;
            transform: none;
          }
        }
      `}</style>
      <div
        className="ai-provider-grid grid h-full min-h-0 overflow-hidden"
        style={{
          gridTemplateColumns: "clamp(220px, 18vw, 248px) minmax(0, 1fr)",
        }}
      >
        <aside
          className="ai-provider-sidebar flex h-full max-h-full min-h-0 flex-col border-r border-[#303030] bg-[#181818]"
          style={{ minWidth: 0 }}
        >
          <div className="shrink-0 border-b border-[#242424] px-5 pb-4 pt-6">
            <div className="flex items-start gap-2">
              <span className="ai-provider-title-icon mt-0.5 flex size-7 items-center justify-center rounded-lg border border-[#343434] bg-[#242424] text-[#e8e8e8] shadow-sm">
                <Server className="h-4 w-4" />
              </span>
              <div className="min-w-0">
                <h1 className="text-[21px] font-semibold leading-6 tracking-tight text-[#f5f5f5]">
                  {t("admin.aiProviders.title")}
                </h1>
                <p className="mt-1 text-xs text-[#888891]">
                  {t("admin.aiProviders.subtitle")}
                </p>
              </div>
            </div>

            <div className="mt-5 flex items-center gap-2">
              <div className="relative min-w-0 flex-1">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#74747c]" />
                <Input
                  placeholder={t("admin.aiProviders.searchProvidersPlaceholder")}
                  value={searchQuery}
                  onChange={(event) => setSearchQuery(event.target.value)}
                  className="h-7 rounded-md border-0 bg-[#242424] pl-8 text-xs text-[#e8e8e8] shadow-none placeholder:text-[#777780] focus-visible:ring-1 focus-visible:ring-[#4a4a4a]"
                />
              </div>
              <button
                type="button"
                onClick={() => setAddDialogOpen(true)}
                className="ai-provider-action flex size-7 shrink-0 items-center justify-center rounded-md text-[#a7a7ad] transition-colors hover:bg-[#242424] hover:text-white"
                aria-label={t("admin.aiProviders.actions.addProvider")}
              >
                <Plus className="h-4 w-4" />
              </button>
            </div>
          </div>

          <div
            className="ai-provider-sidebar-scroll px-3 pb-5 pt-2"
            style={{ scrollbarColor: "#454545 #181818" }}
          >
            {renderProviderList(t("admin.aiProviders.providerGroups.active"), activeProviders)}
            {renderProviderList(t("admin.aiProviders.providerGroups.inactive"), inactiveProviders, true)}
          </div>
        </aside>

        <main className="ai-provider-main min-h-0 min-w-0 overflow-hidden bg-[#171717]">
          {selectedProvider ? (
            <div className="flex h-full min-h-0 flex-col">
              <div className="ai-provider-topbar flex h-[78px] shrink-0 items-center justify-between border-b border-[#303030] bg-[#171717]/95 px-5">
                <div className="flex min-w-0 items-center gap-3">
                  <div className="ai-provider-detail-icon flex size-9 shrink-0 items-center justify-center rounded-lg border border-[#353535] bg-[#242424]">
                    <ProviderIcon
                      builtinId={selectedProvider.name}
                      iconUrl={providerForm.iconUrl || selectedProvider.iconUrl}
                      size={23}
                    />
                  </div>
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <h2 className="truncate text-sm font-semibold text-[#f4f4f5]">
                        {getProviderTitle(selectedProvider)}
                      </h2>
                      {selectedProvider.isBuiltIn && (
                        <span className="rounded-full bg-[#262626] px-2 py-0.5 text-[10px] text-[#9b9ba3]">
                          {t("admin.aiProviders.providerCard.builtin")}
                        </span>
                      )}
                    </div>
                    <p className="mt-1 truncate text-xs text-[#888891]">
                      {getProviderTypeLabel(selectedProvider.providerType, providerTypes)}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={fetchData}
                    className="ai-provider-action flex size-8 items-center justify-center rounded-md text-[#9898a0] transition-colors hover:bg-[#242424] hover:text-white"
                    aria-label={t("admin.aiProviders.actions.refresh")}
                  >
                    <RefreshCw className="h-4 w-4" />
                  </button>
                  {!selectedProvider.isBuiltIn && (
                    <button
                      type="button"
                      onClick={removeSelectedProvider}
                      className="ai-provider-action flex size-8 items-center justify-center rounded-md text-[#9898a0] transition-colors hover:bg-[#2a1f1f] hover:text-red-300"
                      aria-label={t("admin.aiProviders.actions.deleteProvider")}
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  )}
                  <Button
                    size="sm"
                    onClick={saveProvider}
                    disabled={saving}
                    className="ai-provider-primary-action h-8 rounded-md bg-[#f4f4f5] px-3 text-xs font-medium text-[#171717] hover:bg-white"
                  >
                    {saving && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
                    {t("admin.aiProviders.actions.saveProvider")}
                  </Button>
                  <Switch
                    checked={providerForm.isActive}
                    onCheckedChange={(checked) => updateForm("isActive", checked)}
                  />
                </div>
              </div>

              <div className="min-h-0 flex-1 overflow-hidden px-5 py-5">
                <div className="flex h-full min-h-0 flex-col gap-4">
                  <section
                    className="ai-provider-panel max-h-[38vh] shrink-0 overflow-y-auto rounded-2xl border border-[#303030] bg-[#191919] p-4 shadow-[0_18px_55px_rgba(0,0,0,0.18)]"
                    style={{ scrollbarColor: "#454545 #191919" }}
                  >
                    <div className="space-y-2">
                      <div className="flex items-center justify-between">
                        <label className="text-sm font-medium text-[#f0f0f0]">API Key</label>
                        {selectedMetadata.apiKeyUrl && (
                          <a
                            href={selectedMetadata.apiKeyUrl}
                            target="_blank"
                            rel="noreferrer"
                          className="ai-provider-action inline-flex items-center gap-1 text-xs text-[#aaaab2] transition-colors hover:text-white"
                          >
                            <ExternalLink className="h-3.5 w-3.5" />
                            {t("admin.aiProviders.apiKey.getKey")}
                          </a>
                        )}
                      </div>
                      <div className="relative">
                        <Input
                          type={showApiKey ? "text" : "password"}
                          placeholder={
                            selectedProvider.hasApiKey
                              ? t("admin.aiProviders.apiKey.keepExistingPlaceholder")
                              : t("admin.aiProviders.apiKey.placeholder")
                          }
                          value={providerForm.apiKey}
                          onChange={(event) => {
                            setApiKeyTouched(true);
                            updateForm("apiKey", event.target.value);
                          }}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] pr-10 text-sm text-[#f4f4f5] shadow-none placeholder:text-[#777780] focus-visible:ring-1 focus-visible:ring-[#555]"
                        />
                        <button
                          type="button"
                          className="absolute right-3 top-1/2 -translate-y-1/2 text-[#8c8c94] transition-colors hover:text-white"
                          onClick={() => setShowApiKey((value) => !value)}
                          tabIndex={-1}
                        >
                          {showApiKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                        </button>
                      </div>
                      {selectedMetadata.requiresApiKey === false && (
                        <p className="text-xs text-[#85858e]">
                          {t("admin.aiProviders.apiKey.noKeyRequiredHint")}
                        </p>
                      )}
                    </div>

                    <div className="mt-4 space-y-2">
                      <label className="text-sm font-medium text-[#f0f0f0]">{t("admin.aiProviders.baseUrl.label")}</label>
                      <Input
                        placeholder="https://api.example.com/v1"
                        value={providerForm.baseUrl}
                        onChange={(event) => updateForm("baseUrl", event.target.value)}
                        className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5] shadow-none placeholder:text-[#777780] focus-visible:ring-1 focus-visible:ring-[#555]"
                      />
                      <p className="text-xs text-[#85858e]">
                        {t("admin.aiProviders.baseUrl.hint")}
                      </p>
                    </div>

                    <div className="mt-4 grid gap-3 lg:grid-cols-[minmax(0,1fr)_108px]">
                      <label className="space-y-2">
                        <span className="text-sm font-medium text-[#f0f0f0]">{t("admin.aiProviders.connectivity.label")}</span>
                        <Select
                          value={providerForm.defaultModelId || undefined}
                          onValueChange={(value) => updateForm("defaultModelId", value)}
                          disabled={selectedProviderModels.length === 0}
                        >
                          <SelectTrigger className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5] shadow-none focus:ring-[#555]">
                            <SelectValue placeholder={t("admin.aiProviders.connectivity.selectModelPlaceholder")} />
                          </SelectTrigger>
                          <SelectContent>
                            {selectedProviderModels.map((model) => (
                              <SelectItem key={model.id} value={model.modelId}>
                                {getModelTitle(model)}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </label>
                      <div className="flex items-end">
                        <Button
                          variant="outline"
                          disabled={checkingProviderId === selectedProvider.id || selectedProviderModels.length === 0}
                          onClick={() => handleTestConnectivity(selectedProvider)}
                          className="ai-provider-action h-9 w-full rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5] hover:bg-[#2a2a2a] hover:text-white"
                        >
                          {checkingProviderId === selectedProvider.id ? (
                            <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                          ) : (
                            <CheckCircle2 className="mr-1.5 h-3.5 w-3.5" />
                          )}
                          {t("admin.aiProviders.connectivity.check")}
                        </Button>
                      </div>
                    </div>
                  </section>

                  <section className="ai-provider-panel ai-provider-model-panel flex min-h-0 flex-1 flex-col overflow-hidden rounded-2xl border border-[#333] bg-[#191919] shadow-[0_18px_55px_rgba(0,0,0,0.16)]">
                    <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-4">
                      <div>
                        <div className="flex items-center gap-2">
                          <h3 className="text-sm font-semibold text-[#f4f4f5]">{t("admin.aiProviders.models.title")}</h3>
                          <span className="rounded-full border border-[#333] px-2 py-0.5 text-[11px] text-[#b8b8bf]">
                            {selectedStats.active} / {selectedStats.total}
                          </span>
                        </div>
                        <p className="mt-1 text-xs text-[#85858e]">
                          {t("admin.aiProviders.models.summary", {
                            total: selectedStats.total,
                            active: selectedStats.active,
                          })}
                        </p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <button
                          type="button"
                          onClick={() => setAllSelectedModelsActive(true)}
                          className="ai-provider-action h-8 rounded-full border border-[#333] px-3 text-xs text-[#b8b8bf] transition-colors hover:bg-[#252525] hover:text-white"
                        >
                          {t("admin.aiProviders.models.enableAll")}
                        </button>
                        <button
                          type="button"
                          onClick={() => setAllSelectedModelsActive(false)}
                          className="ai-provider-action h-8 rounded-full border border-[#333] bg-[#252525] px-3 text-xs font-medium text-[#f1f1f1] transition-colors hover:bg-[#303030]"
                        >
                          {t("admin.aiProviders.models.disableAll")}
                        </button>
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={discoveringProviderId === selectedProvider.id || !providerForm.supportsModelDiscovery}
                          onClick={() => handleDiscoverModels(selectedProvider)}
                          className="ai-provider-action h-8 rounded-full border-[#333] bg-[#202020] px-3 text-xs text-[#f4f4f5] hover:bg-[#2a2a2a] hover:text-white"
                        >
                          {discoveringProviderId === selectedProvider.id ? (
                            <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
                          ) : (
                            <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
                          )}
                          {t("admin.aiProviders.models.discover")}
                        </Button>
                        <button
                          type="button"
                          onClick={() => openModelDialog()}
                          className="ai-provider-action flex size-8 items-center justify-center rounded-full border border-[#333] text-[#b8b8bf] transition-colors hover:bg-[#252525] hover:text-white"
                          aria-label={t("admin.aiProviders.actions.addModel")}
                        >
                          <Plus className="h-4 w-4" />
                        </button>
                      </div>
                    </div>

                    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[#282828] px-4 py-3">
                      <div className="relative w-full max-w-[320px]">
                        <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#74747c]" />
                        <Input
                          placeholder={t("admin.aiProviders.searchModelsPlaceholder")}
                          value={modelSearchQuery}
                          onChange={(event) => setModelSearchQuery(event.target.value)}
                          className="h-9 rounded-lg border-0 bg-[#242424] pl-9 text-xs text-[#e8e8e8] shadow-none placeholder:text-[#777780] focus-visible:ring-1 focus-visible:ring-[#4a4a4a]"
                        />
                      </div>
                      <div className="flex items-center gap-2 text-xs text-[#85858e]">
                        <span>{getAuthTypeLabel(providerForm.authType, authTypes)}</span>
                        <span>·</span>
                        <span>
                          {providerForm.supportsModelDiscovery
                            ? t("admin.aiProviders.models.discoveryModeEnabled")
                            : t("admin.aiProviders.models.discoveryModeManual")}
                        </span>
                      </div>
                    </div>

                    <div
                      className="min-h-0 flex-1 overflow-y-auto px-4 pb-2"
                      style={{ scrollbarColor: "#454545 #191919" }}
                    >
                      {filteredSelectedModels.length === 0 ? (
                        <div className="flex min-h-[180px] flex-col items-center justify-center gap-2 text-center text-[#85858e]">
                          <Layers3 className="h-8 w-8" />
                          <p className="text-sm">{t("admin.aiProviders.models.emptyTitle")}</p>
                          <p className="text-xs">{t("admin.aiProviders.models.emptyDescription")}</p>
                        </div>
                      ) : (
                        filteredSelectedModels.map((model, index) => (
                          <div
                            key={model.id}
                            className="ai-provider-model-row flex min-h-[64px] items-center gap-3 border-t border-[#282828] py-3 first:border-t-0"
                            style={{ animationDelay: `${Math.min(index * 18, 220)}ms` }}
                          >
                            <span className="ai-provider-model-icon flex size-10 shrink-0 items-center justify-center rounded-full border border-[#303030] bg-[#202020]">
                              <ModelIcon
                                modelId={model.modelId}
                                providerBuiltinId={selectedProvider.name}
                                size={18}
                              />
                            </span>
                            <div className="min-w-0 flex-1">
                              <div className="flex flex-wrap items-center gap-2">
                                <h4 className="truncate text-sm font-medium text-[#f1f1f1]">
                                  {getModelTitle(model)}
                                </h4>
                                <span className="rounded-full bg-[#252525] px-2 py-0.5 text-[10px] text-[#85858e]">
                                  {model.modelId}
                                </span>
                                {model.isDefault && (
                                  <span className="rounded-full bg-emerald-500/10 px-2 py-0.5 text-[10px] text-emerald-300">
                                    {t("admin.aiProviders.modelDialog.flags.defaultModel")}
                                  </span>
                                )}
                              </div>
                              <div className="mt-1 flex flex-wrap items-center gap-2 text-[11px] text-[#7f7f86]">
                                <span>{getProviderTypeLabel(getModelProviderType(model, selectedProvider), providerTypes)}</span>
                                <span>{formatContextWindow(model.contextWindow)}</span>
                                <span>
                                  IN {formatModelPrice(model.inputTokenPrice)} / OUT {formatModelPrice(model.outputTokenPrice)}
                                </span>
                                <span>
                                  HIT {formatModelPrice(model.cacheHitTokenPrice)} / CREATE {formatModelPrice(model.cacheCreationTokenPrice)}
                                </span>
                                {model.supportsThinking && <span className="text-emerald-400">thinking</span>}
                                {model.supportsVision && <span>vision</span>}
                                {model.supportsTools && <span>tools</span>}
                                {model.supportsJsonMode && <span>json</span>}
                              </div>
                            </div>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon"
                              className="size-8 shrink-0 text-[#9a9aa2] hover:bg-[#252525] hover:text-white"
                              aria-label={t("admin.aiProviders.actions.editModel", { name: getModelTitle(model) })}
                              onClick={() => openModelDialog(model)}
                            >
                              <Pencil className="h-3.5 w-3.5" />
                            </Button>
                            <Switch
                              checked={model.isActive}
                              onCheckedChange={(checked) => toggleModelActive(model, checked)}
                            />
                          </div>
                        ))
                      )}
                    </div>
                  </section>

                  <details className="ai-provider-panel ai-provider-advanced-panel max-h-[42vh] shrink-0 overflow-y-auto rounded-2xl border border-[#333] bg-[#191919] px-4 py-3">
                    <summary className="cursor-pointer text-sm font-medium text-[#f1f1f1]">
                      {t("admin.aiProviders.advanced.title")}
                    </summary>
                    <div className="mt-4 grid gap-4 lg:grid-cols-2">
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.uniqueName")}</span>
                        <Input
                          value={providerForm.name}
                          disabled={selectedProvider.isBuiltIn}
                          onChange={(event) => updateForm("name", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.displayName")}</span>
                        <Input
                          value={providerForm.displayName}
                          onChange={(event) => updateForm("displayName", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.providerType")}</span>
                        <Select
                          value={providerForm.providerType}
                          onValueChange={(value) => updateForm("providerType", value)}
                          disabled={selectedProvider.isBuiltIn}
                        >
                          <SelectTrigger className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {providerTypes.map((type) => (
                              <SelectItem key={type.value} value={type.value}>
                                <div className="flex flex-col">
                                  <span>{type.label}</span>
                                  <span className="text-xs text-muted-foreground">{type.hint}</span>
                                </div>
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.authType")}</span>
                        <Select
                          value={providerForm.authType}
                          onValueChange={(value) => updateForm("authType", value)}
                        >
                          <SelectTrigger className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {authTypes.map((type) => (
                              <SelectItem key={type.value} value={type.value}>
                                {type.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.modelsEndpoint")}</span>
                        <Input
                          placeholder={t("admin.aiProviders.advanced.modelsEndpointPlaceholder")}
                          value={providerForm.modelsEndpoint}
                          onChange={(event) => updateForm("modelsEndpoint", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.systemProxyUrl")}</span>
                        <Input
                          placeholder={t("admin.aiProviders.advanced.optional")}
                          value={providerForm.systemProxyUrl}
                          onChange={(event) => updateForm("systemProxyUrl", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.iconUrl")}</span>
                        <Input
                          placeholder={t("admin.aiProviders.advanced.iconUrlPlaceholder")}
                          value={providerForm.iconUrl}
                          onChange={(event) => updateForm("iconUrl", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.sortOrder")}</span>
                        <Input
                          type="number"
                          value={providerForm.sortOrder}
                          onChange={(event) => updateForm("sortOrder", event.target.value)}
                          className="h-9 rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="space-y-2 lg:col-span-2">
                        <span className="text-xs font-medium text-[#b8b8bf]">{t("admin.aiProviders.advanced.description")}</span>
                        <Textarea
                          value={providerForm.description}
                          onChange={(event) => updateForm("description", event.target.value)}
                          rows={3}
                          className="rounded-lg border-[#383838] bg-[#202020] text-sm text-[#f4f4f5]"
                        />
                      </label>
                      <label className="flex items-center justify-between rounded-lg border border-[#333] bg-[#202020] px-3 py-2 lg:col-span-2">
                        <span>
                          <span className="block text-sm font-medium text-[#f1f1f1]">{t("admin.aiProviders.advanced.enableDiscovery")}</span>
                          <span className="text-xs text-[#85858e]">{t("admin.aiProviders.advanced.enableDiscoveryHint")}</span>
                        </span>
                        <Switch
                          checked={providerForm.supportsModelDiscovery}
                          onCheckedChange={(checked) => updateForm("supportsModelDiscovery", checked)}
                        />
                      </label>
                    </div>

                    {(providerForm.authType === "OAuth" || providerForm.authType === "Channel") && (
                      <div className="mt-4 rounded-lg border border-[#333] bg-[#202020] p-3">
                        <div className="flex items-start gap-2 text-[#b8b8bf]">
                          <KeyRound className="mt-0.5 h-4 w-4" />
                          <p className="text-xs leading-5">
                            {t("admin.aiProviders.advanced.oauthChannelHint")}
                          </p>
                        </div>
                      </div>
                    )}

                    <div className="mt-4 grid gap-4 lg:grid-cols-2">
                      <Textarea
                        placeholder="OAuth Config JSON"
                        value={providerForm.oauthConfigJson}
                        onChange={(event) => updateForm("oauthConfigJson", event.target.value)}
                        className="min-h-28 rounded-lg border-[#383838] bg-[#202020] font-mono text-xs text-[#f4f4f5]"
                      />
                      <Textarea
                        placeholder="Channel Config JSON"
                        value={providerForm.channelConfigJson}
                        onChange={(event) => updateForm("channelConfigJson", event.target.value)}
                        className="min-h-28 rounded-lg border-[#383838] bg-[#202020] font-mono text-xs text-[#f4f4f5]"
                      />
                      <Textarea
                        placeholder="Accounts JSON"
                        value={providerForm.accountsJson}
                        onChange={(event) => updateForm("accountsJson", event.target.value)}
                        className="min-h-28 rounded-lg border-[#383838] bg-[#202020] font-mono text-xs text-[#f4f4f5]"
                      />
                      <Textarea
                        placeholder="Request Overrides JSON"
                        value={providerForm.requestOverridesJson}
                        onChange={(event) => updateForm("requestOverridesJson", event.target.value)}
                        className="min-h-28 rounded-lg border-[#383838] bg-[#202020] font-mono text-xs text-[#f4f4f5]"
                      />
                    </div>

                    {selectedMetadata.homepage && (
                      <a
                        href={selectedMetadata.homepage}
                        target="_blank"
                        rel="noreferrer"
                        className="ai-provider-action mt-4 inline-flex items-center gap-1 text-xs text-[#aaaab2] transition-colors hover:text-white"
                      >
                        <ExternalLink className="h-3.5 w-3.5" />
                        {t("admin.aiProviders.advanced.openHomepage")}
                      </a>
                    )}
                  </details>
                </div>
              </div>
            </div>
          ) : (
            <div className="flex h-full min-h-[520px] flex-col items-center justify-center gap-3 text-center">
              <Bot className="h-10 w-10 text-[#85858e]" />
              <div>
                <h2 className="font-semibold text-[#f4f4f5]">{t("admin.aiProviders.empty.title")}</h2>
                <p className="mt-1 text-sm text-[#85858e]">
                  {t("admin.aiProviders.empty.description")}
                </p>
              </div>
              <Button
                onClick={() => setAddDialogOpen(true)}
                className="rounded-lg bg-[#f4f4f5] text-[#171717] hover:bg-white"
              >
                <Plus className="mr-2 h-4 w-4" />
                {t("admin.aiProviders.empty.create")}
              </Button>
            </div>
          )}
        </main>
      </div>

      <Dialog open={!!modelDialog} onOpenChange={(open) => !open && setModelDialog(null)}>
        <DialogContent className="sm:max-w-3xl">
          <DialogHeader>
            <DialogTitle>
              {modelDialog === "new"
                ? t("admin.aiProviders.modelDialog.newTitle")
                : t("admin.aiProviders.modelDialog.editTitle")}
            </DialogTitle>
            <DialogDescription>
              {t("admin.aiProviders.modelDialog.description")}
            </DialogDescription>
          </DialogHeader>
          <div className="max-h-[70vh] space-y-4 overflow-y-auto pr-1">
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.modelIdLabel")}</span>
                <Input
                  value={modelForm.modelId}
                  onChange={(event) => {
                    const modelId = event.target.value;
                    setModelForm((current) => ({
                      ...current,
                      modelId,
                      name: current.name || modelId,
                      providerType: current.providerType === inheritProviderTypeValue &&
                        isOpenAIResponsesModelId(modelId)
                        ? "OpenAIResponses"
                        : current.providerType,
                    }));
                  }}
                  placeholder={t("admin.aiProviders.modelDialog.modelIdPlaceholder")}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.displayNameLabel")}</span>
                <Input
                  value={modelForm.displayName}
                  onChange={(event) => updateModelForm("displayName", event.target.value)}
                  placeholder={t("admin.aiProviders.modelDialog.optionalPlaceholder")}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.modelTypeLabel")}</span>
                <Select
                  value={modelForm.modelType}
                  onValueChange={(value) => updateModelForm("modelType", value)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {modelTypes.map((type) => (
                      <SelectItem key={type} value={type}>{type}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.providerTypeLabel")}</span>
                <Select
                  value={modelForm.providerType}
                  onValueChange={(value) => updateModelForm("providerType", value)}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {modelProviderTypes.map((type) => (
                      <SelectItem key={type.value} value={type.value}>
                        <div className="flex flex-col">
                          <span>
                            {type.value === inheritProviderTypeValue
                              ? t("admin.aiProviders.providerTypes.inheritWithCurrent", {
                                type: getProviderTypeLabel(providerForm.providerType, providerTypes),
                              })
                              : type.label}
                          </span>
                          <span className="text-xs text-muted-foreground">{type.hint}</span>
                        </div>
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.contextWindowLabel")}</span>
                <Input
                  type="number"
                  value={modelForm.contextWindow}
                  onChange={(event) => updateModelForm("contextWindow", event.target.value)}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.maxOutputLabel")}</span>
                <Input
                  type="number"
                  value={modelForm.maxOutputTokens}
                  onChange={(event) => updateModelForm("maxOutputTokens", event.target.value)}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.inputPriceLabel")}</span>
                <Input
                  type="number"
                  value={modelForm.inputTokenPrice}
                  onChange={(event) => updateModelForm("inputTokenPrice", event.target.value)}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.outputPriceLabel")}</span>
                <Input
                  type="number"
                  value={modelForm.outputTokenPrice}
                  onChange={(event) => updateModelForm("outputTokenPrice", event.target.value)}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">Cache hit price / 1M</span>
                <Input
                  type="number"
                  value={modelForm.cacheHitTokenPrice}
                  onChange={(event) => updateModelForm("cacheHitTokenPrice", event.target.value)}
                />
              </label>
              <label className="space-y-2">
                <span className="text-sm font-medium">Cache create price / 1M</span>
                <Input
                  type="number"
                  value={modelForm.cacheCreationTokenPrice}
                  onChange={(event) => updateModelForm("cacheCreationTokenPrice", event.target.value)}
                />
              </label>
            </div>

            <div className="grid gap-3 rounded-lg border border-[#333] bg-[#202020] p-3 sm:grid-cols-3">
              {[
                ["supportsThinking", "Thinking"],
                ["supportsVision", "Vision"],
                ["supportsTools", "Tools"],
                ["supportsJsonMode", "JSON"],
                ["isDefault", t("admin.aiProviders.modelDialog.flags.defaultModel")],
                ["isActive", t("admin.aiProviders.modelDialog.flags.active")],
              ].map(([key, label]) => (
                <label key={key} className="flex items-center justify-between gap-3 text-sm">
                  <span>{label}</span>
                  <Switch
                    checked={Boolean(modelForm[key as keyof ModelForm])}
                    onCheckedChange={(checked) =>
                      updateModelForm(key as keyof ModelForm, checked as never)
                    }
                  />
                </label>
              ))}
            </div>

            <label className="block space-y-2">
              <span className="text-sm font-medium">{t("admin.aiProviders.modelDialog.descriptionLabel")}</span>
              <Textarea
                value={modelForm.description}
                onChange={(event) => updateModelForm("description", event.target.value)}
                rows={2}
              />
            </label>

            <div className="grid gap-4 md:grid-cols-2">
              <Textarea
                placeholder="Capabilities JSON"
                value={modelForm.capabilitiesJson}
                onChange={(event) => updateModelForm("capabilitiesJson", event.target.value)}
                className="min-h-24 font-mono text-xs"
              />
              <ModelThinkingConfig
                modelId={modelForm.modelId}
                supportsThinking={modelForm.supportsThinking}
                value={modelForm.thinkingConfigJson}
                onChange={(thinkingConfigJson) =>
                  updateModelForm("thinkingConfigJson", thinkingConfigJson)
                }
                textareaClassName="min-h-24"
              />
              <Textarea
                placeholder="Request Overrides JSON"
                value={modelForm.requestOverridesJson}
                onChange={(event) => updateModelForm("requestOverridesJson", event.target.value)}
                className="min-h-24 font-mono text-xs"
              />
              <Textarea
                placeholder="Tags JSON"
                value={modelForm.tagsJson}
                onChange={(event) => updateModelForm("tagsJson", event.target.value)}
                className="min-h-24 font-mono text-xs"
              />
            </div>

            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setModelDialog(null)}>
                {t("admin.aiProviders.modelDialog.cancel")}
              </Button>
              <Button disabled={saving || !modelForm.modelId.trim()} onClick={saveModel}>
                {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                {t("admin.aiProviders.modelDialog.save")}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>

      <Dialog open={addDialogOpen} onOpenChange={setAddDialogOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>{t("admin.aiProviders.createProviderDialog.title")}</DialogTitle>
            <DialogDescription>
              {t("admin.aiProviders.createProviderDialog.description")}
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-4 pt-2">
            <label className="space-y-2">
              <span className="text-sm font-medium">{t("admin.aiProviders.createProviderDialog.nameLabel")}</span>
              <Input
                placeholder={t("admin.aiProviders.createProviderDialog.namePlaceholder")}
                value={customProviderForm.name}
                onChange={(event) => setCustomProviderForm((current) => ({
                  ...current,
                  name: event.target.value,
                }))}
                autoFocus
              />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium">{t("admin.aiProviders.createProviderDialog.providerTypeLabel")}</span>
              <Select
                value={customProviderForm.providerType}
                onValueChange={(value) => setCustomProviderForm((current) => ({
                  ...current,
                  providerType: value,
                }))}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {providerTypes.map((type) => (
                    <SelectItem key={type.value} value={type.value}>
                      <div className="flex flex-col">
                        <span>{type.label}</span>
                        <span className="text-xs text-muted-foreground">{type.hint}</span>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium">Base URL</span>
              <Input
                placeholder="https://api.example.com/v1"
                value={customProviderForm.baseUrl}
                onChange={(event) => setCustomProviderForm((current) => ({
                  ...current,
                  baseUrl: event.target.value,
                }))}
              />
              <span className="block text-xs text-muted-foreground">
                {t("admin.aiProviders.createProviderDialog.baseUrlHint")}
              </span>
            </label>
            <div className="flex justify-end gap-2 pt-2">
              <Button variant="ghost" onClick={() => setAddDialogOpen(false)}>
                {t("admin.aiProviders.createProviderDialog.cancel")}
              </Button>
              <Button disabled={saving || !customProviderForm.name.trim()} onClick={createCustomProvider}>
                {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                {t("admin.aiProviders.createProviderDialog.create")}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}
