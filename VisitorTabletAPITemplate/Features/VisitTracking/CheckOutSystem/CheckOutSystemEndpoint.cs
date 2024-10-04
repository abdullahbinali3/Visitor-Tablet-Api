using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.VisitTracking.CheckOutSystem;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.CreateBuilding.CheckOutSystem
{
    public sealed class CheckOutSystemEndpoint : Endpoint<CheckOutSystemRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly VisitTrackingRepository _visitTrackingRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public CheckOutSystemEndpoint(AppSettings appSettings,
            VisitTrackingRepository visitTrackingRepository,
            RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _visitTrackingRepository = visitTrackingRepository;
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/VisitTracking/checkout");
            SerializerContext(CheckOutSystemContext.Default);
            Policies("User");
            AllowFileUploads();
        }

        public override async Task HandleAsync(CheckOutSystemRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            var res = await _visitTrackingRepository.Checkout(req);
        }
    }
}
