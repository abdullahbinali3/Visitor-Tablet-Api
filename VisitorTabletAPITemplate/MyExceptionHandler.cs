/*
 * Based on: https://github.com/FastEndpoints/FastEndpoints/blob/main/Src/Library/Extensions/ExceptionHandlerExtensions.cs
 */
using Microsoft.AspNetCore.Diagnostics;
using System.Net;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate
{
    internal class ExceptionHandler { }

    /// <summary>
    /// extensions for global exception handling
    /// </summary>
    public static class ExceptionHandlerExtensions
    {
        /// <summary>
        /// registers the default global exception handler which will log the exceptions on the server and return a user-friendly json response to the client when unhandled exceptions occur.
        /// TIP: when using this exception handler, you may want to turn off the asp.net core exception middleware logging to avoid duplication like so:
        /// <code>
        /// "Logging": { "LogLevel": { "Default": "Warning", "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware": "None" } }
        /// </code>
        /// </summary>
        /// <param name="logger">an optional logger instance</param>
        public static IApplicationBuilder UseMyExceptionHandler(this IApplicationBuilder app, ILogger? logger = null)
        {
            app.UseExceptionHandler(errApp =>
            {
                errApp.Run(async ctx =>
                {
                    IExceptionHandlerFeature? exHandlerFeature = ctx.Features.Get<IExceptionHandlerFeature>();
                    if (exHandlerFeature is not null)
                    {
                        logger ??= ctx.Resolve<ILogger<ExceptionHandler>>();
                        string? http = exHandlerFeature.Endpoint?.DisplayName?.Split(" => ")[0];
                        string type = exHandlerFeature.Error.GetType().Name;
                        string error = exHandlerFeature.Error.Message;
                        string stackTrace = Toolbox.GetExceptionString(exHandlerFeature.Error);
                        string msg = $"""
                            =================================
                            {http}
                            TYPE: {type}
                            REASON: {error}
                            TRACEID: {ctx.TraceIdentifier}
                            ---------------------------------
                            {stackTrace}
                            """;

                        logger.LogError(msg);

                        ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                        ctx.Response.ContentType = "application/problem+json";

                        MyInternalErrorResponse errorResponse = new MyInternalErrorResponse
                        {
                            StatusCode = ctx.Response.StatusCode,
                            TraceId = ctx.TraceIdentifier
                        };

                        errorResponse.ErrorMessages.Add("GeneralErrors", new List<MyErrorResponseMessage>
                        {
                            new MyErrorResponseMessage
                            {
                                //Message = error
                                Message = stackTrace
                            }
                        });

                        await ctx.Response.WriteAsJsonAsync(errorResponse);
                    }
                });
            });

            return app;
        }
    }
}