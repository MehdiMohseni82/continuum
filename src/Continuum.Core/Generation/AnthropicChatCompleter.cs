using Anthropic;
using Anthropic.Models.Messages;

namespace Continuum.Core.Generation;

/// <summary>
/// Text generation via the Claude API (Anthropic C# SDK). Unlike <see cref="OllamaChatCompleter"/>
/// this reaches an external service, so it is only wired up where a Claude API key is configured
/// (the server-side room agent). It implements <see cref="IChatCompleter"/> so it can be reused,
/// but the global generation path (RAG / memory extraction) stays on the self-hosted completer.
/// </summary>
public sealed class AnthropicChatCompleter : IChatCompleter
{
    private readonly AnthropicClient _client;
    private readonly long _maxTokens;

    public AnthropicChatCompleter(string apiKey, string model, int maxTokens)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        Model = string.IsNullOrWhiteSpace(model) ? "claude-opus-5" : model;
        _maxTokens = maxTokens > 0 ? maxTokens : 1024;
    }

    public string Model { get; }

    public Task<string> CompleteAsync(string system, string user, bool jsonMode, CancellationToken ct) =>
        CompleteChatAsync(system, [new ChatTurn(FromUser: true, user)], jsonMode, ct);

    public async Task<string> CompleteChatAsync(
        string system, IReadOnlyList<ChatTurn> turns, bool jsonMode, CancellationToken ct)
    {
        // The Anthropic API constrains JSON via a schema; without one, the closest equivalent to
        // Ollama's free-form "json" mode is a system-prompt instruction. The room path never sets this.
        if (jsonMode)
            system += "\n\nRespond with a single valid JSON object and nothing else.";

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = _maxTokens,
            System = system,
            // Room messages are short; low effort keeps latency and cost down.
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages = [.. turns.Select(t => new MessageParam
            {
                Role = t.FromUser ? Role.User : Role.Assistant,
                Content = t.Text,
            })],
        }, ct);

        // Claude Opus 5 can decline via a safety classifier (HTTP 200, StopReason "refusal").
        // Return empty so callers skip posting rather than emitting a partial/blank message.
        if (response.StopReason == "refusal") return "";

        var text = string.Concat(
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return text.Trim();
    }
}
