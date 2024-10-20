using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignOut
{
    public sealed class SignOutEndpoint : Endpoint<SignOutRequest>
    {
        private readonly VisitorTabletVisitorRepository _VisitorTabletVisitorRepository;

        public SignOutEndpoint(VisitorTabletVisitorRepository VisitorTabletVisitorRepository)
        {
            _VisitorTabletVisitorRepository = VisitorTabletVisitorRepository;
        }

        public override void Configure()
        {
            Put("/visitor/signout");
            SerializerContext(SignOutContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(SignOutRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            if (req.SignOutDateUtc.HasValue)
            {
                req.SignOutDateUtc = req.SignOutDateUtc.Value.ToUniversalTime();  // Convert to UTC if it has a value
            }
            else
            {
                AddError("Failed to cancel or truncate visit.", "error.valueNotExist");
                await SendErrorsAsync();
            }

            // Loop through Uids and try updating the SignOutDateUtc for each
            foreach (var uid in req.Uid)
            {
                
                // Determine if the visit needs to be cancelled or truncated
                var currentTimeUtc = req.SignOutDateUtc;
                var currentTimeLocal = DateTime.Now;  // Assuming the server's local time is the visit's local time

                var visitUpdateResult = await _VisitorTabletVisitorRepository.CancelOrTruncateVisitAsync(req.WorkplaceVisitId, currentTimeUtc, currentTimeLocal);

                if (visitUpdateResult != SqlQueryResult.Ok)
                {
                    AddError("Failed to cancel or truncate visit.", "error.updateVisitFailed");
                    await SendErrorsAsync();
                    return;
                }
            }

            await SendOkAsync(true);
        }

        private void ValidateInput(SignOutRequest req)
        {
            if (req.WorkplaceVisitId == Guid.Empty)
            {
                AddError(m => m.WorkplaceVisitId, "WorkplaceVisitId is required.", "error.workplaceVisitIdRequired");
            }

            if (req.Uid == null || !req.Uid.Any())
            {
                AddError(m => m.Uid, "At least one Uid is required.", "error.uidsRequired");
            }

            if (!req.SignOutDateUtc.HasValue)
            {
                AddError(m => m.SignOutDateUtc, "SignOutDateUtc is required.", "error.signOutDateRequired");
            }
        }
    }
}
