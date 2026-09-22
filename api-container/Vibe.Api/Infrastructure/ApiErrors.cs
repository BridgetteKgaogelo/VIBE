using Vibe.Api.Contracts;

namespace Vibe.Api.Infrastructure;

/// <summary>
/// A failure the member can act on. Thrown by the services and turned into an
/// <see cref="ErrorBody"/> by <see cref="VibeErrorMiddleware"/>, so every endpoint
/// answers with "what went wrong and what to do next" - a PoE requirement - and
/// none of them leaks a stack trace.
/// </summary>
public sealed class VibeException(int statusCode, string message, string? field = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string? Field { get; } = field;

    public static VibeException NotFound(string message) => new(StatusCodes.Status404NotFound, message);

    public static VibeException Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);

    public static VibeException Conflict(string message) => new(StatusCodes.Status409Conflict, message);

    public static VibeException Validation(string message, string? field = null) =>
        new(StatusCodes.Status422UnprocessableEntity, message, field);
}

public static class VibeErrorMiddleware
{
    public static IApplicationBuilder UseVibeErrors(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (VibeException error)
            {
                context.Response.StatusCode = error.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ErrorBody(error.Message, error.Field));
            }
        });
}
