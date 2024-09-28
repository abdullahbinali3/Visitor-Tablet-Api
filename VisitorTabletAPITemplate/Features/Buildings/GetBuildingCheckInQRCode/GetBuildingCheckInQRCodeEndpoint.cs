using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.GetBuildingCheckInQRCode
{
    public sealed class GetBuildingCheckInQRCodeEndpoint : Endpoint<GetBuildingCheckInQRCodeRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetBuildingCheckInQRCodeEndpoint(BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/getCheckInQRCode/{id}");
            SerializerContext(GetBuildingCheckInQRCodeContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetBuildingCheckInQRCodeRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId!.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            (SqlQueryResult queryResult, byte[]? pngImage) = await _buildingsRepository.GetBuildingCheckInQRCodePngAsync(req.id!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(queryResult, pngImage);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendBytesAsync(pngImage!, $"CheckInQRCode_{req.OrganizationId}_{req.id}.png", contentType: "image/png");
        }

        private async Task ValidateInputAsync(GetBuildingCheckInQRCodeRequest req, Guid userId, CancellationToken cancellationToken = default)
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
                AddError(m => m.id!, "Building Id is required.", "error.building.idIsRequired");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, byte[]? pngImage)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    if (pngImage is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The selected building did not exist.", "error.building.didNotExist");
                    break;
                case SqlQueryResult.SubRecordDidNotExist:
                    AddError("The building did not have a Check-In QR Code assigned to it.", "error.building.buildingDoesNotHaveCheckInQRCode");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
