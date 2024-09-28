using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Functions.UpdateFunction
{
    public sealed class UpdateFunctionEndpoint : Endpoint<UpdateFunctionRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateFunctionEndpoint(FunctionsRepository functionsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/functions/{organizationId}/update");
            SerializerContext(UpdateFunctionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateFunctionRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, Function? function) = await _functionsRepository.UpdateFunctionAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, function);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(function!);
        }

        private async Task ValidateInputAsync(UpdateFunctionRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Trim strings
            req.Name = req.Name?.Trim();
            req.HtmlColor = req.HtmlColor?.Trim().ToLowerInvariant();

            // Remove duplicates
            if (req.FunctionAdjacencies is not null && req.FunctionAdjacencies.Count > 1)
            {
                req.FunctionAdjacencies = Toolbox.DedupeGuidList(req.FunctionAdjacencies);
            }

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Function Id is required.", "error.function.idIsRequired");
            }

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Function Name is required.", "error.function.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Function Name must be 100 characters or less.", "error.function.nameLength|{\"length\":\"100\"}");
            }

            // Validate HtmlColor
            if (string.IsNullOrWhiteSpace(req.HtmlColor))
            {
                AddError(m => m.HtmlColor!, "Floor Plan Desk Color is required.", "error.function.htmlColorIsRequired");
            }
            else if (req.HtmlColor.Length != 7)
            {
                AddError(m => m.HtmlColor!, "Floor Plan Desk Color must be 7 characters.", "error.function.htmlColorLength|{\"length\":\"7\"}");
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

        private void ValidateOutput(SqlQueryResult queryResult, Function? function)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    if (function is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The function was deleted since you last accessed this page.", "error.function.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another function already exists with the specified name in this building.", "error.function.nameExists");
                    break;
                case SqlQueryResult.SubRecordInvalid:
                    AddError(m => m.FunctionAdjacencies!, "Function Adjacencies contains at least one invalid function.", "error.function.functionAdjacenciesIsInvalid");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(function!, UpdateFunctionContext.Default.Function));
                    AddError("The function's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.function.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
