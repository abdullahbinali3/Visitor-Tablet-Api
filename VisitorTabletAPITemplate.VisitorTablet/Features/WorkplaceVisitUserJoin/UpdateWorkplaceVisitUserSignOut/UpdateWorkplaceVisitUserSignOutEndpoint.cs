using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignOut
{
    public sealed class UpdateWorkplaceVisitUserSignOutEndpoint : Endpoint<UpdateWorkplaceVisitUserSignOutRequest>
    {
        private readonly VisitorTabletWorkplaceVisitUserJoinRepository _WorkplaceVisitUserJoinRepository;

        public UpdateWorkplaceVisitUserSignOutEndpoint(VisitorTabletWorkplaceVisitUserJoinRepository WorkplaceVisitUserJoinRepository)
        {
            _WorkplaceVisitUserJoinRepository = WorkplaceVisitUserJoinRepository;
        }

        public override void Configure()
        {
            Put("/visitor/signout");
            SerializerContext(UpdateWorkplaceVisitUserSignOutContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateWorkplaceVisitUserSignOutRequest req, CancellationToken ct)
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

                var visitUpdateResult = await _WorkplaceVisitUserJoinRepository.SignOutAsync(req, uid);

                if (visitUpdateResult != SqlQueryResult.Ok)
                {
                    AddError("Failed to cancel or truncate visit.", "error.updateVisitFailed");
                    await SendErrorsAsync();
                    return;
                }
            }

            await SendOkAsync(true);
        }

        private void ValidateInput(UpdateWorkplaceVisitUserSignOutRequest req)
        {
            if (req.HostUid == Guid.Empty)
            {
                AddError(m => m.HostUid, "HostUid is required.", "error.HostUid");
            }

            if (req.Uid == null || !req.Uid.Any())
            {
                AddError(m => m.Uid, "At least one Uid is required.", "error.uidsRequired");
            }

            if (req.SignOutDate == null)
            {
                AddError(m => m.SignOutDate, "SignOutDateUtc is required.", "error.signOutDateRequired");
            }
        }
    }
}
