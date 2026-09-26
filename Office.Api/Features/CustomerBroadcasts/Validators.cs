using FluentValidation;
using Office.Api.Features.CommentAutomation;

namespace Office.Api.Features.CustomerBroadcasts;

/// <summary>Instagram's limits where it has them (a message: 1000, a button title: 20).</summary>
public static class BroadcastLimits
{
    public const int MaxNameLength = 200;
    public const int MaxTags = 20;
    public const int MaxTagLength = 100;
    public const int MaxTextLength = 1000;
    public const int MaxButtonTitleLength = 20;
    public const int MaxButtonUrlLength = 500;
    public const int MaxPreviewLength = 300_000;
    public const string MediaIdPattern = "^[0-9]{1,32}$";
    public static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(30);
    public const int MaxActivePerChannel = 5;

    /// <summary>Only the thumbnails the flows' media upload returns — never an SVG or a link.</summary>
    public static bool IsImagePreview(string? value) =>
        value is null ||
        (value.Length <= MaxPreviewLength &&
         (value.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ||
          value.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ||
          value.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)));
}

public class CreateBroadcastRequestValidator : AbstractValidator<CreateBroadcastRequest>
{
    public CreateBroadcastRequestValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.Name).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Номи рассылка лозим аст.");
        RuleFor(x => x.Name).MaximumLength(BroadcastLimits.MaxNameLength);

        RuleFor(x => x.Tags).Must(t => t is null || t.Length <= BroadcastLimits.MaxTags)
            .WithMessage($"То {BroadcastLimits.MaxTags} тег.");
        RuleForEach(x => x.Tags).Must(t => !string.IsNullOrWhiteSpace(t) && t.Length <= BroadcastLimits.MaxTagLength)
            .When(x => x.Tags is not null).WithMessage("Тег холӣ ё аз ҳад дароз аст.");

        RuleFor(x => x).Must(x => x.FlowId is not null
                ? string.IsNullOrEmpty(x.Text) && string.IsNullOrEmpty(x.MediaId) && string.IsNullOrEmpty(x.ButtonUrl)
                : !string.IsNullOrWhiteSpace(x.Text) || !string.IsNullOrEmpty(x.MediaId))
            .WithName("content")
            .WithMessage("Ё паём (матн ё расм), ё автоматизатсия — яке аз онҳо.");

        RuleFor(x => x.Text).MaximumLength(BroadcastLimits.MaxTextLength);
        RuleFor(x => x.MediaId).Matches(BroadcastLimits.MediaIdPattern).When(x => !string.IsNullOrEmpty(x.MediaId));
        RuleFor(x => x.MediaPreviewDataUri).Must(BroadcastLimits.IsImagePreview).WithMessage("Пешнамоиши расм нодуруст аст.");

        RuleFor(x => x.ButtonTitle).MaximumLength(BroadcastLimits.MaxButtonTitleLength);
        RuleFor(x => x.ButtonUrl).MaximumLength(BroadcastLimits.MaxButtonUrlLength)
            .Must(AutomationRuleLimits.IsHttpUrl).When(x => !string.IsNullOrEmpty(x.ButtonUrl))
            .WithMessage("Пайванди тугма бояд бо https:// сар шавад.");
        RuleFor(x => x).Must(x => string.IsNullOrEmpty(x.ButtonUrl) == string.IsNullOrEmpty(x.ButtonTitle))
            .WithName("button").WithMessage("Тугма ҳам ном, ҳам пайванд дорад.");
        RuleFor(x => x).Must(x => string.IsNullOrEmpty(x.ButtonUrl) || !string.IsNullOrWhiteSpace(x.Text))
            .WithName("button").WithMessage("Тугма бо матн меравад.");

        RuleFor(x => x.ScheduledAt).Must(at => at is null || (at > DateTimeOffset.UtcNow.AddMinutes(-1) && at < DateTimeOffset.UtcNow + BroadcastLimits.MaxScheduleAhead))
            .WithMessage("Вақт — аз ҳозир то 30 рӯз.");
    }
}
