using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Regions.UpdateRegion
{
    public sealed class UpdateRegionEndpoint : Endpoint<UpdateRegionRequest>
    {
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateRegionEndpoint(RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/regions/{organizationId}/update");
            SerializerContext(UpdateRegionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateRegionRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, Region? region) = await _regionsRepository.UpdateRegionAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, region);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(region!);
        }

        private async Task ValidateInputAsync(UpdateRegionRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Trim strings
            req.Name = req.Name?.Trim();

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "id is required.", "error.region.idIsRequired");
            }

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Region Name is required.", "error.region.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Region Name must be 100 characters or less.", "error.region.nameLength|{\"length\":\"100\"}");
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, Region? region)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The region was deleted since you last accessed this page.", "error.region.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another region already exists with the specified name.", "error.region.nameExists");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(region!, UpdateRegionContext.Default.Region));
                    AddError("The region's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.region.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
