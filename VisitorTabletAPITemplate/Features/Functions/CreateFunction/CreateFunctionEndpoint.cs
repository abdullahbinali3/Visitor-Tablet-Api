using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.Functions.CreateFunction
{
    public sealed class CreateFunctionEndpoint : Endpoint<CreateFunctionRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public CreateFunctionEndpoint(FunctionsRepository functionsRepository,
            BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/functions/{organizationId}/create");
            SerializerContext(CreateFunctionContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(CreateFunctionRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, Function? function) = await _functionsRepository.CreateFunctionAsync(req, userId, adminUserDisplayName, remoteIpAddress);

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

        private async Task ValidateInputAsync(CreateFunctionRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            UserOrganizationRole minimumOrganizationRole = UserOrganizationRole.SuperAdmin;

            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, minimumOrganizationRole, _authCacheService))
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

            // Validate input

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building Id is required.", "error.buildingIdIsRequired");
            }
            else if (!await _buildingsRepository.IsBuildingExistsAsync(req.BuildingId.Value, req.OrganizationId!.Value))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError(m => m.BuildingId!, "Building Id is invalid.", "error.buildingIdIsInvalid");
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
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another function already exists with the specified name in this building.", "error.function.nameExists");
                    break;
                case SqlQueryResult.SubRecordInvalid:
                    AddError(m => m.FunctionAdjacencies!, "Function Adjacencies contains at least one invalid function.", "error.function.functionAdjacenciesIsInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
