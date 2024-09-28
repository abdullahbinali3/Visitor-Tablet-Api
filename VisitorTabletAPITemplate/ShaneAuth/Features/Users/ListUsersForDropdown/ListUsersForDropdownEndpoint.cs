using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListUsersForDropdown
{
    public sealed class ListUsersForDropdownEndpoint : Endpoint<ListUsersForDropdownRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListUsersForDropdownEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/listForDropdown");
            SerializerContext(ListUsersForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListUsersForDropdownRequest req, CancellationToken ct)
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
            SelectListWithImageResponse data = await _usersRepository.ListUsersForDropdownAsync(req.OrganizationId!.Value, req.Search, req.RequestCounter, req.IncludeDisabled!.Value, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListUsersForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken);

            // Validate input

            // Validate IncludeDisabled
            if (!req.IncludeDisabled.HasValue)
            {
                req.IncludeDisabled = false;
            }
        }
    }
}
