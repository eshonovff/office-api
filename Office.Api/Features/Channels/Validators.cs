using FluentValidation;
using ChannelTypeEntity = Office.Api.Data.Entities.ChannelType;

namespace Office.Api.Features.Channels;

public class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(x => x.Type).NotEmpty().Must(BeAValidType).WithMessage("Навъи канал бояд whatsapp, instagram ё facebook бошад.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Credentials).NotEmpty();
    }

    private static bool BeAValidType(string type) => Enum.TryParse<ChannelTypeEntity>(type, ignoreCase: true, out _);
}

public class UpdateChannelRequestValidator : AbstractValidator<UpdateChannelRequest>
{
    public UpdateChannelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class SetChannelMembersRequestValidator : AbstractValidator<SetChannelMembersRequest>
{
    public SetChannelMembersRequestValidator()
    {
        RuleFor(x => x.UserIds).NotNull();
    }
}
