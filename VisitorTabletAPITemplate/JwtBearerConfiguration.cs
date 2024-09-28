using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace VisitorTabletAPITemplate
{
    public static class JwtBearerConfiguration
    {
        public static AuthenticationBuilder AddJwtBearerConfiguration(this AuthenticationBuilder builder, string? issuer, string? audience)
        {
            if (string.IsNullOrEmpty(issuer))
            {
                throw new ArgumentException("issuer is null or empty. Check to ensure appsettings.json is set up correctly.", nameof(issuer));
            }
            if (string.IsNullOrEmpty(audience))
            {
                throw new ArgumentException("audience is null or empty. Check to ensure appsettings.json is set up correctly.", nameof(audience));
            }

            return builder.AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.Audience = audience;
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ClockSkew = new TimeSpan(0, 0, 30),
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
                options.Events = new JwtBearerEvents()
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";

                        // Ensure we always have an error and error description.
                        if (string.IsNullOrEmpty(context.Error))
                            context.Error = "invalid_token";
                        if (string.IsNullOrEmpty(context.ErrorDescription))
                            context.ErrorDescription = "This request requires a valid JWT access token to be provided. " + context.HttpContext.Request.Headers["Authorization"];

                        // Add some extra context for expired tokens.
                        if (context.AuthenticateFailure is SecurityTokenExpiredException authenticationException)
                        {
                            context.Response.Headers.Append("X-Token-Expired", authenticationException.Expires.ToString("o"));
                            context.ErrorDescription = $"The token expired on {authenticationException.Expires:o}";
                        }

                        return context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            error = context.Error,
                            error_description = context.ErrorDescription
                        }));
                    }
                };
            });
        }
    }
}
