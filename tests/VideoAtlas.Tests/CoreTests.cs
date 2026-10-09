using VideoAtlas.Application;
using VideoAtlas.Domain;
using Xunit;

namespace VideoAtlas.Tests;

public sealed class CoreTests
{
    internal const string Transcript = "1\n00:00:00,000 --> 00:00:03,000\nÜrün lansmanı Ekim ayında yapılacak.\n\n2\n00:00:03,000 --> 00:00:06,000\nBütçe yüz bin lira olarak belirlendi.\n\n3\n00:00:06,000 --> 00:00:09,000\nGüvenlik testleri lansmandan önce tamamlanacak.\n";
    [Fact] public void SubtitlesRoundTripKeepsTimestampsAndUnicode()
    {
        var parsed = Subtitles.Parse(Transcript, 10);
        Assert.Equal(3, parsed.Count);
        Assert.Equal(3, parsed[1].Start);
        Assert.Equal(parsed, Subtitles.Parse(Subtitles.Format(parsed), 10));
        Assert.StartsWith("WEBVTT\n\n", Subtitles.Format(parsed, true));
        Assert.Contains("00:00:03.000", Subtitles.Format(parsed, true));
    }
    [Theory]
    [InlineData("1\n00:00:05,000 --> 00:00:03,000\nInvalid")]
    [InlineData("1\n00:00:00,000 --> 00:00:11,100\nOutside video")]
    [InlineData("1\n00:61:00,000 --> 00:62:00,000\nBad time")]
    [InlineData("1\n00:00:00,000 --> 00:00:03,000\n\n")]
    [InlineData("not subtitles")]
    [InlineData("")]
    public void InvalidSubtitlesAreRejected(string text) => Assert.Throws<AppException>(() => Subtitles.Parse(text, 10));
    [Fact] public void SubtitlesStripHtmlAndAllowOverlappingSpeech()
    {
        var text = "1\r\n00:00:00,000 --> 00:00:03,000\r\n<b>merhaba</b>\r\n\r\n2\r\n00:00:01,000 --> 00:00:04,000\r\ndünya\r\n";
        var parsed = Subtitles.Parse(text, 10);
        Assert.Equal("merhaba", parsed[0].Text);
        Assert.Equal(2, parsed.Count);
    }
    [Fact] public void RetrievalSelectsRelevantActualEvidence()
    {
        var segments = Subtitles.Parse(Transcript, 10);
        var results = TranscriptSearch.Retrieve(segments, "Bütçe ne kadar?");
        Assert.Single(results);
        Assert.Equal(1, results[0].Id);
        Assert.Empty(TranscriptSearch.Retrieve(segments, "Mars uzaylıları"));
    }
    [Fact] public void LocalChaptersCoverWholeTranscript()
    {
        var segments = new[] { new TranscriptSegment(0, 0, 40, "Yazılım geliştirme ürün planı"), new TranscriptSegment(1, 50, 85, "Bütçe maliyet finans"), new TranscriptSegment(2, 90, 110, "Bütçe finans hedefi") };
        var chapters = TranscriptSearch.LocalChapters(segments);
        Assert.Equal(2, chapters.Count);
        Assert.Equal(0, chapters[0].Start);
        Assert.Equal(110, chapters[^1].End);
    }
    [Theory] [InlineData("https://example.com")] [InlineData("https://example.com:8443/")] [InlineData("*")]
    public void FrameOriginsAcceptOnlyExplicitOrigins(string input) => Assert.NotEmpty(VideoLibrary.ValidateOrigin(input));
    [Theory] [InlineData("http://example.com")] [InlineData("https://example.com/path")] [InlineData("https://user@example.com")] [InlineData("https://example.com; script-src *")] [InlineData("javascript:alert(1)")]
    public void InvalidFrameOriginsRejected(string input) => Assert.Throws<AppException>(() => VideoLibrary.ValidateOrigin(input));
    [Fact] public void ShareExpiresAndRevokes()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var video = new Video(Guid.NewGuid(), "owner", "Test", 42, now);
        video.Complete(Subtitles.Parse(Transcript, 10), [], "keywords");
        video.Share("hash", now.AddHours(1), "*");
        Assert.True(video.CanShare(now));
        Assert.False(video.CanShare(now.AddHours(1)));
        video.RevokeShare(); Assert.False(video.CanShare(now));
    }
    [Fact] public void BusyAndReadyGuardsRejectWrongState()
    {
        var v = new Video(Guid.NewGuid(), "u", "t", 1, TimeProvider.System.GetUtcNow());
        Assert.Throws<AppException>(() => VideoAccess.RequireIdle(v));
        Assert.Throws<AppException>(() => VideoAccess.RequireReady(v));
        v.Begin(); v.AwaitTranscript(); VideoAccess.RequireIdle(v);
        v.Complete(Subtitles.Parse(Transcript, 10), [], "keywords"); VideoAccess.RequireReady(v);
    }
}
