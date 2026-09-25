using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Office.Api.Email;

namespace Office.Api.Tests.Email;

public class LoggingEmailSenderTests
{
    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Office.Api.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<LoggingEmailSender>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private const string SecretBody = "https://office.nizom.tj/reset-password#token=SECRET-TOKEN";

    [Fact]
    public async Task InDevelopment_TheBodyIsLogged_SoCodesCanBeSeen()
    {
        var logger = new RecordingLogger();
        var sent = await new LoggingEmailSender(logger, new HostEnvironment(Environments.Development))
            .SendAsync("a@example.com", "subject", SecretBody, CancellationToken.None);

        Assert.True(sent);
        Assert.Contains(logger.Messages, m => m.Contains("SECRET-TOKEN"));
    }

    [Fact]
    public async Task ElsewhereTheBodyIsNeverLogged_AndTheSendFails()
    {
        var logger = new RecordingLogger();
        var sent = await new LoggingEmailSender(logger, new HostEnvironment(Environments.Production))
            .SendAsync("a@example.com", "subject", SecretBody, CancellationToken.None);

        Assert.False(sent);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("SECRET-TOKEN") || m.Contains("a@example.com"));
    }
}
