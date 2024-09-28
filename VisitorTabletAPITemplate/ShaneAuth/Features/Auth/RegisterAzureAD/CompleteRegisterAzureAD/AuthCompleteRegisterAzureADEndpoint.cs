using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RegisterAzureAD.CompleteRegisterAzureAD
{
    public sealed class AuthCompleteRegisterAzureADEndpoint : Endpoint<AuthCompleteRegisterAzureADRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCompleteRegisterAzureADEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/registerAzureAD/complete");
            SerializerContext(AuthCompleteRegisterAzureADContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCompleteRegisterAzureADRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Complete register token
            (UserSelfRegistrationResult result, UserData? userData) = await _usersRepository.CompleteRegisterAzureADTokenAsync(req, remoteIpAddress);

            // Validate result
            ValidateOutput(result, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendOkAsync(userData!);
        }

        public void ValidateInput(AuthCompleteRegisterAzureADRequest req)
        {
            // Trim strings
            req.FirstName = req.FirstName?.Trim();
            req.Surname = req.Surname?.Trim();

            // Validate input

            // Validate AzureTenantId
            if (!req.AzureTenantId.HasValue)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }

            // Validate AzureObjectId
            if (!req.AzureObjectId.HasValue)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }

            // Validate Token
            if (string.IsNullOrEmpty(req.Token))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }

            // Validate FirstName
            if (string.IsNullOrEmpty(req.FirstName))
            {
                AddError(m => m.FirstName!, "First Name is required.", "error.register.firstNameIsRequired");
            }
            else if (req.FirstName.Length > 75)
            {
                AddError(m => m.FirstName!, "First Name must be 75 characters or less.", "error.register.firstNameLength|{\"length\":\"75\"}");
            }

            // Validate Surname
            if (string.IsNullOrEmpty(req.Surname))
            {
                AddError(m => m.Surname!, "Surname is required.", "error.register.surnameIsRequired");
            }
            else if (req.Surname.Length > 75)
            {
                AddError(m => m.Surname!, "Surname must be 75 characters or less.", "error.register.surnameLength|{\"length\":\"75\"}");
            }

            // Validate RegionId
            if (!req.RegionId.HasValue)
            {
                AddError(m => m.RegionId!, "Region is required.", "error.register.regionIsRequired");
            }

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building is required.", "error.register.buildingIsRequired");
            }

            // Validate FunctionId
            if (!req.FunctionId.HasValue)
            {
                AddError(m => m.FunctionId!, "Function is required.", "error.register.functionIsRequired");
            }
        }

        public void ValidateOutput(UserSelfRegistrationResult userSelfRegistrationResult, UserData? userData)
        {
            // Validate queried data
            switch (userSelfRegistrationResult)
            {
                case UserSelfRegistrationResult.Ok:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case UserSelfRegistrationResult.SingleSignOnNotEnabled:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("Single Sign On using Azure Active Directory has not been enabled for your organization. Please contact your Facilities Management team for assistance, or create a local account using the 'Create an account' link from the Smart Space Pro login page instead.", "error.register.azureADSingleSignOnNotEnabled");
                    break;
                case UserSelfRegistrationResult.BuildingIdOrFunctionIdDoesNotBelongToMatchedOrganization:
                    AddError("The building or function does not belong to the organization associated with your email domain.", "error.register.buildingIdOrFunctionIdDoesNotBelongToMatchedOrganization");
                    break;
                case UserSelfRegistrationResult.RecordAlreadyExists:
                case UserSelfRegistrationResult.RegisterTokenInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                    break;
                case UserSelfRegistrationResult.GetAppLockFailed:
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
