using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.VisitorTablet.Models;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Examples.GetExample
{
    public sealed class GetExampleEndpoint : Endpoint<GetExampleRequest>
    {
        private readonly VisitorTabletExamplesRepository _examplesRepository;
        private readonly AuthCacheService _authCacheService;

        public GetExampleEndpoint(VisitorTabletExamplesRepository examplesRepository,
            AuthCacheService authCacheService)
        {
            _examplesRepository = examplesRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/visitorTablet/examples/{organizationId}/get/{id}");
            SerializerContext(GetExampleContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetExampleRequest req, CancellationToken ct)
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
            Example? example = await _examplesRepository.GetExampleAsync(req.id!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(example);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(example!);
        }

        private async Task ValidateInputAsync(GetExampleRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Tablet, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Example Id is required.", "error.example.idIsRequired");
            }
        }

        private void ValidateOutput(Example? example)
        {
            if (example is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected example did not exist.", "error.example.didNotExist");
            }
        }
    }
}
