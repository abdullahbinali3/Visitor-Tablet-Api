using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListManageableUsersForDropdown
{
    public sealed class ListManageableUsersForDropdownEndpoint : Endpoint<ListManageableUsersForDropdownRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListManageableUsersForDropdownEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/{buildingId}/listManageableForDropdown");
            SerializerContext(ListManageableUsersForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListManageableUsersForDropdownRequest req, CancellationToken ct)
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
            UserOrganizationPermission? organizationPermission = await _authCacheService.GetUserOrganizationPermissionAsync(userId.Value, req.OrganizationId!.Value, ct);

            SelectListWithImageResponse data;

            if (organizationPermission!.UserOrganizationRole == UserOrganizationRole.Admin)
            {
                // For admin, list all users who belong to the user's User Admin Functions.
                data = await _usersRepository.ListUsersInUserAdminFunctionsForDropdown(userId.Value, req.OrganizationId!.Value, req.BuildingId!.Value, req.Search, req.RequestCounter, req.IncludeDisabled!.Value, ct);
            }
            else if (organizationPermission!.UserOrganizationRole == UserOrganizationRole.SuperAdmin)
            {
                // For Super Admin, list all users in the building.
                data = await _usersRepository.ListUsersInBuildingForDropdownAsync(req.OrganizationId!.Value, req.BuildingId!.Value, req.Search, req.RequestCounter, req.IncludeDisabled!.Value, ct);
            }
            else
            {
                throw new Exception($"Unknown User Organization Role: {organizationPermission.UserOrganizationRole}");
            }

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListManageableUsersForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Admin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate user has minimum required access to the building to perform this action
            if (!await this.ValidateUserBuildingAsync(req.OrganizationId, req.BuildingId, userId, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate IncludeDisabled
            if (!req.IncludeDisabled.HasValue)
            {
                req.IncludeDisabled = false;
            }
        }
    }
}
