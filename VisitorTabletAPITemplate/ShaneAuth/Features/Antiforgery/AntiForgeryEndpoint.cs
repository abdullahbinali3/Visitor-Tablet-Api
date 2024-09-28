using Microsoft.AspNetCore.Antiforgery;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Antiforgery
{
    public sealed class AntiforgeryEndpoint : EndpointWithoutRequest
    {
        private readonly IAntiforgery _antiforgery;

        public AntiforgeryEndpoint(IAntiforgery antiforgery)
        {
            _antiforgery = antiforgery;
        }

        public override void Configure()
        {
            Get("/antiforgery");
            Policies("User");
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            AntiforgeryTokenSet tokens = _antiforgery.GetAndStoreTokens(HttpContext);

            HttpContext.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!,
                new CookieOptions { HttpOnly = false });

            await SendNoContentAsync();
        }
    }
}
