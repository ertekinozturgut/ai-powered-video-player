using System.ComponentModel.DataAnnotations;

namespace VideoAtlas.Web;

public sealed class AccountForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string Password { get; set; } = "";
}
public sealed class PasswordResetForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)] public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")] public string ConfirmPassword { get; set; } = "";
}
public sealed class PasswordRecoveryRequestForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
}
public sealed class AccountProfileForm
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [StringLength(80)] public string DisplayName { get; set; } = "";
    [Required, StringLength(12)] public string Language { get; set; } = "tr";
    [DataType(DataType.Password)] public string? CurrentPassword { get; set; }
}
public sealed record AccountPageView(AccountProfileForm Profile, IReadOnlyList<VideoAtlas.Application.LanguageView> Languages);
public sealed class UploadForm
{
    [Required, StringLength(180)] public string Title { get; set; } = "";
    [Required] public IFormFile File { get; set; } = null!;
    public bool Consent { get; set; }
    [StringLength(32000)] public string? Terms { get; set; }
    [StringLength(4000)] public string? AnalysisInstructions { get; set; }
    [RegularExpression("^[a-z]{2,3}(-[a-z]{2,4})?$")] public string AnalysisLanguage { get; set; } = "tr";
    public bool FullReview { get; set; } = true;
    public bool SpeakerDiarization { get; set; }
    [Range(2, 32)] public int? SpeakerCount { get; set; }
}
public sealed record ReanalysisForm(bool SpeakerDiarization, [Range(2, 32)] int? SpeakerCount);
public sealed record SpeakerNamesForm([Required, MaxLength(32)] Dictionary<string, string> Names);
public sealed record SubtitleForm([Required, StringLength(2_000_000)] string Content);
public sealed record ShareForm([Range(0, 90)] int Days, [Required, MaxLength(10)] string[] Origins,
    VideoAtlas.Domain.SharePresentation? Presentation = null, bool RequireLogin = false, [MaxLength(10)] string[]? Viewers = null);
public sealed record PlayerModel(string ApiBase, bool Embedded);
