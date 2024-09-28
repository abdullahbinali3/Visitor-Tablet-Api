using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Primitives;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.Organizations.AzureSettings.UpdateOrganizationAzureSettings;
using VisitorTabletAPITemplate.Features.Organizations.CreateOrganization;
using VisitorTabletAPITemplate.Features.Organizations.DeleteOrganization;
using VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFloorsForDropdowns;
using VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFunctionsForDropdowns;
using VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.UpdateOrganizationNotificationsSettings;
using VisitorTabletAPITemplate.Features.Organizations.UpdateOrganization;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using ZiggyCreatures.Caching.Fusion;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.Repositories
{
    public sealed class OrganizationsRepository
    {
        private readonly IFusionCache _cache;
        private readonly AppSettings _appSettings;
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public OrganizationsRepository(IFusionCache cache,
            AppSettings appSettings,
            ImageStorageRepository imageStorageRepository,
            IHttpClientFactory httpClientFactory)
        {
            _cache = cache;
            _appSettings = appSettings;
            _imageStorageRepository = imageStorageRepository;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// <para>Retrieves list of buildings for a given organization, along with each building's list of functions.</para>
        /// <para>Returns one of <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.SubRecordDidNotExist"/>.</para>
        /// </summary>
        /// <param name="req"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, ListBuildingsAndFunctionsForDropdownsResponse)> ListBuildingsAndFunctionsForDropdownsAsync(ListBuildingsAndFunctionsForDropdownsRequest req, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_validOrganization bit = 0
declare @_result int = 0

select @_validOrganization = 1
from tblOrganizations
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0

select @_validOrganization

if @_validOrganization = 1
begin
    -- Select buildings in the organization
    select tblBuildings.id as BuildingId
          ,tblBuildings.Name as BuildingName
    from tblBuildings
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblBuildings.OrganizationId = @organizationId
    and tblBuildings.Deleted = 0
    order by tblBuildings.Name

    if @@ROWCOUNT > 0
    begin
        set @_result = 1

        -- Selecting functions in the organization
        select tblBuildings.id as BuildingId
              ,tblFunctions.id as FunctionId
              ,tblFunctions.Name as FunctionName
        from tblFunctions
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.Id
        and tblBuildings.Deleted = 0
        inner join tblOrganizations
        on tblBuildings.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        where tblBuildings.OrganizationId = @organizationId 
        and tblFunctions.Deleted = 0
        order by tblFunctions.Name
    end
    else
    begin
        -- Organization exists, but has no buildings
        set @_result = 3
    end
end
else
begin
    set @_result = 2
end

select @_result
";
                int resultCode = 0;
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", req.id, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using GridReader reader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                ListBuildingsAndFunctionsForDropdownsResponse response = new ListBuildingsAndFunctionsForDropdownsResponse();

                bool organizationValid = await reader.ReadFirstOrDefaultAsync<bool>();
                response.RequestCounter = req.RequestCounter;

                if (organizationValid)
                {
                    response.Buildings = (await reader.ReadAsync<ListBuildingsAndFunctionsForDropdownsResponse_Building>()).AsList();

                    if (response.Buildings.Count > 0)
                    {
                        List<ListBuildingsAndFunctionsForDropdownsResponse_Function> functions = (await reader.ReadAsync<ListBuildingsAndFunctionsForDropdownsResponse_Function>()).AsList();

                        if (functions.Count > 0)
                        {
                            Dictionary<Guid, ListBuildingsAndFunctionsForDropdownsResponse_Building> buildingsDict = new Dictionary<Guid, ListBuildingsAndFunctionsForDropdownsResponse_Building>();

                            foreach (ListBuildingsAndFunctionsForDropdownsResponse_Building building in response.Buildings)
                            {
                                buildingsDict.Add(building.BuildingId, building);
                            }

                            foreach (ListBuildingsAndFunctionsForDropdownsResponse_Function function in functions)
                            {
                                if (buildingsDict.TryGetValue(function.BuildingId, out ListBuildingsAndFunctionsForDropdownsResponse_Building? building))
                                {
                                    building!.Functions.Add(function);
                                }
                            }
                        }
                    }
                }

                resultCode = await reader.ReadFirstOrDefaultAsync<int>();

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        queryResult = SqlQueryResult.RecordDidNotExist;
                        break;
                    case 3:
                        queryResult = SqlQueryResult.SubRecordDidNotExist;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, response);
            }
        }

        /// <summary>
        /// Retrieves a list of organizations to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListOrganizationsForDropdownAsync(string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblOrganizations",
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
from tblOrganizations
where Deleted = 0 
{whereQuery}
order by Name
";

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// Retrieves a list of organizations which are NOT allocated to a user to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListOrganizationsUnallocatedToUserForDropdownAsync(Guid uid, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblOrganizations",
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
from tblOrganizations
where Deleted = 0
and not exists
(
    select *
    from tblUserOrganizationJoin
    where tblUserOrganizationJoin.Uid = @uid
    and tblOrganizations.id = tblUserOrganizationJoin.OrganizationId
)
{whereQuery}
order by Name
";
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of organizations to be used for displaying a data table.
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<Organization>> ListOrganizationsForDataTableAsync(int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblOrganizations",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }
                string sql = "";

                if (string.IsNullOrEmpty(whereQuery))
                {
                    // Queries without search
                    sql += $@"
-- Get total number of organizations in database
-- Note: The index 'IX_tblOrganizations_NameAsc' has a filter for Deleted = 0
select convert(int, rows)
from sys.partitions
where object_id = Object_Id('tblOrganizations')
and index_id = IndexProperty(Object_Id('tblOrganizations'), 'IX_tblOrganizations_NameAsc', 'IndexID')
";
                }
                else
                {
                    // Queries with search
                    sql += $@"
-- Get total number of organizations in database matching search term
select count(*)
from tblOrganizations
where Deleted = 0
{whereQuery}
";
                }

                sql += $@"
-- Get data
;with pg as
(
    select id
    from tblOrganizations
    where Deleted = 0
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      --,LogoImageUrl -- Not used
      --,LogoImageStorageId -- Not used
      ,AutomaticUserInactivityEnabled
      ,CheckInEnabled
      ,MaxCapacityEnabled
      ,WorkplacePortalEnabled
      ,WorkplaceAccessRequestsEnabled
      ,WorkplaceInductionsEnabled
      ,Enforce2faEnabled
      ,DisableLocalLoginEnabled
      ,Disabled
      ,ConcurrencyKey
from tblOrganizations
where exists
(
    select 1
    from pg
    where pg.id = tblOrganizations.id
)
and Deleted = 0
order by tblOrganizations.{sortColumn}
--option (recompile)
";
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<Organization> result = new DataTableResponse<Organization>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<Organization>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified organization from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<Organization?> GetOrganizationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,LogoImageUrl
      ,LogoImageStorageId
      ,AutomaticUserInactivityEnabled
      ,CheckInEnabled
      ,MaxCapacityEnabled
      ,WorkplacePortalEnabled
      ,WorkplaceAccessRequestsEnabled
      ,WorkplaceInductionsEnabled
      ,Enforce2faEnabled
      ,DisableLocalLoginEnabled
      ,Disabled
      ,ConcurrencyKey
from tblOrganizations
where Deleted = 0
and id = @id

if @@ROWCOUNT = 1
begin
    select DomainName
    from tblOrganizationDomains
    where OrganizationId = @id
    order by DomainName
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                Organization? data = await gridReader.ReadFirstOrDefaultAsync<Organization?>();

                if (data is not null && !gridReader.IsConsumed)
                {
                    data.Domains = (await gridReader.ReadAsync<string>()).AsList();
                }

                return data;
            }
        }

        /// <summary>
        /// <para>Returns true if the specified organization exists.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsOrganizationExistsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblOrganizations
    where Deleted = 0
    and id = @id
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Creates an organization.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Organization?, List<OrganizationDomainCollision>?)> CreateOrganizationAsync(CreateOrganizationRequest request,
            ContentInspectorResultWithMemoryStream? organizationLogoContentInspectorResult, ContentInspectorResultWithMemoryStream? buildingFeatureImageContentInspectorResult,
            Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Create Organization";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int

declare @_organizationDomainsData table
(
    LogId uniqueidentifier
   ,DomainName nvarchar(252)
)

declare @_domainsExistData table
(
    OrganizationId uniqueidentifier
   ,DomainName nvarchar(252)
)

insert into @_organizationDomainsData
(LogId, DomainName)
values
");
                for (int i = 0; i < request.Domains!.Count; ++i)
                {
                    if (i > 0)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine($"(@domainLogId{i}, @domainName{i})");
                    parameters.Add($"@domainLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                    parameters.Add($"@domainName{i}", request.Domains[i], DbType.String, ParameterDirection.Input, 252);
                }

                sql.Append(@"
begin transaction

-- Check if any of the domains for the new organization exist in the database already
insert into @_domainsExistData
(DomainName, OrganizationId)
select DomainName, OrganizationId
from tblOrganizationDomains with (updlock, serializable)
where exists
(
    select *
    from @_organizationDomainsData d
    where d.DomainName = tblOrganizationDomains.DomainName
)

if @@ROWCOUNT = 0
begin
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
        insert into tblOrganizations
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,Name
        ,AutomaticUserInactivityEnabled
        ,CheckInEnabled
        ,MaxCapacityEnabled
        ,WorkplacePortalEnabled
        ,WorkplaceAccessRequestsEnabled
        ,WorkplaceInductionsEnabled
        ,Enforce2faEnabled
        ,DisableLocalLoginEnabled
        ,Disabled)
        select @id
              ,@_now
              ,@_now
              ,@name
              ,@automaticUserInactivityEnabled
              ,@checkInEnabled
              ,@maxCapacityEnabled
              ,@workplacePortalEnabled
              ,@workplaceAccessRequestsEnabled
              ,@workplaceInductionsEnabled
              ,@enforce2faEnabled
              ,@disableLocalLoginEnabled
              ,@disabled
        where not exists
        (
            select *
            from tblOrganizations
            where Deleted = 0
            and Name = @name
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            -- Insert domains
            insert into tblOrganizationDomains
            (DomainName, OrganizationId, InsertDateUtc)
            select DomainName, @id, @_now
            from @_organizationDomainsData

            -- Insert to domains log
            insert into tblOrganizationDomains_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,DomainName
            ,OrganizationId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select LogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,DomainName
                  ,@id
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_organizationDomainsData

            -- Insert to organizations log
            insert into tblOrganizations_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Name
            ,AutomaticUserInactivityEnabled
            ,CheckInEnabled
            ,MaxCapacityEnabled
            ,WorkplacePortalEnabled
            ,WorkplaceAccessRequestsEnabled
            ,WorkplaceInductionsEnabled
            ,Enforce2faEnabled
            ,DisableLocalLoginEnabled
            ,Disabled
            ,Deleted
            ,LogAction)
            select @logid
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@id -- OrganizationId
                  ,@name
                  ,@automaticUserInactivityEnabled
                  ,@checkInEnabled
                  ,@maxCapacityEnabled
                  ,@workplacePortalEnabled
                  ,@workplaceAccessRequestsEnabled
                  ,@workplaceInductionsEnabled
                  ,@enforce2faEnabled
                  ,@disableLocalLoginEnabled
                  ,@disabled
                  ,0 -- Deleted
                  ,'Insert' -- LogAction

            -- Insert region
            insert into tblRegions
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Name
            ,OrganizationId)
            select @regionId
                  ,@_now
                  ,@_now
                  ,@regionName
                  ,@id -- OrganizationId

            -- Insert to region log
            insert into tblRegions_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,RegionId
            ,Name
            ,OrganizationId
            ,Deleted
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @regionLogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@regionId
                  ,@regionName
                  ,@id -- OrganizationId
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblRegionHistories for the region we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblRegionHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,RegionId
            ,Name
            ,StartDateUtc
            ,EndDateUtc)
            select @regionHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@id -- OrganizationId
                  ,@regionId
                  ,@regionName
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the region history for the new region
            insert into tblRegionHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,RegionHistoryId
            ,RegionId
            ,Name
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @regionHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@id -- OrganizationId
                  ,@regionHistoryId
                  ,@regionId
                  ,@regionName
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert building
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
            select @buildingId
                  ,@_now
                  ,@_now
                  ,@buildingName
                  ,@id -- OrganizationId
                  ,@regionId
                  ,@buildingAddress
                  ,@buildingLatitude
                  ,@buildingLongitude
                  ,@buildingTimezone
                  ,@buildingFacilitiesManagementEmail
                  ,@buildingFacilitiesManagementEmailDisplayName
                  ,null -- FeatureImageUrl
                  ,null -- FeatureImageStorageId
                  ,null -- FeatureThumbnailUrl
                  ,null -- FeatureThumbnailStorageId
                  ,null -- MapImageUrl
                  ,null -- MapImageStorageId
                  ,null -- MapThumbnailUrl
                  ,null -- MapThumbnailStorageId
                  ,@buildingCheckInEnabled
                  ,@buildingCheckInQRCode
                  ,@buildingAccessCardCheckInWithBookingMessage
                  ,@buildingAccessCardCheckInWithoutBookingMessage
                  ,@buildingQrCodeCheckInWithBookingMessage
                  ,@buildingQrCodeCheckInWithoutBookingMessage
                  ,@buildingCheckInReminderEnabled
                  ,@buildingCheckInReminderTime
                  ,@buildingCheckInReminderMessage
                  ,@buildingAutoUserInactivityEnabled
                  ,@buildingAutoUserInactivityDurationDays
                  ,@buildingAutoUserInactivityScheduledIntervalMonths
                  ,@buildingAutoUserInactivityScheduleStartDateUtc
                  ,null -- AutoUserInactivityScheduleLastRunDateUtc
                  ,@buildingMaxCapacityEnabled
                  ,@buildingMaxCapacityUsers
                  ,@buildingMaxCapacityNotificationMessage
                  ,@buildingDeskBookingReminderEnabled
                  ,@buildingDeskBookingReminderTime
                  ,@buildingDeskBookingReminderMessage
                  ,@buildingDeskBookingReservationDateRangeEnabled
                  ,@buildingDeskBookingReservationDateRangeForUser
                  ,@buildingDeskBookingReservationDateRangeForAdmin
                  ,@buildingDeskBookingReservationDateRangeForSuperAdmin

            -- Insert to building log
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
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @buildingLogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@buildingId
                  ,@buildingName
                  ,@id -- OrganizationId
                  ,@regionId
                  ,@buildingAddress
                  ,@buildingLatitude
                  ,@buildingLongitude
                  ,@buildingTimezone
                  ,@buildingFacilitiesManagementEmail
                  ,@buildingFacilitiesManagementEmailDisplayName
                  ,null -- FeatureImageUrl
                  ,null -- FeatureImageStorageId
                  ,null -- FeatureThumbnailUrl
                  ,null -- FeatureThumbnailStorageId
                  ,null -- MapImageUrl
                  ,null -- MapImageStorageId
                  ,null -- MapThumbnailUrl
                  ,null -- MapThumbnailStorageId
                  ,@buildingCheckInEnabled
                  ,@buildingCheckInQRCode
                  ,@buildingAccessCardCheckInWithBookingMessage
                  ,@buildingAccessCardCheckInWithoutBookingMessage
                  ,@buildingQrCodeCheckInWithBookingMessage
                  ,@buildingQrCodeCheckInWithoutBookingMessage
                  ,@buildingCheckInReminderEnabled
                  ,@buildingCheckInReminderTime
                  ,@buildingCheckInReminderMessage
                  ,@buildingAutoUserInactivityEnabled
                  ,@buildingAutoUserInactivityDurationDays
                  ,@buildingAutoUserInactivityScheduledIntervalMonths
                  ,@buildingAutoUserInactivityScheduleStartDateUtc
                  ,null -- AutoUserInactivityScheduleLastRunDateUtc
                  ,@buildingMaxCapacityEnabled
                  ,@buildingMaxCapacityUsers
                  ,@buildingMaxCapacityNotificationMessage
                  ,@buildingDeskBookingReminderEnabled
                  ,@buildingDeskBookingReminderTime
                  ,@buildingDeskBookingReminderMessage
                  ,@buildingDeskBookingReservationDateRangeEnabled
                  ,@buildingDeskBookingReservationDateRangeForUser
                  ,@buildingDeskBookingReservationDateRangeForAdmin
                  ,@buildingDeskBookingReservationDateRangeForSuperAdmin
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId

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
                  ,@id -- OrganizationId
                  ,@buildingId
                  ,@buildingName
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
                  ,@id -- OrganizationId
                  ,@buildingHistoryId
                  ,@buildingId
                  ,@buildingName
                  ,@regionId
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId

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
                  ,@buildingId
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
                  ,@id -- OrganizationId
                  ,@functionId
                  ,@functionName
                  ,@buildingId
                  ,@functionHtmlColor
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
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
                  ,@id -- OrganizationId
                  ,@functionId -- FunctionId
                  ,@functionName
                  ,@buildingId
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
                  ,@id -- OrganizationId
                  ,@functionHistoryId
                  ,@functionId -- FunctionId
                  ,@functionName
                  ,@buildingId
                  ,@functionHtmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
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
                  ,@buildingId
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
                  ,@id -- OrganizationId
                  ,d.id -- FacilitiesManagementRequestTypeId
                  ,@buildingId
                  ,d.Name
                  ,0 -- Deleted
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
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
            select @id
                  ,@buildingId
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
                  ,@id
                  ,@buildingId
                  ,d.TemplateName
                  ,null -- HtmlContent
                  ,null -- TextContent
                  ,'Insert' -- LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_workplaceEmailTemplateContent d
            left join logIds l
            on d.TemplateName = l.TemplateName
        end
        else
        begin
            -- Record already exists
            set @_result = 2
        end
    
        commit
    end
