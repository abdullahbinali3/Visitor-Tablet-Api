using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.Regions.CreateRegion;
using VisitorTabletAPITemplate.Features.Regions.DeleteRegion;
using VisitorTabletAPITemplate.Features.Regions.UpdateRegion;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace VisitorTabletAPITemplate.Repositories
{
    public sealed class RegionsRepository
    {
        private readonly AppSettings _appSettings;

        public RegionsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Retrieves a list of regions to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListRegionsForDropdownAsync(Guid organizationId, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblRegions",
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
from tblRegions
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
        /// Retrieves a paginated list of regions to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<Region>> ListRegionsForDataTableAsync(Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblRegions",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }
                string sql = $@"
-- Get total number of regions in database matching search term
select count(*)
from tblRegions
where Deleted = 0
and OrganizationId = @organizationId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblRegions
    where Deleted = 0
    and OrganizationId = @organizationId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,OrganizationId
      ,ConcurrencyKey
from tblRegions
where Deleted = 0
and exists
(
    select 1
    from pg
    where pg.id = tblRegions.id
)
order by tblRegions.{sortColumn}
--order by tblRegions.OrganizationId, tblRegions.{sortColumn}
--option (recompile)
";
               
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<Region> result = new DataTableResponse<Region>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<Region>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified region from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Region?> GetRegionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,OrganizationId
      ,ConcurrencyKey
from tblRegions
where Deleted = 0
and id = @id
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<Region>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns true if the specified region exists.</para>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsRegionExistsAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblRegions
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
        /// <para>Creates a region.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Region?)> CreateRegionAsync(CreateRegionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Create Region";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int

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
    insert into tblRegions
    (id
    ,InsertDateUtc
    ,UpdatedDateUtc
    ,Name
    ,OrganizationId)
    select @id
          ,@_now
          ,@_now
          ,@name
          ,@organizationId
    where not exists
    (
        select *
        from tblRegions
        where Deleted = 0
        and Name = @name
        and OrganizationId = @organizationId
    )
    
    if @@ROWCOUNT = 1
    begin
        set @_result = 1
    
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
        ,LogAction)
        select @logid
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@id
              ,@name
              ,@organizationId
              ,0 -- Deleted
              ,'Insert'

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
              ,@organizationId
              ,@id -- RegionId
              ,@name
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
              ,@organizationId
              ,@regionHistoryId
              ,@id -- RegionId
              ,@name
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,'Insert' -- LogAction
              ,'tblRegions' -- CascadeFrom
              ,@logId -- CascadeLogId
    end
    else
    begin
        -- Record already exists
        set @_result = 2
    end
    
    commit
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select id
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,Name
          ,OrganizationId
          ,ConcurrencyKey
    from tblRegions
    where Deleted = 0
    and id = @id
    and OrganizationId = @organizationId
end
";
                Guid id = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblRegionHistories and tblRegionHistories_Log
                Guid regionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblRegions_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@regionHistoryId", regionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionHistoryLogId", regionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Region? data = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    data = await gridReader.ReadFirstOrDefaultAsync<Region>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        queryResult = SqlQueryResult.RecordAlreadyExists;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Updates the specified region.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Region?)> UpdateRegionAsync(UpdateRegionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Region";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int

declare @_data table
(
    Name nvarchar(100)
   ,OldName nvarchar(100)
)

declare @_historyData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

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
    update tblRegions
    set UpdatedDateUtc = @_now
       ,Name = @name
    output inserted.Name
          ,deleted.Name
          into @_data
    where Deleted = 0
    and id = @id
    and OrganizationId = @organizationId
    and ConcurrencyKey = @concurrencyKey
    and not exists
    (
        select *
        from tblRegions
        where Deleted = 0
        and Name = @name
        and id != @id
        and OrganizationId = @organizationId
    )
    
    if @@ROWCOUNT = 1
    begin
        set @_result = 1
    
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
        ,OldName
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
              ,0 -- Deleted
              ,d.OldName
              ,0 -- OldDeleted
              ,'Update'
        from @_data d

        -- Update the old row in tblRegionHistories with updated EndDateUtc
        update tblRegionHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
        output inserted.id -- RegionHistoryId
              ,inserted.Name
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,deleted.EndDateUtc
              into @_historyData
        where RegionId = @id
        and EndDateUtc > @_last15MinuteIntervalUtc

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
        ,OldEndDateUtc
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select @regionHistoryUpdateLogId -- id
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- RegionHistoryId
              ,@id -- RegionId
              ,h.Name
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.OldEndDateUtc
              ,'Update' -- LogAction
              ,'tblRegions' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_historyData h
            
        -- Insert a new row into tblRegionHistories for the region we just updated,
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
              ,@organizationId
              ,@id -- RegionId
              ,@name
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc

        -- Write to log for the region history for the new row
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
              ,@organizationId
              ,@regionHistoryId
              ,@id -- RegionId
              ,@name
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,'Insert' -- LogAction
              ,'tblRegions' -- CascadeFrom
              ,@logId -- CascadeLogId
    end
    else
    begin
        -- Record was not updated
        set @_result = 2
    end
    
    commit
end

select @_result

-- Select row to return with the API result
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,OrganizationId
      ,ConcurrencyKey
from tblRegions
where Deleted = 0
and id = @id
and OrganizationId = @organizationId
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when updating old tblRegionHistories, as well as inserting to tblRegionHistories and tblRegionHistories_Log
                Guid regionHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));
                
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblRegions_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@regionHistoryUpdateLogId", regionHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionHistoryId", regionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@regionHistoryLogId", regionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Region? data = await gridReader.ReadFirstOrDefaultAsync<Region>();

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
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Deletes the specified region.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Region?, DeleteRegionResponse_RegionInUse?)> DeleteRegionAsync(DeleteRegionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete Region";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_isRegionInUse bit = 0

declare @_data table
(
    Name nvarchar(100)
)

declare @_historyData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_buildingsBelongingToRegion table
(
    BuildingId uniqueidentifier
   ,BuildingName nvarchar(100)
)

insert into @_buildingsBelongingToRegion
(BuildingId
,BuildingName)
select tblBuildings.id as BuildingId
      ,tblBuildings.Name as BuildingName
from tblBuildings
where Deleted = 0
and RegionId = @id
and OrganizationId = @organizationId
order by Name

if @@ROWCOUNT > 0
begin
    set @_isRegionInUse = 1
end

if @_isRegionInUse = 0
begin
    update tblRegions
    set Deleted = 1
       ,UpdatedDateUtc = @_now
    output inserted.Name
           into @_data
    where Deleted = 0
    and id = @id
    and OrganizationId = @organizationId
    and ConcurrencyKey = @concurrencyKey

    if @@ROWCOUNT = 1
    begin
        set @_result = 1

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
        ,OldName
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
              ,1 -- Deleted
              ,d.Name
              ,0 -- OldDeleted
              ,'Delete'
        from @_data d

        -- Update the old row in tblRegionHistories with updated EndDateUtc
        update tblRegionHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
        output inserted.id -- RegionHistoryId
              ,inserted.Name
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,deleted.EndDateUtc
              into @_historyData
        where RegionId = @id
        and EndDateUtc > @_last15MinuteIntervalUtc

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
        ,OldEndDateUtc
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select @regionHistoryUpdateLogId -- id
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- RegionHistoryId
              ,@id -- RegionId
              ,h.Name
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.OldEndDateUtc
              ,'Delete' -- LogAction
              ,'tblRegions' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_historyData h
    end
    else
    begin
        -- Record could not be deleted
        set @_result = 2
    end
end
else
begin
    -- Region is in use
    set @_result = 3
end

select @_result

if @_result = 2
begin
    -- Select existing row if delete was unsuccessful
    select id
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,Name
          ,OrganizationId
          ,ConcurrencyKey
    from tblRegions
    where Deleted = 0
    and id = @id
    and OrganizationId = @organizationId
end
else if @_result = 3
begin
    -- Select buildings which belong to the region
    select BuildingId
          ,BuildingName
    from @_buildingsBelongingToRegion
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid regionHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@regionHistoryUpdateLogId", regionHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Region? data = null;
                DeleteRegionResponse_RegionInUse? regionInUseResponse = null;

                // If delete was unsuccessful, also get the updated data to be returned in the response if available
                if (!gridReader.IsConsumed)
                {
                    data = await gridReader.ReadFirstOrDefaultAsync<Region>();
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
                    case 3:
                        if (!gridReader.IsConsumed)
                        {
                            regionInUseResponse = new DeleteRegionResponse_RegionInUse
                            {
                                Buildings = (await gridReader.ReadAsync<DeleteRegionResponse_RegionInUse_Building>()).AsList(),
                            };

                            if (regionInUseResponse.Buildings.Count > 0)
                            {
                                queryResult = SqlQueryResult.RecordIsInUse;
                            }
                            else
                            {
                                // This should never happen
                                queryResult = SqlQueryResult.UnknownError;
                            }
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

                return (queryResult, data, regionInUseResponse);
            }
        }
    }
}
