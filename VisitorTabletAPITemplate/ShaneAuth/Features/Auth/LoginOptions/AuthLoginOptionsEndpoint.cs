using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LoginOptions
{
    public sealed class AuthLoginOptionsEndpoint : Endpoint<AuthLoginOptionsRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthLoginOptionsEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/loginOptions");
            SerializerContext(AuthLoginOptionsContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthLoginOptionsRequest req, CancellationToken ct)
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
            Models.LoginOptions loginOptions = await _usersRepository.GetLoginOptionsAsync(req.Email!, ct);

            await SendAsync(loginOptions);
        }

        public void ValidateInput(AuthLoginOptionsRequest req)
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
        }
    }
}
