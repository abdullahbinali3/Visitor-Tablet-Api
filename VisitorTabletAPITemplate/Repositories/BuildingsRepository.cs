using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.Buildings.CreateBuilding;
using VisitorTabletAPITemplate.Features.Buildings.DeleteBuilding;
using VisitorTabletAPITemplate.Features.Buildings.UpdateBuilding;
using VisitorTabletAPITemplate.Features.Buildings.UpdateBuildingCheckInQRCode;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Repositories
{
    public sealed class BuildingsRepository
    {
        private readonly AppSettings _appSettings;
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AuthCacheService _authCacheService;

        public BuildingsRepository(AppSettings appSettings,
            ImageStorageRepository imageStorageRepository,
            IHttpClientFactory httpClientFactory,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _imageStorageRepository = imageStorageRepository;
            _httpClientFactory = httpClientFactory;
            _authCacheService = authCacheService;
        }

        /// <summary>
        /// Retrieves a list of buildings to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListBuildingsForDropdownAsync(Guid organizationId, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string whereQuery = "";

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblBuildings",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select id as Value
      ,Name as Text
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
{whereQuery}
order by Name
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// Retrieves a list of buildings filtered by <paramref name="regionId"/> to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="regionId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListBuildingsForRegionForDropdownAsync(Guid organizationId, Guid regionId, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string whereQuery = "";

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblBuildings",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select id as Value
      ,Name as Text
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
and RegionId = @regionId
{whereQuery}
order by Name
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionId", regionId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of buildings to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<Building>> ListBuildingsForDataTableAsync(Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "Name asc";
                        break;
                    case SortType.Updated:
                        sortColumn = "UpdatedDateUtc desc";
                        break;
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "Name asc";
                        break;
                }

                string whereQuery = "";

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblBuildings",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
-- Get total number of buildings in database matching search term
select count(*)
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblBuildings
    where Deleted = 0
    and OrganizationId = @organizationId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      -- ,tblBuildings.FeatureImageStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.FeatureThumbnailUrl
      -- ,tblBuildings.FeatureThumbnailStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.MapImageUrl
      --,tblBuildings.MapImageStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.MapThumbnailUrl
      --,tblBuildings.MapThumbnailStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      --,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc -- NOT USED IN DATA TABLE
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where exists
(
    select 1
    from pg
    where pg.id = tblBuildings.id
)
order by tblBuildings.{sortColumn}
--order by tblBuildings.OrganizationId, tblBuildings.{sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<Building> result = new DataTableResponse<Building>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<Building>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of unallocated user buildings to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="uid"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<Building>> ListBuildingsUnallocatedForUserForDataTableAsync(Guid organizationId, Guid uid, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "Name asc";
                        break;
                    case SortType.Updated:
                        sortColumn = "UpdatedDateUtc desc";
                        break;
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "Name asc";
                        break;
                }

                string whereQuery = "";

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblBuildings",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
-- Get total number of buildings in database matching search term
select count(*)
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
and not exists
(
    select *
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and tblBuildings.id = tblUserBuildingJoin.BuildingId
)
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblBuildings
    where Deleted = 0
    and OrganizationId = @organizationId
    and not exists
    (
        select *
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      -- ,tblBuildings.FeatureImageStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.FeatureThumbnailUrl
      -- ,tblBuildings.FeatureThumbnailStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.MapImageUrl
      --,tblBuildings.MapImageStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.MapThumbnailUrl
      --,tblBuildings.MapThumbnailStorageId -- NOT USED IN DATA TABLE
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      --,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc -- NOT USED IN DATA TABLE
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where exists
(
    select 1
    from pg
    where pg.id = tblBuildings.id
)
order by tblBuildings.{sortColumn}
--order by tblBuildings.OrganizationId, tblBuildings.{sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<Building> result = new DataTableResponse<Building>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<Building>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a list of buildings unallocated to the specified user to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="uid"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListBuildingsUnallocatedToUserForDropdownAsync(Guid organizationId, Guid uid, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string whereQuery = "";

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblBuildings",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select id as Value
      ,Name as Text
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
and not exists
(
    select *
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and tblBuildings.id = tblUserBuildingJoin.BuildingId
)
{whereQuery}
order by Name
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified building from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Building?> GetBuildingAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      ,tblBuildings.FeatureImageStorageId
      ,tblBuildings.FeatureThumbnailUrl
      ,tblBuildings.FeatureThumbnailStorageId
      ,tblBuildings.MapImageUrl
      ,tblBuildings.MapImageStorageId
      ,tblBuildings.MapThumbnailUrl
      ,tblBuildings.MapThumbnailStorageId
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where tblBuildings.Deleted = 0
and tblBuildings.id = @id
and tblBuildings.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<Building>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns a Check-In QR Code image as a PNG image byte array for the specified building.</para>
        /// <para>Returns <see cref="SqlQueryResult.RecordDidNotExist"/> if the building did not exist, or <see cref="SqlQueryResult.SubRecordDidNotExist"/> if the
        /// building exists but it did not have a Check-In QR code set.</para>
        /// <para>The image is returned as long as a CheckInQRCode value is set, even if Check-In has been disabled on the organization or building.</para>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, byte[]? pngImage)> GetBuildingCheckInQRCodePngAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select 1 as BuildingExists
      ,tblBuildings.CheckInQRCode
from tblBuildings
where tblBuildings.Deleted = 0
and tblBuildings.id = @id
and tblBuildings.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                (bool? buildingExists, string? checkInQrCode) = await sqlConnection.QueryFirstOrDefaultAsync<(bool?, string?)>(commandDefinition);

                if (buildingExists is null)
                {
                    return (SqlQueryResult.RecordDidNotExist, null);
                }

                if (string.IsNullOrWhiteSpace(checkInQrCode))
                {
                    return (SqlQueryResult.SubRecordDidNotExist, null);
                }

                // Generate and return QR Code
                return (SqlQueryResult.Ok, QRCodeGeneratorHelpers.GeneratePngQRCode(checkInQrCode));
            }
        }

        /// <summary>
        /// <para>Retrieves the specified building from the database by QR code.</para>
        /// <para>Only return buildings that have check-in setting enabled</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<Building?> GetBuildingByQRCodeAsync(string qrCode, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select top 1 tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      ,tblBuildings.FeatureImageStorageId
      ,tblBuildings.FeatureThumbnailUrl
      ,tblBuildings.FeatureThumbnailStorageId
      ,tblBuildings.MapImageUrl
      ,tblBuildings.MapImageStorageId
      ,tblBuildings.MapThumbnailUrl
      ,tblBuildings.MapThumbnailStorageId
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where tblBuildings.Deleted = 0
and tblBuildings.CheckInEnabled = 1
and tblBuildings.CheckInQRCode = @qrCode
and tblBuildings.OrganizationId = @organizationId
order by tblBuildings.OrganizationId
        ,tblBuildings.CheckInQRCode
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@qrCode", qrCode, DbType.AnsiString, ParameterDirection.Input, 100);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<Building>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns true if the specified building exists.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsBuildingExistsAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblBuildings
    where Deleted = 0
    and id = @id
    and OrganizationId = @organizationId
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Creates a building.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.SubRecordAlreadyExists"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Building?)> CreateBuildingAsync(CreateBuildingRequest request, ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Create Building";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_checkInQRCodeExists bit = 0

-- Check if another building exists with the given check-in QR code in this organization.
-- Note: Not checking for CheckInEnabled = 1 here so that we allow storing a QR code while check-in is disabled.
-- TODO: Also check if QR code has already been assigned to a floor once we add QRCode to tblFloors
if @checkInQRCode is not null
begin
    select top 1 @_checkInQRCodeExists = 1
    from tblBuildings
    where Deleted = 0
    and CheckInQRCode = @checkInQRCode
    and OrganizationId = @organizationId
end

if @_checkInQRCodeExists = 0
begin
    begin transaction

    exec @_lockResult = sp_getapplock
        @Resource = @lockResourceName,
        @LockMode = 'Exclusive',
        @LockOwner = 'Transaction',
        @LockTimeout = 0

    if @_lockResult < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
        insert into tblBuildings
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,Name
        ,OrganizationId
        ,RegionId
        ,Address
        ,Latitude
        ,Longitude
        ,Timezone
        ,FacilitiesManagementEmail
        ,FacilitiesManagementEmailDisplayName
        ,FeatureImageUrl
        ,FeatureImageStorageId
        ,FeatureThumbnailUrl
        ,FeatureThumbnailStorageId
        ,MapImageUrl
        ,MapImageStorageId
        ,MapThumbnailUrl
        ,MapThumbnailStorageId
        ,CheckInEnabled
        ,CheckInQRCode
        ,AccessCardCheckInWithBookingMessage
        ,AccessCardCheckInWithoutBookingMessage
        ,QRCodeCheckInWithBookingMessage
        ,QRCodeCheckInWithoutBookingMessage
        ,CheckInReminderEnabled
        ,CheckInReminderTime
        ,CheckInReminderMessage
        ,AutoUserInactivityEnabled
        ,AutoUserInactivityDurationDays
        ,AutoUserInactivityScheduledIntervalMonths
        ,AutoUserInactivityScheduleStartDateUtc
        ,AutoUserInactivityScheduleLastRunDateUtc
        ,MaxCapacityEnabled
        ,MaxCapacityUsers
        ,MaxCapacityNotificationMessage
        ,DeskBookingReminderEnabled
        ,DeskBookingReminderTime
        ,DeskBookingReminderMessage
        ,DeskBookingReservationDateRangeEnabled
        ,DeskBookingReservationDateRangeForUser
        ,DeskBookingReservationDateRangeForAdmin
        ,DeskBookingReservationDateRangeForSuperAdmin)
        select @id
              ,@_now
              ,@_now
              ,@name
              ,@organizationId
              ,@regionId
              ,@address
              ,@latitude
              ,@longitude
              ,@timezone
              ,@facilitiesManagementEmail
              ,@facilitiesManagementEmailDisplayName
              ,null -- FeatureImageUrl
              ,null -- FeatureImageStorageId
              ,null -- FeatureThumbnailUrl
              ,null -- FeatureThumbnailStorageId
              ,null -- MapImageUrl
              ,null -- MapImageStorageId
              ,null -- MapThumbnailUrl
              ,null -- MapThumbnailStorageId
              ,@checkInEnabled
              ,@checkInQRCode
              ,@accessCardCheckInWithBookingMessage
              ,@accessCardCheckInWithoutBookingMessage
              ,@qrCodeCheckInWithBookingMessage
              ,@qrCodeCheckInWithoutBookingMessage
              ,@checkInReminderEnabled
              ,@checkInReminderTime
              ,@checkInReminderMessage
              ,@autoUserInactivityEnabled
              ,@autoUserInactivityDurationDays
              ,@autoUserInactivityScheduledIntervalMonths
              ,@autoUserInactivityScheduleStartDateUtc
              ,null -- AutoUserInactivityScheduleLastRunDateUtc
              ,@maxCapacityEnabled
              ,@maxCapacityUsers
              ,@maxCapacityNotificationMessage
              ,@deskBookingReminderEnabled
              ,@deskBookingReminderTime
              ,@deskBookingReminderMessage
              ,@deskBookingReservationDateRangeEnabled
              ,@deskBookingReservationDateRangeForUser
              ,@deskBookingReservationDateRangeForAdmin
              ,@deskBookingReservationDateRangeForSuperAdmin
        where not exists
        (
            select *
            from tblBuildings
            where Deleted = 0
            and Name = @name
            and OrganizationId = @organizationId
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1
    
            insert into tblBuildings_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,BuildingId
            ,Name
            ,OrganizationId
            ,RegionId
            ,Address
            ,Latitude
            ,Longitude
            ,Timezone
            ,FacilitiesManagementEmail
            ,FacilitiesManagementEmailDisplayName
            ,FeatureImageUrl
            ,FeatureImageStorageId
            ,FeatureThumbnailUrl
            ,FeatureThumbnailStorageId
            ,MapImageUrl
            ,MapImageStorageId
            ,MapThumbnailUrl
            ,MapThumbnailStorageId
            ,CheckInEnabled
            ,CheckInQRCode
            ,AccessCardCheckInWithBookingMessage
            ,AccessCardCheckInWithoutBookingMessage
            ,QRCodeCheckInWithBookingMessage
            ,QRCodeCheckInWithoutBookingMessage
            ,CheckInReminderEnabled
            ,CheckInReminderTime
            ,CheckInReminderMessage
            ,AutoUserInactivityEnabled
            ,AutoUserInactivityDurationDays
            ,AutoUserInactivityScheduledIntervalMonths
            ,AutoUserInactivityScheduleStartDateUtc
            ,AutoUserInactivityScheduleLastRunDateUtc
            ,MaxCapacityEnabled
            ,MaxCapacityUsers
            ,MaxCapacityNotificationMessage
            ,DeskBookingReminderEnabled
            ,DeskBookingReminderTime
            ,DeskBookingReminderMessage
            ,DeskBookingReservationDateRangeEnabled
            ,DeskBookingReservationDateRangeForUser
            ,DeskBookingReservationDateRangeForAdmin
            ,DeskBookingReservationDateRangeForSuperAdmin
            ,Deleted
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@id
                  ,@name
                  ,@organizationId
                  ,@regionId
                  ,@address
                  ,@latitude
                  ,@longitude
                  ,@timezone
                  ,@facilitiesManagementEmail
                  ,@facilitiesManagementEmailDisplayName
                  ,null -- FeatureImageUrl
                  ,null -- FeatureImageStorageId
                  ,null -- FeatureThumbnailUrl
                  ,null -- FeatureThumbnailStorageId
                  ,null -- MapImageUrl
                  ,null -- MapImageStorageId
                  ,null -- MapThumbnailUrl
                  ,null -- MapThumbnailStorageId
                  ,@checkInEnabled
                  ,@checkInQRCode
                  ,@accessCardCheckInWithBookingMessage
                  ,@accessCardCheckInWithoutBookingMessage
                  ,@qrCodeCheckInWithBookingMessage
                  ,@qrCodeCheckInWithoutBookingMessage
                  ,@checkInReminderEnabled
                  ,@checkInReminderTime
                  ,@checkInReminderMessage
                  ,@autoUserInactivityEnabled
                  ,@autoUserInactivityDurationDays
                  ,@autoUserInactivityScheduledIntervalMonths
                  ,@autoUserInactivityScheduleStartDateUtc
                  ,null -- AutoUserInactivityScheduleLastRunDateUtc
                  ,@maxCapacityEnabled
                  ,@maxCapacityUsers
                  ,@maxCapacityNotificationMessage
                  ,@deskBookingReminderEnabled
                  ,@deskBookingReminderTime
                  ,@deskBookingReminderMessage
                  ,@deskBookingReservationDateRangeEnabled
                  ,@deskBookingReservationDateRangeForUser
                  ,@deskBookingReservationDateRangeForAdmin
                  ,@deskBookingReservationDateRangeForSuperAdmin
                  ,0 -- Deleted
                  ,'Insert' -- LogAction

            -- Insert first function into building
            insert into tblFunctions
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Name
            ,BuildingId
            ,HtmlColor)
            select @functionId
                  ,@_now
                  ,@_now
                  ,@functionName
                  ,@id -- BuildingId
                  ,@functionHtmlColor

            insert into tblFunctions_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,FunctionId
            ,Name
            ,BuildingId
            ,HtmlColor
            ,Deleted
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @functionLogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@functionId
                  ,@functionName
                  ,@id -- BuildingId
                  ,@functionHtmlColor
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert default facilities management request types for building
            declare @_facilitiesManagementRequestTypes table
            (
                id uniqueidentifier
               ,Name nvarchar(100)
            )

            ;with generatedIds as (
                select ids.Name, combs.GeneratedId
                from
                (
                    select ROW_NUMBER() over (order by GeneratedId) as RowNumber, GeneratedId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as GeneratedId
                        from tblFacilitiesManagementRequestTypes_DefaultValues
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by Name) as RowNumber, Name
                    from tblFacilitiesManagementRequestTypes_DefaultValues
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblFacilitiesManagementRequestTypes
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Name
            ,BuildingId)
            output inserted.id
                  ,inserted.Name
                   into @_facilitiesManagementRequestTypes
            select l.GeneratedId
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,tblFacilitiesManagementRequestTypes_DefaultValues.Name
                  ,@id -- BuildingId
            from tblFacilitiesManagementRequestTypes_DefaultValues
            left join generatedIds l
            on tblFacilitiesManagementRequestTypes_DefaultValues.Name = l.Name

            -- Insert to building facilities management request types log
            ;with logIds as (
                select ids.id, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_facilitiesManagementRequestTypes
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by id) as RowNumber, id
                    from @_facilitiesManagementRequestTypes
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblFacilitiesManagementRequestTypes_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,FacilitiesManagementRequestTypeId
            ,BuildingId
            ,Name
            ,Deleted
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select l.LogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,d.id -- FacilitiesManagementRequestTypeId
                  ,@id -- BuildingId
                  ,d.Name
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_facilitiesManagementRequestTypes d
            left join logIds l
            on d.id = l.id

            -- Insert default workplace email template content for building
            declare @_workplaceEmailTemplateContent table
            (
                OrganizationId uniqueidentifier
               ,BuildingId uniqueidentifier
               ,TemplateName varchar(100)
            )

            insert into tblWorkplaceEmailTemplateContent
            (OrganizationId
            ,BuildingId
            ,TemplateName
            ,UpdatedDateUtc
            ,HtmlContent
            ,TextContent)
            output inserted.OrganizationId
                  ,inserted.BuildingId
                  ,inserted.TemplateName
                   into @_workplaceEmailTemplateContent
            select @organizationId
                  ,@id
                  ,tblWorkplaceEmailTemplates.TemplateName
                  ,@_now -- InsertDateUtc
                  ,null -- HtmlContent
                  ,null -- TextContent
            from tblWorkplaceEmailTemplates

            -- Insert to workplace email template content log
            ;with logIds as (
                select ids.TemplateName, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_workplaceEmailTemplateContent
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by TemplateName) as RowNumber, TemplateName
                    from @_workplaceEmailTemplateContent
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblWorkplaceEmailTemplateContent_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,BuildingId
            ,TemplateName
            ,HtmlContent
            ,TextContent
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select l.LogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@id
                  ,d.TemplateName
                  ,null -- HtmlContent
                  ,null -- TextContent
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_workplaceEmailTemplateContent d
            left join logIds l
            on d.TemplateName = l.TemplateName

            -- Insert a new row into tblBuildingHistories for the building we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblBuildingHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,BuildingId
            ,Name
            ,RegionId
            ,StartDateUtc
            ,EndDateUtc)
            select @buildingHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@id -- BuildingId
                  ,@name
                  ,@regionId
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the building history for the new building
            insert into tblBuildingHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,BuildingHistoryId
            ,BuildingId
            ,Name
            ,RegionId
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @buildingHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@buildingHistoryId
                  ,@id -- BuildingId
                  ,@name
                  ,@regionId
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblFunctionHistories for the function which was also just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblFunctionHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,FunctionId
            ,Name
            ,BuildingId
            ,HtmlColor
            ,StartDateUtc
            ,EndDateUtc)
            select @functionHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@functionId -- FunctionId
                  ,@functionName
                  ,@id -- BuildingId
                  ,@functionHtmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the floor history for the new floor
            insert into tblFunctionHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,FunctionHistoryId
            ,FunctionId
            ,Name
            ,BuildingId
            ,HtmlColor
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @functionHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@functionHistoryId
                  ,@functionId -- FunctionId
                  ,@functionName
                  ,@id -- BuildingId
                  ,@functionHtmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId
        end
        else
        begin
            -- Building already exists
            set @_result = 2
        end
    
        commit
    end
