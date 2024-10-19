using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.RegisterVisitor
{
    public sealed class RegisterVisitorEndpoint : Endpoint<RegisterVisitorRequest>
    {
        private readonly UsersRepository _usersRepository;

        public RegisterVisitorEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/user/registerVisit");
            SerializerContext(RegisterVisitorContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(RegisterVisitorRequest req, CancellationToken ct)
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
            var result = await _usersRepository.InsertVisitorAsync(req, req.Uid, adminUserDisplayName, HttpContext.Connection.RemoteIpAddress?.ToString());

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


        private void ValidateInput(RegisterVisitorRequest req)
        {
            // Implement your validation logic here, e.g., checking for null or invalid values
            // For example:
            if (string.IsNullOrWhiteSpace(req.FirstName))
            {
                AddError(m => m.FirstName, "First name is required.", "error.firstNameRequired");
            }

            if (string.IsNullOrWhiteSpace(req.Surname))
            {
                AddError(m => m.Surname, "Surname is required.", "error.surnameRequired");
            }

            if (string.IsNullOrWhiteSpace(req.Email))
            {
                AddError(m => m.Email, "A valid email address is required.", "error.emailInvalid");
            }

            // Add additional validation as necessary
        }

    }
}
