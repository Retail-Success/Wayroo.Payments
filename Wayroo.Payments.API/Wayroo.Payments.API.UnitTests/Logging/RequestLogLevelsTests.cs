using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Serilog.Events;
using Wayroo.Payments.API.Logging;

namespace Wayroo.Payments.API.UnitTests.Logging;

/// <summary>
/// Pins the two rules in <see cref="RequestLogLevels"/>: a healthy probe is silent, and a failing
/// one is not.
/// </summary>
/// <remarks>
/// The first rule is why this exists — the ECS container health check curls <c>/status</c> every 30
/// seconds per task and the request log is otherwise mostly that. The second rule is why it is
/// tested rather than trusted: silencing the probe outright would also silence the 503 that precedes
/// ECS cycling a task, and nothing else in the pipeline would notice the difference.
/// </remarks>
public class RequestLogLevelsTests
{
    // The paths are written as literals rather than read from Routes: /status is a deployed contract
    // (the CDK health check curls it by name), so a rename should have to be made in both places
    // deliberately instead of following a constant silently.
    [Theory]
    [InlineData("/status")]
    [InlineData("/status/")]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/STATUS")]
    public void AHealthyProbe_IsDroppedBelowTheMinimumLevel(string path)
    {
        var level = RequestLogLevels.For(new PathString(path), StatusCodes.Status200OK, exception: null);

        level.Should().Be(
            LogEventLevel.Verbose,
            "a probe that succeeded is noise, and Verbose sits below the Information minimum level");
    }

    [Theory]
    [InlineData("/status", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("/health", StatusCodes.Status500InternalServerError)]
    public void AFailingProbe_IsStillLogged(string path, int statusCode)
    {
        var level = RequestLogLevels.For(new PathString(path), statusCode, exception: null);

        level.Should().Be(
            LogEventLevel.Error,
            "a probe going red is the one time anybody reads its log line");
    }

    [Fact]
    public void AProbeThatThrew_IsStillLogged()
    {
        var level = RequestLogLevels.For(
            new PathString("/status"),
            StatusCodes.Status200OK,
            new InvalidOperationException("health check blew up after the response started"));

        level.Should().Be(LogEventLevel.Error);
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status404NotFound)]
    public void AnOrdinaryRequest_IsLoggedAtInformation(int statusCode)
    {
        var level = RequestLogLevels.For(
            new PathString("/api/payments/v1.0/tenants/t1/stores/s1/account"),
            statusCode,
            exception: null);

        level.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void AServerErrorOnAnOrdinaryRequest_IsLoggedAtError()
    {
        var level = RequestLogLevels.For(
            new PathString("/api/payments/v1.0/stores/s1/configurations"),
            StatusCodes.Status500InternalServerError,
            exception: null);

        level.Should().Be(LogEventLevel.Error);
    }

    [Theory]
    [InlineData("/statuses")]
    [InlineData("/healthy-stores")]
    public void APathThatMerelyStartsWithAProbeName_IsNotTreatedAsAProbe(string path)
    {
        // PathString matches whole segments, so a real endpoint is never silenced by sharing a prefix
        // with a probe.
        var level = RequestLogLevels.For(new PathString(path), StatusCodes.Status200OK, exception: null);

        level.Should().Be(LogEventLevel.Information);
    }
}