end
else
begin
    -- QR Code already exists
    set @_result = 3
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select tblBuildings.id
          ,tblBuildings.InsertDateUtc
          ,tblBuildings.UpdatedDateUtc
          ,tblBuildings.Name
          ,tblBuildings.OrganizationId
          ,tblBuildings.RegionId
          ,tblRegions.Name as RegionName
          ,tblBuildings.Address
          ,tblBuildings.Latitude
          ,tblBuildings.Longitude
          ,tblBuildings.Timezone
          ,tblBuildings.FacilitiesManagementEmail
          ,tblBuildings.FacilitiesManagementEmailDisplayName
          ,tblBuildings.FeatureImageUrl
          ,tblBuildings.FeatureImageStorageId
          ,tblBuildings.FeatureThumbnailUrl
          ,tblBuildings.FeatureThumbnailStorageId
          ,tblBuildings.MapImageUrl
          ,tblBuildings.MapImageStorageId
          ,tblBuildings.MapThumbnailUrl
          ,tblBuildings.MapThumbnailStorageId
          ,tblBuildings.CheckInEnabled
          ,tblBuildings.CheckInQRCode
          ,tblBuildings.AccessCardCheckInWithBookingMessage
          ,tblBuildings.AccessCardCheckInWithoutBookingMessage
          ,tblBuildings.QRCodeCheckInWithBookingMessage
          ,tblBuildings.QRCodeCheckInWithoutBookingMessage
          ,tblBuildings.CheckInReminderEnabled
          ,tblBuildings.CheckInReminderTime
          ,tblBuildings.CheckInReminderMessage
          ,tblBuildings.AutoUserInactivityEnabled
          ,tblBuildings.AutoUserInactivityDurationDays
          ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
          ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
          ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
          ,tblBuildings.MaxCapacityEnabled          
          ,tblBuildings.MaxCapacityUsers
          ,tblBuildings.MaxCapacityNotificationMessage
          ,tblBuildings.DeskBookingReminderEnabled
          ,tblBuildings.DeskBookingReminderTime
          ,tblBuildings.DeskBookingReminderMessage
          ,tblBuildings.DeskBookingReservationDateRangeEnabled
          ,tblBuildings.DeskBookingReservationDateRangeForUser
          ,tblBuildings.DeskBookingReservationDateRangeForAdmin
          ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
          ,tblBuildings.ConcurrencyKey
    from tblBuildings
    left join tblRegions
    on tblBuildings.RegionId = tblRegions.id
    and tblRegions.Deleted = 0
    where tblBuildings.Deleted = 0
    and tblBuildings.id = @id
    and tblBuildings.OrganizationId = @organizationId