end
else
begin
    rollback

    -- One or more email domains already belong to other organization(s). Don't insert any data.
    set @_result = 3
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select id
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,Name
          ,AutomaticUserInactivityEnabled
          ,CheckInEnabled
          ,MaxCapacityEnabled
          ,WorkplacePortalEnabled
          ,WorkplaceAccessRequestsEnabled
          ,WorkplaceInductionsEnabled
          ,Enforce2faEnabled
          ,DisableLocalLoginEnabled
          ,Disabled
          ,ConcurrencyKey
    from tblOrganizations
    where Deleted = 0
    and id = @id

    if @@ROWCOUNT = 1
    begin
        select DomainName
        from tblOrganizationDomains
        where OrganizationId = @id
        order by DomainName
    end
end
else if @_result = 3
begin
    -- Select existing domains
    select d.OrganizationId
          ,d.DomainName
          ,tblOrganizations.Name as OrganizationName
    from @_domainsExistData d
    left join tblOrganizations
    on d.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
end
");
                Guid organizationId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid organizationLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblRegionHistories and tblRegionHistories_Log
                Guid regionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblBuildingHistories, tblBuildingHistories_Log,
                // tblFunctionHistories and tblFunctionHistories_Log
                Guid buildingHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid buildingHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                parameters.Add("@lockResourceName", $"tblOrganizations_Name_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@automaticUserInactivityEnabled", request.AutomaticUserInactivityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInEnabled", request.CheckInEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@maxCapacityEnabled", request.MaxCapacityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplacePortalEnabled", request.WorkplacePortalEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplaceAccessRequestsEnabled", request.WorkplaceAccessRequestsEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplaceInductionsEnabled", request.WorkplaceInductionsEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@enforce2faEnabled", request.Enforce2faEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@disableLocalLoginEnabled", request.DisableLocalLoginEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@disabled", request.Disabled, DbType.Boolean, ParameterDirection.Input);

                parameters.Add("@regionId", regionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionName", request.RegionName, DbType.String, ParameterDirection.Input, 100);

                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingName", request.BuildingName, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@buildingAddress", request.BuildingAddress, DbType.String, ParameterDirection.Input, 250);
                parameters.Add("@buildingLatitude", request.BuildingLatitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@buildingLongitude", request.BuildingLongitude, DbType.Decimal, ParameterDirection.Input, precision: 10, scale: 7);
                parameters.Add("@buildingTimezone", request.BuildingTimezone, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@buildingFacilitiesManagementEmail", request.BuildingFacilitiesManagementEmail, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@buildingFacilitiesManagementEmailDisplayName", request.BuildingFacilitiesManagementEmailDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@buildingCheckInEnabled", request.BuildingCheckInEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingCheckInQRCode", request.BuildingCheckInQRCode, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@buildingAccessCardCheckInWithBookingMessage", request.BuildingAccessCardCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingAccessCardCheckInWithoutBookingMessage", request.BuildingAccessCardCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingQrCodeCheckInWithBookingMessage", request.BuildingQRCodeCheckInWithBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingQrCodeCheckInWithoutBookingMessage", request.BuildingQRCodeCheckInWithoutBookingMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingCheckInReminderEnabled", request.BuildingCheckInReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingCheckInReminderTime", request.BuildingCheckInReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@buildingCheckInReminderMessage", request.BuildingCheckInReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingAutoUserInactivityEnabled", request.BuildingAutoUserInactivityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingAutoUserInactivityDurationDays", request.BuildingAutoUserInactivityDurationDays, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@buildingAutoUserInactivityScheduledIntervalMonths", request.BuildingAutoUserInactivityScheduledIntervalMonths, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@buildingAutoUserInactivityScheduleStartDateUtc", request.BuildingAutoUserInactivityScheduleStartDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@buildingMaxCapacityEnabled", request.BuildingMaxCapacityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingMaxCapacityUsers", request.BuildingMaxCapacityUsers, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@buildingMaxCapacityNotificationMessage", request.BuildingMaxCapacityNotificationMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingDeskBookingReminderEnabled", request.BuildingDeskBookingReminderEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingDeskBookingReminderTime", request.BuildingDeskBookingReminderTime, DbType.Time, ParameterDirection.Input, 0);
                parameters.Add("@buildingDeskBookingReminderMessage", request.BuildingDeskBookingReminderMessage, DbType.String, ParameterDirection.Input, 2000);
                parameters.Add("@buildingDeskBookingReservationDateRangeEnabled", request.BuildingDeskBookingReservationDateRangeEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@buildingDeskBookingReservationDateRangeForUser", request.BuildingDeskBookingReservationDateRangeForUser, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@buildingDeskBookingReservationDateRangeForAdmin", request.BuildingDeskBookingReservationDateRangeForAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@buildingDeskBookingReservationDateRangeForSuperAdmin", request.BuildingDeskBookingReservationDateRangeForSuperAdmin, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@functionId", functionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionName", request.FunctionName, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@functionHtmlColor", request.FunctionHtmlColor, DbType.AnsiString, ParameterDirection.Input, 7);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@regionHistoryId", regionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionHistoryLogId", regionHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingHistoryId", buildingHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingHistoryLogId", buildingHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryId", functionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryLogId", functionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logId", organizationLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionLogId", regionLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingLogId", buildingLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionLogId", functionLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Organization? data = null;
                List<OrganizationDomainCollision>? organizationDomainCollisions = null;

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        // If insert was successful, also get the data.
                        data = await gridReader.ReadFirstOrDefaultAsync<Organization>();

                        if (data is not null)
                        {
                            if (!gridReader.IsConsumed)
                            {
                                data.Domains = (await gridReader.ReadAsync<string>()).AsList();
                            }

                            // Store organization logo
                            if (request.LogoImage != null)
                            {
                                // Store logo image file to disk
                                (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile) = await StoreOrganizationLogoImageAsync(sqlConnection, organizationLogoContentInspectorResult, organizationId, organizationLogId, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null)
                                {
                                    // Set FileUrl in response to be returned.
                                    data.LogoImageUrl = storedImageFile.FileUrl;
                                    data.LogoImageStorageId = storedImageFile.Id;
                                }
                            }
                        }

                        // Store FeatureImage for created Building
                        if (request.BuildingFeatureImage is not null)
                        {
                            // Store building feature image file to disk
                            (SqlQueryResult _, StoredImageFile? _, StoredImageFile? _) = await StoreBuildingFeatureImageAsync(sqlConnection,
                                buildingFeatureImageContentInspectorResult, buildingId, organizationLogId, organizationId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                        }

                        /*
                        // Download map image using google maps API
                        ContentInspectorResultWithMemoryStream? mapImageContentInspectorResult;
                        MemoryStream memoryStream = new MemoryStream();

                        HttpClient httpClient = _httpClientFactory.CreateClient("googlemaps");

                        // Example URL: https://maps.googleapis.com/maps/api/staticmap?size=640x320&scale=1&center=-37.8227503,144.968869&format=png&maptype=roadmap&key=AIzaxxx&markers=color:red%7C-37.8227503,144.968869

                        Dictionary<string, StringValues> queryParameters = new Dictionary<string, StringValues>
                        {
                            { "size", new StringValues($"{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageWidth}x{_appSettings.ImageUpload.ObjectRestrictions.BuildingMapImage.MaxImageHeight}") },
                            { "scale", new StringValues("1") },
                            { "center", new StringValues($"{request.BuildingLatitude:0.0000000},{request.BuildingLongitude:0.0000000}") },
                            { "format", new StringValues($"png") },
                            { "maptype", new StringValues($"roadmap") },
                            { "key", new StringValues(_appSettings.GoogleMaps.GoogleMapsApiKey) },
                            { "markers", new StringValues($"color:red|{request.BuildingLatitude:0.0000000},{request.BuildingLongitude:0.0000000}") },
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
                                    (SqlQueryResult _, StoredImageFile? _, StoredImageFile? _) =
                                        await StoreMapImageAsync(sqlConnection,
                                        mapImageContentInspectorResult,
                                        buildingId, organizationLogId, organizationId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                }
                            }
                        }
                        */
                        break;
                    case 2:
                        // Organization exists
                        queryResult = SqlQueryResult.RecordAlreadyExists;
                        break;
                    case 3:
                        // Organization domains exist
                        queryResult = SqlQueryResult.SubRecordAlreadyExists;

                        // If domains already exist, also get the list of existing domains.
                        organizationDomainCollisions = (await gridReader.ReadAsync<OrganizationDomainCollision>()).AsList();
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data, organizationDomainCollisions);
            }
        }

        private async Task<(SqlQueryResult, StoredImageFile?)> StoreOrganizationLogoImageAsync(SqlConnection sqlConnection, ContentInspectorResultWithMemoryStream? contentInspectorResult,
            Guid organizationId, Guid logId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            SqlQueryResult sqlQueryResult = SqlQueryResult.UnknownError;
            StoredImageFile? storedImageFile = null;

            if (ImageStorageHelpers.IsValidVectorImageExtension(contentInspectorResult!.InspectedExtension!))
            {
                // if it is an svg image
                (SqlQueryResult svgDimensionQueryResult, short? widthPixels, short? heightPixels) = _imageStorageRepository.GetSvgImageDimensions(contentInspectorResult);

                if (svgDimensionQueryResult == SqlQueryResult.Ok)
                {
                    (sqlQueryResult, storedImageFile) =
                    await _imageStorageRepository.WriteSvgImageAsync(
                        widthPixels!.Value,
                        heightPixels!.Value,
                        contentInspectorResult,
                        ImageStorageRelatedObjectType.OrganizationLogo,
                        organizationId,
                        organizationId,
                        "tblOrganizations",
                        logId,
                        adminUserUid,
                        adminUserDisplayName,
                        remoteIpAddress);
                }
                else
                {
                    sqlQueryResult = svgDimensionQueryResult;
                }
            }
            else if (ImageStorageHelpers.IsValidImageExtension(contentInspectorResult!.InspectedExtension!))
            {
                (sqlQueryResult, storedImageFile) =
                    await _imageStorageRepository.WriteImageAsync(
                        _appSettings.ImageUpload.ObjectRestrictions.OrganizationLogo.MaxImageWidth,
                        _appSettings.ImageUpload.ObjectRestrictions.OrganizationLogo.MaxImageHeight,
                        true,
                        false,
                        contentInspectorResult,
                        ImageStorageRelatedObjectType.OrganizationLogo,
                        organizationId,
                        organizationId,
                        "tblOrganizations",
                        logId,
                        adminUserUid,
                        adminUserDisplayName,
                        remoteIpAddress);
            }

            if (sqlQueryResult != SqlQueryResult.Ok)
            {
                return (sqlQueryResult, storedImageFile);
            }
            else if (storedImageFile is null)
            {
                // This should never happen
                return (SqlQueryResult.UnknownError, storedImageFile);
            }

            string sql = @"
update tblOrganizations
set LogoImageUrl = @fileUrl
   ,LogoImageStorageId = @imageStorageId
where Deleted = 0
and id = @organizationId

update tblOrganizations_Log
set LogoImageUrl = @fileUrl
   ,LogoImageStorageId = @imageStorageId
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@fileUrl", storedImageFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@imageStorageId", storedImageFile.Id, DbType.Guid, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);

            return (sqlQueryResult, storedImageFile);
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
                    "tblOrganizations",
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
                    "tblOrganizations",
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

        /// <summary>
        /// <para>Updates the specified organization.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Organization?, List<OrganizationDomainCollision>?)> UpdateOrganizationAsync(UpdateOrganizationRequest request, ContentInspectorResultWithMemoryStream? contentInspectorResult, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Organization";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder();
                sql.Append(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_lockResult int

declare @_data table
(
    Name nvarchar(100)
   ,LogoImageUrl varchar(255)
   ,LogoImageStorageId uniqueidentifier
   ,AutomaticUserInactivityEnabled bit
   ,CheckInEnabled bit
   ,MaxCapacityEnabled bit
   ,WorkplacePortalEnabled bit
   ,WorkplaceAccessRequestsEnabled bit
   ,WorkplaceInductionsEnabled bit
   ,Enforce2faEnabled bit
   ,DisableLocalLoginEnabled bit
   ,Disabled bit
   ,OldName nvarchar(100)
   ,OldLogoImageUrl varchar(255)
   ,OldLogoImageStorageId uniqueidentifier
   ,OldAutomaticUserInactivityEnabled bit
   ,OldCheckInEnabled bit
   ,OldMaxCapacityEnabled bit
   ,OldWorkplacePortalEnabled bit
   ,OldWorkplaceAccessRequestsEnabled bit
   ,OldWorkplaceInductionsEnabled bit
   ,OldEnforce2faEnabled bit
   ,OldDisableLocalLoginEnabled bit
   ,OldDisabled bit
)

declare @_organizationDomainsData table
(
    LogId uniqueidentifier
   ,DomainName nvarchar(252)
)

declare @_organizationDomainsLogs table
(
    DomainName nvarchar(252)
   ,LogAction varchar(6)
)

declare @_organizationDomainsLogIds table
(
    LogId uniqueidentifier
)

declare @_domainsExistData table
(
    DomainName nvarchar(252)
   ,OrganizationId uniqueidentifier
)

insert into @_organizationDomainsData
(LogId, DomainName)
values
");
                for (int i = 0; i < request.Domains!.Count; ++i)
                {
                    if (i > 0)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine($"(@domainLogId{i}, @domainName{i})");
                    parameters.Add($"@domainLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                    parameters.Add($"@domainName{i}", request.Domains[i], DbType.String, ParameterDirection.Input, 252);
                }

                sql.Append(@"
begin transaction

-- Check if any of the domains for the new organization exist in the database already
insert into @_domainsExistData
(DomainName, OrganizationId)
select DomainName, OrganizationId
from tblOrganizationDomains with (updlock, serializable)
where OrganizationId != @id
and exists
(
    select *
    from @_organizationDomainsData d
    where d.DomainName = tblOrganizationDomains.DomainName
)

if @@ROWCOUNT = 0
begin
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
        update tblOrganizations
        set UpdatedDateUtc = @_now
           ,Name = @name
");
                if (request.LogoImageChanged!.Value && request.LogoImage is null)
                {
                    // Clear logo image if it's being removed
                    sql.Append(@"
           ,LogoImageUrl = null
           ,LogoImageStorageId = null
");
                }

                sql.Append(@"
           ,AutomaticUserInactivityEnabled = @automaticUserInactivityEnabled
           ,CheckInEnabled = @checkInEnabled
           ,MaxCapacityEnabled = @maxCapacityEnabled
           ,WorkplacePortalEnabled = @workplacePortalEnabled
           ,WorkplaceAccessRequestsEnabled = @workplaceAccessRequestsEnabled
           ,WorkplaceInductionsEnabled = @workplaceInductionsEnabled
           ,Enforce2faEnabled = @enforce2faEnabled
           ,DisableLocalLoginEnabled = @disableLocalLoginEnabled
           ,Disabled = @disabled
        output inserted.Name
              ,inserted.LogoImageUrl
              ,inserted.LogoImageStorageId
              ,inserted.AutomaticUserInactivityEnabled
              ,inserted.CheckInEnabled
              ,inserted.MaxCapacityEnabled
              ,inserted.WorkplacePortalEnabled
              ,inserted.WorkplaceAccessRequestsEnabled 
              ,inserted.WorkplaceInductionsEnabled 
              ,inserted.Enforce2faEnabled
              ,inserted.DisableLocalLoginEnabled
              ,inserted.Disabled
              ,deleted.Name
              ,deleted.LogoImageUrl
              ,deleted.LogoImageStorageId
              ,deleted.AutomaticUserInactivityEnabled
              ,deleted.CheckInEnabled
              ,deleted.MaxCapacityEnabled
              ,deleted.WorkplacePortalEnabled
              ,deleted.WorkplaceAccessRequestsEnabled 
              ,deleted.WorkplaceInductionsEnabled 
              ,deleted.Enforce2faEnabled
              ,deleted.DisableLocalLoginEnabled
              ,deleted.Disabled
              into @_data
        where Deleted = 0
        and id = @id
        and ConcurrencyKey = @concurrencyKey
        and not exists
        (
            select *
            from tblOrganizations
            where Deleted = 0
            and Name = @name
            and id != @id
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            -- Delete removed domains and insert into log table variable
            delete from tblOrganizationDomains
            output deleted.DomainName
                  ,'Delete' -- LogAction
                  into @_organizationDomainsLogs
            where OrganizationId = @id
            and not exists
            (
                select *
                from @_organizationDomainsData d
                where tblOrganizationDomains.DomainName = d.DomainName
            )

            -- Insert new domains into log table variable
            insert into @_organizationDomainsLogs
            (DomainName, LogAction)
            select DomainName, 'Insert'
            from @_organizationDomainsData d
            where not exists
            (
                select *
                from tblOrganizationDomains destTable
                where d.DomainName = destTable.DomainName
            )

            -- Insert new domains
            insert into tblOrganizationDomains
            (DomainName, OrganizationId, InsertDateUtc)
            select DomainName, @id, @_now
            from @_organizationDomainsData d
            where not exists
            (
                select *
                from tblOrganizationDomains destTable
                where d.DomainName = destTable.DomainName
            )

            -- Insert to domains log
            insert into tblOrganizationDomains_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,DomainName
            ,OrganizationId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            output inserted.id
                   into @_organizationDomainsLogIds
            select newid() -- Temporary ID to be overwritten later
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,DomainName
                  ,@id
                  ,LogAction
                  ,'tblOrganizations' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_organizationDomainsLogs

            -- Insert to organizations log
            insert into tblOrganizations_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Name
            ,LogoImageUrl
            ,LogoImageStorageId
            ,AutomaticUserInactivityEnabled
            ,CheckInEnabled
            ,MaxCapacityEnabled
            ,WorkplacePortalEnabled
            ,WorkplaceAccessRequestsEnabled 
            ,WorkplaceInductionsEnabled 
            ,Enforce2faEnabled
            ,DisableLocalLoginEnabled
            ,Disabled
            ,Deleted
            ,OldName
            ,OldLogoImageUrl
            ,OldLogoImageStorageId
            ,OldAutomaticUserInactivityEnabled
            ,OldCheckInEnabled
            ,OldMaxCapacityEnabled
            ,OldWorkplacePortalEnabled
            ,OldWorkplaceAccessRequestsEnabled 
            ,OldWorkplaceInductionsEnabled 
            ,OldEnforce2faEnabled
            ,OldDisableLocalLoginEnabled
            ,OldDisabled
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
                  ,d.LogoImageUrl
                  ,d.LogoImageStorageId
                  ,d.AutomaticUserInactivityEnabled
                  ,d.CheckInEnabled
                  ,d.MaxCapacityEnabled
                  ,d.WorkplacePortalEnabled
                  ,d.WorkplaceAccessRequestsEnabled 
                  ,d.WorkplaceInductionsEnabled 
                  ,d.Enforce2faEnabled
                  ,d.DisableLocalLoginEnabled
                  ,d.Disabled
                  ,0 -- Deleted
                  ,d.OldName
                  ,d.OldLogoImageUrl
                  ,d.OldLogoImageStorageId
                  ,d.OldAutomaticUserInactivityEnabled
                  ,d.OldCheckInEnabled
                  ,d.OldMaxCapacityEnabled
                  ,d.OldWorkplacePortalEnabled
                  ,d.OldWorkplaceAccessRequestsEnabled 
                  ,d.OldWorkplaceInductionsEnabled 
                  ,d.OldEnforce2faEnabled
                  ,d.OldDisableLocalLoginEnabled
                  ,d.OldDisabled
                  ,0 -- OldDeleted
                  ,'Update' -- LogAction
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
    rollback

    -- One or more email domains already belong to other organization(s). Don't change any data.
    set @_result = 3
end

select @_result

-- Select organization domain log IDs to be overwritten
select LogId
from @_organizationDomainsLogIds

-- Select existing domains
select d.OrganizationId
      ,d.DomainName
      ,tblOrganizations.Name as OrganizationName
from @_domainsExistData d
left join tblOrganizations
on d.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
order by DomainName, OrganizationName

-- Select old ImageStorageIds so we can delete off disk
select OldLogoImageStorageId
from @_data

-- Select row to return with the API result
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,LogoImageUrl
      ,LogoImageStorageId
      ,AutomaticUserInactivityEnabled
      ,CheckInEnabled
      ,MaxCapacityEnabled
      ,WorkplacePortalEnabled
      ,WorkplaceAccessRequestsEnabled 
      ,WorkplaceInductionsEnabled 
      ,Enforce2faEnabled
      ,DisableLocalLoginEnabled
      ,Disabled
      ,ConcurrencyKey
from tblOrganizations
where Deleted = 0
and id = @id

if @@ROWCOUNT = 1
begin
    select DomainName
    from tblOrganizationDomains
    where OrganizationId = @id
    order by DomainName
end
");
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                parameters.Add("@lockResourceName", $"tblOrganizations_Name_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@automaticUserInactivityEnabled", request.AutomaticUserInactivityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInEnabled", request.CheckInEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@maxCapacityEnabled", request.MaxCapacityEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplacePortalEnabled", request.WorkplacePortalEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplaceAccessRequestsEnabled", request.WorkplaceAccessRequestsEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@workplaceInductionsEnabled", request.WorkplaceInductionsEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@enforce2faEnabled", request.Enforce2faEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@disableLocalLoginEnabled", request.DisableLocalLoginEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@disabled", request.Disabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                List<Guid> organizationDomainsLog = (await gridReader.ReadAsync<Guid>()).AsList();
                List<OrganizationDomainCollision>? organizationDomainCollisions = (await gridReader.ReadAsync<OrganizationDomainCollision>()).AsList();
                Guid? oldLogoImageStorageId = await gridReader.ReadFirstOrDefaultAsync<Guid?>();
                Organization? data = await gridReader.ReadFirstOrDefaultAsync<Organization>();

                // If record existed, also get the domains
                if (data is not null && !gridReader.IsConsumed)
                {
                    data.Domains = (await gridReader.ReadAsync<string>()).AsList();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        // Update log ids generated using newid() with RT.Comb instead
                        await Toolbox.UpdateLogGuidsWithRTCombAsync(sqlConnection, organizationDomainsLog, "tblOrganizationDomains_Log");

                        // Check if logo image was deleted
                        if (request.LogoImageChanged!.Value && request.LogoImage is null)
                        {
                            // Remove the image from database and delete from disk if required
                            if (oldLogoImageStorageId is not null)
                            {
                                await _imageStorageRepository.DeleteImageAsync(oldLogoImageStorageId.Value, "tblOrganizations", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                            }
                        }

                        // Check if logo image was replaced
                        if (request.LogoImageChanged!.Value && request.LogoImage is not null)
                        {
                            // Store logo image file to disk
                            (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile) = await StoreOrganizationLogoImageAsync(sqlConnection, contentInspectorResult, request.id!.Value, logId, adminUserUid, adminUserDisplayName, remoteIpAddress);

                            if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null)
                            {
                                // Set FileUrl in response to be returned.
                                data!.LogoImageUrl = storedImageFile.FileUrl;
                                data!.LogoImageStorageId = storedImageFile.Id;
                            }

                            // Remove the image from database and delete from disk if required
                            if (oldLogoImageStorageId is not null)
                            {
                                await _imageStorageRepository.DeleteImageAsync(oldLogoImageStorageId.Value, "tblOrganizations", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                            }
                        }
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
                        queryResult = SqlQueryResult.SubRecordAlreadyExists;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data, organizationDomainCollisions);
            }
        }

        /// <summary>
        /// <para>Deletes the specified organization.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Organization?)> DeleteOrganizationAsync(DeleteOrganizationRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete Organization";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Name nvarchar(100)
   ,LogoImageUrl varchar(255)
   ,LogoImageStorageId uniqueidentifier
   ,AutomaticUserInactivityEnabled bit
   ,CheckInEnabled bit
   ,MaxCapacityEnabled bit
   ,WorkplacePortalEnabled bit
   ,WorkplaceAccessRequestsEnabled bit
   ,WorkplaceInductionsEnabled bit
   ,Enforce2faEnabled bit
   ,DisableLocalLoginEnabled bit
   ,Disabled bit
)

declare @_organizationDomainsLogs table
(
    DomainName nvarchar(252)
)

declare @_organizationDomainsLogIds table
(
    LogId uniqueidentifier
)

update tblOrganizations
set Deleted = 1
   ,UpdatedDateUtc = @_now
output inserted.Name
      ,inserted.LogoImageUrl
      ,inserted.LogoImageStorageId
      ,inserted.AutomaticUserInactivityEnabled
      ,inserted.CheckInEnabled
      ,inserted.MaxCapacityEnabled
      ,inserted.WorkplacePortalEnabled
      ,inserted.WorkplaceAccessRequestsEnabled
      ,inserted.WorkplaceInductionsEnabled
      ,inserted.Enforce2faEnabled
      ,inserted.DisableLocalLoginEnabled
      ,inserted.Disabled
      into @_data
where Deleted = 0
and id = @id
and ConcurrencyKey = @concurrencyKey

if @@ROWCOUNT = 1
begin
    set @_result = 1

    -- Delete removed domains and insert into log table variable
    delete from tblOrganizationDomains
    output deleted.DomainName
           into @_organizationDomainsLogs
    where OrganizationId = @id

    -- Insert to domains log
    insert into tblOrganizationDomains_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,DomainName
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    output inserted.id
           into @_organizationDomainsLogIds
    select newid() -- Temporary ID to be overwritten later
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@id
          ,DomainName
          ,'Delete' -- LogAction
          ,'tblOrganizations' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_organizationDomainsLogs

    -- Insert to Organizations log
    insert into tblOrganizations_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Name
    ,LogoImageUrl
    ,LogoImageStorageId
    ,AutomaticUserInactivityEnabled
    ,CheckInEnabled
    ,MaxCapacityEnabled
    ,WorkplacePortalEnabled
    ,WorkplaceAccessRequestsEnabled
    ,WorkplaceInductionsEnabled
    ,Enforce2faEnabled
    ,DisableLocalLoginEnabled
    ,Disabled
    ,Deleted
    ,OldName
    ,OldLogoImageUrl
    ,OldLogoImageStorageId
    ,OldAutomaticUserInactivityEnabled
    ,OldCheckInEnabled
    ,OldMaxCapacityEnabled
    ,OldWorkplacePortalEnabled
    ,OldWorkplaceAccessRequestsEnabled
    ,OldWorkplaceInductionsEnabled
    ,OldEnforce2faEnabled
    ,OldDisableLocalLoginEnabled
    ,OldDisabled
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
          ,d.LogoImageUrl
          ,d.LogoImageStorageId
          ,d.AutomaticUserInactivityEnabled
          ,d.CheckInEnabled
          ,d.MaxCapacityEnabled
          ,d.WorkplacePortalEnabled
          ,d.WorkplaceAccessRequestsEnabled
          ,d.WorkplaceInductionsEnabled
          ,d.Enforce2faEnabled
          ,d.DisableLocalLoginEnabled
          ,d.Disabled
          ,1 -- Deleted
          ,d.Name
          ,d.LogoImageUrl
          ,d.LogoImageStorageId
          ,d.AutomaticUserInactivityEnabled
          ,d.CheckInEnabled
          ,d.MaxCapacityEnabled
          ,d.WorkplacePortalEnabled
          ,d.WorkplaceAccessRequestsEnabled
          ,d.WorkplaceInductionsEnabled
          ,d.Enforce2faEnabled
          ,d.DisableLocalLoginEnabled
          ,d.Disabled
          ,0 -- OldDeleted
          ,'Delete'
    from @_data d
end
else
begin
    -- Record could not be deleted
    set @_result = 2
end

select @_result

if @_result != 1
begin
    -- Select existing row if delete was unsuccessful
    select id
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,Name
          ,AutomaticUserInactivityEnabled
          ,CheckInEnabled
          ,MaxCapacityEnabled
          ,WorkplacePortalEnabled
          ,WorkplaceAccessRequestsEnabled
          ,WorkplaceInductionsEnabled
          ,Enforce2faEnabled
          ,DisableLocalLoginEnabled
          ,Disabled
          ,ConcurrencyKey
    from tblOrganizations
    where Deleted = 0
    and id = @id
end
else
begin
    -- Select organization domain log IDs to be overwritten if delete was successful
    select LogId
    from @_organizationDomainsLogIds
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Organization? data = null;

                if (resultCode == 1)
                {
                    // If delete was successful, get Log IDs for deleted organization domains
                    List<Guid> organizationDomainsLog = (await gridReader.ReadAsync<Guid>()).AsList();

                    // Update log ids generated using newid() with RT.Comb instead
                    await Toolbox.UpdateLogGuidsWithRTCombAsync(sqlConnection, organizationDomainsLog, "tblOrganizationDomains_Log");
                }
                else
                {
                    // If delete was unsuccessful, also get the updated data to be returned in the response if available
                    data = await gridReader.ReadFirstOrDefaultAsync<Organization>();
                }

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

        /// <summary>
        /// <para>Retrieves the organization auto user inactivity settings for the specified organization from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<OrganizationAutoUserInactivitySetting?> GetOrganizationAutoUserInactivitySettingsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblOrganizationAutoUserInactivitySettings.OrganizationId
      ,tblOrganizations.Name as OrganizationName  
      ,tblOrganizationAutoUserInactivitySettings.InsertDateUtc
      ,tblOrganizationAutoUserInactivitySettings.UpdatedDateUtc
      ,tblOrganizationAutoUserInactivitySettings.AutoUserInactivityEnabled
      ,tblOrganizationAutoUserInactivitySettings.AutoUserInactivityDurationDays
      ,tblOrganizationAutoUserInactivitySettings.AutoUserInactivityScheduledIntervalMonths
      ,tblOrganizationAutoUserInactivitySettings.AutoUserInactivityScheduleStartDateUtc
      ,tblOrganizationAutoUserInactivitySettings.AutoUserInactivityScheduleLastRunDateUtc
      ,tblOrganizationAutoUserInactivitySettings.ConcurrencyKey
from tblOrganizationAutoUserInactivitySettings
inner join tblOrganizations
on tblOrganizationAutoUserInactivitySettings.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblOrganizationAutoUserInactivitySettings.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();

                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                OrganizationAutoUserInactivitySetting? organizationAutoUserInactivitySetting = await sqlConnection.QueryFirstOrDefaultAsync<OrganizationAutoUserInactivitySetting>(commandDefinition);

                return organizationAutoUserInactivitySetting;
            }
        }

        /// <summary>
        /// <para>Retrieves the organization Notifications settings for the specified organization from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<OrganizationNotificationsSetting?> GetOrganizationNotificationsSettingsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblOrganizationNotifications.OrganizationId
      ,tblOrganizations.Name as OrganizationName
      ,tblOrganizationNotifications.UpdatedDateUtc
      ,tblOrganizationNotifications.Enabled
      ,tblOrganizationNotifications.BookingModifiedByAdmin
      ,tblOrganizationNotifications.PermanentBookingAllocated
      ,tblOrganizationNotifications.CheckInReminderEnabled
      ,tblOrganizationNotifications.DeskBookingReminderEnabled
      ,tblOrganizationNotifications.ConcurrencyKey
from tblOrganizationNotifications
inner join tblOrganizations
on tblOrganizationNotifications.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblOrganizationNotifications.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                OrganizationNotificationsSetting? organizationNotificationsSetting = await sqlConnection.QueryFirstOrDefaultAsync<OrganizationNotificationsSetting>(commandDefinition);

                return organizationNotificationsSetting;
            }
        }

        /// <summary>
        /// <para>Updates the organization Notifications settings for the specified organization in the database.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, OrganizationNotificationsSetting?)> UpdateOrganizationNotificationsSettingsAsync(UpdateOrganizationNotificationsSettingsRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Organization Notifications Settings";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Enabled bit
   ,BookingModifiedByAdmin bit
   ,PermanentBookingAllocated bit
   ,CheckInReminderEnabled bit
   ,DeskBookingReminderEnabled bit
   ,OldEnabled bit
   ,OldBookingModifiedByAdmin bit
   ,OldPermanentBookingAllocated bit
   ,OldCheckInReminderEnabled bit
   ,OldDeskBookingReminderEnabled bit
)

