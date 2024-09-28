using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.CheckRegister
{
    public sealed class AuthCheckRegisterEndpoint : Endpoint<AuthCheckRegisterRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCheckRegisterEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/register/check");
            SerializerContext(AuthCheckRegisterContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCheckRegisterRequest req, CancellationToken ct)
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
            (UserSelfRegistrationResult result, RegisterFormData? registerFormData) = await _usersRepository.CheckRegisterTokenAsync(req.Email!, req.Token!, remoteIpAddress);

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

        public void ValidateInput(AuthCheckRegisterRequest req)
        {
            // Validate input

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }
            else if (req.Email.Length > 254)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }
            else if (!Toolbox.IsValidEmail(req.Email))
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
                case UserSelfRegistrationResult.LocalLoginDisabled:
                    AddError("Local login has been disabled for your organization. Please log in using Single Sign On instead.", "error.register.localLoginDisabled");
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
