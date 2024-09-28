using Microsoft.AspNetCore.Antiforgery;

namespace VisitorTabletAPITemplate.ShaneAuth.Processors
{
    public sealed class AntiForgeryProcessor : IGlobalPreProcessor
    {
        public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
        {
            HttpContext httpContext = context.HttpContext;

            // Ignore anti-forgery token if Authorization header present and Cookie header not present
            if (httpContext.Request.Headers.Authorization.Any() && !httpContext.Request.Headers.Cookie.Any())
            {
                return;
            }

            // Validate anti-forgery token
            try
            {
                IAntiforgery antiforgery = httpContext.Resolve<IAntiforgery>();
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                await httpContext.Response.SendForbiddenAsync();
            }
        }
    }
}
