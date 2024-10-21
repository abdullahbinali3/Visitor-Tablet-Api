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

            // Loop through Uids and try updating the SignOutDateUtc for each
            foreach (var uid in req.Uid)
            {
              
                var visitUpdateResult = await _VisitorTabletVisitorRepository.CancelOrTruncateVisitAsync(req, uid);

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
            if (req.HostUid == Guid.Empty)
            {
                AddError(m => m.HostUid, "HostUid is required.", "error.HostUid");
            }

            if (req.Uid == null || !req.Uid.Any())
            {
                AddError(m => m.Uid, "At least one Uid is required.", "error.uidsRequired");
            }

            if ( req.SignOutDate == null)
            {
                AddError(m => m.SignOutDate, "SignOutDateUtc is required.", "error.signOutDateRequired");
            }
        }
    }
}
