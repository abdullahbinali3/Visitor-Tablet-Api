using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.Data;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.VisitTracking.CheckInSystem;
using VisitorTabletAPITemplate.Features.VisitTracking.CheckOutSystem;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Repositories
{
    public sealed class VisitTrackingRepository
    {
        private readonly AppSettings _appSettings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AuthCacheService _authCacheService;

        public VisitTrackingRepository(AppSettings appSettings,
            ImageStorageRepository imageStorageRepository,
            IHttpClientFactory httpClientFactory,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _httpClientFactory = httpClientFactory;
            _authCacheService = authCacheService;
        }

        public async Task<int> Checkin(CheckInSystemRequest model, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                string sql = $@"
INSERT INTO tblVisit (Id, HostId, VisitorId, CheckinTime, CheckinSource, InsertDateUtc, Disabled, Deleted)
VALUES ( @Id, @HostId, @VisitorId, @CheckinTime, @CheckinSource, @InsertDateUtc, @Disabled, @Deleted )
";
                parameters.Add("@Id", Guid.NewGuid());
                parameters.Add("@HostId", model.HostId);
                parameters.Add("@VisitorId", model.HostId);
                parameters.Add("@CheckinTime", DateTime.UtcNow);
                parameters.Add("@CheckinSource", model.CheckinSource);
                parameters.Add("@InsertDateUtc", DateTime.UtcNow);
                parameters.Add("@Disabled", model.Disabled);
                parameters.Add("@Deleted", model.Deleted);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                return resultCode;
            }
        }

        public async Task<int> Checkout(CheckOutSystemRequest model, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                string sql = $@"
Update tblVisit
SET CheckoutTime = @CheckoutTime,
SET CheckoutSource = @CheckoutSource
WHERE Id = @Id
";
                parameters.Add("@Id", model.Id);
                parameters.Add("@CheckoutTime", model.HostId);
                parameters.Add("@CheckoutSource", model.HostId);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                return resultCode;
            }
        }
    }
}
