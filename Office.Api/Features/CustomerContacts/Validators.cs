using FluentValidation;

namespace Office.Api.Features.CustomerContacts;

/// <summary>
/// Ceilings on what a мизоҷ may store on a contact — the database's own column sizes, and a count
/// per contact so one contact cannot grow without end.
/// </summary>
public static class ContactLimits
{
    public const int MaxTagLength = 100;
    public const int MaxTagsPerContact = 50;
    public const int MaxVariableKeyLength = 100;
    public const int MaxVariableValueLength = 2000;
    public const int MaxVariablesPerContact = 50;
    public const int MaxSearchLength = 100;

    /// <summary>No control characters (line breaks, tabs) in a tag or a key: they are one-line labels.</summary>
    public static bool IsOneLine(string? value) => value is not null && !value.Any(char.IsControl);
}

public class AddContactTagRequestValidator : AbstractValidator<AddContactTagRequest>
{
    public AddContactTagRequestValidator()
    {
        RuleFor(x => x.Tag).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Тег холӣ набошад.");
        RuleFor(x => x.Tag).MaximumLength(ContactLimits.MaxTagLength)
            .Must(ContactLimits.IsOneLine).WithMessage("Тег як сатр бошад.");
    }
}

public class SetContactVariableRequestValidator : AbstractValidator<SetContactVariableRequest>
{
    public SetContactVariableRequestValidator()
    {
        RuleFor(x => x.Key).Must(k => !string.IsNullOrWhiteSpace(k)).WithMessage("Номи майдон холӣ набошад.");
        RuleFor(x => x.Key).MaximumLength(ContactLimits.MaxVariableKeyLength)
            .Must(ContactLimits.IsOneLine).WithMessage("Номи майдон як сатр бошад.");
        RuleFor(x => x.Value).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Қимат холӣ набошад.");
        RuleFor(x => x.Value).MaximumLength(ContactLimits.MaxVariableValueLength);
    }
}
