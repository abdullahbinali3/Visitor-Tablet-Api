using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Register
{
    public sealed class RegisterEndpoint : Endpoint<RegisterRequest>
    {
        private readonly TabletVisitRepository _VisitorTabletVisitorRepository;

        public RegisterEndpoint(TabletVisitRepository VisitorTabletVisitorRepository)
        {
            _VisitorTabletVisitorRepository = VisitorTabletVisitorRepository;
        }

        public override void Configure()
        {
            Post("/visit/register");
            SerializerContext(RegisterContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
        {
            if (IsRequestBodyEmpty(req))
            {
                AddError("Request body is empty or contains default values.");
                await SendErrorsAsync();
            }
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
            var remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Insert data into the database
            var result = await _VisitorTabletVisitorRepository.InsertVisitorAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Handle the result of the insertion
            if (result == SqlQueryResult.Ok)
            {
                await SendOkAsync();
            }
            else
            {
                AddError("Please fill in the correct request body.");
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

        private bool IsRequestBodyEmpty(RegisterRequest req)
        {
            return req.BuildingId == Guid.Empty &&
                   req.OrganizationId == Guid.Empty &&
                   req.HostUid == Guid.Empty &&
                   string.IsNullOrWhiteSpace(req.Purpose) &&
                   string.IsNullOrWhiteSpace(req.Company) &&
                   req.StartDate == default(DateTime) &&
                   req.EndDate == default(DateTime) &&
                   (req.Users == null || !req.Users.Any());
        }

        private void AddError(string v1, string v2, string v3)
        {
            throw new NotImplementedException();
        }
    }
}
