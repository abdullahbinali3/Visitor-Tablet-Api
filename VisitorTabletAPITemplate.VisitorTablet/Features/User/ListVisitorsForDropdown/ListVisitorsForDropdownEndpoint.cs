using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.User.ListVisitorsForDropdown
{
    public class ListVisitorsForDropdownEndpoint : Endpoint<ListVisitorsForDropdownRequest>
    {
        private readonly UserRepository _UserRepository;
        private readonly AuthCacheService _authCacheService;

        public ListVisitorsForDropdownEndpoint(UserRepository UserRepository,
            AuthCacheService authCacheService)
        {
            _UserRepository = UserRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/visitor/{HostUid}/listForDropdown");
            SerializerContext(ListVisitorsForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListVisitorsForDropdownRequest req, CancellationToken ct)
        {
            // Get logged-in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate user access
            await ValidateInputAsync(req, userId.Value, ct);

            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            var visitors = await _UserRepository.GetVisitorsAsync(req.HostUid, ct);

            if (visitors is null)
            {
                AddError("No visitors found.", "error.visitorTablet.noVisitorsFound");
                await SendErrorsAsync();
                return;
            }

            await SendOkAsync(visitors);
        }

        private async Task<bool> ValidateInputAsync(ListVisitorsForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate HostUid
            if (req.HostUid == null)
            {
                AddError("HostUid Id is required.", "error.HostUidIsRequired");
                return false;
            }
            return true;
        }
    }
}
