using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RegisterAzureAD.CheckRegisterAzureAD
{
    public sealed class AuthCheckRegisterAzureADEndpoint : Endpoint<AuthCheckRegisterAzureADRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCheckRegisterAzureADEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/registerAzureAD/check");
            SerializerContext(AuthCheckRegisterAzureADContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCheckRegisterAzureADRequest req, CancellationToken ct)
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

            // Check register token
            (UserSelfRegistrationResult result, RegisterFormData? registerFormData) = await _usersRepository.CheckRegisterAzureADTokenAsync(req.AzureTenantId!.Value, req.AzureObjectId!.Value, req.Token!, remoteIpAddress);

            // Validate result
            ValidateOutput(result, registerFormData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendOkAsync(registerFormData!);
        }

        public void ValidateInput(AuthCheckRegisterAzureADRequest req)
        {
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
            }
        }

        private void ValidateOutput(UserSelfRegistrationResult queryResult, RegisterFormData? registerFormData)
        {
            switch (queryResult)
            {
                case UserSelfRegistrationResult.Ok:
                    if (registerFormData is null || registerFormData.Regions is null)
                    {
                        AddError("An administrator has not fully configured your organization, so registrations for your organization are not yet possible.", "error.register.organizationNotConfigured");
                        return;
                    }
                    return;
                case UserSelfRegistrationResult.SingleSignOnNotEnabled:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("Single Sign On using Azure Active Directory has not been enabled for your organization. Please contact your Facilities Management team for assistance, or create a local account using the 'Create an account' link from the Smart Space Pro login page instead.", "error.register.azureADSingleSignOnNotEnabled");
                    break;
                case UserSelfRegistrationResult.RecordAlreadyExists:
                case UserSelfRegistrationResult.RegisterTokenInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
