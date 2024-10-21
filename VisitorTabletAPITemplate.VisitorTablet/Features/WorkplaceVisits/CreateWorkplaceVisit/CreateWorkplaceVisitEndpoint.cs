using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisits.CreateWorkplaceVisit
{
    public sealed class CreateWorkplaceVisitEndpoint : Endpoint<CreateWorkplaceVisitRequest>
    {
        private readonly VisitorTabletWorkplaceVisitsRepository _WorkplaceVisitsRepository;

        public CreateWorkplaceVisitEndpoint(VisitorTabletWorkplaceVisitsRepository WorkplaceVisitsRepository)
        {
            _WorkplaceVisitsRepository = WorkplaceVisitsRepository;
        }

        public override void Configure()
        {
            Post("/visit/register");
            SerializerContext(CreateWorkplaceVisitContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(CreateWorkplaceVisitRequest req, CancellationToken ct)
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
            var result = await _WorkplaceVisitsRepository.InsertVisitorAsync(req, userId, adminUserDisplayName, remoteIpAddress);

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

        private void ValidateInput(CreateWorkplaceVisitRequest req)
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

        private bool IsRequestBodyEmpty(CreateWorkplaceVisitRequest req)
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
