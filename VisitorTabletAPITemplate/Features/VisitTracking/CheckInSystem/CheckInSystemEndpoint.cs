using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.VisitTracking.CheckInSystem
{
    public sealed class CheckInSystemEndpoint : Endpoint<CheckInSystemRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly VisitTrackingRepository _visitTrackingRepository;
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public CheckInSystemEndpoint(AppSettings appSettings,
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
            Post("/VisitTracking/checkin");
            SerializerContext(CheckInSystemContext.Default);
            Policies("User");
            AllowFileUploads();
        }

        public override async Task HandleAsync(CheckInSystemRequest req, CancellationToken ct)
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

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            var res = await _visitTrackingRepository.Checkin(req);
        }
    }
}
