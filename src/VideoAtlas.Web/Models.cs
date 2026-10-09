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
public sealed class UploadForm
{
    [Required, StringLength(180)] public string Title { get; set; } = "";
    [Required] public IFormFile File { get; set; } = null!;
    public bool Consent { get; set; }
    [StringLength(32000)] public string? Terms { get; set; }
    [StringLength(4000)] public string? AnalysisInstructions { get; set; }
    [RegularExpression("^(tr|en)$")] public string AnalysisLanguage { get; set; } = "tr";
    public bool FullReview { get; set; }
}
public sealed record SubtitleForm([Required, StringLength(2_000_000)] string Content);
public sealed record ShareForm([Range(1, 90)] int Days, [Required, MaxLength(10), MinLength(1)] string[] Origins,
    VideoAtlas.Domain.SharePresentation? Presentation = null, bool RequireLogin = false, [MaxLength(10)] string[]? Viewers = null);
public sealed record PlayerModel(string ApiBase, bool Embedded);
