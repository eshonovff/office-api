using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Office.Api.Common;

/// <summary>
/// Кадом истиснои коркарднашуда воқеан хатогии input-и корбар аст (400), на хатогии
/// дохилии сервер (500). Минимал APIs body binding-и JSON-и вайроншударо (масалан
/// DateOnly-и бидуни формати "yyyy-MM-dd") ба BadHttpRequestException мепечонад — бе ин
/// санҷиш, UseExceptionHandler-и глобалӣ (Program.cs) ҳар ду навъро якхела ба 500 табдил медод.
/// </summary>
public static class ClientErrorClassifier
{
    public static bool IsClientInputError(Exception exception) =>
        exception is BadHttpRequestException or JsonException;
}
