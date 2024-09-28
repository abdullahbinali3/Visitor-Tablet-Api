using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Organizations.GetOrganization
{
    public sealed class GetOrganizationEndpoint : Endpoint<GetOrganizationRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetOrganizationEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/organizations/get/{id}");
            SerializerContext(GetOrganizationContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetOrganizationRequest req, CancellationToken ct)
        {
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
            Organization? organization = await _organizationsRepository.GetOrganizationAsync(req.id!.Value, ct);

            // Validate result
            ValidateOutput(organization);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(organization!);
        }

        private async Task ValidateInputAsync(GetOrganizationRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user is a Master, ignoring the organization permission, OR is a Super Admin and has access to the organization
            if (!await this.ValidateMasterOrUserOrganizationRoleAsync(req.id, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "id is required.", "error.organization.idIsRequired");
            }
        }

        private void ValidateOutput(Organization? organization)
        {
            if (organization is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected organization did not exist.", "error.organization.didNotExist");
            }
        }
    }
}
