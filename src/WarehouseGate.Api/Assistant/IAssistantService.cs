namespace WarehouseGate.Api.Assistant;

// Plain POCO, not a Semantic Kernel type - Role is "user" or "assistant". Lets the web portal
// maintain multi-turn conversation state client-side (resend the transcript each call) without
// this interface leaking ChatHistory or any other SK type to callers.
public record AssistantChatTurn(string Role, string Content);

// The rest of the Api project (controllers, DI registration) should only ever depend on this
// interface, never on AssistantService or Semantic Kernel types directly - keeps the model/
// orchestration swap contained to this folder even though it isn't a separate assembly anymore.
public interface IAssistantService
{
    // "plugins" are plain objects whose public methods carry Semantic Kernel's [KernelFunction]
    // attribute (see Plugins/OutwardJobsPlugin.cs for the shape) - deliberately typed as `object`
    // here so this interface still doesn't leak any Semantic Kernel types to callers. The caller
    // (a controller) builds each plugin instance itself, already wired to the CURRENT request's
    // scoped services and the caller's own resolved warehouse/role - that's what makes a tool call
    // respect the same authorization the REST endpoints already enforce, rather than needing a
    // parallel scoping mechanism inside the assistant itself.
    Task<string> AskAsync(
        string prompt,
        IEnumerable<object> plugins,
        IReadOnlyList<AssistantChatTurn>? history = null,
        string? pageContext = null,
        CancellationToken cancellationToken = default);
}
