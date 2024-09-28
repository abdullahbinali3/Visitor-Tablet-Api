using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.InitRegister
{
    public sealed class AuthInitRegisterEndpoint : Endpoint<AuthInitRegisterRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthInitRegisterEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/register/init");
            SerializerContext(AuthInitRegisterContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthInitRegisterRequest req, CancellationToken ct)
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

            // Query data
            UserSelfRegistrationResult selfRegistrationResult = await _usersRepository.InitRegisterAsync(req, remoteIpAddress);

            // Validate result
            ValidateOutput(selfRegistrationResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        public void ValidateInput(AuthInitRegisterRequest req)
        {
            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();

            // Validate input

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                AddError(m => m.Email!, "Email is required.", "error.register.emailIsRequired");
            }
            else if (req.Email.Length > 254)
            {
                AddError(m => m.Email!, "Email must be 254 characters or less.", "error.register.emailLength|{\"length\":\"254\"}");
            }
            else if (!Toolbox.IsValidEmail(req.Email))
            {
                AddError(m => m.Email!, "Email format is invalid.", "error.register.emailIsInvalidFormat");
            }
        }

        private void ValidateOutput(UserSelfRegistrationResult selfRegistrationResult)
        {
            // Validate queried data
            switch (selfRegistrationResult)
            {
                case UserSelfRegistrationResult.Ok:
                    return;
                case UserSelfRegistrationResult.RecordAlreadyExists:
                    AddError("A user with the specified email address already exists.", "error.register.userEmailExists");
                    break;
                case UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization:
                    AddError("Your email domain does not belong to an existing organization.", "error.register.emailDomainDoesNotBelongToAnExistingOrganization");
                    break;
                case UserSelfRegistrationResult.LocalLoginDisabled:
                    AddError("Local login has been disabled for your organization. Please log in using Single Sign On instead.", "error.register.localLoginDisabled");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
