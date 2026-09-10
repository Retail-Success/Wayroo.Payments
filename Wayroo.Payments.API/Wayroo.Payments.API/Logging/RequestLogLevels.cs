using Serilog.Events;

namespace Wayroo.Payments.API.Logging;

/// <summary>
/// Decides what level Serilog's request-logging middleware records a completed request at.
/// </summary>
/// <remarks>
/// <para>
/// A separate class rather than a lambda inside <c>Program.cs</c> because one of the two rules here
/// is easy to lose and expensive to lose: a probe that <i>fails</i> must still be logged. Suppressing
/// <c>/status</c> unconditionally would silence the one occasion anybody wants to read it — the ECS
/// container health check going red just before it cycles the task. <c>RequestLogLevelTests</c> pins
/// both rules.
/// </para>
/// </remarks>
public static class RequestLogLevels
{
    /// <summary>
    /// The level for a completed request.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="exception">The unhandled exception, if the request produced one.</param>
    /// <returns>
    /// <see cref="LogEventLevel.Error"/> for anything that failed, <see cref="LogEventLevel.Verbose"/>
    /// for a healthy probe, and <see cref="LogEventLevel.Information"/> for every other request.
    /// </returns>
    /// <remarks>
    /// <see cref="LogEventLevel.Verbose"/> is the suppression, not a demotion: it sits below the
    /// configured Information minimum level, so the event is discarded rather than written somewhere
    /// quieter. The volume being discarded is the point — the ECS health check curls
    /// <see cref="Routes.StatusRoute"/> every 30 seconds per task, across 2 to 4 tasks, forever.
    /// </remarks>
    public static LogEventLevel For(PathString path, int statusCode, Exception? exception)
    {
        if (exception is not null || statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        return Routes.IsProbePath(path)
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    }
}