insert into tblOrganizationNotifications
(OrganizationId
,InsertDateUtc
,UpdatedDateUtc
,Enabled
,BookingModifiedByAdmin
,PermanentBookingAllocated
,CheckInReminderEnabled
,DeskBookingReminderEnabled)
select @organizationId
      ,@_now -- InsertDateUtc
      ,@_now -- UpdatedDateUtc
      ,@enabled
      ,@bookingModifiedByAdmin
      ,@permanentBookingAllocated
      ,@checkInReminderEnabled
      ,@deskBookingReminderEnabled
from tblOrganizations
where tblOrganizations.Deleted = 0
and tblOrganizations.id = @organizationId
and not exists
(
    select top 1 *
    from tblOrganizationNotifications with (updlock, serializable)
    where tblOrganizationNotifications.OrganizationId = @organizationId
)

if @@ROWCOUNT = 0
begin
    update tblOrganizationNotifications
    set UpdatedDateUtc = @_now
       ,Enabled = @enabled
       ,BookingModifiedByAdmin = @bookingModifiedByAdmin
       ,PermanentBookingAllocated = @permanentBookingAllocated
       ,CheckInReminderEnabled = @checkInReminderEnabled
       ,DeskBookingReminderEnabled = @deskBookingReminderEnabled
    output inserted.Enabled
          ,inserted.BookingModifiedByAdmin
          ,inserted.PermanentBookingAllocated
          ,inserted.CheckInReminderEnabled
          ,inserted.DeskBookingReminderEnabled
          ,deleted.Enabled
          ,deleted.BookingModifiedByAdmin
          ,deleted.PermanentBookingAllocated
          ,deleted.CheckInReminderEnabled        
          ,deleted.DeskBookingReminderEnabled
          into @_data
    from tblOrganizationNotifications
    inner join tblOrganizations
    on tblOrganizationNotifications.OrganizationId = @organizationId
    and tblOrganizations.Deleted = 0
    where tblOrganizationNotifications.OrganizationId = @organizationId
    and tblOrganizationNotifications.ConcurrencyKey = @concurrencyKey
