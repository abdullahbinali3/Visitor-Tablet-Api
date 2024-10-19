using Microsoft.AspNetCore.WebUtilities;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.GenerateMobileQRCode
{
    public sealed class VisitorTabletGetMobileQRCodeEndpoint : Endpoint<VisitorTabletGetMobileQRCodeRequest>
    {
        private readonly VisitorTabletOrganizationsRepository _visitorTabletOrganizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public VisitorTabletGetMobileQRCodeEndpoint(VisitorTabletOrganizationsRepository visitorTabletOrganizationsRepository,
            AuthCacheService authCacheService)
        {
            _visitorTabletOrganizationsRepository = visitorTabletOrganizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/visitorTablet/getMobileQrCode/{organizationId}/{buildingId}");
            SerializerContext(VisitorTabletGetMobileQRCodeContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(VisitorTabletGetMobileQRCodeRequest req, CancellationToken ct)
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
            string? organizationEncryptionKey = await _visitorTabletOrganizationsRepository.GetEncryptionKeyAsync(req.OrganizationId!.Value, ct);

            if (organizationEncryptionKey is null)
            {
                AddError("The Mobile QR Code could not be generated.", "error.visitorTablet.mobileQrCodeCouldNotBeGenerated");

                await SendErrorsAsync();
                return;
            }

            string token = $"{req.OrganizationId}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            string encryptedToken = StringCipherAesGcm.Encrypt(token, organizationEncryptionKey);

            string qrCodeUri = $"http://localhost:5000/mypage";

            List<KeyValuePair<string, string?>> queryStringParams = new List<KeyValuePair<string, string?>>()
            {
                new KeyValuePair<string, string?>("organizationId", req.OrganizationId.Value.ToString()),
                new KeyValuePair<string, string?>("buildingId", req.BuildingId!.Value.ToString()),
                new KeyValuePair<string, string?>("token", encryptedToken),
            };

            qrCodeUri = QueryHelpers.AddQueryString(qrCodeUri, queryStringParams);

            byte[] pngImage = QRCodeGeneratorHelpers.GeneratePngQRCode(qrCodeUri);

            await SendBytesAsync(pngImage!, $"VisitorMobile_{req.OrganizationId.Value}_{req.BuildingId.Value}.png", contentType: "image/png");
        }

        private async Task ValidateInputAsync(VisitorTabletGetMobileQRCodeRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            // Normally would only need to check the user has access to the building, but in this case we also want to
            // restrict the endpoint to users in the Tablet role.
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Tablet, _authCacheService, cancellationToken))
            {
                return;
            }

            // Check the user has access to the building
            if (!await this.ValidateUserBuildingAsync(req.OrganizationId, req.BuildingId, userId, _authCacheService, cancellationToken))
            {
                return;
            }
        }
    }
}
