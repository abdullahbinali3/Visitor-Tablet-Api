using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.CompleteRegister
{
    public sealed class AuthCompleteRegisterEndpoint : Endpoint<AuthCompleteRegisterRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCompleteRegisterEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/register/complete");
            SerializerContext(AuthCompleteRegisterContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCompleteRegisterRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Register user
            (UserSelfRegistrationResult userSelfRegistrationResult, UserData? userData) = await _usersRepository.CompleteRegisterAsync(req, remoteIpAddress);

            // Validate result
            ValidateOutput(userSelfRegistrationResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(userData!);
        }

        public void ValidateInput(AuthCompleteRegisterRequest req)
        {
            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();
            req.FirstName = req.FirstName?.Trim();
            req.Surname = req.Surname?.Trim();

            // Validate input

            // Validate Token
            if (string.IsNullOrEmpty(req.Token))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The registration link you followed was either invalid or expired.", "error.register.registerTokenInvalid");
                return;
            }

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                AddError(m => m.Email!, "Email is required.", "error.login.emailIsRequired");
            }
            else if (req.Email.Length > 254)
            {
                AddError(m => m.Email!, "Email must be 254 characters or less.", "error.login.emailLength|{\"length\":\"254\"}");
            }
            else if (!Toolbox.IsValidEmail(req.Email))
            {
                AddError(m => m.Email!, "Email format is invalid.", "error.login.emailIsInvalidFormat");
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

            // Validate LocalPassword
            if (string.IsNullOrEmpty(req.LocalPassword))
            {
                AddError(m => m.LocalPassword!, "Local Password is required.", "error.register.localPasswordIsRequired");
            }
            else if (req.LocalPassword.Length < 10)
            {
                AddError(m => m.LocalPassword!, "Local Password must be at least 10 characters long.", "error.register.localPasswordLength|{\"length\":\"10\"}");
            }

            // Validate LocalPasswordConfirm
            if (string.IsNullOrEmpty(req.LocalPasswordConfirm))
            {
                AddError(m => m.LocalPasswordConfirm!, "Local Password (Confirm) is required.", "error.register.localPasswordConfirmIsRequired");
            }
            else if (req.LocalPasswordConfirm.Length < 10)
            {
                AddError(m => m.LocalPasswordConfirm!, "Local Password (Confirm) must be at least 10 characters long.", "error.register.localPasswordConfirmLength|{\"length\":\"10\"}");
            }
            else if (req.LocalPassword != req.LocalPasswordConfirm)
            {
                AddError(m => m.LocalPasswordConfirm!, "Local Password and Local Password (Confirm) must match.", "error.register.localPasswordConfirmMismatch");
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
                case UserSelfRegistrationResult.RecordAlreadyExists:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("A user with the specified email address already exists.", "error.register.userEmailExists");
                    break;
                case UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("Your email domain does not belong to an existing organization.", "error.register.emailDomainDoesNotBelongToAnExistingOrganization");
                    break;
                case UserSelfRegistrationResult.BuildingIdOrFunctionIdDoesNotBelongToMatchedOrganization:
                    AddError("The building or function does not belong to the organization associated with your email domain.", "error.register.buildingIdOrFunctionIdDoesNotBelongToMatchedOrganization");
                    break;
                case UserSelfRegistrationResult.LocalLoginDisabled:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("Local login has been disabled for your organization. Please log in using Single Sign On instead.", "error.register.localLoginDisabled");
                    break;
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
