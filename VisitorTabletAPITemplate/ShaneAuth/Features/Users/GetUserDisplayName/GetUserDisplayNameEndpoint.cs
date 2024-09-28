using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.GetUserDisplayName
{
    public sealed class GetUserDisplayNameEndpoint : Endpoint<GetUserDisplayNameRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public GetUserDisplayNameEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/getDisplayName/{uid}");
            SerializerContext(GetUserDisplayNameContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetUserDisplayNameRequest req, CancellationToken ct)
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
            UserDisplayNameData? data = await _usersRepository.GetUserDisplayNameAsync(req.Uid!.Value, req.OrganizationId!.Value, ct);

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

        private async Task ValidateInputAsync(GetUserDisplayNameRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }
        }

        private void ValidateOutput(UserDisplayNameData? data)
        {
            if (data is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected user did not exist.", "error.user.didNotExist");
            }
        }
    }
}
