using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsUnallocatedToUserForDropdown
{
    public sealed class ListOrganizationsUnallocatedToUserForDropdownEndpoint : Endpoint<ListOrganizationsUnallocatedToUserForDropdownRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly UsersRepository _usersRepository;

        public ListOrganizationsUnallocatedToUserForDropdownEndpoint(OrganizationsRepository organizationsRepository,
            UsersRepository usersRepository)
        {
            _organizationsRepository = organizationsRepository;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/organizations/listUnallocatedToUserForDropdown/{uid}");
            SerializerContext(ListOrganizationsUnallocatedToUserForDropdownContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(ListOrganizationsUnallocatedToUserForDropdownRequest req, CancellationToken ct)
        {
            // Validate request
            await ValidateInputAsync(req, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            SelectListResponse data = await _organizationsRepository.ListOrganizationsUnallocatedToUserForDropdownAsync(req.Uid!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListOrganizationsUnallocatedToUserForDropdownRequest req, CancellationToken cancellationToken)
        {
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
