using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Host.GetHosts
{
    public class GetHostsEndpoint : Endpoint<GetHostsRequest>
    {

        private readonly GetHostsRepository _GetHostsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetHostsEndpoint(GetHostsRepository GetHostsRepository,
            AuthCacheService authCacheService)
        {
            _GetHostsRepository = GetHostsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/host/{organizationId}/listForDropdown");
            SerializerContext(GetHostsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetHostsRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            SelectListWithImageResponse data = await _GetHostsRepository.ListUsersForDropdownAsync(req.OrganizationId!.Value, req.Search, req.RequestCounter, req.IncludeDisabled!.Value, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(GetHostsRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Tablet, _authCacheService, cancellationToken);

            // Validate input

            // Validate IncludeDisabled
            if (!req.IncludeDisabled.HasValue)
            {
                req.IncludeDisabled = false;
            }
        }
    }
}