using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDropdown
{
    public sealed class MasterListUsersForDropdownEndpoint : Endpoint<MasterListUsersForDropdownRequest>
    {
        private readonly UsersRepository _usersRepository;

        public MasterListUsersForDropdownEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/master/users/listForDropdown");
            SerializerContext(MasterListUsersForDropdownContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterListUsersForDropdownRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            SelectListWithImageResponse data = await _usersRepository.MasterListUsersForDropdownAsync(req.OrganizationId, req.Search, req.RequestCounter, req.IncludeDisabled!.Value, ct);

            await SendAsync(data);
        }

        private static void ValidateInput(MasterListUsersForDropdownRequest req)
        {
            // Validate input

            // Validate IncludeDisabled
            if (!req.IncludeDisabled.HasValue)
            {
                req.IncludeDisabled = false;
            }
        }
    }
}
