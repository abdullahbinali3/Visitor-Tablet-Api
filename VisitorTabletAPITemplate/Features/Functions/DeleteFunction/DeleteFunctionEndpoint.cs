using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Functions.DeleteFunction
{
    public sealed class DeleteFunctionEndpoint : Endpoint<DeleteFunctionRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly AuthCacheService _authCacheService;

        public DeleteFunctionEndpoint(FunctionsRepository functionsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/functions/{organizationId}/delete");
            SerializerContext(DeleteFunctionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(DeleteFunctionRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Function? function, DeleteFunctionResponse_FunctionInUse? functionInUseResponse) = await _functionsRepository.DeleteFunctionAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, function, functionInUseResponse);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private async Task ValidateInputAsync(DeleteFunctionRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Function Id is required.", "error.function.idIsRequired");
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, Function? function, DeleteFunctionResponse_FunctionInUse? functionInUseResponse)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The function was deleted since you last accessed this page.", "error.function.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.RecordIsInUse:
                    if (functionInUseResponse is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(functionInUseResponse, DeleteFunctionContext.Default.DeleteFunctionResponse_FunctionInUse));
                    AddError("The function could not be deleted as it is still in use. Please assign the following desks and users to a different function and try again.", "error.function.cannotDeleteWhileInUse");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    if (function is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(function, DeleteFunctionContext.Default.Function));
                    AddError("The function's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.function.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
