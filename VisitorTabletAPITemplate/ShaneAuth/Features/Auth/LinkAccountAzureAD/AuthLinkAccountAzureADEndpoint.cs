using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LinkAccountAzureAD
{
    public sealed class AuthLinkAccountAzureADEndpoint : Endpoint<AuthLinkAccountAzureADRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthLinkAccountAzureADEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/linkAccountAzureAD/complete");
            SerializerContext(AuthLinkAccountAzureADContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthLinkAccountAzureADRequest req, CancellationToken ct)
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

            // Disable two-factor authentication
            UserLinkAccountResult queryResult = await _usersRepository.CompleteLinkAccountAzureADTokenAsync(req.Uid!.Value, req.Token!, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        public void ValidateInput(AuthLinkAccountAzureADRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The confirmation link you followed was either invalid or expired.", "error.linkAccountAzureAD.confirmTokenInvalid");
                return;
            }

            // Validate Token
            if (string.IsNullOrEmpty(req.Token))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The confirmation link you followed was either invalid or expired.", "error.linkAccountAzureAD.confirmTokenInvalid");
            }
        }

        private void ValidateOutput(UserLinkAccountResult queryResult)
        {
            switch (queryResult)
            {
                case UserLinkAccountResult.Ok:
                    return;
                case UserLinkAccountResult.SingleSignOnNotEnabled:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("Your Azure AD account could not be linked with your Smart Space Pro account as Single Sign On is not enabled for your organization.", "error.linkAccountAzureAD.singleSignOnNotEnabled");
                    break;
                case UserLinkAccountResult.UserInvalid:
                case UserLinkAccountResult.AccountAlreadyLinked:
                case UserLinkAccountResult.LinkAccountTokenInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The confirmation link you followed was either invalid or expired.", "error.linkAccountAzureAD.confirmTokenInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
