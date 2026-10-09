using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class AdvancedProviderTests
{
    [Theory] [InlineData("scribe_v1",false)] [InlineData("scribe_v2",true)]
    public async Task ElevenLabsSendsTermsOnlyForSupportingModel(string model, bool expected)
    {
        var handler = new CaptureHandler("{\"words\":[{\"type\":\"word\",\"start\":0,\"end\":1,\"text\":\"ARR.\"}]}");
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider();
        var path = Path.GetTempFileName();
        try
        {
            await new ElevenLabsSpeechAdapter(services.GetRequiredService<ProviderTransport>()).TranscribeAsync(
                new("test", "elevenlabs", "https://api.elevenlabs.io/v1/", model, 0, "synthetic"), path, 0, new([new("ARR", "Yıllık gelir")], ""), default);
            Assert.Equal(expected, handler.Fields.ContainsKey("keyterms"));
            if (expected) Assert.Equal("ARR", handler.Fields["keyterms"]);
            Assert.DoesNotContain("Yıllık gelir", string.Join(' ', handler.Fields.Values));
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task WhisperReceivesGlossaryPromptNotAnalysisInstructions()
    {
        var handler = new CaptureHandler("{\"segments\":[{\"start\":0,\"end\":1,\"text\":\"ARR\"}]}");
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider(); var path = Path.GetTempFileName();
        try
        {
            await new OpenAiSpeechAdapter(services.GetRequiredService<ProviderTransport>()).TranscribeAsync(
                new("test", "openai", "https://api.openai.com/v1/", "whisper-1", 0, "synthetic"), path, 0, new([new("ARR", "Yıllık gelir")], "PRIVATE preference"), default);
            Assert.Equal("ARR", handler.Fields["prompt"]); Assert.DoesNotContain("PRIVATE", string.Join(' ', handler.Fields.Values));
        }
        finally { File.Delete(path); }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AlignmentPreservesOffsetAndRejectsInvalidTimes(bool invalid)
    {
        var handler = new CaptureHandler(invalid ? "{\"words\":[{\"start\":2,\"end\":1}]}" : "{\"words\":[{\"start\":0.5,\"end\":1.5},{\"start\":1.6,\"end\":2}]}");
        await using var services = ProviderTestServices.Create(handler).BuildServiceProvider(); var path = Path.GetTempFileName();
        try
        {
            var adapter = new ElevenLabsAlignmentAdapter(services.GetRequiredService<ProviderTransport>());
            var connection = new ProviderConnection("test", "elevenlabs", "https://api.elevenlabs.io/v1/", "scribe_v1", 0, "synthetic");
            if (invalid) await Assert.ThrowsAsync<AiException>(() => adapter.AlignAsync(connection, path, "Özgün metin", 30, default));
            else { var result = await adapter.AlignAsync(connection, path, "Özgün metin", 30, default); Assert.Equal((30.5,32d), result); }
            Assert.Equal("/v1/forced-alignment", handler.Path); Assert.Equal("Özgün metin", handler.Fields["text"]);
        }
        finally { File.Delete(path); }
    }
    [Fact] public async Task SecondarySpeechSelectionDoesNotReplacePrimaryRoute()
    {
        await using var app = new PausedVideoAppFactory(); using var client = app.CreateClient(); using var scope = app.Services.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IProviderAdministration>();
        foreach (var id in new[] {"speech-elevenlabs", "speech-openai"})
        {
            var p = (await admin.ReadAsync(default)).Profiles.Single(p => p.Id == id);
            await admin.SaveAsync(new(p.Id,p.Endpoint,p.Model,p.Dimensions,"synthetic",false,p.Version),"test",default);
        }
        var state = await admin.ReadAsync(default);
        await Assert.ThrowsAsync<AppException>(() => admin.SelectAsync(new("speech-elevenlabs",null,null,state.Selection.Version,"speech-elevenlabs"),"test",default));
        await admin.SelectAsync(new("speech-elevenlabs",null,null,state.Selection.Version,"speech-openai"),"test",default);
        var factory = scope.ServiceProvider.GetRequiredService<IAiProviderFactory>();
        Assert.Equal("elevenlabs", (await factory.SpeechAsync(default))!.Adapter.Kind);
        Assert.Equal("openai", (await factory.SecondarySpeechAsync(default))!.Adapter.Kind);
    }
    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public Dictionary<string,string> Fields { get; } = [];
        public string Path { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            foreach (var field in (MultipartFormDataContent)request.Content!)
                if (field.Headers.ContentDisposition?.FileName is null) Fields[field.Headers.ContentDisposition!.Name!.Trim('"')] = await field.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
