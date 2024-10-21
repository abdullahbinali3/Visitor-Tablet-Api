using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignIn
{
    public sealed class UpdateWorkplaceVisitUserSignInEndpoint : Endpoint<UpdateWorkplaceVisitUserSignInRequest>
    {
        private readonly VisitorTabletWorkplaceVisitUserJoinRepository _WorkplaceVisitUserJoinRepository;

        public UpdateWorkplaceVisitUserSignInEndpoint(VisitorTabletWorkplaceVisitUserJoinRepository WorkplaceVisitUserJoinRepository)
        {
            _WorkplaceVisitUserJoinRepository = WorkplaceVisitUserJoinRepository;
        }

        public override void Configure()
        {
            Put("/visitor/signin");
            SerializerContext(UpdateWorkplaceVisitUserSignInContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateWorkplaceVisitUserSignInRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Loop through Uids and try updating the SignInDateUtc for each
            foreach (var uid in req.Uid)
            {
                var result = await _WorkplaceVisitUserJoinRepository.SignInAsync(uid, req);

                switch (result)
                {
                    case SqlQueryResult.Ok:
                        continue;  // Continue with next Uid
                    case SqlQueryResult.RecordDidNotExist:
                        AddError($"The specified WorkplaceVisitId and Uid combination does not exist: {uid}", "error.recordNotFound");
                        await SendErrorsAsync();
                        return;
                    case SqlQueryResult.UnknownError:
                    default:
                        AddError("Failed to sign in.", "error.signInFailed");
                        await SendErrorsAsync();
                        return;
                }
            }

            await SendOkAsync(true);
        }

        private void ValidateInput(UpdateWorkplaceVisitUserSignInRequest req)
        {
            if (req.HostUid == Guid.Empty)
            {
                AddError(m => m.HostUid, "HostUid is required.", "error.HostUidRequired");
            }

            if (req.Uid == null || !req.Uid.Any())
            {
                AddError(m => m.Uid, "At least one Uid is required.", "error.uidsRequired");
            }

            if (req.SignInDate == null)
            {
                AddError(m => m.SignInDate, "SignInDateUtc is required.", "error.signInDateRequired");
            }
        }
    }
}