end
else
begin
    -- Table was empty and row was just inserted, so manually insert into @_data
    insert into @_data
    (Enabled
    ,BookingModifiedByAdmin
    ,PermanentBookingAllocated
    ,CheckInReminderEnabled
    ,DeskBookingReminderEnabled
    ,OldEnabled
    ,OldBookingModifiedByAdmin
    ,OldPermanentBookingAllocated
    ,OldCheckInReminderEnabled
    ,OldDeskBookingReminderEnabled)
    values
    (@enabled
    ,@bookingModifiedByAdmin
    ,@permanentBookingAllocated
    ,@checkInReminderEnabled
    ,@deskBookingReminderEnabled
    ,null -- OldEnabled
    ,null -- OldBookingModifiedByAdmin
    ,null -- OldPermanentBookingAllocated
    ,null -- OldCheckInReminderEnabled
    ,null) -- OldDeskBookingReminderEnabled 
end

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblOrganizationNotifications_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Enabled
    ,BookingModifiedByAdmin
    ,PermanentBookingAllocated
    ,CheckInReminderEnabled
    ,DeskBookingReminderEnabled
    ,OldEnabled
    ,OldBookingModifiedByAdmin
    ,OldPermanentBookingAllocated
    ,OldCheckInReminderEnabled    
    ,OldDeskBookingReminderEnabled
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,d.Enabled
          ,d.BookingModifiedByAdmin
          ,d.PermanentBookingAllocated
          ,d.CheckInReminderEnabled
          ,d.DeskBookingReminderEnabled
          ,d.OldEnabled
          ,d.OldBookingModifiedByAdmin
          ,d.OldPermanentBookingAllocated
          ,d.OldCheckInReminderEnabled
          ,d.OldDeskBookingReminderEnabled
          ,'Update' -- LogAction
          ,null -- CascadeFrom
          ,null -- CascadeLogId
    from @_data d
