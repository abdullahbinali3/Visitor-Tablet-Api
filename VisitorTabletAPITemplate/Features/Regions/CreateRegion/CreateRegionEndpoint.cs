using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Regions.CreateRegion
{
    public sealed class CreateRegionEndpoint : Endpoint<CreateRegionRequest> 
    {
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public CreateRegionEndpoint(RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/regions/{organizationId}/create");
            SerializerContext(CreateRegionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(CreateRegionRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Region? region) = await _regionsRepository.CreateRegionAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(region!);
        }

        private async Task ValidateInputAsync(CreateRegionRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Trim strings
            req.Name = req.Name?.Trim();

            // Validate input

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Region Name is required.", "error.region.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Region Name must be 100 characters or less.", "error.region.nameLength|{\"length\":\"100\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another region already exists with the specified name.", "error.region.nameExists");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