end
";
                Guid id = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblBuildingHistories, tblBuildingHistories_Log,
                // tblFunctionHistories and tblFunctionHistories_Log
                Guid buildingHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblBuildings_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@regionId", request.RegionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@address", request.Address, DbType.String, ParameterDirection.Input, 250);
                parameters.Add("@latitude", request.Latitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@longitude", request.Longitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@timezone", request.Timezone, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@facilitiesManagementEmail", request.FacilitiesManagementEmail, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@facilitiesManagementEmailDisplayName", request.FacilitiesManagementEmailDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@checkInEnabled", request.CheckInEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInQRCode", request.CheckInQRCode, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@accessCardCheckInWithBookingMessage", request.AccessCardCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@accessCardCheckInWithoutBookingMessage", request.AccessCardCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@qrCodeCheckInWithBookingMessage", request.QRCodeCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@qrCodeCheckInWithoutBookingMessage", request.QRCodeCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@checkInReminderEnabled", request.CheckInReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInReminderTime", request.CheckInReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@checkInReminderMessage", request.CheckInReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@autoUserInactivityEnabled", request.AutoUserInactivityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityDurationDays", request.AutoUserInactivityDurationDays, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityScheduledIntervalMonths", request.AutoUserInactivityScheduledIntervalMonths, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityScheduleStartDateUtc", request.AutoUserInactivityScheduleStartDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@maxCapacityEnabled", request.MaxCapacityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@maxCapacityUsers", request.MaxCapacityUsers, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@maxCapacityNotificationMessage", request.MaxCapacityNotificationMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@deskBookingReminderEnabled", request.DeskBookingReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@deskBookingReminderTime", request.DeskBookingReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@deskBookingReminderMessage", request.DeskBookingReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@deskBookingReservationDateRangeEnabled", request.DeskBookingReservationDateRangeEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForUser", request.DeskBookingReservationDateRangeForUser, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForAdmin", request.DeskBookingReservationDateRangeForAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForSuperAdmin", request.DeskBookingReservationDateRangeForSuperAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@functionId", functionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionName", request.FunctionName, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@functionHtmlColor", request.FunctionHtmlColor, DbType.AnsiString, ParameterDirection.Input, 7);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@buildingHistoryId", buildingHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingHistoryLogId", buildingHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryId", functionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryLogId", functionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionLogId", functionLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Building? data = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    data = await gridReader.ReadFirstOrDefaultAsync<Building>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        if (data is not null)
                        {
                            // Update FeatureImage for created Building
                            if (request.FeatureImage is not null)
                            {
                                // Store building feature image file to disk
                                (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) = await StoreBuildingFeatureImageAsync(sqlConnection,
                                    featureImageContentInspectorResult, id, logId, request.OrganizationId!.Value, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                {
                                    // Set image URLs and storage IDs in response to be returned.
                                    data.FeatureImageUrl = storedImageFile.FileUrl;
                                    data.FeatureImageStorageId = storedImageFile.Id;
                                    data.FeatureThumbnailUrl = thumbnailFile.FileUrl;
                                    data.FeatureThumbnailStorageId = thumbnailFile.Id;
                                }
                                else
                                {
                                    queryResult = storeImageResult;
                                    break;
                                }
                            }

                            /*
                            // Download map image using google maps API
                            ContentInspectorResultWithMemoryStream? mapImageContentInspectorResult;
                            using MemoryStream memoryStream = new MemoryStream();

                            HttpClient httpClient = _httpClientFactory.CreateClient("googlemaps");

                            // Example URL: https://maps.googleapis.com/maps/api/staticmap?size=640x320&scale=1&center=-37.8227503,144.968869&format=png&maptype=roadmap&key=AIzaxxx&markers=color:red%7C-37.8227503,144.968869

                            Dictionary<string, StringValues> queryParameters = new Dictionary<string, StringValues>
                            {
                                { "size", new StringValues($"{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageWidth}x{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageHeight}") },
                                { "scale", new StringValues("1") },
                                { "center", new StringValues($"{request.Latitude:0.0000000},{request.Longitude:0.0000000}") },
                                { "format", new StringValues($"png") },
                                { "maptype", new StringValues($"roadmap") },
                                { "key", new StringValues(_appSettings.GoogleMaps.GoogleMapsApiKey) },
                                { "markers", new StringValues($"color:red|{request.Latitude:0.0000000},{request.Longitude:0.0000000}") },
                            };

                            string staticMapRequestUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{httpClient.BaseAddress}maps/api/staticmap", queryParameters);

                            using (HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, staticMapRequestUrl))
                            {
                                using (HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                                {
                                    if (!httpResponseMessage.IsSuccessStatusCode)
                                    {
                                        // For create, if retrieving map image not successful, just ignore it
                                        // as the map url will be null in database anyway.
                                    }
                                    else
                                    {
                                        await httpResponseMessage.Content.CopyToAsync(memoryStream);

                                        mapImageContentInspectorResult = new ContentInspectorResultWithMemoryStream
                                        {
                                            OriginalExtension = "png",
                                            InspectedExtension = "png",
                                            InspectedMimeType = "image/png",
                                            IsSanitized = false,
                                            FileName = null,
                                            FileDataStream = memoryStream,
                                        };

                                        // Store building Map image file to disk
                                        (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                            await StoreMapImageAsync(sqlConnection,
                                            mapImageContentInspectorResult,
                                            id, logId, request.OrganizationId!.Value, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                        if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                        {
                                            // Set image URLs and storage IDs in response to be returned.
                                            data.MapImageUrl = storedImageFile.FileUrl;
                                            data.MapImageStorageId = storedImageFile.Id;
                                            data.MapThumbnailUrl = thumbnailFile.FileUrl;
                                            data.MapThumbnailStorageId = thumbnailFile.Id;
                                        }
                                        else
                                        {
                                            queryResult = storeImageResult;
                                            break;
                                        }
                                    }
                                }
                            }
                            */
                        }
                        break;
                    case 2:
                        // Building name already exists
                        queryResult = SqlQueryResult.RecordAlreadyExists;
                        break;
                    case 3:
                        // Check-in QR Code already exists
                        queryResult = SqlQueryResult.SubRecordAlreadyExists;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        private async Task<(SqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile)> StoreMapImageAsync(SqlConnection sqlConnection,
            ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult,
            Guid buildingId, Guid logId, Guid organizationId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                await _imageStorageRepository.WriteImageAndThumbnailAsync(
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageWidth,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageHeight,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.ThumbnailMaxImageWidth,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.ThumbnailMaxImageHeight,
                    true,
                    false,
                    featureImageContentInspectorResult,
                    ImageStorageRelatedObjectType.SSBuildingMapImage,
                    ImageStorageRelatedObjectType.SSBuildingMapImageThumbnail,
                    buildingId,
                    organizationId,
                    "tblBuildings",
                    logId,
                    adminUserUid,
                    adminUserDisplayName,
                    remoteIpAddress,
                    "png"); // SaveFileType

            if (sqlQueryResult != SqlQueryResult.Ok)
            {
                return (sqlQueryResult, storedImageFile, thumbnailFile);
            }
            else if (storedImageFile is null || thumbnailFile is null)
            {
                // This should never happen
                return (SqlQueryResult.UnknownError, storedImageFile, thumbnailFile);
            }

            string sql = @"
update tblBuildings
set MapImageUrl = @fileUrl
   ,MapImageStorageId = @imageStorageId
   ,MapThumbnailUrl = @thumbnailUrl
   ,MapThumbnailStorageId = @thumbnailStorageId
where Deleted = 0
and id = @buildingId

update tblBuildings_Log
set MapImageUrl = @fileUrl
   ,MapImageStorageId = @imageStorageId
   ,MapThumbnailUrl = @thumbnailUrl
   ,MapThumbnailStorageId = @thumbnailStorageId
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@fileUrl", storedImageFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@imageStorageId", storedImageFile.Id, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@thumbnailUrl", thumbnailFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@thumbnailStorageId", thumbnailFile.Id, DbType.Guid, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);

            return (sqlQueryResult, storedImageFile, thumbnailFile);
        }

        private async Task ClearMapImageAsync(SqlConnection sqlConnection, Guid buildingId, Guid logId)
        {
            string sql = @"
update tblBuildings
set MapImageUrl = null
   ,MapImageStorageId = null
   ,MapThumbnailUrl = null
   ,MapThumbnailStorageId = null
where Deleted = 0
and id = @buildingId

update tblBuildings_Log
set MapImageUrl = null
   ,MapImageStorageId = null
   ,MapThumbnailUrl = null
   ,MapThumbnailStorageId = null
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);
        }

        /// <summary>
        /// <para>Updates the specified building.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Building?)> UpdateBuildingAsync(UpdateBuildingRequest request, 
            ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Building";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_checkInQRCodeExists bit = 0

declare @_data table
(
    Name nvarchar(100)
   ,RegionId uniqueidentifier
   ,Address nvarchar(250)
   ,Latitude decimal(10, 7)
   ,Longitude decimal(10, 7)
   ,Timezone varchar(50)
   ,FacilitiesManagementEmail nvarchar(254)
   ,FacilitiesManagementEmailDisplayName nvarchar(151)
   ,FeatureImageUrl varchar(255)
   ,FeatureImageStorageId uniqueidentifier
   ,FeatureThumbnailUrl varchar(255)
   ,FeatureThumbnailStorageId uniqueidentifier
   ,MapImageUrl varchar(255)
   ,MapImageStorageId uniqueidentifier
   ,MapThumbnailUrl varchar(255)
   ,MapThumbnailStorageId uniqueidentifier
   ,CheckInEnabled bit
   ,CheckInQRCode nvarchar(100)
   ,AccessCardCheckInWithBookingMessage nvarchar(2000)
   ,AccessCardCheckInWithoutBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithoutBookingMessage nvarchar(2000)
   ,CheckInReminderEnabled bit
   ,CheckInReminderTime time(0)
   ,CheckInReminderMessage nvarchar(2000)
   ,AutoUserInactivityEnabled bit
   ,AutoUserInactivityDurationDays int
   ,AutoUserInactivityScheduledIntervalMonths int
   ,AutoUserInactivityScheduleStartDateUtc datetime2(3)
   ,AutoUserInactivityScheduleLastRunDateUtc datetime2(3)
   ,MaxCapacityEnabled bit 
   ,MaxCapacityUsers int
   ,MaxCapacityNotificationMessage nvarchar(2000)
   ,DeskBookingReminderEnabled bit
   ,DeskBookingReminderTime time(0)
   ,DeskBookingReminderMessage nvarchar(2000)
   ,DeskBookingReservationDateRangeEnabled bit
   ,DeskBookingReservationDateRangeForUser int
   ,DeskBookingReservationDateRangeForAdmin int
   ,DeskBookingReservationDateRangeForSuperAdmin int
   ,OldName nvarchar(100)
   ,OldRegionId uniqueidentifier
   ,OldAddress nvarchar(250)
   ,OldLatitude decimal(10, 7)
   ,OldLongitude decimal(10, 7)
   ,OldTimezone varchar(50)
   ,OldFacilitiesManagementEmail nvarchar(254)
   ,OldFacilitiesManagementEmailDisplayName nvarchar(151)
   ,OldFeatureImageUrl varchar(255)
   ,OldFeatureImageStorageId uniqueidentifier
   ,OldFeatureThumbnailUrl varchar(255)
   ,OldFeatureThumbnailStorageId uniqueidentifier
   ,OldMapImageUrl varchar(255)
   ,OldMapImageStorageId uniqueidentifier
   ,OldMapThumbnailUrl varchar(255)
   ,OldMapThumbnailStorageId uniqueidentifier
   ,OldCheckInEnabled bit
   ,OldCheckInQRCode nvarchar(100)
   ,OldAccessCardCheckInWithBookingMessage nvarchar(2000)
   ,OldAccessCardCheckInWithoutBookingMessage nvarchar(2000)
   ,OldQRCodeCheckInWithBookingMessage nvarchar(2000)
   ,OldQRCodeCheckInWithoutBookingMessage nvarchar(2000)
   ,OldCheckInReminderEnabled bit
   ,OldCheckInReminderTime time(0)
   ,OldCheckInReminderMessage nvarchar(2000)
   ,OldAutoUserInactivityEnabled bit
   ,OldAutoUserInactivityDurationDays int
   ,OldAutoUserInactivityScheduledIntervalMonths int
   ,OldAutoUserInactivityScheduleStartDateUtc datetime2(3)
   ,OldAutoUserInactivityScheduleLastRunDateUtc datetime2(3)
   ,OldMaxCapacityEnabled bit 
   ,OldMaxCapacityUsers int
   ,OldMaxCapacityNotificationMessage nvarchar(2000)
   ,OldDeskBookingReminderEnabled bit 
   ,OldDeskBookingReminderTime time(0)
   ,OldDeskBookingReminderMessage nvarchar(2000)
   ,OldDeskBookingReservationDateRangeEnabled bit
   ,OldDeskBookingReservationDateRangeForUser int
   ,OldDeskBookingReservationDateRangeForAdmin int
   ,OldDeskBookingReservationDateRangeForSuperAdmin int
)

declare @_historyData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,RegionId uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

-- Check if another building exists with the given check-in QR code in this organization.
-- Note: Not checking for CheckInEnabled = 1 here so that we allow storing a QR code while check-in is disabled.
-- TODO: Also check if QR code has already been assigned to a floor once we add QRCode to tblFloors
if @checkInQRCode is not null
begin
    select top 1 @_checkInQRCodeExists = 1
    from tblBuildings
    where Deleted = 0
    and CheckInQRCode = @checkInQRCode
    and OrganizationId = @organizationId
    and id != @id
end

if @_checkInQRCodeExists = 0
begin
    begin transaction

    exec @_lockResult = sp_getapplock 
        @Resource = @lockResourceName, 
        @LockMode = 'Exclusive', 
        @LockOwner = 'Transaction',
        @LockTimeout = 0

    if @_lockResult < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
        update tblBuildings
        set UpdatedDateUtc = @_now
           ,Name = @name
           ,RegionId = @regionId
           ,Address = @address
           ,Latitude = @latitude
           ,Longitude = @longitude
           ,Timezone = @timezone
           ,FacilitiesManagementEmail = @facilitiesManagementEmail
           ,FacilitiesManagementEmailDisplayName = @facilitiesManagementEmailDisplayName
