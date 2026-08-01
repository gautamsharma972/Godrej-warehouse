namespace WarehouseGate.Api.Assistant;

// Deliberately just a plain options POCO with no dependency on Microsoft.Extensions.Options -
// this whole folder is meant to stay a thin, swappable layer (see the isolation note on
// AssistantService) even though it now compiles into the Api assembly rather than a separate
// project (see AssistantService's note on why). Bound from appsettings.json's "Assistant" section.
public class AssistantOptions
{
    // Ollama's own default port. Base URL only (no "/v1" suffix) - AssistantService appends
    // whatever path Semantic Kernel's OpenAI-compatible connector expects.
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    // Must match a model already pulled via `ollama pull <ModelId>` on the host running Ollama.
    public string ModelId { get; set; } = "qwen2.5:7b-instruct-q4_K_M";
}
