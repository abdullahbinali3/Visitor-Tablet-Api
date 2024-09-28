using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsUnallocatedToUserForDropdown
{
    public sealed class ListBuildingsUnallocatedToUserForDropdownEndpoint : Endpoint<ListBuildingsUnallocatedToUserForDropdownRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingsUnallocatedToUserForDropdownEndpoint(BuildingsRepository buildingsRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/listUnallocatedToUserForDropdown/{uid}");
            SerializerContext(ListBuildingsUnallocatedToUserForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingsUnallocatedToUserForDropdownRequest req, CancellationToken ct)
        {
            // Get logged in user's details
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
            SelectListResponse data = await _buildingsRepository.ListBuildingsUnallocatedToUserForDropdownAsync(req.OrganizationId!.Value, req.Uid!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListBuildingsUnallocatedToUserForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }
            else if (!await _usersRepository.IsUserExistsAsync(req.Uid.Value, cancellationToken))
            {
                AddError(m => m.Uid!, "The selected user did not exist.", "error.user.didNotExist");
            }
        }
    }
}