";
                if (request.FeatureImageChanged!.Value && request.FeatureImage is null)
                {
                    // Clear feature image if it's being removed
                    sql += @"
           ,FeatureImageUrl = null
           ,FeatureImageStorageId = null
           ,FeatureThumbnailUrl = null
           ,FeatureThumbnailStorageId = null
          
";
                }
                sql += @"
           ,MapImageUrl = case when Address != @address then null else MapImageUrl end
           ,MapImageStorageId = case when Address != @address then null else MapImageStorageId end
           ,MapThumbnailUrl = case when Address != @address then null else MapThumbnailUrl end
           ,MapThumbnailStorageId = case when Address != @address then null else MapThumbnailStorageId end
           ,CheckInEnabled = @checkInEnabled
           ,CheckInQRCode = @checkInQRCode
           ,AccessCardCheckInWithBookingMessage = @accessCardCheckInWithBookingMessage
           ,AccessCardCheckInWithoutBookingMessage = @accessCardCheckInWithoutBookingMessage
           ,QRCodeCheckInWithBookingMessage = @qrCodeCheckInWithBookingMessage
           ,QRCodeCheckInWithoutBookingMessage = @qrCodeCheckInWithoutBookingMessage
           ,CheckInReminderEnabled = @checkInReminderEnabled
           ,CheckInReminderTime = @checkInReminderTime
           ,CheckInReminderMessage = @checkInReminderMessage
           ,AutoUserInactivityEnabled = @autoUserInactivityEnabled
           ,AutoUserInactivityDurationDays = @autoUserInactivityDurationDays
           ,AutoUserInactivityScheduledIntervalMonths = @autoUserInactivityScheduledIntervalMonths
           ,AutoUserInactivityScheduleStartDateUtc = @autoUserInactivityScheduleStartDateUtc
           ,MaxCapacityEnabled = @maxCapacityEnabled 
           ,MaxCapacityUsers = @maxCapacityUsers
           ,MaxCapacityNotificationMessage = @maxCapacityNotificationMessage
           ,DeskBookingReminderEnabled = @deskBookingReminderEnabled
           ,DeskBookingReminderTime = @deskBookingReminderTime
           ,DeskBookingReminderMessage = @deskBookingReminderMessage
           ,DeskBookingReservationDateRangeEnabled = @deskBookingReservationDateRangeEnabled
           ,DeskBookingReservationDateRangeForUser = @deskBookingReservationDateRangeForUser
           ,DeskBookingReservationDateRangeForAdmin = @deskBookingReservationDateRangeForAdmin
           ,DeskBookingReservationDateRangeForSuperAdmin = @deskBookingReservationDateRangeForSuperAdmin
        output inserted.Name
              ,inserted.RegionId
              ,inserted.Address
              ,inserted.Latitude
              ,inserted.Longitude
              ,inserted.Timezone
              ,inserted.FacilitiesManagementEmail
              ,inserted.FacilitiesManagementEmailDisplayName
              ,inserted.FeatureImageUrl
              ,inserted.FeatureImageStorageId
              ,inserted.FeatureThumbnailUrl
              ,inserted.FeatureThumbnailStorageId
              ,inserted.MapImageUrl
              ,inserted.MapImageStorageId
              ,inserted.MapThumbnailUrl
              ,inserted.MapThumbnailStorageId
              ,inserted.CheckInEnabled
              ,inserted.CheckInQRCode
              ,inserted.AccessCardCheckInWithBookingMessage
              ,inserted.AccessCardCheckInWithoutBookingMessage
              ,inserted.QRCodeCheckInWithBookingMessage
              ,inserted.QRCodeCheckInWithoutBookingMessage
              ,inserted.CheckInReminderEnabled
              ,inserted.CheckInReminderTime
              ,inserted.CheckInReminderMessage
              ,inserted.AutoUserInactivityEnabled
              ,inserted.AutoUserInactivityDurationDays
              ,inserted.AutoUserInactivityScheduledIntervalMonths
              ,inserted.AutoUserInactivityScheduleStartDateUtc
              ,inserted.AutoUserInactivityScheduleLastRunDateUtc
              ,inserted.MaxCapacityEnabled
              ,inserted.MaxCapacityUsers
              ,inserted.MaxCapacityNotificationMessage
              ,inserted.DeskBookingReminderEnabled
              ,inserted.DeskBookingReminderTime
              ,inserted.DeskBookingReminderMessage
              ,inserted.DeskBookingReservationDateRangeEnabled
              ,inserted.DeskBookingReservationDateRangeForUser
              ,inserted.DeskBookingReservationDateRangeForAdmin
              ,inserted.DeskBookingReservationDateRangeForSuperAdmin
              ,deleted.Name
              ,deleted.RegionId
              ,deleted.Address
              ,deleted.Latitude
              ,deleted.Longitude
              ,deleted.Timezone
              ,deleted.FacilitiesManagementEmail
              ,deleted.FacilitiesManagementEmailDisplayName
              ,deleted.FeatureImageUrl
              ,deleted.FeatureImageStorageId
              ,deleted.FeatureThumbnailUrl
              ,deleted.FeatureThumbnailStorageId
              ,deleted.MapImageUrl
              ,deleted.MapImageStorageId
              ,deleted.MapThumbnailUrl
              ,deleted.MapThumbnailStorageId
              ,deleted.CheckInEnabled
              ,deleted.CheckInQRCode
              ,deleted.AccessCardCheckInWithBookingMessage
              ,deleted.AccessCardCheckInWithoutBookingMessage
              ,deleted.QRCodeCheckInWithBookingMessage
              ,deleted.QRCodeCheckInWithoutBookingMessage
              ,deleted.CheckInReminderEnabled
              ,deleted.CheckInReminderTime
              ,deleted.CheckInReminderMessage
              ,deleted.AutoUserInactivityEnabled
              ,deleted.AutoUserInactivityDurationDays
              ,deleted.AutoUserInactivityScheduledIntervalMonths
              ,deleted.AutoUserInactivityScheduleStartDateUtc
              ,deleted.AutoUserInactivityScheduleLastRunDateUtc
              ,deleted.MaxCapacityEnabled
              ,deleted.MaxCapacityUsers
              ,deleted.MaxCapacityNotificationMessage
              ,deleted.DeskBookingReminderEnabled
              ,deleted.DeskBookingReminderTime
              ,deleted.DeskBookingReminderMessage
              ,deleted.DeskBookingReservationDateRangeEnabled
              ,deleted.DeskBookingReservationDateRangeForUser
              ,deleted.DeskBookingReservationDateRangeForAdmin
              ,deleted.DeskBookingReservationDateRangeForSuperAdmin
              into @_data
        where Deleted = 0
        and id = @id
        and OrganizationId = @organizationId
        and ConcurrencyKey = @concurrencyKey
        and not exists
        (
            select *
            from tblBuildings
            where Deleted = 0
            and Name = @name
            and id != @id
            and OrganizationId = @organizationId
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1
    
            insert into tblBuildings_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,BuildingId
            ,Name
            ,OrganizationId
            ,RegionId
            ,Address
            ,Latitude
            ,Longitude
            ,Timezone
            ,FacilitiesManagementEmail
            ,FacilitiesManagementEmailDisplayName
            ,FeatureImageUrl
            ,FeatureImageStorageId
            ,FeatureThumbnailUrl
            ,FeatureThumbnailStorageId
            ,MapImageUrl
            ,MapImageStorageId
            ,MapThumbnailUrl
            ,MapThumbnailStorageId
            ,CheckInEnabled
            ,CheckInQRCode
            ,AccessCardCheckInWithBookingMessage
            ,AccessCardCheckInWithoutBookingMessage
            ,QRCodeCheckInWithBookingMessage
            ,QRCodeCheckInWithoutBookingMessage
            ,CheckInReminderEnabled
            ,CheckInReminderTime
            ,CheckInReminderMessage
            ,AutoUserInactivityEnabled
            ,AutoUserInactivityDurationDays
            ,AutoUserInactivityScheduledIntervalMonths
            ,AutoUserInactivityScheduleStartDateUtc
            ,AutoUserInactivityScheduleLastRunDateUtc
            ,MaxCapacityEnabled
            ,MaxCapacityUsers
            ,MaxCapacityNotificationMessage
            ,DeskBookingReminderEnabled
            ,DeskBookingReminderTime
            ,DeskBookingReminderMessage
            ,DeskBookingReservationDateRangeEnabled
            ,DeskBookingReservationDateRangeForUser
            ,DeskBookingReservationDateRangeForAdmin
            ,DeskBookingReservationDateRangeForSuperAdmin
            ,Deleted
            ,OldName
            ,OldRegionId
            ,OldAddress
            ,OldLatitude
            ,OldLongitude
            ,OldTimezone
            ,OldFacilitiesManagementEmail
            ,OldFacilitiesManagementEmailDisplayName
            ,OldFeatureImageUrl
            ,OldFeatureImageStorageId
            ,OldFeatureThumbnailUrl
            ,OldFeatureThumbnailStorageId
            ,OldMapImageUrl
            ,OldMapImageStorageId
            ,OldMapThumbnailUrl
            ,OldMapThumbnailStorageId
            ,OldCheckInEnabled
            ,OldCheckInQRCode
            ,OldAccessCardCheckInWithBookingMessage
            ,OldAccessCardCheckInWithoutBookingMessage
            ,OldQRCodeCheckInWithBookingMessage
            ,OldQRCodeCheckInWithoutBookingMessage
            ,OldCheckInReminderEnabled
            ,OldCheckInReminderTime
            ,OldCheckInReminderMessage
            ,OldAutoUserInactivityEnabled
            ,OldAutoUserInactivityDurationDays
            ,OldAutoUserInactivityScheduledIntervalMonths
            ,OldAutoUserInactivityScheduleStartDateUtc
            ,OldAutoUserInactivityScheduleLastRunDateUtc
            ,OldMaxCapacityEnabled
            ,OldMaxCapacityUsers
            ,OldMaxCapacityNotificationMessage
            ,OldDeskBookingReminderEnabled
            ,OldDeskBookingReminderTime
            ,OldDeskBookingReminderMessage
            ,OldDeskBookingReservationDateRangeEnabled
            ,OldDeskBookingReservationDateRangeForUser
            ,OldDeskBookingReservationDateRangeForAdmin
            ,OldDeskBookingReservationDateRangeForSuperAdmin
            ,OldDeleted
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@id
                  ,d.Name
                  ,@organizationId
                  ,d.RegionId
                  ,d.Address
                  ,d.Latitude
                  ,d.Longitude
                  ,d.Timezone
                  ,d.FacilitiesManagementEmail
                  ,d.FacilitiesManagementEmailDisplayName
                  ,d.FeatureImageUrl
                  ,d.FeatureImageStorageId
                  ,d.FeatureThumbnailUrl
                  ,d.FeatureThumbnailStorageId
                  ,d.MapImageUrl
                  ,d.MapImageStorageId
                  ,d.MapThumbnailUrl
                  ,d.MapThumbnailStorageId
                  ,d.CheckInEnabled
                  ,d.CheckInQRCode
                  ,d.AccessCardCheckInWithBookingMessage
                  ,d.AccessCardCheckInWithoutBookingMessage
                  ,d.QRCodeCheckInWithBookingMessage
                  ,d.QRCodeCheckInWithoutBookingMessage
                  ,d.CheckInReminderEnabled
                  ,d.CheckInReminderTime
                  ,d.CheckInReminderMessage
                  ,d.AutoUserInactivityEnabled
                  ,d.AutoUserInactivityDurationDays
                  ,d.AutoUserInactivityScheduledIntervalMonths
                  ,d.AutoUserInactivityScheduleStartDateUtc
                  ,d.AutoUserInactivityScheduleLastRunDateUtc
                  ,d.MaxCapacityEnabled
                  ,d.MaxCapacityUsers
                  ,d.MaxCapacityNotificationMessage
                  ,d.DeskBookingReminderEnabled
                  ,d.DeskBookingReminderTime
                  ,d.DeskBookingReminderMessage
                  ,d.DeskBookingReservationDateRangeEnabled
                  ,d.DeskBookingReservationDateRangeForUser
                  ,d.DeskBookingReservationDateRangeForAdmin
                  ,d.DeskBookingReservationDateRangeForSuperAdmin
                  ,0 -- Deleted
                  ,d.OldName
                  ,d.OldRegionId
                  ,d.OldAddress
                  ,d.OldLatitude
                  ,d.OldLongitude
                  ,d.OldTimezone
                  ,d.OldFacilitiesManagementEmail
                  ,d.OldFacilitiesManagementEmailDisplayName
                  ,d.OldFeatureImageUrl
                  ,d.OldFeatureImageStorageId
                  ,d.OldFeatureThumbnailUrl
                  ,d.OldFeatureThumbnailStorageId
                  ,d.OldMapImageUrl
                  ,d.OldMapImageStorageId
                  ,d.OldMapThumbnailUrl
                  ,d.OldMapThumbnailStorageId
                  ,d.OldCheckInEnabled
                  ,d.OldCheckInQRCode
                  ,d.OldAccessCardCheckInWithBookingMessage
                  ,d.OldAccessCardCheckInWithoutBookingMessage
                  ,d.OldQRCodeCheckInWithBookingMessage
                  ,d.OldQRCodeCheckInWithoutBookingMessage
                  ,d.OldCheckInReminderEnabled
                  ,d.OldCheckInReminderTime
                  ,d.OldCheckInReminderMessage
                  ,d.OldAutoUserInactivityEnabled
                  ,d.OldAutoUserInactivityDurationDays
                  ,d.OldAutoUserInactivityScheduledIntervalMonths
                  ,d.OldAutoUserInactivityScheduleStartDateUtc
                  ,d.OldAutoUserInactivityScheduleLastRunDateUtc
                  ,d.OldMaxCapacityEnabled
                  ,d.OldMaxCapacityUsers
                  ,d.OldMaxCapacityNotificationMessage
                  ,d.OldDeskBookingReminderEnabled
                  ,d.OldDeskBookingReminderTime
                  ,d.OldDeskBookingReminderMessage
                  ,d.OldDeskBookingReservationDateRangeEnabled
                  ,d.OldDeskBookingReservationDateRangeForUser
                  ,d.OldDeskBookingReservationDateRangeForAdmin
                  ,d.OldDeskBookingReservationDateRangeForSuperAdmin
                  ,0 -- OldDeleted
                  ,'Update'
            from @_data d

            -- Update the old row in tblBuildingHistories with updated EndDateUtc
            update tblBuildingHistories
            set UpdatedDateUtc = @_now
               ,EndDateUtc = @_last15MinuteIntervalUtc
            output inserted.id -- BuildingHistoryId
                  ,inserted.Name
                  ,inserted.RegionId
                  ,inserted.StartDateUtc
                  ,inserted.EndDateUtc
                  ,deleted.EndDateUtc
                  into @_historyData
            where BuildingId = @id
            and EndDateUtc > @_last15MinuteIntervalUtc

            insert into tblBuildingHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,BuildingHistoryId
            ,BuildingId
            ,Name
            ,RegionId
            ,StartDateUtc
            ,EndDateUtc
            ,OldEndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @buildingHistoryUpdateLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,h.id -- BuildingHistoryId
                  ,@id -- BuildingId
                  ,h.Name
                  ,h.RegionId
                  ,h.StartDateUtc
                  ,h.EndDateUtc
                  ,h.OldEndDateUtc
                  ,'Update' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_historyData h
            
            -- Insert a new row into tblBuildingHistories for the building we just updated,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblBuildingHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,BuildingId
            ,Name
            ,RegionId
            ,StartDateUtc
            ,EndDateUtc)
            select @buildingHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@id -- BuildingId
                  ,@name
                  ,@regionId
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the floor building for the new building
            insert into tblBuildingHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,BuildingHistoryId
            ,BuildingId
            ,Name
            ,RegionId
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @buildingHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@buildingHistoryId
                  ,@id -- BuildingId
                  ,@name
                  ,@regionId
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblBuildings' -- CascadeFrom
                  ,@logId -- CascadeLogId
        end
        else
        begin
            -- Record was not updated
            set @_result = 2
        end
    
        commit
    end
