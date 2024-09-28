using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Features.Functions.CreateFunction;
using VisitorTabletAPITemplate.Features.Functions.DeleteFunction;
using VisitorTabletAPITemplate.Features.Functions.UpdateFunction;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.Repositories
{
    public sealed class FunctionsRepository
    {
        private readonly AppSettings _appSettings;

        public FunctionsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Retrieves a list of functions to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListResponse> ListFunctionsForDropdownAsync(Guid organizationId, Guid buildingId, string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblFunctions",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }
                string sql = $@"
select tblFunctions.id as Value
      ,tblFunctions.Name as Text
from tblFunctions
inner join tblBuildings
on tblFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
where tblFunctions.Deleted = 0
and tblBuildings.OrganizationId = @organizationId
and tblFunctions.BuildingId = @buildingId
{whereQuery}
order by tblFunctions.Name
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListResponse selectListResponse = new SelectListResponse();
                selectListResponse.RequestCounter = requestCounter;
                selectListResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuid>(commandDefinition)).AsList();

                return selectListResponse;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of functions to be used for displaying a data table.
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<Function>> ListFunctionsForDataTableAsync(Guid organizationId, Guid buildingId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "tblFunctions.Name asc";
                        break;
                    case SortType.Updated:
                        sortColumn = "tblFunctions.UpdatedDateUtc desc";
                        break;
                    case SortType.Created:
                        sortColumn = "tblFunctions.id desc";
                        break;
                    default:
                        sortColumn = "tblFunctions.Name asc";
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
                            SqlTableName = "tblFunctions",
                            SqlColumnName = "Name",
                            DbType = DbType.String,
                            Size = 100
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }
                string sql = $@"
-- Get total number of functions in database matching search term
select count(*)
from tblFunctions
inner join tblBuildings
on tblFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
where tblFunctions.Deleted = 0
and tblBuildings.OrganizationId = @organizationId
and tblFunctions.BuildingId = @buildingId
{whereQuery}

declare @_data table
(
    id uniqueidentifier
   ,InsertDateUtc datetime2(3)
   ,UpdatedDateUtc datetime2(3)
   ,Name nvarchar(100)
   ,BuildingId uniqueidentifier
   ,HtmlColor varchar(7)
   ,ConcurrencyKey binary(4)
)

-- Get data
;with pg as
(
    select tblFunctions.id
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.BuildingId = @buildingId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
insert into @_data
(id
,InsertDateUtc
,UpdatedDateUtc
,Name
,BuildingId
,HtmlColor
,ConcurrencyKey)
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,Name
      ,BuildingId
      ,HtmlColor
      ,ConcurrencyKey
from tblFunctions
where exists
(
    select 1
    from pg
    where pg.id = tblFunctions.id
)
order by {sortColumn}
--order by tblBuildings.OrganizationId, {sortColumn} asc
--option (recompile)

select *
from @_data

select tblFunctionAdjacencies.FunctionId
      ,tblFunctionAdjacencies.AdjacentFunctionId
      ,tblFunctions.Name as AdjacentFunctionName
      ,tblFunctions.HtmlColor as AdjacentFunctionHtmlColor
      ,tblFunctionAdjacencies.InsertDateUtc
from tblFunctionAdjacencies
inner join tblFunctions
on tblFunctionAdjacencies.AdjacentFunctionId = tblFunctions.id
and tblFunctions.Deleted = 0
where exists
(
    select id
    from @_data d
    where tblFunctionAdjacencies.FunctionId = d.id
)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<Function> result = new DataTableResponse<Function>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<Function>()).AsList();
                //create dictionary to hash the function Id and it will be easier to get relevant adjacency
                List<FunctionAdjacency> functionAdjacencies = (await gridReader.ReadAsync<FunctionAdjacency>()).AsList();
                Dictionary<Guid, Function> functionsDict = new Dictionary<Guid, Function>();

                // If record exists, also get the functionAdjacency
                if (result.Records is not null)
                {
                    foreach (Function function in result.Records)
                    {
                        functionsDict.Add(function.id, function);
                    }

                    foreach (FunctionAdjacency functionAdjacency in functionAdjacencies)
                    {
                        if (functionsDict.TryGetValue(functionAdjacency.FunctionId, out Function? function))
                        {
                            function.FunctionAdjacencies.Add(functionAdjacency);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified function from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<Function?> GetFunctionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblFunctions.id
      ,tblFunctions.InsertDateUtc
      ,tblFunctions.UpdatedDateUtc
      ,tblFunctions.Name
      ,tblFunctions.BuildingId
      ,tblBuildings.Name as BuildingName
      ,tblFunctions.HtmlColor
      ,tblFunctions.ConcurrencyKey
from tblFunctions
inner join tblBuildings
on tblFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
where tblFunctions.Deleted = 0
and tblFunctions.id = @id
and tblBuildings.OrganizationId = @organizationId

if @@ROWCOUNT > 0
begin
    select tblFunctionAdjacencies.FunctionId
          ,tblFunctionAdjacencies.AdjacentFunctionId
          ,tblFunctions.Name as AdjacentFunctionName
          ,tblFunctions.HtmlColor as AdjacentFunctionHtmlColor
          ,tblFunctionAdjacencies.InsertDateUtc
    from tblFunctionAdjacencies
    inner join tblFunctions
    on tblFunctionAdjacencies.AdjacentFunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblFunctionAdjacencies.FunctionId = @id
    order by tblFunctions.Name
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
               
                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                Function? data = await gridReader.ReadFirstOrDefaultAsync<Function?>();

                if (data is not null && !gridReader.IsConsumed)
                {
                    data.FunctionAdjacencies = (await gridReader.ReadAsync<FunctionAdjacency>()).AsList();
                }

                return data;
            }
        }

        /// <summary>
        /// <para>Returns true if the specified function exists.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsFunctionExistsAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @id
    and tblBuildings.OrganizationId = @organizationId
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
        /// <para>Returns true if the specified function exists in the given building.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsFunctionExistsInBuildingAsync(Guid id, Guid buildingId, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @id
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.BuildingId = @buildingId
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns true if all of the specified functions exist in the given building.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsFunctionsExistsInBuildingAsync(List<Guid> ids, Guid buildingId, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                StringBuilder sql = new StringBuilder();

                DynamicParameters parameters = new DynamicParameters();

                sql.AppendLine(@"
declare @_functionIds table
(
    FunctionId uniqueidentifier
)
");

                // Add functions to temporary table
                if (ids.Count > 0)
                {
                    sql.Append(@"
insert into @_functionIds
(FunctionId)
values
");
                    for (int i = 0; i < ids.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }
                        sql.AppendLine($"(@functionId{i})");
                        parameters.Add($"@functionId{i}", ids[i], DbType.Guid, ParameterDirection.Input);
                    }
                }

                sql.AppendLine(@"
select case when not exists
(
    select *
    from @_functionIds d
    where not exists
    (
        select *
        from tblFunctions
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblFunctions.Deleted = 0
        and tblBuildings.OrganizationId = @organizationId
        and tblFunctions.BuildingId = @buildingId
        and d.FunctionId = tblFunctions.id
    )
)
then 1 else 0 end
");
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Creates a function.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Function?)> CreateFunctionAsync(CreateFunctionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Create Function";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_hasInvalidFunctionIds bit = 0

declare @_functionAdjacenciesData table
(
    LogId uniqueidentifier
   ,AdjacentFunctionId uniqueidentifier
)

");
                // check for null adjacent function
                if (request.FunctionAdjacencies is not null && request.FunctionAdjacencies.Count > 0)
                {
                    sql.Append(@"
insert into @_functionAdjacenciesData
(LogId, AdjacentFunctionId)
values
            ");
                    for (int i = 0; i < request.FunctionAdjacencies!.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }
                        sql.AppendLine($"(@functionAdjacenciesLogId{i}, @adjacentFunctionId{i})");
                        parameters.Add($"@functionAdjacenciesLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                        parameters.Add($"@adjacentFunctionId{i}", request.FunctionAdjacencies[i], DbType.Guid, ParameterDirection.Input);
                    }
                }

        sql.Append(@"

select top 1 @_hasInvalidFunctionIds = 1
from @_functionAdjacenciesData d
where not exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where d.AdjacentFunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.BuildingId = @buildingId
)

if @_hasInvalidFunctionIds = 0
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
        insert into tblFunctions
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,Name
        ,BuildingId
        ,HtmlColor)
        select @id
              ,@_now
              ,@_now
              ,@name
              ,@buildingId
              ,@htmlColor
        from tblBuildings
        where id = @buildingId
        and OrganizationId = @organizationId
        and Deleted = 0
        and not exists
        (
            select *
            from tblFunctions
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblFunctions.Deleted = 0
            and tblFunctions.Name = @name
            and tblBuildings.OrganizationId = @organizationId
            and tblFunctions.BuildingId = @buildingId
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1
    
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
            ,LogAction)
            select @logid
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@id
                  ,@name
                  ,@buildingId
                  ,@htmlColor
                  ,0 -- Deleted
                  ,'Insert'

            -- Insert a new row into tblFunctionHistories for the function we just created,
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
                  ,@id -- FunctionId
                  ,@name
                  ,@buildingId
                  ,@htmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the function history for the new function
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
                  ,@id -- FunctionId
                  ,@name
                  ,@buildingId
                  ,@htmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblFunctions' -- CascadeFrom
                  ,@logId -- CascadeLogId

            insert into tblFunctionAdjacencies
            (FunctionId
            ,AdjacentFunctionId
            ,InsertDateUtc)
            select @id
                  ,AdjacentFunctionId
                  ,@_now
            from @_functionAdjacenciesData

            insert into tblFunctionAdjacencies_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,FunctionId
            ,AdjacentFunctionId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select LogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@id
                  ,AdjacentFunctionId
                  ,'Insert'
                  ,'tblFunctions' -- CascadeFrom
                  ,@logid -- CascadeLogId
            from @_functionAdjacenciesData
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
    -- FunctionAdjacencies contains invalid function IDs
    set @_result = 3
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select tblFunctions.id
          ,tblFunctions.InsertDateUtc
          ,tblFunctions.UpdatedDateUtc
          ,tblFunctions.Name
          ,tblFunctions.BuildingId
          ,tblBuildings.Name as BuildingName
          ,tblFunctions.HtmlColor
          ,tblFunctions.ConcurrencyKey
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @id
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.BuildingId = @buildingId

    -- Select Function Adjacencies
    select tblFunctionAdjacencies.FunctionId
          ,tblFunctionAdjacencies.AdjacentFunctionId
          ,tblFunctions.Name as AdjacentFunctionName
          ,tblFunctions.HtmlColor as AdjacentFunctionHtmlColor
          ,tblFunctionAdjacencies.InsertDateUtc
    from tblFunctionAdjacencies
    inner join tblFunctions
    on tblFunctionAdjacencies.AdjacentFunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblFunctionAdjacencies.FunctionId = @id
    order by tblFunctions.Name
end
");
                Guid id = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblFunctionHistories and tblFunctionHistories_Log
                Guid functionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                parameters.Add("@lockResourceName", $"tblFunctions_Name_{request.BuildingId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@htmlColor", request.HtmlColor, DbType.AnsiString, ParameterDirection.Input, 7);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@functionHistoryId", functionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryLogId", functionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Function? data = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    data = await gridReader.ReadFirstOrDefaultAsync<Function>();

                    if (data is not null)
                    {
                        data.FunctionAdjacencies = (await gridReader.ReadAsync<FunctionAdjacency>()).AsList();
                    }
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
                    case 3:
                        // FunctionAdjacencies contains invalid function IDs
                        queryResult = SqlQueryResult.SubRecordInvalid;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Updates the specified function.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Function?)> UpdateFunctionAsync(UpdateFunctionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Function";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_hasInvalidFunctionIds bit = 0

declare @_data table
(
    Name nvarchar(100)
   ,BuildingId uniqueidentifier
   ,HtmlColor varchar(7)
   ,OldName nvarchar(100)
   ,OldHtmlColor varchar(7)
)

declare @_historyData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,BuildingId uniqueidentifier
   ,HtmlColor varchar(7)
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_functionAdjacenciesData table
(
    LogId uniqueidentifier
   ,AdjacentFunctionId uniqueidentifier
)");
                // check for null adjacent function
                if (request.FunctionAdjacencies is not null && request.FunctionAdjacencies.Count > 0)
                {
                    sql.Append(@"
            insert into @_functionAdjacenciesData
            (LogId, AdjacentFunctionId)
            values
            ");
                    for (int i = 0; i < request.FunctionAdjacencies!.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }
                        sql.AppendLine($"(@functionAdjacenciesLogId{i}, @adjacentFunctionId{i})");
                        parameters.Add($"@functionAdjacenciesLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                        parameters.Add($"@adjacentFunctionId{i}", request.FunctionAdjacencies[i], DbType.Guid, ParameterDirection.Input);
                    }
                }
        sql.Append(@"

select top 1 @_hasInvalidFunctionIds = 1
from @_functionAdjacenciesData d
where not exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where d.AdjacentFunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.BuildingId = @buildingId
)

if @_hasInvalidFunctionIds = 0
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
        update tblFunctions
        set UpdatedDateUtc = @_now
           ,Name = @name
           ,HtmlColor = @htmlColor
        output inserted.Name
              ,inserted.BuildingId
              ,inserted.HtmlColor
              ,deleted.Name
              ,deleted.HtmlColor
              into @_data
        from tblFunctions
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblFunctions.Deleted = 0
        and tblFunctions.id = @id
        and tblFunctions.BuildingId = @buildingId
        and tblBuildings.OrganizationId = @organizationId
        and tblFunctions.ConcurrencyKey = @concurrencyKey
        and not exists
        (
            select *
            from tblFunctions
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblFunctions.Deleted = 0
            and tblFunctions.Name = @name
            and tblFunctions.id != @id
            and tblFunctions.BuildingId = @buildingId
            and tblBuildings.OrganizationId = @organizationId
        )
    
        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            -- Insert to functions log
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
            ,OldName
            ,OldHtmlColor
            ,OldDeleted
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@id
                  ,d.Name
                  ,d.BuildingId
                  ,d.HtmlColor
                  ,0 -- Deleted
                  ,d.OldName
                  ,d.OldHtmlColor
                  ,0 -- OldDeleted
                  ,'Update' -- LogAction
            from @_data d

            -- Update the old row in tblFunctionHistories with updated EndDateUtc
            update tblFunctionHistories
            set UpdatedDateUtc = @_now
               ,EndDateUtc = @_last15MinuteIntervalUtc
            output inserted.id -- FunctionHistoryId
                  ,inserted.Name
                  ,inserted.BuildingId
                  ,inserted.HtmlColor
                  ,inserted.StartDateUtc
                  ,inserted.EndDateUtc
                  ,deleted.EndDateUtc
                  into @_historyData
            where FunctionId = @id
            and EndDateUtc > @_last15MinuteIntervalUtc

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
            ,OldEndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @functionHistoryUpdateLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,h.id -- FunctionHistoryId
                  ,@id -- FunctionId
                  ,h.Name
                  ,h.BuildingId
                  ,h.HtmlColor
                  ,h.StartDateUtc
                  ,h.EndDateUtc
                  ,h.OldEndDateUtc
                  ,'Update' -- LogAction
                  ,'tblFunctions' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_historyData h
            
            -- Insert a new row into tblFunctionHistories for the function we just updated,
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
                  ,@id -- FunctionId
                  ,@name
                  ,@buildingId
                  ,@htmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the function history for the new row
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
                  ,@id -- FunctionId
                  ,@name
                  ,@buildingId
                  ,@htmlColor
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblFunctions' -- CascadeFrom
                  ,@logId -- CascadeLogId

            declare @_functionAdjacenciesLogs table
            (
                AdjacentFunctionId uniqueidentifier
               ,LogAction varchar(6)
            )

            -- Delete removed adjacent functions and insert into log table variable
            delete from tblFunctionAdjacencies
            output deleted.AdjacentFunctionId
                  ,'Delete' -- LogAction
                  into @_functionAdjacenciesLogs
            where FunctionId = @id
            and not exists
            (
                select *
                from @_functionAdjacenciesData d
                where tblFunctionAdjacencies.FunctionId = @id
                and d.AdjacentFunctionId = tblFunctionAdjacencies.AdjacentFunctionId
            )

            -- Insert new adjacent functions into log table variable
            insert into @_functionAdjacenciesLogs
            (AdjacentFunctionId, LogAction)
            select AdjacentFunctionId, 'Insert'
            from @_functionAdjacenciesData d
            where not exists
            (
                select *
                from tblFunctionAdjacencies destTable
                where destTable.FunctionId = @id
                and d.AdjacentFunctionId = destTable.AdjacentFunctionId
            )

            -- Insert new adjacent functions
            insert into tblFunctionAdjacencies
            (FunctionId, AdjacentFunctionId, InsertDateUtc)
            select @id, AdjacentFunctionId, @_now
            from @_functionAdjacenciesData d
            where not exists
            (
                select *
                from tblFunctionAdjacencies destTable
                where destTable.FunctionId = @id
                and d.AdjacentFunctionId = destTable.AdjacentFunctionId
            )

            -- Insert to adjacent functions log
            ;with logIds as (
                select ids.AdjacentFunctionId, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_functionAdjacenciesLogs
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by AdjacentFunctionId) as RowNumber, AdjacentFunctionId
                    from @_functionAdjacenciesLogs
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblFunctionAdjacencies_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,FunctionId
            ,AdjacentFunctionId
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
                  ,d.AdjacentFunctionId
                  ,d.LogAction
                  ,'tblFunctions' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_functionAdjacenciesLogs d
            inner join logIds l
            on d.AdjacentFunctionId = l.AdjacentFunctionId
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
    -- FunctionAdjacencies contains invalid function IDs
    set @_result = 3
end

select @_result

-- Select row to return with the API result
select tblFunctions.id
      ,tblFunctions.InsertDateUtc
      ,tblFunctions.UpdatedDateUtc
      ,tblFunctions.Name
      ,tblFunctions.BuildingId
      ,tblBuildings.Name as BuildingName
      ,tblFunctions.HtmlColor
      ,tblFunctions.ConcurrencyKey
from tblFunctions
inner join tblBuildings
on tblFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
where tblFunctions.Deleted = 0
and tblFunctions.id = @id
and tblBuildings.OrganizationId = @organizationId

if @@ROWCOUNT = 1
begin
    -- Select Function Adjacencies
    select tblFunctionAdjacencies.FunctionId
          ,tblFunctionAdjacencies.AdjacentFunctionId
          ,tblFunctions.Name as AdjacentFunctionName
          ,tblFunctions.HtmlColor as AdjacentFunctionHtmlColor
          ,tblFunctionAdjacencies.InsertDateUtc
    from tblFunctionAdjacencies
    inner join tblFunctions
    on tblFunctionAdjacencies.AdjacentFunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblFunctionAdjacencies.FunctionId = @id
    order by tblFunctions.Name
end
");
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when updating old tblFunctionHistories, as well as inserting to tblFunctionHistories and tblFunctionHistories_Log
                Guid functionHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Name!.ToUpperInvariant())));

                parameters.Add("@lockResourceName", $"tblFunctions_Name_{request.OrganizationId}_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@name", request.Name, DbType.String, ParameterDirection.Input, 100);
                parameters.Add("@htmlColor", request.HtmlColor, DbType.AnsiString, ParameterDirection.Input, 7);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@functionHistoryUpdateLogId", functionHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryId", functionHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionHistoryLogId", functionHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Function? data = await gridReader.ReadFirstOrDefaultAsync<Function>();

                if (data is not null && !gridReader.IsConsumed)
                {
                    data.FunctionAdjacencies = (await gridReader.ReadAsync<FunctionAdjacency>()).AsList();
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
                            // Concurrency key matches, assume that another row with the name must already exist.
                            queryResult = SqlQueryResult.RecordAlreadyExists;
                        }
                        break;
                    case 3:
                        // FunctionAdjacencies contains invalid function IDs
                        queryResult = SqlQueryResult.SubRecordInvalid;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Deletes the specified function.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>, <see cref="SqlQueryResult.RecordIsInUse"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, Function?, DeleteFunctionResponse_FunctionInUse?)> DeleteFunctionAsync(DeleteFunctionRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete Function";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_isFunctionInUse bit = 0

declare @_data table
(
    Name nvarchar(100)
   ,BuildingId uniqueidentifier
   ,HtmlColor varchar(7)
)

declare @_historyData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,BuildingId uniqueidentifier
   ,HtmlColor varchar(7)
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_desksBelongingToFunction table
(
    DeskId uniqueidentifier
   ,DeskName nvarchar(100)
   ,FloorId uniqueidentifier
   ,FloorName nvarchar(100)
)

insert into @_desksBelongingToFunction
(DeskId
,DeskName
,FloorId
,FloorName)
select tblDesks.id as DeskId
      ,tblDesks.Name as DeskName
      ,tblDesks.FloorId
      ,tblFloors.Name as FloorName
from tblDesks
inner join tblFloors
on tblDesks.FloorId = tblFloors.id
and tblFloors.Deleted = 0
inner join tblBuildings
on tblFloors.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
where tblDesks.Deleted = 0
and tblDesks.FunctionType = {(int)FunctionType.Default}
and tblDesks.FunctionId = @id
and tblBuildings.OrganizationId = @organizationId
order by tblFloors.Name, tblDesks.Name

if @@ROWCOUNT > 0
begin
    set @_isFunctionInUse = 1
end

declare @_usersBelongingToFunction table
(
    Uid uniqueidentifier
   ,DisplayName nvarchar(100)
   ,Email nvarchar(254)
   ,AvatarThumbnailUrl varchar(255)
)

insert into @_usersBelongingToFunction
(Uid
,DisplayName
,Email
,AvatarThumbnailUrl)
select tblUsers.Uid
      ,tblUsers.DisplayName
      ,tblUsers.Email
      ,tblUsers.AvatarThumbnailUrl
from tblUserBuildingJoin
inner join tblUserOrganizationJoin
on tblUserBuildingJoin.Uid = tblUserOrganizationJoin.Uid
inner join tblUsers
on tblUserBuildingJoin.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
where tblUserBuildingJoin.FunctionId = @id
and tblUserOrganizationJoin.OrganizationId = @organizationId
order by tblUsers.DisplayName

if @@ROWCOUNT > 0
begin
    set @_isFunctionInUse = 1
end

if @_isFunctionInUse = 0
begin
    update tblFunctions
    set tblFunctions.Deleted = 1
       ,tblFunctions.UpdatedDateUtc = @_now
    output inserted.Name
          ,inserted.BuildingId
          ,inserted.HtmlColor
          into @_data
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @id
    and tblBuildings.OrganizationId = @organizationId
    and tblFunctions.ConcurrencyKey = @concurrencyKey

    if @@ROWCOUNT = 1
    begin
        set @_result = 1

        -- Insert to functions log
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
        ,OldName
        ,OldHtmlColor
        ,OldDeleted
        ,LogAction)
        select @logId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,@id
              ,d.Name
              ,d.BuildingId
              ,d.HtmlColor
              ,1 -- Deleted
              ,d.Name
              ,d.HtmlColor
              ,0 -- OldDeleted
              ,'Delete'
        from @_data d

        -- Update the old row in tblFunctionHistories with updated EndDateUtc
        update tblFunctionHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
        output inserted.id -- FunctionHistoryId
              ,inserted.Name
              ,inserted.BuildingId
              ,inserted.HtmlColor
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,deleted.EndDateUtc
              into @_historyData
        where FunctionId = @id
        and EndDateUtc > @_last15MinuteIntervalUtc

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
        ,OldEndDateUtc
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select @functionHistoryUpdateLogId -- id
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- FunctionHistoryId
              ,@id -- FunctionId
              ,h.Name
              ,h.BuildingId
              ,h.HtmlColor
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.OldEndDateUtc
              ,'Delete' -- LogAction
              ,'tblFunctions' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_historyData h

        declare @_functionAdjacenciesLogs table
        (
            AdjacentFunctionId uniqueidentifier
        )

        -- Delete adjacent functions and insert into log table variable
        delete from tblFunctionAdjacencies
        output deleted.AdjacentFunctionId
               into @_functionAdjacenciesLogs
        where FunctionId = @id

        -- Insert to adjacent functions log
        ;with logIds as (
            select ids.AdjacentFunctionId, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_functionAdjacenciesLogs
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by AdjacentFunctionId) as RowNumber, AdjacentFunctionId
                from @_functionAdjacenciesLogs
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblFunctionAdjacencies_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,FunctionId
        ,AdjacentFunctionId
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
              ,d.AdjacentFunctionId
              ,'Delete' -- LogAction
              ,'tblFunctions' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_functionAdjacenciesLogs d
        left join logIds l
        on d.AdjacentFunctionId = l.AdjacentFunctionId
    end
    else
    begin
        -- Function did not exist
        set @_result = 2
    end
end
else
begin
    -- Function is in use
    set @_result = 3
end

select @_result

if @_result = 2
begin
    -- Select existing row if delete was unsuccessful
    select tblFunctions.id
          ,tblFunctions.InsertDateUtc
          ,tblFunctions.UpdatedDateUtc
          ,tblFunctions.Name
          ,tblFunctions.BuildingId
          ,tblBuildings.Name as BuildingName
          ,tblFunctions.HtmlColor
          ,tblFunctions.ConcurrencyKey
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @id
    and tblBuildings.OrganizationId = @organizationId

    if @@ROWCOUNT = 1
    begin
        -- Select Function Adjacencies
        select tblFunctionAdjacencies.FunctionId
              ,tblFunctionAdjacencies.AdjacentFunctionId
              ,tblFunctions.Name as AdjacentFunctionName
              ,tblFunctions.HtmlColor as AdjacentFunctionHtmlColor
              ,tblFunctionAdjacencies.InsertDateUtc
        from tblFunctionAdjacencies
        inner join tblFunctions
        on tblFunctionAdjacencies.AdjacentFunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblFunctionAdjacencies.FunctionId = @id
        order by tblFunctions.Name
    end
end
else if @_result = 3
begin
    -- Select desks which belong to the function
    select DeskId
          ,DeskName
          ,FloorId
          ,FloorName
    from @_desksBelongingToFunction

    -- Select users which belong to the function
    select Uid
          ,DisplayName
          ,Email
          ,AvatarThumbnailUrl
    from @_usersBelongingToFunction
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid functionHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", request.id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@functionHistoryUpdateLogId", functionHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                Function? data = null;
                DeleteFunctionResponse_FunctionInUse? functionInUseResponse = null;

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;
                        break;
                     case 2:
                        // If delete was unsuccessful, also get the updated data to be returned in the response if available
                        if (!gridReader.IsConsumed)
                        {
                            data = await gridReader.ReadFirstOrDefaultAsync<Function>();

                            if (data is not null && !gridReader.IsConsumed)
                            {
                                data.FunctionAdjacencies = (await gridReader.ReadAsync<FunctionAdjacency>()).AsList();
                            }
                        }

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
                            functionInUseResponse = new DeleteFunctionResponse_FunctionInUse
                            {
                                Desks = (await gridReader.ReadAsync<DeleteFunctionResponse_FunctionInUse_Desk>()).AsList(),
                                Users = (await gridReader.ReadAsync<DeleteFunctionResponse_FunctionInUse_User>()).AsList(),
                            };

                            if (functionInUseResponse.Desks.Count > 0 || functionInUseResponse.Users.Count > 0)
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

                return (queryResult, data, functionInUseResponse);
            }
        }
    }
}
