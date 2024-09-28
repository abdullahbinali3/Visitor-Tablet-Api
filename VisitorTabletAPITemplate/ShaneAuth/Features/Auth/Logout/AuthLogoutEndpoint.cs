namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Logout
{
    public sealed class AuthLogoutEndpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Post("/auth/logout");
            Policies("User");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            // Sign the user out
            await CookieAuth.SignOutAsync();

            // Delete XSRF-TOKEN cookie
            HttpContext.Response.Cookies.Delete("XSRF-TOKEN");

            await SendNoContentAsync();
        }
    }
}
