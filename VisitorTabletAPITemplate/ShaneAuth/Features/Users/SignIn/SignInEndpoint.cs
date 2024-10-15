using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.SignIn
{
    
    public sealed class SignInEndpoint : Endpoint<SignInRequest>
    {
        private readonly UsersRepository _UsersRepository;

        public SignInEndpoint(UsersRepository usersRepository)
        {
            _UsersRepository = usersRepository;
        }

        public override void Configure()
        {
            Put("/users/signin");
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

            // Try updating the SignInDateUtc
            var result = await _UsersRepository.SignInAsync(req.WorkplaceVisitId, req.Uid, req.SignInDateUtc);

            switch (result)
            {
                case SqlQueryResult.Ok:
                    await SendOkAsync();
                    break;
                case SqlQueryResult.RecordDidNotExist:
                    AddError("The specified WorkplaceVisitId and Uid combination does not exist.", "error.recordNotFound");
                    await SendErrorsAsync();
                    break;
                case SqlQueryResult.UnknownError:
                default:
                    AddError("Failed to sign in.", "error.signInFailed");
                    await SendErrorsAsync();
                    break;
            }
        }

        private void ValidateInput(SignInRequest req)
        {
            if (req.WorkplaceVisitId == Guid.Empty)
            {
                AddError(m => m.WorkplaceVisitId, "WorkplaceVisitId is required.", "error.workplaceVisitIdRequired");
            }

            if (req.Uid == Guid.Empty)
            {
                AddError(m => m.Uid, "Uid is required.", "error.uidRequired");
            }

            if (!req.SignInDateUtc.HasValue)
            {
                AddError(m => m.SignInDateUtc, "SignInDateUtc is required.", "error.signInDateRequired");
            }
        }
    }
}
