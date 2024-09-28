using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LogoutJwt
{
    public sealed class AuthLogoutJwtEndpoint : EndpointWithoutRequest
    {
        private readonly RefreshTokensRepository _refreshTokensRepository;

        public AuthLogoutJwtEndpoint(RefreshTokensRepository refreshTokensRepository)
        {
            _refreshTokensRepository = refreshTokensRepository;
        }

        public override void Configure()
        {
            Post("/auth/logoutJwt");
            Policies("User");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Clear all refresh tokens for user
            await _refreshTokensRepository.ClearRefreshTokens(userId.Value);

            // Delete XSRF-TOKEN cookie
            HttpContext.Response.Cookies.Delete("XSRF-TOKEN");

            await SendNoContentAsync();
        }
    }
}
