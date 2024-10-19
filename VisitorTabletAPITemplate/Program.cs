global using FastEndpoints;
global using FastEndpoints.Security;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using VisitorTabletAPITemplate;
using VisitorTabletAPITemplate.Features.Status;
using VisitorTabletAPITemplate.FileStorage.Repositories;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.StartupValidation;
using VisitorTabletAPITemplate.Utilities;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;
using ZiggyCreatures.Caching.Fusion;

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Set up logging
    SetupLogging(builder);

    //Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;

    builder.WebHost.ConfigureKestrel(o =>
    {
        // Set max request body size to prevent uploading excessive files/images.
        // This should be a bit higher than the actual max image filesize (in appsettings.json),
        // so we can still accept just under the max image size + other legitimate data in the same request.

        // Set to 25mb to allow 2 images + 5mb of other stuff.
        o.Limits.MaxRequestBodySize = 25 * 1024 * 1024; // 25mb
    });

    // Set up configuration
    SetupConfiguration(builder);

    // Set up DataProtection
    SetupDataProtection(builder);

    // Set up FastEndpoints
    builder.Services.AddFastEndpoints(o =>
    {
        o.Assemblies = new[]
        {
            typeof(StatusEndpoint).Assembly, // VisitorTabletAPITemplate
            typeof(VisitorTabletAPITemplate.VisitorTablet.Features.Buildings.ListBuildings.VisitorTabletListBuildingsEndpoint).Assembly, // VisitorTabletAPITemplate.VisitorTablet
        };
    });

    // Register the Swagger generator service
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "Visitor Tablet API Template",
            Description = "API Documentation",
        });

        // Optional: Add security definitions for JWT authentication, if used.
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme.",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    });

    // Set up FusionCache
    SetupFusionCache(builder);

    // Set up Forwarded Headers
    SetupForwardedHeaders(builder);

    // Set up CORS
    SetupCORS(builder);

    // Set up Authentication
    SetupAuthentication(builder);

    // Setup ImageStorage
    SetupImageStorage(builder);

    // Setup FileStorage
    SetupFileStorage(builder);

    // Setup HttpClients
    SetupHttpClients(builder);

    // Set up repositories
    SetupRepositories(builder);

    // Set up visitor tablet repositories
    SetupVisitorTabletRepositories(builder);

    WebApplication app = builder.Build();
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
            c.RoutePrefix = "swagger";
        });
    }
    app.UseForwardedHeaders();
    app.UseMyExceptionHandler();
    app.UseCors("CorsPolicy");
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseFastEndpoints(SetupFastEndpointsConfig);
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            context.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        }
    });
    await app.RunAsync();
}
catch (Exception ex) when (!Debug)
{
    VisitorTabletAPITemplate.Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    await Serilog.Log.CloseAndFlushAsync();
}


static void SetupLogging(WebApplicationBuilder builder)
{
    Serilog.Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {RequestId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(path: Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) + $@"\Log\{typeof(StatusEndpoint).Namespace!.Split(".")[0]} Log.txt",
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {RequestId}] {Message:lj}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Day,
            retainedFileTimeLimit: TimeSpan.FromDays(7),
            rollOnFileSizeLimit: true)
        .CreateLogger();

    builder.Host.UseSerilog();
}

static void SetupDataProtection(WebApplicationBuilder builder)
{
    // Only need this if you plan to use Cookies and/or AntiforgeryTokens.

    // Add the following usings:
    //using Microsoft.AspNetCore.DataProtection;
    //using System.Security.Cryptography.X509Certificates;

    /*
    string filename = builder.Configuration["AppSettings:DataProtection:CertificateFilename"]!;
    string password = builder.Configuration["AppSettings:DataProtection:CertificatePassword"]!;
    string path = Path.Combine(Toolbox.GetExecutableDirectory(), "Certificates", filename);

    X509Certificate2 certificate;

    if (builder.Environment.IsProduction())
    {
        certificate = new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }
    else
    {
        certificate = new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet);
    }

    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo($@"{Toolbox.GetExecutableDirectory()}\KeyRing"))
        .ProtectKeysWithCertificate(certificate);
    */
}

