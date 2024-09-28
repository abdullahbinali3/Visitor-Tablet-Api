using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.GetUserDisplayNameByEmailAndIsAssignedToBuilding
{
    public sealed class GetUserDisplayNameByEmailAndIsAssignedToBuildingEndpoint : Endpoint<GetUserDisplayNameByEmailAndIsAssignedToBuildingRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public GetUserDisplayNameByEmailAndIsAssignedToBuildingEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/{buildingId}/getDisplayNameByEmailAndIsAssignedToBuilding/{email}");
            SerializerContext(GetUserDisplayNameByEmailAndIsAssignedToBuildingContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetUserDisplayNameByEmailAndIsAssignedToBuildingRequest req, CancellationToken ct)
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
            UserDisplayNameAndIsAssignedToBuildingData? data = await _usersRepository.GetUserDisplayNameByEmailAndIsAssignedToBuildingAsync(req.Email!, req.OrganizationId!.Value, req.BuildingId!.Value, ct);

            // Validate result
            ValidateOutput(data);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(data!);
        }

        private async Task ValidateInputAsync(GetUserDisplayNameByEmailAndIsAssignedToBuildingRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input
            req.Email = req.Email?.Trim();

            // Validate Email
            if (string.IsNullOrWhiteSpace(req.Email))
            {
                AddError(m => m.Email!, "Email is required.", "error.user.emailIsRequired");
            }
            else if (req.Email.Length > 254)
            {
                AddError(m => m.Email!, "Email must be 254 characters or less.", "error.user.emailLength|{\"length\":\"254\"}");
            }
            else if (!Toolbox.IsValidEmail(req.Email))
            {
                AddError(m => m.Email!, "Email is invalid.", "error.user.emailIsInvalid");
            }
        }

        private void ValidateOutput(UserDisplayNameAndIsAssignedToBuildingData? data)
        {
            if (data is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected user did not exist.", "error.user.didNotExist");
            }
        }
    }
}
