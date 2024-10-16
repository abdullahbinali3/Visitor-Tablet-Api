using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignIn
{
    public sealed class SignInEndpoint : Endpoint<SignInRequest>
    {
        private readonly VisitorTabletVisitorRepository _VisitorTabletVisitorRepository;

        public SignInEndpoint(VisitorTabletVisitorRepository VisitorTabletVisitorRepository)
        {
            _VisitorTabletVisitorRepository = VisitorTabletVisitorRepository;
        }

        public override void Configure()
        {
            Put("/visitor/signin");
            SerializerContext(SignInContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(SignInRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            if (req.SignInDateUtc.HasValue)
            {
                req.SignInDateUtc = req.SignInDateUtc.Value.ToUniversalTime();  // Convert to UTC if it has a value
            }

            // Loop through Uids and try updating the SignInDateUtc for each
            foreach (var uid in req.Uid)
            {
                var result = await _VisitorTabletVisitorRepository.SignInAsync(req.WorkplaceVisitId, uid, req.SignInDateUtc);

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

            await SendOkAsync();
        }

        private void ValidateInput(SignInRequest req)
        {
            if (req.WorkplaceVisitId == Guid.Empty)
            {
                AddError(m => m.WorkplaceVisitId, "WorkplaceVisitId is required.", "error.workplaceVisitIdRequired");
            }

            if (req.Uid == null || !req.Uid.Any())
            {
                AddError(m => m.Uid, "At least one Uid is required.", "error.uidsRequired");
            }

            if (!req.SignInDateUtc.HasValue)
            {
                AddError(m => m.SignInDateUtc, "SignInDateUtc is required.", "error.signInDateRequired");
            }
        }
    }
}
