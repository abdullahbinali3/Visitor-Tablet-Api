using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.PasswordLoginJwtTablet
{
    public sealed class AuthPasswordLoginJwtTabletEndpoint : Endpoint<AuthPasswordLoginJwtTabletRequest, TokenResponse>
    {
        private readonly UsersRepository _usersRepository;

        public AuthPasswordLoginJwtTabletEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/passwordLoginJwtTablet");
            SerializerContext(AuthPasswordLoginJwtTabletContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthPasswordLoginJwtTabletRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Check user credentials
            (VerifyCredentialsResult result, UserData? userData) = await _usersRepository.VerifyCredentialsAndGetUserAsync(req.Email!, req.Password!, req.TotpCode, "Tablet", ct);

            // Validate result
            ValidateOutput(result, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // For master users, also populate MasterInfo
            if (userData!.UserSystemRole == UserSystemRole.Master)
            {
                userData.ExtendedData.MasterInfo = await _usersRepository.GetMasterInfoAsync(ct);
            }

            // Create JWT Token to sign in the user
            Response = await CreateTokenWith<AuthJwtTokenTabletService>(userData.Uid.ToString(), userPrivileges =>
            {
                ShaneAuthHelpers.PopulateUserPrivileges(userPrivileges, userData);
            });
        }

        public void ValidateInput(AuthPasswordLoginJwtTabletRequest req)
        {
            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();

            // Validate input

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

            // Validate Password
            if (string.IsNullOrEmpty(req.Password))
            {
                AddError(m => m.Password!, "Password is required.", "error.login.passwordIsRequired");
            }
            /*
            else if (req.Password.Length < 10)
            {
                AddError(m => m.Password!, "Password must be at least 10 characters long.", "error.login.passwordLength|{\"length\":\"10\"}");
            }
            */

            // Validate TotpCode
            if (!string.IsNullOrEmpty(req.TotpCode) && req.TotpCode.Length != 6)
            {
                AddError(m => m.TotpCode!, "Code should be 6 digits long.", "error.login.totpLength|{\"length\":\"6\"}");
            }
        }

        public void ValidateOutput(VerifyCredentialsResult result, UserData? userData)
        {
            switch (result)
            {
                case VerifyCredentialsResult.Ok:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case VerifyCredentialsResult.UserDidNotExist:
                case VerifyCredentialsResult.PasswordInvalid:
                    AddError("Invalid email or password.", "error.login.invalidEmailOrPassword");
                    break;
                case VerifyCredentialsResult.NoAccess:
                    AddError("Your account does not have access to this system.", "error.login.accountHasNoAccess");
                    break;
                case VerifyCredentialsResult.PasswordNotSet:
                    AddError("Your account does not have a password set. Please login using Single Sign On instead.", "error.login.accountNoPasswordSet");
                    break;
                case VerifyCredentialsResult.PasswordLoginLockedOut:
                    AddError("Your account has been locked out after too many failed login attempts. Please wait a few minutes before trying again.", "error.login.passwordLoginLockedOut");
                    break;
                case VerifyCredentialsResult.TotpCodeRequired:
                    AddError(m => m.TotpCode!, "Two-factor authentication code is required.", "error.login.totpCodeRequired");
                    break;
                case VerifyCredentialsResult.TotpLockedOut:
                    AddError("Your account has been locked out after too many failed two-factor authentication code attempts. Please wait a few minutes before trying again.", "error.login.totpLockedOut");
                    break;
                case VerifyCredentialsResult.TotpCodeInvalid:
                    AddError(m => m.TotpCode!, "The two-factor authentication code was invalid.", "error.login.totpCodeInvalid");
                    break;
                case VerifyCredentialsResult.TotpCodeAlreadyUsed:
                    AddError(m => m.TotpCode!, "Successful login has already been made with the specified two-factor authentication code. Please wait until a new code has been generated in your authenticator app before logging in again.", "error.login.totpCodeAlreadyUsed");
                    break;
                case VerifyCredentialsResult.UnknownError:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
