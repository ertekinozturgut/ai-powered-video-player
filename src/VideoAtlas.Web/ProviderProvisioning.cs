using System.Text.Json;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

public static class ProviderProvisioning
{
    // Operator-only CLI, never an HTTP endpoint. Import secrets from protected stdin.
    public static async Task RunAsync(IServiceProvider services, bool useOllama = false)
    {
        if (useOllama)
        {
            var local = services.GetRequiredService<IProviderAdministration>();
            var localState = await local.ReadAsync(CancellationToken.None);
            await local.SelectAsync(new(null, "chat-ollama", "embedding-ollama", localState.Selection.Version), "operator-cli", CancellationToken.None);
            Console.WriteLine("Local Ollama chat and embedding providers selected.");
            return;
        }
        var input = await Console.In.ReadToEndAsync();
        if (input.Length > 20000) throw new InvalidOperationException("Provider input exceeds limit.");
        var admin = services.GetRequiredService<IProviderAdministration>();
        if (string.IsNullOrWhiteSpace(input))
        {
            var current = await admin.ReadAsync(CancellationToken.None);
            await admin.SelectAsync(new("speech-elevenlabs", "chat-openrouter", "embedding-openrouter", current.Selection.Version), "operator-cli", CancellationToken.None);
            Console.WriteLine("Environment-backed providers selected. Live connectivity has NOT been verified.");
            return;
        }
        var keys = JsonSerializer.Deserialize<BootstrapKeys>(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Expected provider credentials on stdin.");
        if (string.IsNullOrWhiteSpace(keys.OpenRouter) || string.IsNullOrWhiteSpace(keys.ElevenLabs))
            throw new InvalidOperationException("Both provider credentials are required.");
        foreach (var id in new[] { "speech-elevenlabs", "chat-openrouter", "embedding-openrouter" })
        {
            var p = (await admin.ReadAsync(CancellationToken.None)).Profiles.Single(p => p.Id == id);
            await admin.SaveAsync(new(p.Id, p.Endpoint, p.Model, p.Dimensions, id == "speech-elevenlabs" ? keys.ElevenLabs : keys.OpenRouter, false, p.Version), "operator-cli", CancellationToken.None);
        }
        var state = await admin.ReadAsync(CancellationToken.None);
        await admin.SelectAsync(new("speech-elevenlabs", "chat-openrouter", "embedding-openrouter", state.Selection.Version), "operator-cli", CancellationToken.None);
        Console.WriteLine("Provider credentials encrypted and active routes selected. Live connectivity has NOT been verified.");
    }
    private sealed record BootstrapKeys(string OpenRouter, string ElevenLabs);
}
