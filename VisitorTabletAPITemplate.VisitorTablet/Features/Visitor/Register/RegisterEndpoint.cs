using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Register
{
    public sealed class RegisterEndpoint : Endpoint<RegisterRequest>
    {
        private readonly VisitorTabletVisitorRepository _VisitorTabletVisitorRepository;

        public RegisterEndpoint(VisitorTabletVisitorRepository VisitorTabletVisitorRepository)
        {
            _VisitorTabletVisitorRepository = VisitorTabletVisitorRepository;
        }

        public override void Configure()
        {
            Post("/visitor/register");
            SerializerContext(RegisterContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
        {
            // Get logged-in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }
            
                // Validate input
            ValidateInput(req);
            
            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Set InsertDateUtc for the request
           
            req.InsertDateUtc = DateTime.UtcNow; // Set the current UTC time

            // Insert data into the database
            var result = await _VisitorTabletVisitorRepository.InsertVisitorAsync(req, req.FormCompletedByUid, adminUserDisplayName, HttpContext.Connection.RemoteIpAddress?.ToString());

            // Handle the result of the insertion
            if (result == SqlQueryResult.Ok)
            {
                await SendOkAsync();
            }
            else
            {
                AddError("An error occurred while registering the visitor.");
                await SendErrorsAsync();
            }
        }

        private void ValidateInput(RegisterRequest req)
        {
            foreach (var user in req.Users)
            {
                if (string.IsNullOrWhiteSpace(user.FirstName))
                {
                    AddError("FirstName", "First name is required.", "error.firstNameRequired");
                }

                if (string.IsNullOrWhiteSpace(user.Surname))
                {
                    AddError("Surname", "Surname is required.", "error.surnameRequired");
                }

                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    AddError("Email", "A valid email address is required.", "error.emailInvalid");
                }
            }
        }

        private void AddError(string v1, string v2, string v3)
        {
            throw new NotImplementedException();
        }
    }
}
