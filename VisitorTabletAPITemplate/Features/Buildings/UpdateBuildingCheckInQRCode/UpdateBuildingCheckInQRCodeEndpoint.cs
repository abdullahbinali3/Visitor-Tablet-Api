using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Buildings.UpdateBuildingCheckInQRCode
{
    public sealed class UpdateBuildingCheckInQRCodeEndpoint : Endpoint<UpdateBuildingCheckInQRCodeRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateBuildingCheckInQRCodeEndpoint(BuildingsRepository buildingsRepository,
             OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/buildings/{organizationId}/updateCheckInQRCode");
            SerializerContext(UpdateBuildingCheckInQRCodeContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateBuildingCheckInQRCodeRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

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

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Building? building) = await _buildingsRepository.UpdateBuildingCheckInQRCodeAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, building);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(building!);
        }

        private async Task ValidateInputAsync(UpdateBuildingCheckInQRCodeRequest req, Guid userId, CancellationToken ct)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Get organization params to check CheckInEnabled for the organization
            Organization? organization = await _organizationsRepository.GetOrganizationAsync(req.OrganizationId!.Value);
            if (organization is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("You do not have permission to perform this action.", "error.doNotHavePermission");
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Building Id is required.", "error.building.idIsRequired");
            }

            // Validate CheckInEnabled
            if (!organization.CheckInEnabled)
            {
                AddError(m => m.CheckInQRCode!, "Check-In must be enabled in the Organization setting before assigning/updating Check-In QR Code.", "error.building.checkInEnabledMustBeTrue");
            }
            else 
            {
                // Validate CheckInQRCode
                if (string.IsNullOrWhiteSpace(req.CheckInQRCode))
                {
                    AddError(m => m.CheckInQRCode!, "Check-In QR Code is required.", "error.building.checkInQRCodeIsRequired");
                }
                else if (req.CheckInQRCode.Length > 100)
                {
                    AddError(m => m.CheckInQRCode!, "Check-In QR Code must be 100 characters or less.", "error.building.checkInQRCodeLength|{\"length\":\"100\"}");
                }
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
                case SqlQueryResult.RecordAlreadyExists:  // should not happen
                    break;
                case SqlQueryResult.SubRecordAlreadyExists:
                    AddError(m => m.CheckInQRCode!, "The specified Check-In QR Code has already been assigned to another building.", "error.building.checkInQRCodeExists");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(building!, UpdateBuildingCheckInQRCodeContext.Default.Building));
                    AddError("The building's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.building.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