end
else
begin
    -- Record was not updated
    -- This means the settings row already existed and the specified ConcurrencyKey was wrong
    set @_result = 2
end

select @_result

select tblOrganizationNotifications.OrganizationId
      ,tblOrganizations.Name as OrganizationName
      ,tblOrganizationNotifications.UpdatedDateUtc
      ,tblOrganizationNotifications.Enabled
      ,tblOrganizationNotifications.BookingModifiedByAdmin
      ,tblOrganizationNotifications.PermanentBookingAllocated
      ,tblOrganizationNotifications.CheckInReminderEnabled
      ,tblOrganizationNotifications.DeskBookingReminderEnabled
      ,tblOrganizationNotifications.ConcurrencyKey
from tblOrganizationNotifications
inner join tblOrganizations
on tblOrganizationNotifications.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblOrganizationNotifications.OrganizationId = @organizationId
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@enabled", request.Enabled!.Value, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@bookingModifiedByAdmin", request.BookingModifiedByAdmin!.Value, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@permanentBookingAllocated", request.PermanentBookingAllocated!.Value, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@checkInReminderEnabled", request.CheckInReminderEnabled!.Value, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@deskBookingReminderEnabled", request.DeskBookingReminderEnabled!.Value, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                OrganizationNotificationsSetting? data = await gridReader.ReadFirstOrDefaultAsync<OrganizationNotificationsSetting>();

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        if (data is null)
                        {
                            // This should never happen
                            queryResult = SqlQueryResult.UnknownError;
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

        /// <summary>
        /// <para>Retrieves the organization Azure settings for the specified organization from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<OrganizationAzureSetting?> GetOrganizationAzureSettingsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_systemAzureADIntegrationClientId uniqueidentifier
declare @_systemAzureADSingleSignOnClientId uniqueidentifier

select @_systemAzureADIntegrationClientId = AzureADIntegrationClientId
      ,@_systemAzureADSingleSignOnClientId = AzureADSingleSignOnClientId
from tblSystemAzureSettings

select tblOrganizationAzureSettings.OrganizationId
      ,tblOrganizations.Name as OrganizationName
      ,tblOrganizationAzureSettings.UpdatedDateUtc
      ,tblOrganizationAzureSettings.UseCustomAzureADApplication
      ,tblOrganizationAzureSettings.AzureADTenantId
      ,tblOrganizationAzureSettings.AzureADIntegrationEnabled
      ,tblOrganizationAzureSettings.AzureADIntegrationClientId
      ,tblOrganizationAzureSettings.AzureADIntegrationClientSecret
      ,tblOrganizationAzureSettings.AzureADIntegrationNote
      ,@_systemAzureADIntegrationClientId as SystemAzureADIntegrationClientId
      ,tblOrganizationAzureSettings.AzureADSingleSignOnEnabled
      ,tblOrganizationAzureSettings.AzureADSingleSignOnClientId
      ,tblOrganizationAzureSettings.AzureADSingleSignOnClientSecret
      ,tblOrganizationAzureSettings.AzureADSingleSignOnNote
      ,@_systemAzureADSingleSignOnClientId as SystemAzureADSingleSignOnClientId
      ,tblOrganizationAzureSettings.ConcurrencyKey
from tblOrganizationAzureSettings
inner join tblOrganizations
on tblOrganizationAzureSettings.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblOrganizationAzureSettings.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                OrganizationAzureSetting? organizationAzureSetting = await sqlConnection.QueryFirstOrDefaultAsync<OrganizationAzureSetting>(commandDefinition);

                if (organizationAzureSetting is not null)
                {
                    // Decrypt AzureADIntegrationClientSecret
                    if (!string.IsNullOrEmpty(organizationAzureSetting.AzureADIntegrationClientSecret))
                    {
                        organizationAzureSetting.AzureADIntegrationClientSecret = StringCipherAesGcm.Decrypt(organizationAzureSetting.AzureADIntegrationClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                    }

                    // Decrypt AzureADSingleSignOnClientSecret
                    if (!string.IsNullOrEmpty(organizationAzureSetting.AzureADSingleSignOnClientSecret))
                    {
                        organizationAzureSetting.AzureADSingleSignOnClientSecret = StringCipherAesGcm.Decrypt(organizationAzureSetting.AzureADSingleSignOnClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                    }
                }

                return organizationAzureSetting;
            }
        }

        /// <summary>
        /// <para>Updates the organization Azure settings for the specified organization in the database.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, OrganizationAzureSetting?)> UpdateOrganizationAzureSettingsAsync(UpdateOrganizationAzureSettingsRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Organization Azure Settings";
            bool azureADIntegrationClientSecretChanged = true;
            bool azureADSingleSignOnClientSecretChanged = true;

            // Check if secrets were changed
            OrganizationAzureSetting? currentOrganizationAzureSetting = await GetOrganizationAzureSettingsAsync(request.OrganizationId!.Value);

            if (currentOrganizationAzureSetting is not null)
            {
                if (currentOrganizationAzureSetting.AzureADIntegrationClientSecret == request.AzureADIntegrationClientSecret)
                {
                    azureADIntegrationClientSecretChanged = false;
                }

                if (currentOrganizationAzureSetting.AzureADSingleSignOnClientSecret == request.AzureADSingleSignOnClientSecret)
                {
                    azureADSingleSignOnClientSecretChanged = false;
                }
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    UseCustomAzureADApplication bit
   ,AzureADTenantId uniqueidentifier
   ,AzureADIntegrationEnabled bit
   ,AzureADIntegrationClientId uniqueidentifier
   ,AzureADIntegrationNote nvarchar(500)
   ,AzureADSingleSignOnEnabled bit
   ,AzureADSingleSignOnClientId uniqueidentifier
   ,AzureADSingleSignOnNote nvarchar(500)
   ,OldUseCustomAzureADApplication bit
   ,OldAzureADTenantId uniqueidentifier
   ,OldAzureADIntegrationEnabled bit
   ,OldAzureADIntegrationClientId uniqueidentifier
   ,OldAzureADIntegrationNote nvarchar(500)
   ,OldAzureADSingleSignOnEnabled bit
   ,OldAzureADSingleSignOnClientId uniqueidentifier
   ,OldAzureADSingleSignOnNote nvarchar(500)
)

insert into tblOrganizationAzureSettings
(OrganizationId
,InsertDateUtc
,UpdatedDateUtc
,UseCustomAzureADApplication
,AzureADTenantId
,AzureADIntegrationEnabled
,AzureADIntegrationClientId
,AzureADIntegrationClientSecret
,AzureADIntegrationNote
,AzureADSingleSignOnEnabled
,AzureADSingleSignOnClientId
,AzureADSingleSignOnClientSecret
,AzureADSingleSignOnNote)
select @organizationId
      ,@_now -- InsertDateUtc
      ,@_now -- UpdatedDateUtc
      ,@UseCustomAzureADApplication
      ,@azureADTenantId
      ,@azureADIntegrationEnabled
      ,@azureADIntegrationClientId
      ,@azureADIntegrationClientSecret
      ,@azureADIntegrationNote
      ,@azureADSingleSignOnEnabled
      ,@azureADSingleSignOnClientId
      ,@azureADSingleSignOnClientSecret
      ,@azureADSingleSignOnNote
from tblOrganizations
where tblOrganizations.Deleted = 0
and tblOrganizations.id = @organizationId
and not exists
(
    select top 1 *
    from tblOrganizationAzureSettings with (updlock, serializable)
    where tblOrganizationAzureSettings.OrganizationId = @organizationId
)

if @@ROWCOUNT = 0
begin
    update tblOrganizationAzureSettings
    set UpdatedDateUtc = @_now
       ,UseCustomAzureADApplication = @useCustomAzureADApplication
       ,AzureADTenantId = @azureADTenantId
       ,AzureADIntegrationEnabled = @azureADIntegrationEnabled
       ,AzureADIntegrationClientId = @azureADIntegrationClientId
       ,AzureADIntegrationClientSecret = @azureADIntegrationClientSecret
       ,AzureADIntegrationNote = @azureADIntegrationNote
       ,AzureADSingleSignOnEnabled = @azureADSingleSignOnEnabled
       ,AzureADSingleSignOnClientId = @azureADSingleSignOnClientId
       ,AzureADSingleSignOnClientSecret = @azureADSingleSignOnClientSecret
       ,AzureADSingleSignOnNote = @azureADSingleSignOnNote
    output inserted.UseCustomAzureADApplication
          ,inserted.AzureADTenantId
          ,inserted.AzureADIntegrationEnabled
          ,inserted.AzureADIntegrationClientId
          ,inserted.AzureADIntegrationNote
          ,inserted.AzureADSingleSignOnEnabled
          ,inserted.AzureADSingleSignOnClientId
          ,inserted.AzureADSingleSignOnNote
          ,deleted.UseCustomAzureADApplication
          ,deleted.AzureADTenantId
          ,deleted.AzureADIntegrationEnabled
          ,deleted.AzureADIntegrationClientId
          ,deleted.AzureADIntegrationNote
          ,deleted.AzureADSingleSignOnEnabled
          ,deleted.AzureADSingleSignOnClientId
          ,deleted.AzureADSingleSignOnNote
          into @_data
    from tblOrganizationAzureSettings
    inner join tblOrganizations
    on tblOrganizationAzureSettings.OrganizationId = @organizationId
    and tblOrganizations.Deleted = 0
    where tblOrganizationAzureSettings.OrganizationId = @organizationId
    and tblOrganizationAzureSettings.ConcurrencyKey = @concurrencyKey
end
else
begin
    -- Table was empty and row was just inserted, so manually insert into @_data
    insert into @_data
    (UseCustomAzureADApplication
    ,AzureADTenantId
    ,AzureADIntegrationEnabled
    ,AzureADIntegrationClientId
    ,AzureADIntegrationNote
    ,AzureADSingleSignOnEnabled
    ,AzureADSingleSignOnClientId
    ,AzureADSingleSignOnNote
    ,OldUseCustomAzureADApplication
    ,OldAzureADTenantId
    ,OldAzureADIntegrationEnabled
    ,OldAzureADIntegrationClientId
    ,OldAzureADIntegrationNote
    ,OldAzureADSingleSignOnEnabled
    ,OldAzureADSingleSignOnClientId
    ,OldAzureADSingleSignOnNote)
    values
    (@UseCustomAzureADApplication
    ,@azureADTenantId
    ,@azureADIntegrationEnabled
    ,@azureADIntegrationClientId
    ,@azureADIntegrationNote
    ,@azureADSingleSignOnEnabled
    ,@azureADSingleSignOnClientId
    ,@azureADSingleSignOnNote
    ,null -- OldUseCustomAzureADApplication
    ,null -- OldAzureADTenantId
    ,null -- OldAzureADIntegrationEnabled
    ,null -- OldAzureADIntegrationClientId
    ,null -- OldAzureADIntegrationNote
    ,null -- OldAzureADSingleSignOnEnabled
    ,null -- OldAzureADSingleSignOnClientId
    ,null) -- OldAzureADSingleSignOnNote
end

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblOrganizationAzureSettings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,UseCustomAzureADApplication
    ,AzureADTenantId
    ,AzureADIntegrationEnabled
    ,AzureADIntegrationClientId
    ,AzureADIntegrationNote
    ,AzureADSingleSignOnEnabled
    ,AzureADSingleSignOnClientId
    ,AzureADSingleSignOnNote
    ,OldUseCustomAzureADApplication
    ,OldAzureADTenantId
    ,OldAzureADIntegrationEnabled
    ,OldAzureADIntegrationClientId
    ,OldAzureADIntegrationNote
    ,OldAzureADSingleSignOnEnabled
    ,OldAzureADSingleSignOnClientId
    ,OldAzureADSingleSignOnNote
    ,AzureADIntegrationClientSecretChanged
    ,AzureADSingleSignOnClientSecretChanged
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,d.UseCustomAzureADApplication
          ,d.AzureADTenantId
          ,d.AzureADIntegrationEnabled
          ,d.AzureADIntegrationClientId
          ,d.AzureADIntegrationNote
          ,d.AzureADSingleSignOnEnabled
          ,d.AzureADSingleSignOnClientId
          ,d.AzureADSingleSignOnNote
          ,d.OldUseCustomAzureADApplication
          ,d.OldAzureADTenantId
          ,d.OldAzureADIntegrationEnabled
          ,d.OldAzureADIntegrationClientId
          ,d.OldAzureADIntegrationNote
          ,d.OldAzureADSingleSignOnEnabled
          ,d.OldAzureADSingleSignOnClientId
          ,d.OldAzureADSingleSignOnNote
          ,@azureADIntegrationClientSecretChanged
          ,@azureADSingleSignOnClientSecretChanged
          ,'Update' -- LogAction
          ,null -- CascadeFrom
          ,null -- CascadeLogId
    from @_data d
end
else
begin
    -- Record was not updated
    -- This means the settings row already existed and the specfied ConcurrencyKey was wrong
    set @_result = 2
end

select @_result

select tblOrganizationAzureSettings.OrganizationId
      ,tblOrganizations.Name as OrganizationName
      ,tblOrganizationAzureSettings.UpdatedDateUtc
      ,tblOrganizationAzureSettings.UseCustomAzureADApplication
      ,tblOrganizationAzureSettings.AzureADTenantId
      ,tblOrganizationAzureSettings.AzureADIntegrationEnabled
      ,tblOrganizationAzureSettings.AzureADIntegrationClientId
      ,tblOrganizationAzureSettings.AzureADIntegrationClientSecret
      ,tblOrganizationAzureSettings.AzureADIntegrationNote
      ,tblOrganizationAzureSettings.AzureADSingleSignOnEnabled
      ,tblOrganizationAzureSettings.AzureADSingleSignOnClientId
      ,tblOrganizationAzureSettings.AzureADSingleSignOnClientSecret
      ,tblOrganizationAzureSettings.AzureADSingleSignOnNote
      ,tblOrganizationAzureSettings.ConcurrencyKey
from tblOrganizationAzureSettings
inner join tblOrganizations
on tblOrganizationAzureSettings.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblOrganizationAzureSettings.OrganizationId = @organizationId
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string? encryptedAzureADIntegrationClientSecret = null;
                string? encryptedAzureADSingleSignOnClientSecret = null;

                if (!string.IsNullOrEmpty(request.AzureADIntegrationClientSecret))
                {
                    encryptedAzureADIntegrationClientSecret = StringCipherAesGcm.Encrypt(request.AzureADIntegrationClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                }

                if (!string.IsNullOrEmpty(request.AzureADSingleSignOnClientSecret))
                {
                    encryptedAzureADSingleSignOnClientSecret = StringCipherAesGcm.Encrypt(request.AzureADSingleSignOnClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                }

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@UseCustomAzureADApplication", request.UseCustomAzureADApplication, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@azureADTenantId", request.AzureADTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureADIntegrationEnabled", request.AzureADIntegrationEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@azureADIntegrationClientId", request.AzureADIntegrationClientId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureADIntegrationClientSecret", encryptedAzureADIntegrationClientSecret, DbType.AnsiString, ParameterDirection.Input, 200);
                parameters.Add("@azureADIntegrationNote", request.AzureADIntegrationNote, DbType.String, ParameterDirection.Input, 500);
                parameters.Add("@azureADSingleSignOnEnabled", request.AzureADSingleSignOnEnabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@azureADSingleSignOnClientId", request.AzureADSingleSignOnClientId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureADSingleSignOnClientSecret", encryptedAzureADSingleSignOnClientSecret, DbType.AnsiString, ParameterDirection.Input, 200);
                parameters.Add("@azureADSingleSignOnNote", request.AzureADSingleSignOnNote, DbType.String, ParameterDirection.Input, 500);
                parameters.Add("@azureADIntegrationClientSecretChanged", azureADIntegrationClientSecretChanged, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@azureADSingleSignOnClientSecretChanged", azureADSingleSignOnClientSecretChanged, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                OrganizationAzureSetting? data = await gridReader.ReadFirstOrDefaultAsync<OrganizationAzureSetting>();

                if (data is not null)
                {
                    // Decrypt AzureADIntegrationClientSecret
                    if (!string.IsNullOrEmpty(data.AzureADIntegrationClientSecret))
                    {
                        data.AzureADIntegrationClientSecret = StringCipherAesGcm.Decrypt(data.AzureADIntegrationClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                    }

                    // Decrypt AzureADSingleSignOnClientSecret
                    if (!string.IsNullOrEmpty(data.AzureADSingleSignOnClientSecret))
                    {
                        data.AzureADSingleSignOnClientSecret = StringCipherAesGcm.Decrypt(data.AzureADSingleSignOnClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                    }
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        // Remove the organization from the GraphServiceClient cache so that it will be
                        // reloaded next time with the updated Azure credentials.
                        await _cache.RemoveAsync($"GraphServiceClient:{request.OrganizationId}");
                        break;
                    case 2:
                        if (data is null)
                        {
                            // This should never happen
                            queryResult = SqlQueryResult.UnknownError;
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

        public async Task<OrganizationAzureADSingleSignOnInfo> GetAzureADSingleSignOnInfo(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_azureADSingleSignOnEnabled bit = 0
declare @_azureADTenantId uniqueidentifier
declare @_useCustomAzureADApplication bit = 0
declare @_customAzureADSingleSignOnClientId uniqueidentifier
declare @_customAzureADSingleSignOnClientSecret varchar(200)
declare @_systemAzureADSingleSignOnClientId uniqueidentifier
declare @_systemAzureADSingleSignOnClientSecret varchar(200)

select @_azureADSingleSignOnEnabled = AzureADSingleSignOnEnabled
      ,@_azureADTenantId = AzureADTenantId
      ,@_useCustomAzureADApplication = UseCustomAzureADApplication
      ,@_customAzureADSingleSignOnClientId = case when tblOrganizationAzureSettings.UseCustomAzureADApplication = 1
                                                  then tblOrganizationAzureSettings.AzureADSingleSignOnClientID
                                                  else null
                                             end
      ,@_customAzureADSingleSignOnClientSecret = case when tblOrganizationAzureSettings.UseCustomAzureADApplication = 1
                                                      then tblOrganizationAzureSettings.AzureADSingleSignOnClientSecret
                                                      else null
                                                 end
from tblOrganizations
inner join tblOrganizationAzureSettings
on tblOrganizations.id = tblOrganizationAzureSettings.OrganizationId
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0

-- If not using Custom Azure AD Application, then get the system one
if @_azureADSingleSignOnEnabled = 1 and @_useCustomAzureADApplication = 0
begin
    select @_systemAzureADSingleSignOnClientId = AzureADSingleSignOnClientId
          ,@_systemAzureADSingleSignOnClientSecret = AzureADSingleSignOnClientSecret
    from tblSystemAzureSettings
end

select @_azureADSingleSignOnEnabled as SingleSignOnEnabled
      ,case when @_azureADSingleSignOnEnabled = 1 then @_azureADTenantId else null end as TenantId
      ,case when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 0 then @_systemAzureADSingleSignOnClientId
            when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 1 then @_customAzureADSingleSignOnClientId
            else null
       end ClientId
      ,case when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 0 then @_systemAzureADSingleSignOnClientSecret
            when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 1 then @_customAzureADSingleSignOnClientSecret
            else null
       end ClientSecret
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                OrganizationAzureADSingleSignOnInfo singleSignOnInfo = await sqlConnection.QueryFirstAsync<OrganizationAzureADSingleSignOnInfo>(commandDefinition);

                if (singleSignOnInfo.ClientSecret is not null)
                {
                    singleSignOnInfo.ClientSecret = StringCipherAesGcm.Decrypt(singleSignOnInfo.ClientSecret, _appSettings.AzureAD.SecretEncryptionKey);
                }

                return singleSignOnInfo;
            }
        }

        /// <summary>
        /// <para>Retrieves list of buildings for a given organization, along with each building's list of floors.</para>
        /// <para>Returns one of <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.SubRecordDidNotExist"/>.</para>
        /// </summary>
        /// <param name="req"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, ListBuildingsAndFloorsForDropdownsResponse)> ListBuildingsAndFloorsForDropdownsAsync(Guid? id, long? requestCounter = default, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_validOrganization bit = 0
declare @_result int = 0

select @_validOrganization = 1
from tblOrganizations
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0

select @_validOrganization

if @_validOrganization = 1
begin
    -- Select floors in the organization
    select tblBuildings.id as BuildingId
          ,tblBuildings.Name as BuildingName
    from tblBuildings
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblBuildings.OrganizationId = @organizationId
    and tblBuildings.Deleted = 0
    order by tblBuildings.Name

    if @@ROWCOUNT > 0
    begin
        set @_result = 1

        -- Selecting functions in the organization
        select tblBuildings.id as BuildingId
              ,tblFloors.id as FloorId
              ,tblFloors.Name as FloorName
        from tblFloors
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.Id
        and tblBuildings.Deleted = 0
        inner join tblOrganizations
        on tblBuildings.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        where tblBuildings.OrganizationId = @organizationId 
        and tblFloors.Deleted = 0
        order by tblFloors.FloorOrderKey
    end
    else
    begin
        -- Organization exists, but has no buildings
        set @_result = 3
    end
end
else
begin
    set @_result = 2
end

select @_result
";
                int resultCode = 0;
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", id, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using GridReader reader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                ListBuildingsAndFloorsForDropdownsResponse response = new ListBuildingsAndFloorsForDropdownsResponse();

                bool organizationValid = await reader.ReadFirstOrDefaultAsync<bool>();
                response.RequestCounter = requestCounter;

                if (organizationValid)
                {
                    response.Buildings = (await reader.ReadAsync<ListBuildingsAndFloorsForDropdownsResponse_Building>()).AsList();

                    if (response.Buildings.Count > 0)
                    {
                        List<ListBuildingsAndFloorsForDropdownsResponse_Floor> floors = (await reader.ReadAsync<ListBuildingsAndFloorsForDropdownsResponse_Floor>()).AsList();

                        if (floors.Count > 0)
                        {
                            Dictionary<Guid, ListBuildingsAndFloorsForDropdownsResponse_Building> buildingsDict = new Dictionary<Guid, ListBuildingsAndFloorsForDropdownsResponse_Building>();

                            foreach (ListBuildingsAndFloorsForDropdownsResponse_Building building in response.Buildings)
                            {
                                buildingsDict.Add(building.BuildingId, building);
                            }

                            foreach (ListBuildingsAndFloorsForDropdownsResponse_Floor floor in floors)
                            {
                                if (buildingsDict.TryGetValue(floor.BuildingId, out ListBuildingsAndFloorsForDropdownsResponse_Building? building))
                                {
                                    building!.Floors.Add(floor);
                                }
                            }
                        }
                    }
                }

                resultCode = await reader.ReadFirstOrDefaultAsync<int>();

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        queryResult = SqlQueryResult.RecordDidNotExist;
                        break;
                    case 3:
                        queryResult = SqlQueryResult.SubRecordDidNotExist;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, response);
            }
        }
    }
}
