namespace Office.Api.Channels.WhatsApp;

/// <summary>Шаблони тасдиқшудаи WhatsApp (аз Meta, барои фиристодан берун аз тирезаи 24-соата).</summary>
public record WhatsAppTemplateInfo(string Name, string Language, string Status, string? BodyText);
