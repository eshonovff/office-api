using System.Text.Json;
using Office.Api.Features.Tasks;

namespace Office.Api.Tests.Features.Tasks;

/// <summary>
/// dueDate ҳамчун DateOnly? (на DateTimeOffset?) — санаи бидуни вақт бояд қабул шавад,
/// вале вақт/офсет бояд рад шавад (JsonException, на 500). Options-ҳо ҳамон
/// JsonSerializerDefaults.Web-ро истифода мебаранд, ки ASP.NET Core minimal APIs воқеан
/// истифода мебарад (ниг. docs/bug-task-duedate-date-only.md).
/// </summary>
public class TaskDueDateJsonTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DueDate_DateOnly_Deserializes()
    {
        var request = JsonSerializer.Deserialize<UpdateTaskRequest>(
            """{"title":"T","priority":"Low","dueDate":"2026-09-01"}""", Options);

        Assert.Equal(new DateOnly(2026, 9, 1), request!.DueDate);
    }

    [Fact]
    public void DueDate_Null_Deserializes()
    {
        var request = JsonSerializer.Deserialize<UpdateTaskRequest>(
            """{"title":"T","priority":"Low","dueDate":null}""", Options);

        Assert.Null(request!.DueDate);
    }

    [Fact]
    public void DueDate_Omitted_DeserializesAsNull()
    {
        var request = JsonSerializer.Deserialize<UpdateTaskRequest>(
            """{"title":"T","priority":"Low"}""", Options);

        Assert.Null(request!.DueDate);
    }

    [Fact]
    public void DueDate_WithTimeComponent_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateTaskRequest>(
            """{"title":"T","priority":"Low","dueDate":"2026-09-01T00:00:00Z"}""", Options));
    }

    [Fact]
    public void DueDate_WithTimezoneOffset_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateTaskRequest>(
            """{"title":"T","priority":"Low","dueDate":"2026-09-01T00:00:00+05:00"}""", Options));
    }
}
