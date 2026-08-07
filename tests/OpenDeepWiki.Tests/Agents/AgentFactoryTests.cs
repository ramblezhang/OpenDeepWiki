using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenAI.Responses;
using Xunit;

namespace OpenDeepWiki.Tests.Agents;

public class AgentFactoryTests
{
    [Fact]
    public void CreateSimpleChatClient_ShouldThrowHelpfulException_WhenApiKeyIsMissing()
    {
        var factory = new AgentFactory(Options.Create(new AiRequestOptions
        {
            Endpoint = "https://example.com/v1",
            RequestType = AiRequestType.OpenAI
        }));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateSimpleChatClient("test-model"));

        Assert.Contains("AI API key", exception.Message);
    }

    [Theory]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void ApplyOpenAIResponsesThinkingConfig_AppliesExtendedReasoningEffort(string effort)
    {
        var clientAgentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions()
        };
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingConfigJson = $$"""
                {
                  "bodyParams": { "reasoning_effort": "{{effort}}" }
                }
                """
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        var responseOptions = GetResponseOptions(clientAgentOptions);
        Assert.Equal(effort, GetReasoningEffort(responseOptions));
    }

    [Fact]
    public void ApplyOpenAIResponsesThinkingConfig_UsesDefaultReasoningEffort()
    {
        var clientAgentOptions = CreateClientAgentOptions();
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingConfigJson = """
                {
                  "bodyParams": {},
                  "defaultReasoningEffort": "medium"
                }
                """
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        var responseOptions = GetResponseOptions(clientAgentOptions);
        Assert.Equal("medium", GetReasoningEffort(responseOptions));
    }

    [Fact]
    public void ApplyOpenAIResponsesThinkingConfig_UsesDisabledBodyParamsWhenThinkingDisabled()
    {
        var clientAgentOptions = CreateClientAgentOptions();
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingEnabled = false,
            ThinkingConfigJson = """
                {
                  "bodyParams": { "reasoning_effort": "high" },
                  "disabledBodyParams": { "reasoning_effort": "none" },
                  "defaultReasoningEffort": "medium"
                }
                """
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        var responseOptions = GetResponseOptions(clientAgentOptions);
        Assert.Equal("none", GetReasoningEffort(responseOptions));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{ \"bodyParams\": { \"reasoning_effort\": \"invalid\" } }")]
    public void ApplyOpenAIResponsesThinkingConfig_IgnoresInvalidConfiguration(string config)
    {
        var clientAgentOptions = CreateClientAgentOptions();
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingConfigJson = config
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        Assert.Null(clientAgentOptions.ChatOptions!.RawRepresentationFactory);
    }

    [Fact]
    public void ApplyOpenAIResponsesThinkingConfig_DoesNothingWhenThinkingIsUnsupported()
    {
        var clientAgentOptions = CreateClientAgentOptions();
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = false,
            ThinkingConfigJson = "{ \"bodyParams\": { \"reasoning_effort\": \"high\" } }"
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        Assert.Null(clientAgentOptions.ChatOptions!.RawRepresentationFactory);
    }

    [Fact]
    public void ApplyOpenAIResponsesThinkingConfig_PreservesExplicitRawReasoning()
    {
        var explicitResponseOptions = new CreateResponseOptions
        {
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = new ResponseReasoningEffortLevel("low")
            }
        };
        var clientAgentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                RawRepresentationFactory = underlyingClient => explicitResponseOptions
            }
        };
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingConfigJson = "{ \"bodyParams\": { \"reasoning_effort\": \"high\" } }"
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        var responseOptions = GetResponseOptions(clientAgentOptions);
        Assert.Same(explicitResponseOptions, responseOptions);
        Assert.Equal("low", GetReasoningEffort(responseOptions));
    }

    [Fact]
    public void ApplyOpenAIResponsesThinkingConfig_PreservesExplicitChatReasoning()
    {
        var clientAgentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High }
            }
        };
        var requestOptions = new AiRequestOptions
        {
            SupportsThinking = true,
            ThinkingConfigJson = "{ \"bodyParams\": { \"reasoning_effort\": \"low\" } }"
        };

        AgentFactory.ApplyOpenAIResponsesThinkingConfig(clientAgentOptions, requestOptions);

        Assert.Null(clientAgentOptions.ChatOptions.RawRepresentationFactory);
    }

    private static ChatClientAgentOptions CreateClientAgentOptions()
    {
        return new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions()
        };
    }

    private static CreateResponseOptions? GetResponseOptions(
        ChatClientAgentOptions clientAgentOptions)
    {
        return clientAgentOptions.ChatOptions!.RawRepresentationFactory?.Invoke(null!)
            as CreateResponseOptions;
    }

    private static string? GetReasoningEffort(CreateResponseOptions? responseOptions)
    {
        object? effort = responseOptions?.ReasoningOptions?.ReasoningEffortLevel;
        return effort?.ToString();
    }
}