static void SetupConfiguration(WebApplicationBuilder builder)
{
    // https://andrewlock.net/adding-validation-to-strongly-typed-configuration-objects-in-asp-net-core/#creating-a-settings-validation-step-with-an-istartupfilter
    builder.Services.AddTransient<IStartupFilter, SettingValidationStartupFilter>();

    // Bind the configuration using IOptions
    builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

    // Explicitly register the settings object so IOptions not required (optional)
    builder.Services.AddSingleton(resolver =>
        resolver.GetRequiredService<IOptions<AppSettings>>().Value);

    // Register as an IValidatable
    builder.Services.AddSingleton<IValidatable>(resolver =>
        resolver.GetRequiredService<IOptions<AppSettings>>().Value);
}

static void SetupForwardedHeaders(WebApplicationBuilder builder)
{
    // Configure known load balancer IPs so that HttpContext.Connection.RemoteIpAddress returns
    // the incoming request's external IP address, rather than a load balancer IP address.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = null;
        options.KnownProxies.Add(IPAddress.Parse("10.0.5.5")); // OracleCMS Azure Cluster Load Balancer
        options.KnownProxies.Add(IPAddress.Parse("10.0.5.6")); // OracleCMS Azure Cluster Load Balancer
    });
}

static void SetupFusionCache(WebApplicationBuilder builder)
{
    // Cache Machine Name to global variable
    Globals.MachineName = Environment.MachineName;

    // Register the serializer to be used by FusionCache
    builder.Services.AddFusionCacheSystemTextJsonSerializer();

    // Register FusionCache
    builder.Services.AddFusionCache()
        .WithDefaultEntryOptions(new FusionCacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            Priority = Microsoft.Extensions.Caching.Memory.CacheItemPriority.Low
        });
}

static void SetupCORS(WebApplicationBuilder builder)
{
    IConfigurationSection prodUrlsSection = builder.Configuration.GetSection("AppSettings:Cors:ProductionUrls");
    string?[] prodUrls = prodUrlsSection.GetChildren().AsEnumerable().Select(c => c.Value).ToArray();

    IConfigurationSection devUrlsSection = builder.Configuration.GetSection("AppSettings:Cors:DevelopmentUrls");
    string?[] devUrls = devUrlsSection.GetChildren().AsEnumerable().Select(c => c.Value).ToArray();

    List<string> origins = new List<string>();

    if (builder.Environment.IsDevelopment() && devUrls.Length > 0)
    {
        foreach (string? devUrl in devUrls)
        {
            if (!string.IsNullOrWhiteSpace(devUrl))
            {
                origins.Add(devUrl);
            }
        }
    }

    if (prodUrls.Length > 0)
    {
        foreach (string? prodUrl in prodUrls)
        {
            if (!string.IsNullOrWhiteSpace(prodUrl))
            {
                origins.Add(prodUrl);
            }
        }
    }

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", policyBuilder => policyBuilder
            .WithOrigins(origins.ToArray())
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "Access-Control-Allow-Headers", "Authorization", "X-Requested-With", "X-Request-Counter", "X-XSRF-TOKEN", "X-Id-Token")
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition"));
    });
}