end
else
begin
    -- QR Code already exists
    set @_result = 3
end

select @_result

-- Select old ImageStorageIds for FeatureImage and FeatureThumbnail, so we can delete off disk
select OldFeatureImageStorageId
      ,OldFeatureThumbnailStorageId
from @_data

-- Select row to return with the API result
select tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      ,tblBuildings.FeatureImageStorageId
      ,tblBuildings.FeatureThumbnailUrl
      ,tblBuildings.FeatureThumbnailStorageId
      ,tblBuildings.MapImageUrl
      ,tblBuildings.MapImageStorageId
      ,tblBuildings.MapThumbnailUrl
      ,tblBuildings.MapThumbnailStorageId
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where tblBuildings.Deleted = 0
and tblBuildings.id = @id
and tblBuildings.OrganizationId = @organizationId

if @_result = 1
begin
    -- Select whether address changed or not,
    -- so we know whether we need to update the google map image.
    -- Also select the old image storage IDs so we can delete them.
    select case when d.Address != d.OldAddress then 1 else 0 end as AddressChanged
          ,d.OldMapImageStorageId
          ,d.OldMapThumbnailStorageId
    from @_data d
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when updating old tblBuildingHistories, as well as inserting to tblBuildingHistories and tblBuildingHistories_Log
                Guid buildingHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblBuildings_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@regionId", request.RegionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@address", request.Address, DbType.String, ParameterDirection.Input, 250);
                parameters.Add("@latitude", request.Latitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@longitude", request.Longitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@timezone", request.Timezone, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@facilitiesManagementEmail", request.FacilitiesManagementEmail, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@facilitiesManagementEmailDisplayName", request.FacilitiesManagementEmailDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@checkInEnabled", request.CheckInEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInQRCode", request.CheckInQRCode, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@accessCardCheckInWithBookingMessage", request.AccessCardCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@accessCardCheckInWithoutBookingMessage", request.AccessCardCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@qrCodeCheckInWithBookingMessage", request.QRCodeCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@qrCodeCheckInWithoutBookingMessage", request.QRCodeCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@checkInReminderEnabled", request.CheckInReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInReminderTime", request.CheckInReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@checkInReminderMessage", request.CheckInReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@autoUserInactivityEnabled", request.AutoUserInactivityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityDurationDays", request.AutoUserInactivityDurationDays, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityScheduledIntervalMonths", request.AutoUserInactivityScheduledIntervalMonths, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@autoUserInactivityScheduleStartDateUtc", request.AutoUserInactivityScheduleStartDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@maxCapacityEnabled", request.MaxCapacityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@maxCapacityUsers", request.MaxCapacityUsers, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@maxCapacityNotificationMessage", request.MaxCapacityNotificationMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@deskBookingReminderEnabled", request.DeskBookingReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@deskBookingReminderTime", request.DeskBookingReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@deskBookingReminderMessage", request.DeskBookingReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@deskBookingReservationDateRangeEnabled", request.DeskBookingReservationDateRangeEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForUser", request.DeskBookingReservationDateRangeForUser, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForAdmin", request.DeskBookingReservationDateRangeForAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@deskBookingReservationDateRangeForSuperAdmin", request.DeskBookingReservationDateRangeForSuperAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@buildingHistoryUpdateLogId", buildingHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingHistoryId", buildingHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingHistoryLogId", buildingHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                (Guid? oldFeatureImageStorageId, Guid? oldFeatureThumbnailStorageId) = await gridReader.ReadFirstOrDefaultAsync<(Guid?, Guid?)>();
                Building? data = await gridReader.ReadFirstOrDefaultAsync<Building>();
                bool addressChanged = false;
                Guid? oldMapImageStorageId = null;
                Guid? oldMapThumbnailStorageid = null;

                // If update was successful, also get whether the address changed
                if (!gridReader.IsConsumed)
                {
                    (addressChanged, oldMapImageStorageId, oldMapThumbnailStorageid) = await gridReader.ReadFirstOrDefaultAsync<(bool, Guid?, Guid?)>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        List<Task> tasks = new List<Task>();

                        // Start invalidate building timezone cache task
                        tasks.Add(_authCacheService.InvalidateBuildingTimeZoneInfoCacheAsync(request.id!.Value, request.OrganizationId!.Value));

                        // Update FeatureImage for updated Building
                        if (data is not null)
                        {
                            // Check if feature image was deleted
                            if (request.FeatureImageChanged!.Value && request.FeatureImage is null)
                            {
                                // Remove the image from database and delete from disk if required
                                if (oldFeatureImageStorageId is not null)
                                {
                                    tasks.Add(_imageStorageRepository.DeleteImageAsync(oldFeatureImageStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                                }

                                // Remove the thumbnail from database and delete from disk if required
                                if (oldFeatureThumbnailStorageId is not null)
                                {
                                    tasks.Add(_imageStorageRepository.DeleteImageAsync(oldFeatureThumbnailStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                                }
                            }

                            // Check if feature image was replaced
                            if (request.FeatureImageChanged!.Value && request.FeatureImage is not null)
                            {
                                // Store floor feature image file to disk
                                (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                    await StoreBuildingFeatureImageAsync(sqlConnection,
                                    featureImageContentInspectorResult,
                                    request.id!.Value, logId, request.OrganizationId!.Value, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                {
                                    // Set image URLs and storage IDs in response to be returned.
                                    data.FeatureImageUrl = storedImageFile.FileUrl;
                                    data.FeatureImageStorageId = storedImageFile.Id;
                                    data.FeatureThumbnailUrl = thumbnailFile.FileUrl;
                                    data.FeatureThumbnailStorageId = thumbnailFile.Id;
                                }
                                else
                                {
                                    queryResult = storeImageResult;
                                }

                                // Remove the old image from database and delete from disk
                                if (oldFeatureImageStorageId is not null)
                                {
                                    tasks.Add(_imageStorageRepository.DeleteImageAsync(oldFeatureImageStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                                }

                                // Remove the old thumbnail from database and delete from disk
                                if (oldFeatureThumbnailStorageId is not null)
                                {
                                    tasks.Add(_imageStorageRepository.DeleteImageAsync(oldFeatureThumbnailStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                                }
                            }
                        }

                        if (data is not null && addressChanged)
                        {
                            // Remove old map image from disk
                            if (oldMapImageStorageId.HasValue)
                            {
                                tasks.Add(_imageStorageRepository.DeleteImageAsync(oldMapImageStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                            }

                            // Remove old map thumbnail image from disk
                            if (oldMapThumbnailStorageid.HasValue)
                            {
                                tasks.Add(_imageStorageRepository.DeleteImageAsync(oldMapThumbnailStorageid.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                            }

                            /*
                            // Download map image using google maps API
                            ContentInspectorResultWithMemoryStream? mapImageContentInspectorResult;
                            using MemoryStream memoryStream = new MemoryStream();

                            HttpClient httpClient = _httpClientFactory.CreateClient("googlemaps");

                            // Example URL: https://maps.googleapis.com/maps/api/staticmap?size=640x320&scale=1&center=-37.8227503,144.968869&format=png&maptype=roadmap&key=AIzaxxx&markers=color:red%7C-37.8227503,144.968869

                            Dictionary<string, StringValues> queryParameters = new Dictionary<string, StringValues>
                            {
                                { "size", new StringValues($"{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageWidth}x{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageHeight}") },
                                { "scale", new StringValues("1") },
                                { "center", new StringValues($"{request.Latitude:0.0000000},{request.Longitude:0.0000000}") },
                                { "format", new StringValues($"png") },
                                { "maptype", new StringValues($"roadmap") },
                                { "key", new StringValues(_appSettings.GoogleMaps.GoogleMapsApiKey) },
                                { "markers", new StringValues($"color:red|{request.Latitude:0.0000000},{request.Longitude:0.0000000}") },
                            };

                            string staticMapRequestUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString($"{httpClient.BaseAddress}/maps/api/staticmap", queryParameters);

                            using (HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, staticMapRequestUrl))
                            {
                                using (HttpResponseMessage httpResponseMessage = await httpClient.SendAsync(httpRequestMessage))
                                {
                                    if (!httpResponseMessage.IsSuccessStatusCode)
                                    {
                                        // If an error occurred, just clear the map image from the database
                                        await ClearMapImageAsync(sqlConnection, request.id!.Value, logId);
                                    }
                                    else
                                    {
                                        await httpResponseMessage.Content.CopyToAsync(memoryStream);

                                        mapImageContentInspectorResult = new ContentInspectorResultWithMemoryStream
                                        {
                                            OriginalExtension = "png",
                                            InspectedExtension = "png",
                                            InspectedMimeType = "image/png",
                                            IsSanitized = false,
                                            FileName = null,
                                            FileDataStream = memoryStream,
                                        };

                                        // Store the updated building map image file to disk
                                        (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                            await StoreMapImageAsync(sqlConnection,
                                            mapImageContentInspectorResult,
                                            request.id!.Value, logId, request.OrganizationId!.Value, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                        if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                        {
                                            // Set image URLs and storage IDs in response to be returned.
                                            data.MapImageUrl = storedImageFile.FileUrl;
                                            data.MapImageStorageId = storedImageFile.Id;
                                            data.MapThumbnailUrl = thumbnailFile.FileUrl;
                                            data.MapThumbnailStorageId = thumbnailFile.Id;
                                        }
                                        else
                                        {
                                            queryResult = storeImageResult;
                                            break;
                                        }
                                    }
                                }
                            }
                            */
                        }

                        // Wait for tasks which don't have dependencies to finish
                        await Task.WhenAll(tasks);
                        break;
                    case 2:
                        if (data is null)
                        {
                            // Row did not exist
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(data.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // Row exists but concurrency key was invalid
                            queryResult = SqlQueryResult.ConcurrencyKeyInvalid;
                        }
                        else
                        {
                            // Concurrency key matches, assume that another row with the name must already exist.
                            queryResult = SqlQueryResult.RecordAlreadyExists;
                        }
                        break;
                    case 3:
                        // Check-in QR Code already exists
                        queryResult = SqlQueryResult.SubRecordAlreadyExists;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Updates the specified building's CheckInQRCode field.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Building?)> UpdateBuildingCheckInQRCodeAsync(UpdateBuildingCheckInQRCodeRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Building CheckInQRCode";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_lockResult int
declare @_checkInQRCodeExists bit = 0

declare @_data table
(
    Name nvarchar(100)
   ,RegionId uniqueidentifier
   ,Address nvarchar(250)
   ,Latitude decimal(10, 7)
   ,Longitude decimal(10, 7)
   ,Timezone varchar(50)
   ,FacilitiesManagementEmail nvarchar(254)
   ,FacilitiesManagementEmailDisplayName nvarchar(151)
   ,FeatureImageUrl varchar(255)
   ,FeatureImageStorageId uniqueidentifier
   ,FeatureThumbnailUrl varchar(255)
   ,FeatureThumbnailStorageId uniqueidentifier
   ,MapImageUrl varchar(255)
   ,MapImageStorageId uniqueidentifier
   ,MapThumbnailUrl varchar(255)
   ,MapThumbnailStorageId uniqueidentifier
   ,CheckInEnabled bit
   ,AccessCardCheckInWithBookingMessage nvarchar(2000)
   ,AccessCardCheckInWithoutBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithoutBookingMessage nvarchar(2000)
   ,CheckInReminderEnabled bit
   ,CheckInReminderTime time(0)
   ,CheckInReminderMessage nvarchar(2000)
   ,AutoUserInactivityEnabled bit
   ,AutoUserInactivityDurationDays int
   ,AutoUserInactivityScheduledIntervalMonths int
   ,AutoUserInactivityScheduleStartDateUtc datetime2(3)
   ,AutoUserInactivityScheduleLastRunDateUtc datetime2(3)
   ,MaxCapacityEnabled bit 
   ,MaxCapacityUsers int
   ,MaxCapacityNotificationMessage nvarchar(2000)
   ,DeskBookingReminderEnabled bit
   ,DeskBookingReminderTime time(0)
   ,DeskBookingReminderMessage nvarchar(2000)
   ,DeskBookingReservationDateRangeEnabled bit
   ,DeskBookingReservationDateRangeForUser int
   ,DeskBookingReservationDateRangeForAdmin int
   ,DeskBookingReservationDateRangeForSuperAdmin int
   
   ,CheckInQRCode nvarchar(100)
   ,OldCheckInQRCode nvarchar(100)
)

-- Check if another building exists with the given check-in QR code in this organization.
-- Note: Not checking for CheckInEnabled = 1 here so that we allow storing a QR code while check-in is disabled.
-- And the endpoint can decide if CheckInEnabled = 1 is required.
-- TODO: Also check if QR code has already been assigned to a floor once we add QRCode to tblFloors
if @checkInQRCode is not null
begin
    select top 1 @_checkInQRCodeExists = 1
    from tblBuildings
    where Deleted = 0
    and CheckInQRCode = @checkInQRCode
    and OrganizationId = @organizationId
    and id != @id
end

if @_checkInQRCodeExists = 0
begin
    begin transaction

    exec @_lockResult = sp_getapplock 
        @Resource = @lockResourceName, 
        @LockMode = 'Exclusive', 
        @LockOwner = 'Transaction',
        @LockTimeout = 0

    if @_lockResult < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
        update tblBuildings
        set UpdatedDateUtc = @_now
           ,CheckInQRCode = @checkInQRCode
        output inserted.Name
              ,inserted.RegionId
              ,inserted.Address
              ,inserted.Latitude
              ,inserted.Longitude
              ,inserted.Timezone
              ,inserted.FacilitiesManagementEmail
              ,inserted.FacilitiesManagementEmailDisplayName
              ,inserted.FeatureImageUrl
              ,inserted.FeatureImageStorageId
              ,inserted.FeatureThumbnailUrl
              ,inserted.FeatureThumbnailStorageId
              ,inserted.MapImageUrl
              ,inserted.MapImageStorageId
              ,inserted.MapThumbnailUrl
              ,inserted.MapThumbnailStorageId
              ,inserted.CheckInEnabled
              ,inserted.AccessCardCheckInWithBookingMessage
              ,inserted.AccessCardCheckInWithoutBookingMessage
              ,inserted.QRCodeCheckInWithBookingMessage
              ,inserted.QRCodeCheckInWithoutBookingMessage
              ,inserted.CheckInReminderEnabled
              ,inserted.CheckInReminderTime
              ,inserted.CheckInReminderMessage
              ,inserted.AutoUserInactivityEnabled
              ,inserted.AutoUserInactivityDurationDays
              ,inserted.AutoUserInactivityScheduledIntervalMonths
              ,inserted.AutoUserInactivityScheduleStartDateUtc
              ,inserted.AutoUserInactivityScheduleLastRunDateUtc
              ,inserted.MaxCapacityEnabled
              ,inserted.MaxCapacityUsers
              ,inserted.MaxCapacityNotificationMessage
              ,inserted.DeskBookingReminderEnabled
              ,inserted.DeskBookingReminderTime
              ,inserted.DeskBookingReminderMessage
              ,inserted.DeskBookingReservationDateRangeEnabled
              ,inserted.DeskBookingReservationDateRangeForUser
              ,inserted.DeskBookingReservationDateRangeForAdmin
              ,inserted.DeskBookingReservationDateRangeForSuperAdmin
              ,inserted.CheckInQRCode
              ,deleted.CheckInQRCode
              into @_data
        where Deleted = 0
        and id = @id
        and OrganizationId = @organizationId
        and ConcurrencyKey = @concurrencyKey
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1
    
            insert into tblBuildings_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,BuildingId
            ,Name
            ,OrganizationId
            ,RegionId
            ,Address
            ,Latitude
            ,Longitude
            ,Timezone
            ,FacilitiesManagementEmail
            ,FacilitiesManagementEmailDisplayName
            ,FeatureImageUrl
            ,FeatureImageStorageId
            ,FeatureThumbnailUrl
            ,FeatureThumbnailStorageId
            ,MapImageUrl
            ,MapImageStorageId
            ,MapThumbnailUrl
            ,MapThumbnailStorageId
            ,CheckInEnabled
            ,CheckInQRCode
            ,AccessCardCheckInWithBookingMessage
            ,AccessCardCheckInWithoutBookingMessage
            ,QRCodeCheckInWithBookingMessage
            ,QRCodeCheckInWithoutBookingMessage
            ,CheckInReminderEnabled
            ,CheckInReminderTime
            ,CheckInReminderMessage
            ,AutoUserInactivityEnabled
            ,AutoUserInactivityDurationDays
            ,AutoUserInactivityScheduledIntervalMonths
            ,AutoUserInactivityScheduleStartDateUtc
            ,AutoUserInactivityScheduleLastRunDateUtc
            ,MaxCapacityEnabled
            ,MaxCapacityUsers
            ,MaxCapacityNotificationMessage
            ,DeskBookingReminderEnabled
            ,DeskBookingReminderTime
            ,DeskBookingReminderMessage
            ,DeskBookingReservationDateRangeEnabled
            ,DeskBookingReservationDateRangeForUser
            ,DeskBookingReservationDateRangeForAdmin
            ,DeskBookingReservationDateRangeForSuperAdmin
            ,Deleted
            ,OldName
            ,OldRegionId
            ,OldAddress
            ,OldLatitude
            ,OldLongitude
            ,OldTimezone
            ,OldFacilitiesManagementEmail
            ,OldFacilitiesManagementEmailDisplayName
            ,OldFeatureImageUrl
            ,OldFeatureImageStorageId
            ,OldFeatureThumbnailUrl
            ,OldFeatureThumbnailStorageId
            ,OldMapImageUrl
            ,OldMapImageStorageId
            ,OldMapThumbnailUrl
            ,OldMapThumbnailStorageId
            ,OldCheckInEnabled
            ,OldCheckInQRCode
            ,OldAccessCardCheckInWithBookingMessage
            ,OldAccessCardCheckInWithoutBookingMessage
            ,OldQRCodeCheckInWithBookingMessage
            ,OldQRCodeCheckInWithoutBookingMessage
            ,OldCheckInReminderEnabled
            ,OldCheckInReminderTime
            ,OldCheckInReminderMessage
            ,OldAutoUserInactivityEnabled
            ,OldAutoUserInactivityDurationDays
            ,OldAutoUserInactivityScheduledIntervalMonths
            ,OldAutoUserInactivityScheduleStartDateUtc
            ,OldAutoUserInactivityScheduleLastRunDateUtc
            ,OldMaxCapacityEnabled
            ,OldMaxCapacityUsers
            ,OldMaxCapacityNotificationMessage
            ,OldDeskBookingReminderEnabled
            ,OldDeskBookingReminderTime
            ,OldDeskBookingReminderMessage
            ,OldDeskBookingReservationDateRangeEnabled
            ,OldDeskBookingReservationDateRangeForUser
            ,OldDeskBookingReservationDateRangeForAdmin
            ,OldDeskBookingReservationDateRangeForSuperAdmin
            ,OldDeleted
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@id
                  ,d.Name
                  ,@organizationId
                  ,d.RegionId
                  ,d.Address
                  ,d.Latitude
                  ,d.Longitude
                  ,d.Timezone
                  ,d.FacilitiesManagementEmail
                  ,d.FacilitiesManagementEmailDisplayName
                  ,d.FeatureImageUrl
                  ,d.FeatureImageStorageId
                  ,d.FeatureThumbnailUrl
                  ,d.FeatureThumbnailStorageId
                  ,d.MapImageUrl
                  ,d.MapImageStorageId
                  ,d.MapThumbnailUrl
                  ,d.MapThumbnailStorageId
                  ,d.CheckInEnabled
                  ,d.CheckInQRCode
                  ,d.AccessCardCheckInWithBookingMessage
                  ,d.AccessCardCheckInWithoutBookingMessage
                  ,d.QRCodeCheckInWithBookingMessage
                  ,d.QRCodeCheckInWithoutBookingMessage
                  ,d.CheckInReminderEnabled
                  ,d.CheckInReminderTime
                  ,d.CheckInReminderMessage
                  ,d.AutoUserInactivityEnabled
                  ,d.AutoUserInactivityDurationDays
                  ,d.AutoUserInactivityScheduledIntervalMonths
                  ,d.AutoUserInactivityScheduleStartDateUtc
                  ,d.AutoUserInactivityScheduleLastRunDateUtc
                  ,d.MaxCapacityEnabled
                  ,d.MaxCapacityUsers
                  ,d.MaxCapacityNotificationMessage
                  ,d.DeskBookingReminderEnabled
                  ,d.DeskBookingReminderTime
                  ,d.DeskBookingReminderMessage
                  ,d.DeskBookingReservationDateRangeEnabled
                  ,d.DeskBookingReservationDateRangeForUser
                  ,d.DeskBookingReservationDateRangeForAdmin
                  ,d.DeskBookingReservationDateRangeForSuperAdmin
                  ,0 -- Deleted
                  ,d.Name
                  ,d.RegionId
                  ,d.Address
                  ,d.Latitude
                  ,d.Longitude
                  ,d.Timezone
                  ,d.FacilitiesManagementEmail
                  ,d.FacilitiesManagementEmailDisplayName
                  ,d.FeatureImageUrl
                  ,d.FeatureImageStorageId
                  ,d.FeatureThumbnailUrl
                  ,d.FeatureThumbnailStorageId
                  ,d.MapImageUrl
                  ,d.MapImageStorageId
                  ,d.MapThumbnailUrl
                  ,d.MapThumbnailStorageId
                  ,d.CheckInEnabled
                  ,d.OldCheckInQRCode
                  ,d.AccessCardCheckInWithBookingMessage
                  ,d.AccessCardCheckInWithoutBookingMessage
                  ,d.QRCodeCheckInWithBookingMessage
                  ,d.QRCodeCheckInWithoutBookingMessage
                  ,d.CheckInReminderEnabled
                  ,d.CheckInReminderTime
                  ,d.CheckInReminderMessage
                  ,d.AutoUserInactivityEnabled
                  ,d.AutoUserInactivityDurationDays
                  ,d.AutoUserInactivityScheduledIntervalMonths
                  ,d.AutoUserInactivityScheduleStartDateUtc
                  ,d.AutoUserInactivityScheduleLastRunDateUtc
                  ,d.MaxCapacityEnabled
                  ,d.MaxCapacityUsers
                  ,d.MaxCapacityNotificationMessage
                  ,d.DeskBookingReminderEnabled
                  ,d.DeskBookingReminderTime
                  ,d.DeskBookingReminderMessage
                  ,d.DeskBookingReservationDateRangeEnabled
                  ,d.DeskBookingReservationDateRangeForUser
                  ,d.DeskBookingReservationDateRangeForAdmin
                  ,d.DeskBookingReservationDateRangeForSuperAdmin
                  ,0 -- OldDeleted
                  ,'Update'
            from @_data d
        end
        else
        begin
            -- Record was not updated
            set @_result = 2
        end
    
        commit
    end
end
else
begin
    -- QR Code already exists
    set @_result = 3
end

select @_result

-- Select row to return with the API result
select tblBuildings.id
      ,tblBuildings.InsertDateUtc
      ,tblBuildings.UpdatedDateUtc
      ,tblBuildings.Name
      ,tblBuildings.OrganizationId
      ,tblBuildings.RegionId
      ,tblRegions.Name as RegionName
      ,tblBuildings.Address
      ,tblBuildings.Latitude
      ,tblBuildings.Longitude
      ,tblBuildings.Timezone
      ,tblBuildings.FacilitiesManagementEmail
      ,tblBuildings.FacilitiesManagementEmailDisplayName
      ,tblBuildings.FeatureImageUrl
      ,tblBuildings.FeatureImageStorageId
      ,tblBuildings.FeatureThumbnailUrl
      ,tblBuildings.FeatureThumbnailStorageId
      ,tblBuildings.MapImageUrl
      ,tblBuildings.MapImageStorageId
      ,tblBuildings.MapThumbnailUrl
      ,tblBuildings.MapThumbnailStorageId
      ,tblBuildings.CheckInEnabled
      ,tblBuildings.CheckInQRCode
      ,tblBuildings.AccessCardCheckInWithBookingMessage
      ,tblBuildings.AccessCardCheckInWithoutBookingMessage
      ,tblBuildings.QRCodeCheckInWithBookingMessage
      ,tblBuildings.QRCodeCheckInWithoutBookingMessage
      ,tblBuildings.CheckInReminderEnabled
      ,tblBuildings.CheckInReminderTime
      ,tblBuildings.CheckInReminderMessage
      ,tblBuildings.AutoUserInactivityEnabled
      ,tblBuildings.AutoUserInactivityDurationDays
      ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
      ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
      ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
      ,tblBuildings.MaxCapacityEnabled 
      ,tblBuildings.MaxCapacityUsers
      ,tblBuildings.MaxCapacityNotificationMessage
      ,tblBuildings.DeskBookingReminderEnabled
      ,tblBuildings.DeskBookingReminderTime
      ,tblBuildings.DeskBookingReminderMessage
      ,tblBuildings.DeskBookingReservationDateRangeEnabled
      ,tblBuildings.DeskBookingReservationDateRangeForUser
      ,tblBuildings.DeskBookingReservationDateRangeForAdmin
      ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
      ,tblBuildings.ConcurrencyKey
from tblBuildings
left join tblRegions
on tblBuildings.RegionId = tblRegions.id
and tblRegions.Deleted = 0
where tblBuildings.Deleted = 0
and tblBuildings.id = @id
and tblBuildings.OrganizationId = @organizationId
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.CheckInQRCode!.ToUpperInvariant())));

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblBuildings_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@checkInQRCode", request.CheckInQRCode, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Building? data = await gridReader.ReadFirstOrDefaultAsync<Building>();

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        if (data is null)
                        {
                            // Row did not exist
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(data.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // Row exists but concurrency key was invalid
                            queryResult = SqlQueryResult.ConcurrencyKeyInvalid;
                        }
                        else
                        {
                            // Concurrency key matches, assume that another row with the name must already exist.
                            queryResult = SqlQueryResult.RecordAlreadyExists;
                        }
                        break;
                    case 3:
                        // Check-in QR Code already exists
                        queryResult = SqlQueryResult.SubRecordAlreadyExists;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }
                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Deletes the specified building.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Building?)> DeleteBuildingAsync(DeleteBuildingRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete Building";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Name nvarchar(100)
   ,RegionId uniqueidentifier
   ,Address nvarchar(250)
   ,Latitude decimal(10, 7)
   ,Longitude decimal(10, 7)
   ,Timezone varchar(50)
   ,FacilitiesManagementEmail nvarchar(254)
   ,FacilitiesManagementEmailDisplayName nvarchar(151)
   ,FeatureImageUrl varchar(255)
   ,FeatureImageStorageId uniqueidentifier
   ,FeatureThumbnailUrl varchar(255)
   ,FeatureThumbnailStorageId uniqueidentifier
   ,MapImageUrl varchar(255)
   ,MapImageStorageId uniqueidentifier
   ,MapThumbnailUrl varchar(255)
   ,MapThumbnailStorageId uniqueidentifier
   ,CheckInEnabled bit
   ,CheckInQRCode nvarchar(100)
   ,AccessCardCheckInWithBookingMessage nvarchar(2000)
   ,AccessCardCheckInWithoutBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithBookingMessage nvarchar(2000)
   ,QRCodeCheckInWithoutBookingMessage nvarchar(2000)
   ,CheckInReminderEnabled bit
   ,CheckInReminderTime time(0)
   ,CheckInReminderMessage nvarchar(2000)
   ,AutoUserInactivityEnabled bit
   ,AutoUserInactivityDurationDays int
   ,AutoUserInactivityScheduledIntervalMonths int
   ,AutoUserInactivityScheduleStartDateUtc datetime2(3)
   ,AutoUserInactivityScheduleLastRunDateUtc datetime2(3)
   ,MaxCapacityEnabled bit
   ,MaxCapacityUsers int
   ,MaxCapacityNotificationMessage nvarchar(2000)
   ,DeskBookingReminderEnabled bit
   ,DeskBookingReminderTime time(0)
   ,DeskBookingReminderMessage nvarchar(2000)
   ,DeskBookingReservationDateRangeEnabled bit
   ,DeskBookingReservationDateRangeForUser int
   ,DeskBookingReservationDateRangeForAdmin int
   ,DeskBookingReservationDateRangeForSuperAdmin int  
)

update tblBuildings
set Deleted = 1
   ,UpdatedDateUtc = @_now
output inserted.Name
      ,inserted.RegionId
      ,inserted.Address
      ,inserted.Latitude
      ,inserted.Longitude
      ,inserted.Timezone
      ,inserted.FacilitiesManagementEmail
      ,inserted.FacilitiesManagementEmailDisplayName
      ,inserted.FeatureImageUrl
      ,inserted.FeatureImageStorageId
      ,inserted.FeatureThumbnailUrl
      ,inserted.FeatureThumbnailStorageId
      ,inserted.MapImageUrl
      ,inserted.MapImageStorageId
      ,inserted.MapThumbnailUrl
      ,inserted.MapThumbnailStorageId
      ,inserted.CheckInEnabled
      ,inserted.CheckInQRCode
      ,inserted.AccessCardCheckInWithBookingMessage
      ,inserted.AccessCardCheckInWithoutBookingMessage
      ,inserted.QRCodeCheckInWithBookingMessage
      ,inserted.QRCodeCheckInWithoutBookingMessage
      ,inserted.CheckInReminderEnabled
      ,inserted.CheckInReminderTime
      ,inserted.CheckInReminderMessage
      ,inserted.AutoUserInactivityEnabled
      ,inserted.AutoUserInactivityDurationDays
      ,inserted.AutoUserInactivityScheduledIntervalMonths
      ,inserted.AutoUserInactivityScheduleStartDateUtc
      ,inserted.AutoUserInactivityScheduleLastRunDateUtc
      ,inserted.MaxCapacityEnabled
      ,inserted.MaxCapacityUsers
      ,inserted.MaxCapacityNotificationMessage
      ,inserted.DeskBookingReminderEnabled
      ,inserted.DeskBookingReminderTime
      ,inserted.DeskBookingReminderMessage
      ,inserted.DeskBookingReservationDateRangeEnabled
      ,inserted.DeskBookingReservationDateRangeForUser
      ,inserted.DeskBookingReservationDateRangeForAdmin
      ,inserted.DeskBookingReservationDateRangeForSuperAdmin
      into @_data
where Deleted = 0
and id = @id
and OrganizationId = @organizationId
and ConcurrencyKey = @concurrencyKey

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblBuildings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,BuildingId
    ,Name
    ,OrganizationId
    ,RegionId
    ,Address
    ,Latitude
    ,Longitude
    ,Timezone
    ,FacilitiesManagementEmail
    ,FacilitiesManagementEmailDisplayName
    ,FeatureImageUrl
    ,FeatureImageStorageId
    ,FeatureThumbnailUrl
    ,FeatureThumbnailStorageId
    ,MapImageUrl
    ,MapImageStorageId
    ,MapThumbnailUrl
    ,MapThumbnailStorageId
    ,CheckInEnabled
    ,CheckInQRCode
    ,AccessCardCheckInWithBookingMessage
    ,AccessCardCheckInWithoutBookingMessage
    ,QRCodeCheckInWithBookingMessage
    ,QRCodeCheckInWithoutBookingMessage
    ,CheckInReminderEnabled
    ,CheckInReminderTime
    ,CheckInReminderMessage
    ,AutoUserInactivityEnabled
    ,AutoUserInactivityDurationDays
    ,AutoUserInactivityScheduledIntervalMonths
    ,AutoUserInactivityScheduleStartDateUtc
    ,AutoUserInactivityScheduleLastRunDateUtc
    ,MaxCapacityEnabled
    ,MaxCapacityUsers
    ,MaxCapacityNotificationMessage
    ,DeskBookingReminderEnabled
    ,DeskBookingReminderTime
    ,DeskBookingReminderMessage
    ,DeskBookingReservationDateRangeEnabled
    ,DeskBookingReservationDateRangeForUser
    ,DeskBookingReservationDateRangeForAdmin
    ,DeskBookingReservationDateRangeForSuperAdmin
    ,Deleted
    ,OldName
    ,OldRegionId
    ,OldAddress
    ,OldLatitude
    ,OldLongitude
    ,OldTimezone
    ,OldFacilitiesManagementEmail
    ,OldFacilitiesManagementEmailDisplayName
    ,OldFeatureImageUrl
    ,OldFeatureImageStorageId
    ,OldFeatureThumbnailUrl
    ,OldFeatureThumbnailStorageId
    ,OldMapImageUrl
    ,OldMapImageStorageId
    ,OldMapThumbnailUrl
    ,OldMapThumbnailStorageId
    ,OldCheckInEnabled
    ,OldCheckInQRCode
    ,OldAccessCardCheckInWithBookingMessage
    ,OldAccessCardCheckInWithoutBookingMessage
    ,OldQRCodeCheckInWithBookingMessage
    ,OldQRCodeCheckInWithoutBookingMessage
    ,OldCheckInReminderEnabled
    ,OldCheckInReminderTime
    ,OldCheckInReminderMessage
    ,OldAutoUserInactivityEnabled
    ,OldAutoUserInactivityDurationDays
    ,OldAutoUserInactivityScheduledIntervalMonths
    ,OldAutoUserInactivityScheduleStartDateUtc
    ,OldAutoUserInactivityScheduleLastRunDateUtc
    ,OldMaxCapacityEnabled   
    ,OldMaxCapacityUsers
    ,OldMaxCapacityNotificationMessage
    ,OldDeskBookingReminderEnabled
    ,OldDeskBookingReminderTime
    ,OldDeskBookingReminderMessage
    ,OldDeskBookingReservationDateRangeEnabled
    ,OldDeskBookingReservationDateRangeForUser
    ,OldDeskBookingReservationDateRangeForAdmin
    ,OldDeskBookingReservationDateRangeForSuperAdmin
    ,OldDeleted
    ,LogAction)
    select @logId
            ,@_now
            ,@adminUserUid
            ,@adminUserDisplayName
            ,@remoteIpAddress
            ,@logDescription
            ,@id
            ,d.Name
            ,@organizationId
            ,d.RegionId
            ,d.Address
            ,d.Latitude
            ,d.Longitude
            ,d.Timezone
            ,d.FacilitiesManagementEmail
            ,d.FacilitiesManagementEmailDisplayName
            ,d.FeatureImageUrl
            ,d.FeatureImageStorageId
            ,d.FeatureThumbnailUrl
            ,d.FeatureThumbnailStorageId
            ,d.MapImageUrl
            ,d.MapImageStorageId
            ,d.MapThumbnailUrl
            ,d.MapThumbnailStorageId
            ,d.CheckInEnabled
            ,d.CheckInQRCode
            ,d.AccessCardCheckInWithBookingMessage
            ,d.AccessCardCheckInWithoutBookingMessage
            ,d.QRCodeCheckInWithBookingMessage
            ,d.QRCodeCheckInWithoutBookingMessage
            ,d.CheckInReminderEnabled
            ,d.CheckInReminderTime
            ,d.CheckInReminderMessage
            ,d.AutoUserInactivityEnabled
            ,d.AutoUserInactivityDurationDays
            ,d.AutoUserInactivityScheduledIntervalMonths
            ,d.AutoUserInactivityScheduleStartDateUtc
            ,d.AutoUserInactivityScheduleLastRunDateUtc
            ,d.MaxCapacityEnabled
            ,d.MaxCapacityUsers
            ,d.MaxCapacityNotificationMessage
            ,d.DeskBookingReminderEnabled
            ,d.DeskBookingReminderTime
            ,d.DeskBookingReminderMessage
            ,d.DeskBookingReservationDateRangeEnabled
            ,d.DeskBookingReservationDateRangeForUser
            ,d.DeskBookingReservationDateRangeForAdmin
            ,d.DeskBookingReservationDateRangeForSuperAdmin
            ,1 -- Deleted
            ,d.Name
            ,d.RegionId
            ,d.Address
            ,d.Latitude
            ,d.Longitude
            ,d.Timezone
            ,d.FacilitiesManagementEmail
            ,d.FacilitiesManagementEmailDisplayName
            ,d.FeatureImageUrl
            ,d.FeatureImageStorageId
            ,d.FeatureThumbnailUrl
            ,d.FeatureThumbnailStorageId
            ,d.MapImageUrl
            ,d.MapImageStorageId
            ,d.MapThumbnailUrl
            ,d.MapThumbnailStorageId
            ,d.CheckInEnabled
            ,d.CheckInQRCode
            ,d.AccessCardCheckInWithBookingMessage
            ,d.AccessCardCheckInWithoutBookingMessage
            ,d.QRCodeCheckInWithBookingMessage
            ,d.QRCodeCheckInWithoutBookingMessage
            ,d.CheckInReminderEnabled
            ,d.CheckInReminderTime
            ,d.CheckInReminderMessage
            ,d.AutoUserInactivityEnabled
            ,d.AutoUserInactivityDurationDays
            ,d.AutoUserInactivityScheduledIntervalMonths
            ,d.AutoUserInactivityScheduleStartDateUtc
            ,d.AutoUserInactivityScheduleLastRunDateUtc
            ,d.MaxCapacityEnabled
            ,d.MaxCapacityUsers
            ,d.MaxCapacityNotificationMessage
            ,d.DeskBookingReminderEnabled
            ,d.DeskBookingReminderTime
            ,d.DeskBookingReminderMessage
            ,d.DeskBookingReservationDateRangeEnabled
            ,d.DeskBookingReservationDateRangeForUser
            ,d.DeskBookingReservationDateRangeForAdmin
            ,d.DeskBookingReservationDateRangeForSuperAdmin
            ,0 -- OldDeleted
            ,'Update'
    from @_data d
end
else
begin
    -- Record could not be deleted
    set @_result = 2
end

select @_result

if @_result = 1
begin
    -- Also select the old image storage IDs so we can delete them.
    select d.MapImageStorageId
          ,d.MapThumbnailStorageId
    from @_data d 
end
else
begin
    -- Select existing row if delete was unsuccessful
    select tblBuildings.id
          ,tblBuildings.InsertDateUtc
          ,tblBuildings.UpdatedDateUtc
          ,tblBuildings.Name
          ,tblBuildings.OrganizationId
          ,tblBuildings.RegionId
          ,tblRegions.Name as RegionName
          ,tblBuildings.Address
          ,tblBuildings.Latitude
          ,tblBuildings.Longitude
          ,tblBuildings.Timezone
          ,tblBuildings.FacilitiesManagementEmail
          ,tblBuildings.FacilitiesManagementEmailDisplayName
          ,tblBuildings.FeatureImageUrl
          ,tblBuildings.FeatureImageStorageId
          ,tblBuildings.FeatureThumbnailUrl
          ,tblBuildings.FeatureThumbnailStorageId
          ,tblBuildings.MapImageUrl
          ,tblBuildings.MapImageStorageId
          ,tblBuildings.MapThumbnailUrl
          ,tblBuildings.MapThumbnailStorageId
          ,tblBuildings.CheckInEnabled
          ,tblBuildings.CheckInQRCode
          ,tblBuildings.AccessCardCheckInWithBookingMessage
          ,tblBuildings.AccessCardCheckInWithoutBookingMessage
          ,tblBuildings.QRCodeCheckInWithBookingMessage
          ,tblBuildings.QRCodeCheckInWithoutBookingMessage
          ,tblBuildings.CheckInReminderEnabled
          ,tblBuildings.CheckInReminderTime
          ,tblBuildings.CheckInReminderMessage
          ,tblBuildings.AutoUserInactivityEnabled
          ,tblBuildings.AutoUserInactivityDurationDays
          ,tblBuildings.AutoUserInactivityScheduledIntervalMonths
          ,tblBuildings.AutoUserInactivityScheduleStartDateUtc
          ,tblBuildings.AutoUserInactivityScheduleLastRunDateUtc
          ,tblBuildings.MaxCapacityEnabled 
          ,tblBuildings.MaxCapacityUsers
          ,tblBuildings.MaxCapacityNotificationMessage
          ,tblBuildings.DeskBookingReminderEnabled
          ,tblBuildings.DeskBookingReminderTime
          ,tblBuildings.DeskBookingReminderMessage
          ,tblBuildings.DeskBookingReservationDateRangeEnabled
          ,tblBuildings.DeskBookingReservationDateRangeForUser
          ,tblBuildings.DeskBookingReservationDateRangeForAdmin
          ,tblBuildings.DeskBookingReservationDateRangeForSuperAdmin
          ,tblBuildings.ConcurrencyKey
    from tblBuildings
    left join tblRegions
    on tblBuildings.RegionId = tblRegions.id
    and tblRegions.Deleted = 0
    where tblBuildings.Deleted = 0
    and tblBuildings.id = @id
    and tblBuildings.OrganizationId = @organizationId
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Building? data = null;
                Guid? oldMapImageStorageId = null;
                Guid? oldMapThumbnailStorageid = null;

                if (resultCode == 1 && !gridReader.IsConsumed)
                {
                    // If delete was successful, also get the image storage IDs so we can delete them from disk.
                    (oldMapImageStorageId, oldMapThumbnailStorageid) = await gridReader.ReadFirstOrDefaultAsync<(Guid?, Guid?)>();
                }
                else if (resultCode != 1 && !gridReader.IsConsumed)
                {
                    // If delete was unsuccessful, also get the updated data to be returned in the response if available.
                    data = await gridReader.ReadFirstOrDefaultAsync<Building>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        List<Task> tasks = new List<Task>();

                        // Start invalidate building timezone cache task
                        tasks.Add(_authCacheService.InvalidateBuildingTimeZoneInfoCacheAsync(request.id!.Value, request.OrganizationId!.Value));

                        // Remove old map image from disk
                        if (oldMapImageStorageId.HasValue)
                        {
                            tasks.Add(_imageStorageRepository.DeleteImageAsync(oldMapImageStorageId.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                        }

                        // Remove old map thumbnail image from disk
                        if (oldMapThumbnailStorageid.HasValue)
                        {
                            tasks.Add(_imageStorageRepository.DeleteImageAsync(oldMapThumbnailStorageid.Value, "tblBuildings", logId, adminUserUid, adminUserDisplayName, remoteIpAddress));
                        }

                        // Wait for tasks to finish.
                        await Task.WhenAll(tasks);
                        break;
                     case 2:
                        if (data is null)
                        {
                            // Row did not exist
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(data.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // Row exists but concurrency key was invalid
                            queryResult = SqlQueryResult.ConcurrencyKeyInvalid;
                        }
                        else
                        {
                            // This should never happen
                            queryResult = SqlQueryResult.UnknownError;
                        }
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        public async Task<Dictionary<Guid, string>> GetBuildingsTimeZoneStringDictionary(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select id
      ,Timezone
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using DbDataReader reader = await sqlConnection.ExecuteReaderAsync(commandDefinition);

                Dictionary<Guid, string> result = new Dictionary<Guid, string>();

                while (await reader.ReadAsync())
                {
                    result.Add(reader.GetGuid(0), reader.GetString(1));
                }

                return result;
            }
        }

        public async Task<string?> GetBuildingTimeZoneString(Guid organizationId, Guid buildingId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select Timezone
from tblBuildings
where Deleted = 0
and id = @buildingId
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                return await gridReader.ReadFirstOrDefaultAsync<string>();
            }
        }

        public async Task<Dictionary<Guid, TimeZoneInfo>> GetTimeZoneInfoDictionary(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select id
      ,Timezone
from tblBuildings
where Deleted = 0
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using DbDataReader reader = await sqlConnection.ExecuteReaderAsync(commandDefinition);

                Dictionary<string, TimeZoneInfo> timeZoneInfoDict = new Dictionary<string, TimeZoneInfo>();
                Dictionary<Guid, TimeZoneInfo> result = new Dictionary<Guid, TimeZoneInfo>();

                while (await reader.ReadAsync())
                {
                    Guid buildingId = reader.GetGuid(0);
                    string buildingTimezone = reader.GetString(1);
                    TimeZoneInfo? timeZoneInfo;

                    try
                    {
                        if (!timeZoneInfoDict.TryGetValue(buildingTimezone, out timeZoneInfo))
                        {
                            timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(buildingTimezone);
                            timeZoneInfoDict.Add(buildingTimezone, timeZoneInfo);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    result.Add(buildingId, timeZoneInfo);
                }

                return result;
            }
        }

        public async Task<TimeZoneInfo?> GetTimeZoneInfoById(Guid organizationId, Guid buildingId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select Timezone
from tblBuildings
where Deleted = 0
and id = @buildingId
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);
                
                string? buildingTimezone = await gridReader.ReadFirstOrDefaultAsync<string>();

                if (string.IsNullOrEmpty(buildingTimezone))
                {
                    return null;
                }

                return TimeZoneInfo.FindSystemTimeZoneById(buildingTimezone);
            }
        }

        private async Task<(SqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile)> StoreBuildingFeatureImageAsync(SqlConnection sqlConnection,
            ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult,
            Guid buildingId, Guid logId, Guid organizationId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                await _imageStorageRepository.WriteImageAndThumbnailAsync(
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingFeatureImage.MaxImageWidth,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingFeatureImage.MaxImageHeight,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingFeatureImage.ThumbnailMaxImageWidth,
                    _appSettings.ImageUpload.ObjectRestrictions.BuildingFeatureImage.ThumbnailMaxImageHeight,
                    true,
                    false,
                    featureImageContentInspectorResult,
                    ImageStorageRelatedObjectType.SSBuildingFeatureImage,
                    ImageStorageRelatedObjectType.SSBuildingFeatureImageThumbnail,
                    buildingId,
                    organizationId,
                    "tblBuildings",
                    logId,
                    adminUserUid,
                    adminUserDisplayName,
                    remoteIpAddress);

            if (sqlQueryResult != SqlQueryResult.Ok)
            {
                return (sqlQueryResult, storedImageFile, thumbnailFile);
            }
            else if (storedImageFile is null || thumbnailFile is null)
            {
                // This should never happen
                return (SqlQueryResult.UnknownError, storedImageFile, thumbnailFile);
            }

            string sql = @"
update tblBuildings
set FeatureImageUrl = @fileUrl
   ,FeatureImageStorageId = @imageStorageId
   ,FeatureThumbnailUrl = @thumbnailUrl
   ,FeatureThumbnailStorageId = @thumbnailStorageId
where Deleted = 0
and id = @buildingId

update tblBuildings_Log
set FeatureImageUrl = @fileUrl
   ,FeatureImageStorageId = @imageStorageId
   ,FeatureThumbnailUrl = @thumbnailUrl
   ,FeatureThumbnailStorageId = @thumbnailStorageId
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@fileUrl", storedImageFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@imageStorageId", storedImageFile.Id, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@thumbnailUrl", thumbnailFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@thumbnailStorageId", thumbnailFile.Id, DbType.Guid, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);

            return (sqlQueryResult, storedImageFile, thumbnailFile);
        }
    }
}
