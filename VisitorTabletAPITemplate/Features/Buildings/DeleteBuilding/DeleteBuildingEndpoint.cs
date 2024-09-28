using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.Regions.DeleteRegion;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Buildings.DeleteBuilding
{
    public sealed class DeleteBuildingEndpoint : Endpoint<DeleteBuildingRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public DeleteBuildingEndpoint(BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/buildings/{organizationId}/delete");
            SerializerContext(DeleteBuildingContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(DeleteBuildingRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, Building? building) = await _buildingsRepository.DeleteBuildingAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, building);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private async Task ValidateInputAsync(DeleteBuildingRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Building Id is required.", "error.building.idIsRequired");
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

        private void ValidateOutput(SqlQueryResult queryResult, Building? building)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The building was deleted since you last accessed this page.", "error.building.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(building!, DeleteBuildingContext.Default.Building));
                    AddError("The building's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit again if you still wish to delete.", "error.building.deleteConcurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