static void SetupAuthentication(WebApplicationBuilder builder)
{
    // Register repositories
    builder.Services.AddSingleton<UsersRepository>();
    builder.Services.AddSingleton<UserBuildingsRepository>();
    builder.Services.AddSingleton<UserOrganizationsRepository>();
    builder.Services.AddSingleton<UserLastUsedBuildingRepository>();
    builder.Services.AddSingleton<RefreshTokensRepository>();
    builder.Services.AddSingleton<AuthCacheService>();
    builder.Services.AddSingleton<AuthJwtTokenService>();
    builder.Services.AddSingleton<AuthJwtTokenTabletService>();
    builder.Services.AddSingleton<MicrosoftAccountService>();
    builder.Services.AddSingleton<TotpHelpers>();

    // Cookie Auth
    builder.Services.AddAuthenticationCookie(validFor: TimeSpan.FromMinutes(int.Parse(builder.Configuration["AppSettings:UserSession:UserSessionTimeoutMinutes"]!)), o =>
    {
        o.Events = new CookieAuthenticationEvents()
        {
            OnRedirectToLogin = (ctx) =>
            {
                //if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Response.StatusCode == 200)
                if (ctx.Response.StatusCode == 200)
                {
                    ctx.Response.StatusCode = 401;
                }
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = (ctx) =>
            {
                //if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Response.StatusCode == 200)
                if (ctx.Response.StatusCode == 200)
                {
                    ctx.Response.StatusCode = 403;
                }
                return Task.CompletedTask;
            }
        };
    });

    // JWT Bearer Token Auth
    builder.Services.AddAuthenticationJwtBearer(s => s.SigningKey = builder.Configuration["AppSettings:Jwt:TokenSigningKey"]!);

    // Add custom authentication which allows either Cookie or JWT Token.
    builder.Services.AddAuthentication("Jwt-Or-Cookie")
        .AddPolicyScheme("Jwt-Or-Cookie", "Jwt-Or-Cookie", o =>
        {
            o.ForwardDefaultSelector = ctx =>
            {
                if (ctx.Request.Headers.TryGetValue(HeaderNames.Authorization, out var authHeader) &&
                    authHeader.FirstOrDefault()?.StartsWith($"{JwtBearerDefaults.AuthenticationScheme} ") is true)
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }
                return CookieAuthenticationDefaults.AuthenticationScheme;
            };
        });

    // Add Antiforgery and set header name
    builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

    // Authorization Policies
    builder.Services.AddAuthorization(options =>
    {
        // System Role can be either User or Master only, and the below policies
        // reflect the user's System Role. A "Master" user has full access to all
        // organizations and is the intended role for developers or team members of the
        // SaaS solution provider. Users of the system should have the "User" role.

        // This is separate to Organization Roles, where a user may have "User" access
        // to the system as a whole, but may have e.g. "SuperAdmin" permission for a
        // specific organization.

        options.AddPolicy("User", policy =>
        {
            policy.RequireAuthenticatedUser();

            // Check user role
            policy.RequireAssertion(c =>
            {
                return c.User.IsInRole("User") || c.User.IsInRole("Master");
                /*
                UserSystemRole userSystemRole =  ShaneAuthHelpers.GetUserSystemRole(c.User);

                switch (userSystemRole)
                {
                    case UserSystemRole.User:
                    case UserSystemRole.Master:
                        return true;
                    default:
                        return false;
                }
                */
            });
        });

        options.AddPolicy("Master", policy =>
        {
            policy.RequireAuthenticatedUser();

            // Check user role
            policy.RequireAssertion(c =>
            {
                return c.User.IsInRole("Master");
                /*
                UserSystemRole userSystemRole = ShaneAuthHelpers.GetUserSystemRole(c.User);

                switch (userSystemRole)
                {
                    case UserSystemRole.Master:
                        return true;
                    case UserSystemRole.User:
                    default:
                        return false;
                }
                */
            });
        });
    });
}

static void SetupImageStorage(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<ImageStorageRepository>();
}

static void SetupFileStorage(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<FileStorageRepository>();
}

static void SetupRepositories(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<BuildingsRepository>();
    builder.Services.AddSingleton<FunctionsRepository>();
    builder.Services.AddSingleton<OrganizationsRepository>();
    builder.Services.AddSingleton<RegionsRepository>();
}

static void SetupVisitorTabletRepositories(WebApplicationBuilder builder)
{
    builder.Services.AddSingleton<VisitorTabletExamplesRepository>();
    builder.Services.AddSingleton<VisitorTabletVisitorRepository>();
    builder.Services.AddSingleton<GetHostsRepository>();
    builder.Services.AddSingleton<VisitorTabletBuildingsRepository>();
<<<<<<< HEAD
    builder.Services.AddSingleton<GetVisitorsRepository>();
=======
    builder.Services.AddSingleton<VisitorTabletVisitorRepository>();
>>>>>>> 44e5cc5 (feat(register-visitor): add logging and refactor code from Template to VisitorTablet project)
}

static void SetupHttpClients(WebApplicationBuilder builder)
{
    builder.Services.AddHttpClient("graph-api", httpClient =>
    {
        httpClient.BaseAddress = new Uri("https://login.microsoftonline.com/");
    });
}

