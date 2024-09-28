using System;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.SsoCompleteMobile
{
    public sealed class SsoCompleteMobileEndpoint : Endpoint<SsoCompleteMobileRequest>
    {
        public SsoCompleteMobileEndpoint() { }

        public override void Configure()
        {
            Get("/auth/ssoComplete/mobile");
            SerializerContext(SsoCompleteMobileContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(SsoCompleteMobileRequest req, CancellationToken ct)
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
            // string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Get authorization code and state
            string authorizationCode = req.Code!;
            string authorizationState = req.State!;

            // Get organizationId and platform
            string organizationId = authorizationState.Split(':')[0];
            string platform = authorizationState.Split(':')[1];

            // build baseUri and path based on platform
            string baseUri = "";
            string path = "/sso-complete";

            switch (platform)
            {
                case "ios":
                    baseUri = "smartspaceprorebuildios://";
                    break;
                case "android":
                    baseUri = "smartspaceprorebuildandroid://";
                    break;
                case "dev":
                    baseUri = "http://localhost:5000";
                    break;
                default:
                    break;
            }

            // Validate result
            ValidateOutput(baseUri);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // build redirect uri
            string redirectUri = CreateRedirectUri(baseUri, path, authorizationCode, organizationId);

            // set isPermanent to true to perform 301 redirect
            // set allowRemoteRedirects to true to allow to redirect to remote address
            await SendRedirectAsync(redirectUri, true, true);
        }

        private string CreateRedirectUri(string baseUri, string path, string authorizationCode, string organizationId)
        {
            return $"{baseUri}{path}?code={Uri.EscapeDataString(authorizationCode)}&state={Uri.EscapeDataString(organizationId)}";
        }

        public void ValidateInput(SsoCompleteMobileRequest req)
        {
            // Validate input

            // Validate Code
            if (string.IsNullOrEmpty(req.Code))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("Code does not exist in the link.", "error.ssoComplete.codeDoesNotExist");
                return;
            }

            // Validate State
            if (string.IsNullOrEmpty(req.State))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("State does not exist in the link.", "error.ssoComplete.stateDoesNotExist");
            }
            else 
            {
                // Try to get organiztionId and platform 
                string[] stateSplit = req.State.Split(':');
                
                if (stateSplit.Length != 2)
                {
                    HttpContext.Items.Add("FatalError", true);
                    AddError("State does not contain exact 2 elements split by a \":\".", "error.ssoComplete.stateDoesNotContainExact2Elements");
                }
            }
        }

        private void ValidateOutput(string baseUri)
        {
            // Validate result

            // Validate baseUri
            if (string.IsNullOrEmpty(baseUri))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("Base Uri is still empty after parsing the link.", "error.ssoComplete.baseUriIsEmpty");
                return;
            }
        }
    }
}
