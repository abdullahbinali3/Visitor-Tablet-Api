using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Get
{
    public class GetVisitorsEndpoint : Endpoint<GetVisitorsRequest>
    {
        private readonly GetVisitorsRepository _GetVisitorsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetVisitorsEndpoint(GetVisitorsRepository GetVisitorsRepository,
            AuthCacheService authCacheService)
        {
            _GetVisitorsRepository = GetVisitorsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/visitorTablet/getVisitors/{HostUid}");
            SerializerContext(GetVisitorsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetVisitorsRequest req, CancellationToken ct)
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
            var visitors = await _GetVisitorsRepository.GetVisitorsAsync(req.HostUid, ct);

            if (visitors is null)
            {
                AddError("No visitors found.", "error.visitorTablet.noVisitorsFound");
                await SendErrorsAsync();
                return;
            }

            await SendOkAsync(visitors);
        }

        private async Task<bool> ValidateInputAsync(GetVisitorsRequest req, Guid userId, CancellationToken cancellationToken)
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
