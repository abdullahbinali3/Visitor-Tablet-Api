using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Functions.GetFunction
{
    public sealed class GetFunctionEndpoint : Endpoint<GetFunctionRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetFunctionEndpoint(FunctionsRepository functionsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/functions/{organizationId}/get/{id}");
            SerializerContext(GetFunctionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetFunctionRequest req, CancellationToken ct)
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
            Function? function = await _functionsRepository.GetFunctionAsync(req.id!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(function);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(function!);
        }

        private async Task ValidateInputAsync(GetFunctionRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Id is required.", "error.idIsRequired");
            }
        }

        private void ValidateOutput(Function? function)
        {
            if (function is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected function did not exist.", "error.function.didNotExist");
            }
        }
    }
}