static void SetupFastEndpointsConfig(Config c)
{
    c.Errors.ResponseBuilder = (failures, ctx, statusCode) =>
    {
        MyErrorResponse validationApiResult = new MyErrorResponse
        {
            StatusCode = statusCode
        };

        // Add errors into ErrorMessages dictionary
        foreach (ValidationFailure failure in failures)
        {
            ref List<MyErrorResponseMessage>? errorList = ref CollectionsMarshal.GetValueRefOrAddDefault(validationApiResult.ErrorMessages, failure.PropertyName, out bool exists);

            if (!exists)
            {
                errorList = new List<MyErrorResponseMessage>();
            }

            // Set ErrorCode and message for Model Binding and Json Parser errors.
            if (failure.ErrorCode is null)
            {
                if (failure.ErrorMessage == "ModelBindingError")
                {
                    if (!string.IsNullOrEmpty(failure.PropertyName))
                    {
                        failure.ErrorCode = "error.modelBindingWithProperty|{\"propertyName\":\"" + failure.PropertyName + "\"}";
                        failure.ErrorMessage = $"Application Error: {failure.PropertyName} was either missing or not provided in a valid format.";
                    }
                    else
                    {
                        failure.ErrorCode = "error.modelBinding";
                        failure.ErrorMessage = $"Application Error: The request data was either missing or not provided in a valid format.";
                    }
                }
                else if (failure.ErrorMessage == "JsonBindingError")
                {
                    if (!string.IsNullOrEmpty(failure.PropertyName))
                    {
                        failure.ErrorCode = "error.jsonBindingWithProperty|{\"propertyName\":\"" + failure.PropertyName + "\"}";
                        failure.ErrorMessage = $"Application Error: {failure.PropertyName} was either missing or not provided in a valid format.";
                    }
                    else
                    {
                        failure.ErrorCode = "error.jsonBinding";
                        failure.ErrorMessage = $"Application Error: The request data was either missing or not provided in a valid format.";
                    }
                }
            }

            errorList!.Add(new MyErrorResponseMessage
            {
                Message = failure.ErrorMessage,
                ErrorCode = failure.ErrorCode
            });
        }

        // Check for FatalError
        if (ctx.Items.ContainsKey("FatalError"))
        {
            validationApiResult.FatalError = true;
        }

        // Check for ConcurrencyKeyInvalid
        if (ctx.Items.ContainsKey("ConcurrencyKeyInvalid"))
        {
            validationApiResult.ConcurrencyKeyInvalid = true;
        }

        // Check for AdditionalData
        if (ctx.Items.TryGetValue("ErrorAdditionalData", out object? errorAdditionalDataObj)
            && errorAdditionalDataObj is string errorAdditionalData)
        {
            validationApiResult.AdditionalData = errorAdditionalData;
        }

        return validationApiResult;
    };

    c.Binding.JsonExceptionTransformer = (JsonException jsonException) =>
    {
        // Get property name
        //string propertyName = jsonException.Path != "$" ? jsonException.Path?[2..] : c.Serializer.SerializerErrorsField;
        string? propertyName = jsonException.Path != "$" ? jsonException.Path?[2..] : "SerializerErrors";

        // Set first letter to uppercase to match behaviour of AddError() used in endpoints.
        if (propertyName is not null && propertyName.Length > 0)
        {
            propertyName = string.Create(propertyName.Length, propertyName, (chars, state) =>
            {
                state.AsSpan().CopyTo(chars);
                chars[0] = char.ToUpper(chars[0]);
            });
        }

        return new ValidationFailure
        {
            PropertyName = propertyName,
            ErrorCode = null,
            ErrorMessage = "JsonBindingError",
        };
    };

    c.Binding.FailureMessage = (Type tProp, string propName, Microsoft.Extensions.Primitives.StringValues attemptedValue) =>
    {
        return "ModelBindingError";
    };

    c.Endpoints.Configurator = ep =>
    {
        // Add routes to globally accessible list. For use with /master/listEndpoints
        if (ep.Routes is not null)
        {
            foreach (string? route in ep.Routes)
            {
                if (route is null)
                {
                    continue;
                }

                Globals.Endpoints.Add(new EndpointRouteInfo
                {
                    Verbs = ep.Verbs,
                    Route = route,
                    ReqDtoType = ep.ReqDtoType.Name
                });
            }
        }
    };
}

// Below part must be at the bottom of the file
public static partial class Program
{
    public static bool Debug
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}