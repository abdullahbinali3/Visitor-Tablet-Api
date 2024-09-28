using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Regions.GetRegion
{
    public sealed class GetRegionEndpoint : Endpoint<GetRegionRequest>
    {
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetRegionEndpoint(RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/regions/{organizationId}/get/{id}");
            SerializerContext(GetRegionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetRegionRequest req, CancellationToken ct)
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
            Region? region = await _regionsRepository.GetRegionAsync(req.id!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(region);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(region!);
        }

        private async Task ValidateInputAsync(GetRegionRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "id is required.", "error.region.idIsRequired");
            }
        }

        private void ValidateOutput(Region? region)
        {
            if (region is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected region did not exist.", "error.region.didNotExist");
            }
        }
    }
}
