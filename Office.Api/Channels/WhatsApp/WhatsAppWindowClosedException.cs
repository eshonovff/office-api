namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Meta тирезаи 24-соатаро вайроншуда донист — матни озод рад шуд.
/// Такрор (retry) фоида надорад, танҳо SendTemplateAsync кор мекунад.
/// </summary>
public class WhatsAppWindowClosedException(string message) : Exception(message);
