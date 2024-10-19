using Dapper;
using hmac_bcrypt;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.InitForgotPassword;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.CompleteRegister;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.InitRegister;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RegisterAzureAD.CompleteRegisterAzureAD;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.CreateUser;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.DeleteUser;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDataTable;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUser;
using VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitDisableTwoFactorAuthentication;
using VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitTwoFactorAuthentication;
using VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateProfile;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models;
using VisitorTabletAPITemplate.Utilities;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.ShaneAuth.Repositories
{
    public sealed class UsersRepository
    {
        private readonly string _bcryptSettings;
        private readonly AppSettings _appSettings;
        private readonly TotpHelpers _totpHelpers;
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly AuthCacheService _authCacheService;

        public UsersRepository(AppSettings appSettings,
            TotpHelpers totpHelpers,
            ImageStorageRepository imageStorageRepository,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _bcryptSettings = $"$2a${_appSettings.Password.BcryptCost}";
            _totpHelpers = totpHelpers;
            _imageStorageRepository = imageStorageRepository;
            _authCacheService = authCacheService;
        }

        /// <summary>
        /// Retrieves a list of users to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListWithImageResponse> MasterListUsersForDropdownAsync(string? searchTerm, long? requestCounter, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select uid as Value
      ,DisplayName as Text
      ,Email as SecondaryText
      ,AvatarThumbnailUrl as ImageUrl
from tblUsers
where Deleted = 0
{whereQuery}
order by DisplayName
";
                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListWithImageResponse selectListWithImageResponse = new SelectListWithImageResponse();
                selectListWithImageResponse.RequestCounter = requestCounter;
                selectListWithImageResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuidWithImage>(commandDefinition)).AsList();

                return selectListWithImageResponse;
            }
        }

        /// <summary>
        /// <para>Retrieves a paginated list of users to be used for displaying a data table.</para>
        /// <para>Intended to be used on Master Settings page as it includes users from all organizations.</para>
        /// </summary>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<MasterUserDataForDataTable>> MasterListUsersForDataTableAsync(int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, MasterListUsersForDataTableRequest_Filter? filter = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    case SortType.Email:
                        sortColumn = "Email asc";
                        break;
                    case SortType.Updated:
                        sortColumn = "LastAccessDateUtc desc";
                        break;
                    case SortType.Created:
                        sortColumn = "Uid desc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
                        break;
                }

                StringBuilder whereQuerySb = new StringBuilder();

                DynamicParameters parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    List<SqlTableColumnParam> sqlTableColumnParams = new List<SqlTableColumnParam>
                    {
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuerySb.Append(SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm"));
                }


                // Add filters
                if (filter is not null)
                {
                    // Filter organization IDs (this works like an OR check - includes users who have any of the given organization IDs)
                    if (filter.OrganizationIds is not null && filter.OrganizationIds.Count > 0)
                    {
                        whereQuerySb.Append(@" and exists
(
    select *
    from tblUserOrganizationJoin
    where tblUsers.Uid = tblUserOrganizationJoin.Uid
    and tblUserOrganizationJoin.OrganizationId in
    (
");

                        for (int i = 0; i < filter.OrganizationIds.Count; ++i)
                        {
                            if (i > 0)
                            {
                                whereQuerySb.Append(',');
                            }

                            whereQuerySb.Append($"@organizationIdFilter{i}");
                            parameters.Add($"@organizationIdFilter{i}", filter.OrganizationIds[i], DbType.Guid, ParameterDirection.Input);
                        }

                        whereQuerySb.Append(@"
    )
)");
                    }

                    // Filter Enabled
                    if (filter.Enabled is not null)
                    {
                        // Note: In database the column is "Disabled" - not "Enabled" - so we invert the filter,
                        // i.e. Enabled = true means we check Disabled = false in the database query.
                        whereQuerySb.Append(" and Disabled = @enabledFilter");
                        parameters.Add($"@enabledFilter", !filter.Enabled, DbType.Boolean, ParameterDirection.Input);
                    }

                    // Filter UserSystemRole
                    if (filter.UserSystemRole is not null)
                    {
                        whereQuerySb.Append(" and UserSystemRole = @userSystemRoleFilter");
                        parameters.Add($"@userSystemRoleFilter", filter.UserSystemRole, DbType.Int32, ParameterDirection.Input);
                    }
                }

                string whereQuery = whereQuerySb.ToString();

                string sql = "";

                if (string.IsNullOrEmpty(whereQuery))
                {
                    // Queries without search
                    sql += $@"
-- Get total number of users in database
-- Note: The index 'IX_tblUsers_DisplayNameAsc' has a filter for Deleted = 0
select convert(int, rows)
from sys.partitions
where object_id = Object_Id('tblUsers')
and index_id = IndexProperty(Object_Id('tblUsers'), 'IX_tblUsers_DisplayNameAsc', 'IndexID')
";
                }
                else
                {
                    // Queries with search
                    sql += $@"
-- Get total number of users in database matching search term
select count(*)
from tblUsers
where Deleted = 0
{whereQuery}
";
                }

                sql += $@"
-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUsers
    where Deleted = 0
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select Uid
      ,InsertDateUtc
      ,LastAccessDateUtc
      ,Email
      ,UserSystemRole
      ,DisplayName
      ,AvatarUrl
      ,AvatarThumbnailUrl
      ,Disabled
      ,ConcurrencyKey
from tblUsers
where exists
(
    select 1
    from pg
    where pg.uid = tblUsers.uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<MasterUserDataForDataTable> result = new DataTableResponse<MasterUserDataForDataTable>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<MasterUserDataForDataTable>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of users belonging to the specified organization, to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ManageableUserDataForDataTable>> ListUsersForDataTableAsync(Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                if (!includeDisabled)
                {
                    whereQuery += " and tblUsers.Disabled = 0";
                }

                string sql = $@"
-- Get total number of users in database matching search term
select count(*)
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
{whereQuery}

-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUsers
    inner join tblUserOrganizationJoin
    on tblUsers.Uid = tblUserOrganizationJoin.Uid
    where tblUsers.Deleted = 0
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblUsers.Uid
      ,tblUsers.Email
      ,tblUsers.DisplayName
      ,tblUsers.AvatarThumbnailUrl
      ,tblUsers.Disabled
from tblUsers
where exists
(
    select 1
    from pg
    where pg.Uid = tblUsers.Uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ManageableUserDataForDataTable> result = new DataTableResponse<ManageableUserDataForDataTable>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ManageableUserDataForDataTable>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of users who have access to the specified building, to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ManageableUserDataForDataTable>> ListUsersInBuildingForDataTableAsync(Guid organizationId, Guid buildingId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                if (!includeDisabled)
                {
                    whereQuery += " and tblUsers.Disabled = 0";
                }

                string sql = $@"
-- Get total number of users in database matching search term
select count(*)
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
and tblUserBuildingJoin.BuildingId = @buildingId
and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
{whereQuery}

-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUsers
    inner join tblUserOrganizationJoin
    on tblUsers.Uid = tblUserOrganizationJoin.Uid
    inner join tblUserBuildingJoin
    on tblUsers.Uid = tblUserBuildingJoin.Uid
    where tblUsers.Deleted = 0
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    and tblUserBuildingJoin.BuildingId = @buildingId
    and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblUsers.Uid
      ,tblUsers.Email
      ,tblUsers.DisplayName
      ,tblUsers.AvatarThumbnailUrl
      ,tblUserBuildingJoin.FunctionId
      ,tblFunctions.Name as FunctionName
      ,tblUsers.Disabled
from tblUsers
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
inner join tblFunctions
on tblUserBuildingJoin.FunctionId = tblFunctions.id
and tblFunctions.Deleted = 0
and tblUserBuildingJoin.BuildingId = @buildingId
where exists
(
    select 1
    from pg
    where pg.Uid = tblUsers.Uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ManageableUserDataForDataTable> result = new DataTableResponse<ManageableUserDataForDataTable>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ManageableUserDataForDataTable>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves a paginated list of users who have access to the specified building, together with their permanent desk information, to be used for displaying a data table.</para>
        /// <para>Intended to be used on Floor Management page when selecting a permanent owner for a desk</para>
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<UserWithPermanentDeskData>> ListUsersWithPermanentDeskInBuildingForDataTableAsync(Guid organizationId, Guid buildingId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                if (!includeDisabled)
                {
                    whereQuery += " and tblUsers.Disabled = 0";
                }

                string sql = $@"
-- Get total number of users in database matching search term
select count(*)
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
inner join tblFunctions
on tblFunctions.id = tblUserBuildingJoin.FunctionId
and tblFunctions.Deleted = 0
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
and tblUserBuildingJoin.BuildingId = @buildingId
{whereQuery}

-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUsers
    inner join tblUserOrganizationJoin
    on tblUsers.Uid = tblUserOrganizationJoin.Uid
    inner join tblUserBuildingJoin
    on tblUsers.Uid = tblUserBuildingJoin.Uid
    inner join tblFunctions
    on tblFunctions.id = tblUserBuildingJoin.FunctionId
    and tblFunctions.Deleted = 0
    where tblUsers.Deleted = 0
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    and tblUserBuildingJoin.BuildingId = @buildingId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblUsers.Uid
      ,tblUsers.Email
      ,tblUsers.DisplayName
      ,tblUsers.AvatarThumbnailUrl
      ,tblUserBuildingJoin.FunctionId
      ,tblFunctions.Name as FunctionName
      ,deskTbl.PermanentDeskId
      ,deskTbl.PermanentDeskName
      ,deskTbl.PermanentDeskFloorId
      ,deskTbl.PermanentDeskFloorName
      ,deskTbl.PermanentDeskBuildingId
      ,deskTbl.PermanentDeskBuildingName
      ,deskTbl.PermanentDeskLocation
      ,tblUsers.Disabled
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
inner join tblFunctions
on tblFunctions.id = tblUserBuildingJoin.FunctionId
and tblFunctions.Deleted = 0
left join
(
    select tblDesks.id as PermanentDeskId
          ,tblDesks.Name as PermanentDeskName
          ,tblFloors.Id as PermanentDeskFloorId
          ,tblFloors.Name as PermanentDeskFloorName
          ,tblBuildings.Id as PermanentDeskBuildingId
          ,tblBuildings.Name as PermanentDeskBuildingName
          ,tblFloors.Name + ' ' + '(' + tblBuildings.Name + ')' as PermanentDeskLocation
          ,tblDesks.PermanentOwnerUid as PermanentOwnerUid
    from tblDesks
    inner join tblFloors
    on tblFloors.id = tblDesks.FloorId
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblBuildings.id = tblFloors.BuildingId
    and tblBuildings.Deleted = 0
    inner join tblOrganizations
    on tblOrganizations.id = tblBuildings.OrganizationId
    and tblOrganizations.Deleted = 0
    where tblDesks.Deleted = 0
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblBuildings.id = @buildingId
    and tblOrganizations.id = @organizationId
) as deskTbl
on deskTbl.PermanentOwnerUid = tblUsers.Uid
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
and tblUserBuildingJoin.BuildingId = @buildingId
and exists
(
    select 1
    from pg
    where pg.Uid = tblUsers.Uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<UserWithPermanentDeskData> result = new DataTableResponse<UserWithPermanentDeskData>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<UserWithPermanentDeskData>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves a paginated list of users who have access to the specified asset type, together with their permanent asset slot information, to be used for displaying a data table.</para>
        /// <para>Intended to be used on Asset Management page when selecting a permanent owner for an asset slot</para>
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="assetTypeId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<UserWithPermanentAssetSlotData>> ListUsersWithPermanentAssetSlotInAssetTypeForDataTableAsync(Guid organizationId, Guid assetTypeId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        },
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                if (!includeDisabled)
                {
                    whereQuery += " and tblUsers.Disabled = 0";
                }

                string sql = $@"
-- Get total number of users in database matching search term
select count(*)
from tblUsers
where exists
(
    select tblUserAssetTypeJoin.Uid
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblAssetTypes.id = tblUserAssetTypeJoin.AssetTypeId
    and tblAssetTypes.Deleted = 0
    and tblAssetTypes.id = @assetTypeId
    inner join tblBuildings
    on tblBuildings.id = tblAssetTypes.BuildingId
    and tblBuildings.Deleted = 0
    inner join tblOrganizations
    on tblOrganizations.id = tblBuildings.OrganizationId
    and tblOrganizations.Deleted = 0
    and tblOrganizations.id = @organizationId
    where tblUsers.Uid = tblUserAssetTypeJoin.Uid
)
and tblUsers.Deleted = 0
{whereQuery}

-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUsers
    where exists
    (
        select tblUserAssetTypeJoin.Uid
        from tblUserAssetTypeJoin
        inner join tblAssetTypes
        on tblAssetTypes.id = tblUserAssetTypeJoin.AssetTypeId
        and tblAssetTypes.Deleted = 0
        and tblAssetTypes.id = @assetTypeId
        inner join tblBuildings
        on tblBuildings.id = tblAssetTypes.BuildingId
        and tblBuildings.Deleted = 0
        inner join tblOrganizations
        on tblOrganizations.id = tblBuildings.OrganizationId
        and tblOrganizations.Deleted = 0
        and tblOrganizations.id = @organizationId
        where tblUsers.Uid = tblUserAssetTypeJoin.Uid
    )
    and tblUsers.Deleted = 0
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblUsers.Uid
      ,tblUsers.Email
      ,tblUsers.DisplayName
      ,tblUsers.AvatarThumbnailUrl
      ,tblUsers.Disabled
      ,assetSlots.PermanentAssetSlotId
      ,assetSlots.PermanentAssetSlotName
      ,assetSlots.PermanentAssetSlotAssetSectionId
      ,assetSlots.PermanentAssetSlotAssetSectionName
      ,assetSlots.PermanentAssetSlotAssetTypeId
      ,assetSlots.PermanentAssetSlotAssetTypeName
      ,assetSlots.PermanentAssetSlotLocation
from tblUsers
left join
(
    select tblAssetSlots.id as PermanentAssetSlotId
          ,tblAssetSlots.Name as PermanentAssetSlotName
          ,tblAssetSections.id as PermanentAssetSlotAssetSectionId
          ,tblAssetSections.Name as PermanentAssetSlotAssetSectionName
          ,tblAssetTypes.id as PermanentAssetSlotAssetTypeId
          ,tblAssetTypes.Name as PermanentAssetSlotAssetTypeName
          ,tblAssetSections.Name as PermanentAssetSlotLocation
          ,tblAssetSlots.PermanentOwnerUid as PermanentOwnerUid
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSections.id = tblAssetSlots.AssetSectionId
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetTypes.id = tblAssetSections.AssetTypeId
    and tblAssetTypes.Deleted = 0
    and tblAssetTypes.id = @assetTypeId
    inner join tblBuildings
    on tblBuildings.id = tblAssetTypes.BuildingId
    and tblBuildings.Deleted = 0
    inner join tblOrganizations
    on tblOrganizations.id = tblBuildings.OrganizationId
    and tblOrganizations.Deleted = 0
    and tblOrganizations.id = @organizationId
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
) as assetSlots
on assetSlots.PermanentOwnerUid = tblUsers.Uid
where tblUsers.Deleted = 0
and exists
(
    select 1
    from pg
    where pg.Uid = tblUsers.Uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@assetTypeId", assetTypeId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<UserWithPermanentAssetSlotData> result = new DataTableResponse<UserWithPermanentAssetSlotData>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<UserWithPermanentAssetSlotData>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of users who have access to the admin functions, to be used for displaying a data table.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerm"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ManageableUserDataForDataTable>> ListUsersInUserAdminFunctionsForDataTableAsync(Guid userId, Guid organizationId, Guid buildingId, int pageNumber, int pageSize, SortType sort, long? requestCounter, string? searchTerm = null, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Name:
                        sortColumn = "DisplayName asc";
                        break;
                    default:
                        sortColumn = "DisplayName asc";
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                if (!includeDisabled)
                {
                    whereQuery += "and tblUsers.Disabled = 0";
                }

                string sql = $@"
-- Get total number of users in database matching search term
select count(*)
from tblUserAdminFunctions
inner join tblUserBuildingJoin
on tblUserAdminFunctions.BuildingId = tblUserBuildingJoin.BuildingId
and tblUserAdminFunctions.FunctionId = tblUserBuildingJoin.FunctionId
inner join tblUserOrganizationJoin
on tblUserBuildingJoin.Uid = tblUserOrganizationJoin.Uid
and tblUserOrganizationJoin.OrganizationId = @organizationId
inner join tblBuildings
on tblUserAdminFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
inner join tblUsers
on tblUserBuildingJoin.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
where tblUserAdminFunctions.Uid = @userId
and tblBuildings.id = @buildingId
and tblBuildings.OrganizationId = @organizationId
and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
{whereQuery}

-- Get data
;with pg as
(
    select tblUsers.Uid
    from tblUserAdminFunctions
    inner join tblUserBuildingJoin
    on tblUserAdminFunctions.BuildingId = tblUserBuildingJoin.BuildingId
    and tblUserAdminFunctions.FunctionId = tblUserBuildingJoin.FunctionId
    inner join tblUserOrganizationJoin
    on tblUserBuildingJoin.Uid = tblUserOrganizationJoin.Uid
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    inner join tblBuildings
    on tblUserAdminFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblUsers
    on tblUserBuildingJoin.Uid = tblUsers.Uid
    and tblUsers.Deleted = 0
    where tblUserAdminFunctions.Uid = @userId
    and tblBuildings.id = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select tblUsers.Uid
      ,tblUsers.Email
      ,tblUsers.DisplayName
      ,tblUsers.AvatarThumbnailUrl
      ,tblUserBuildingJoin.FunctionId
      ,tblFunctions.Name as FunctionName
      ,tblUsers.Disabled
from tblUsers
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
inner join tblFunctions
on tblUserBuildingJoin.FunctionId = tblFunctions.id
and tblFunctions.Deleted = 0
and tblUserBuildingJoin.BuildingId = @buildingId
where exists
(
    select 1
    from pg
    where pg.Uid = tblUsers.Uid
)
order by {sortColumn}
--option (recompile)
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userId", userId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ManageableUserDataForDataTable> result = new DataTableResponse<ManageableUserDataForDataTable>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ManageableUserDataForDataTable>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Returns true if the specified user exists. Does not check whether the user is disabled.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsUserExistsAsync(Guid uid, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUsers
    where Deleted = 0
    and Uid = @uid
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Retrieves a user by Uid.
        /// </summary>
        /// <param name="uid">The user's Uid.</param>
        /// <param name="cancellationToken">A token that may be used to cancel the database query.</param>
        /// <returns></returns>
        public async Task<UserData?> GetUserByUidAsync(Guid uid, bool updateLastAccessDate = false, bool includeOrganizationDisabled = false, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = "";

                if (updateLastAccessDate)
                {
                    sql = """
                        update tblUsers
                        set LastAccessDateUtc = sysutcdatetime()
                        where Deleted = 0
                        and Uid = @uid
                        and Disabled = 0
                        and UserSystemRole > 0

                        """;
                }

                sql += $"""
                    -- Query user data
                    select Uid
                          ,InsertDateUtc
                          ,UpdatedDateUtc
                          ,LastAccessDateUtc
                          ,LastPasswordChangeDateUtc
                          ,Email
                          ,HasPassword
                          ,TotpEnabled
                          ,UserSystemRole
                          ,DisplayName
                          ,FirstName
                          ,Surname
                          ,Timezone
                          ,AvatarUrl
                          ,AvatarThumbnailUrl
                          ,Disabled
                          ,ConcurrencyKey
                    from tblUsers
                    where Deleted = 0
                    and Uid = @uid

                    if @@ROWCOUNT = 1
                    begin
                        -- Also query user's organization access
                        select tblUserOrganizationJoin.OrganizationId as id
                              ,tblOrganizations.Name
                              ,tblOrganizations.LogoImageUrl
                              ,tblOrganizations.CheckInEnabled
                              ,tblOrganizations.WorkplacePortalEnabled
                              ,tblOrganizations.WorkplaceAccessRequestsEnabled
                              ,tblOrganizations.WorkplaceInductionsEnabled
                              ,tblUserOrganizationJoin.UserOrganizationRole
                              ,tblUserOrganizationJoin.Note
                              ,tblUserOrganizationJoin.Contractor
                              ,tblUserOrganizationJoin.Visitor
                              ,tblUserOrganizationJoin.UserOrganizationDisabled
                              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserOrganizationJoin
                        inner join tblOrganizations
                        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
                        and tblOrganizations.Deleted = 0
                        and tblOrganizations.Disabled = 0
                        where tblUserOrganizationJoin.Uid = @uid
                    """;

                if (!includeOrganizationDisabled)
                {
                    sql += """
                        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
                        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
                    """;
                }

                sql += $"""
                        order by tblOrganizations.Name

                        -- Also query user's last used building
                        select Uid
                              ,WebLastUsedOrganizationId
                              ,WebLastUsedBuildingId
                              ,MobileLastUsedOrganizationId
                              ,MobileLastUsedBuildingId
                        from tblUserLastUsedBuilding
                        where Uid = @uid

                        -- Also query user's building access
                        select tblUserBuildingJoin.BuildingId as id
                              ,tblBuildings.Name
                              ,tblBuildings.RegionId
                              ,tblRegions.Name as RegionName
                              ,tblBuildings.OrganizationId
                              ,tblBuildings.Timezone
                              ,tblBuildings.CheckInEnabled
                              ,0 as HasBookableMeetingRooms -- Queried separately
                              ,0 as HasBookableAssetSlots -- Queried separately
                              ,tblUserBuildingJoin.FunctionId
                              ,tblFunctions.Name as FunctionName
                              ,tblFunctions.HtmlColor as FunctionHtmlColor
                              ,tblUserBuildingJoin.FirstAidOfficer
                              ,tblUserBuildingJoin.FireWarden
                              ,tblUserBuildingJoin.PeerSupportOfficer
                              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
                              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
                              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
                              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserBuildingJoin
                        inner join tblBuildings
                        on tblUserBuildingJoin.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        inner join tblRegions
                        on tblBuildings.RegionId = tblRegions.id
                        and tblRegions.Deleted = 0
                        inner join tblFunctions
                        on tblUserBuildingJoin.FunctionId = tblFunctions.id
                        and tblFunctions.Deleted = 0
                        where tblUserBuildingJoin.Uid = @uid
                        order by tblBuildings.Name

                        -- Also query user's buildings with bookable desks
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @uid
                        and exists
                        (
                            select *
                            from tblDesks
                            inner join tblFloors
                            on tblDesks.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblDesks.Deleted = 0
                            and tblDesks.DeskType != {(int)DeskType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query user's buildings with bookable meeting rooms
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @uid
                        and exists
                        (
                            select *
                            from tblMeetingRooms
                            inner join tblFloors
                            on tblMeetingRooms.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblMeetingRooms.Deleted = 0
                            and tblMeetingRooms.OfflineRoom = 0
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                            and
                            (
                                tblMeetingRooms.RestrictedRoom = 0
                                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
                            )
                        )

                        -- Also query user's buildings with bookable asset slots
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @uid
                        and exists
                        (
                            select *
                            from tblAssetSlots
                            inner join tblAssetSections
                            on tblAssetSlots.AssetSectionId = tblAssetSections.id
                            and tblAssetSections.Deleted = 0
                            inner join tblAssetTypes
                            on tblAssetSections.AssetTypeId = tblAssetTypes.id
                            and tblAssetTypes.Deleted = 0
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblAssetSlots.Deleted = 0
                            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query the user's permanent seat
                        select tblDesks.id as DeskId
                              ,tblBuildings.id as BuildingId
                        from tblDesks
                        inner join tblFloors
                        on tblDesks.FloorId = tblFloors.id
                        and tblFloors.Deleted = 0
                        inner join tblBuildings
                        on tblFloors.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblDesks.Deleted = 0
                        and tblDesks.DeskType = {(int)DeskType.Permanent}
                        and tblDesks.PermanentOwnerUid = @uid

                        -- Also query the user's asset types
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblUserAssetTypeJoin
                        inner join tblAssetTypes
                        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblUserAssetTypeJoin.Uid = @uid

                        -- Also query the user's permanent assets
                        select tblAssetSlots.id as AssetSlotId
                              ,tblAssetSections.AssetTypeId
                              ,tblBuildings.id as BuildingId
                        from tblAssetSlots
                        inner join tblAssetSections
                        on tblAssetSlots.AssetSectionId = tblAssetSections.id
                        and tblAssetSections.Deleted = 0
                        inner join tblAssetTypes
                        on tblAssetSections.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblAssetSlots.Deleted = 0
                        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
                        and tblAssetSlots.PermanentOwnerUid = @uid

                        -- Also query the user's admin functions if the user is an Admin,
                        -- or all functions if they are a Super Admin.
                        select tblFunctions.id
                              ,tblFunctions.Name
                              ,tblFunctions.BuildingId
                        from tblFunctions
                        where tblFunctions.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblFunctions.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @uid
                            left join tblUserAdminFunctions
                            on tblFunctions.id = tblUserAdminFunctions.FunctionId
                            and tblUserAdminFunctions.Uid = @uid
                            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminFunctions.FunctionId is not null
                                )
                            )
                        )

                        -- Also query the user's admin asset types if the user is an Admin,
                        -- or all asset types if they are a Super Admin.
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblAssetTypes
                        where tblAssetTypes.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @uid
                            left join tblUserAdminAssetTypes
                            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
                            and tblUserAdminAssetTypes.Uid = @uid
                            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminAssetTypes.AssetTypeId is not null
                                )
                            )
                        )
                    end
                    """;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                UserData? result = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                if (result is not null)
                {
                    result.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                    result.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                    FillExtendedDataOrganizations(result, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                }

                return result;
            }
        }

        /// <summary>
        /// Note: <paramref name="userData"/> should already have ExtendedData.Organizations assigned before calling this function.
        /// </summary>
        /// <param name="userData"></param>
        /// <param name="buildings"></param>
        /// <param name="buildingsWithBookableMeetingRooms"></param>
        /// <param name="buildingsWithBookableAssetSlots"></param>
        /// <param name="permanentSeats"></param>
        /// <param name="assetTypes"></param>
        /// <param name="permanentAssets"></param>
        /// <param name="adminAssetTypes"></param>
        public static void FillExtendedDataOrganizations(UserData userData, List<UserData_Building> buildings,
            List<Guid> buildingsWithBookableDesks, List<Guid> buildingsWithBookableMeetingRooms, List<Guid> buildingsWithBookableAssetSlots, List<UserData_PermanentSeat> permanentSeats,
            List<UserData_AssetType> assetTypes, List<UserData_PermanentAsset> permanentAssets, List<UserData_AdminFunction> adminFunctions, List<UserData_AdminAssetType> adminAssetTypes)
        {
            if (userData.ExtendedData.Organizations is null)
            {
                return;
            }

            Dictionary<Guid, UserData_UserOrganizations> organizationsDict = new Dictionary<Guid, UserData_UserOrganizations>();
            Dictionary<Guid, UserData_Building> buildingsDict = new Dictionary<Guid, UserData_Building>();
            Dictionary<(Guid, Guid), UserData_AssetType> assetTypesDict = new Dictionary<(Guid, Guid), UserData_AssetType>();

            // Add organizations to dictionary
            foreach (UserData_UserOrganizations organization in userData.ExtendedData.Organizations)
            {
                organizationsDict.Add(organization.Id, organization);
            }

            foreach (UserData_Building building in buildings)
            {
                buildingsDict.Add(building.Id, building);

                if (organizationsDict.TryGetValue(building.OrganizationId, out UserData_UserOrganizations? organization))
                {
                    if (organization is not null)
                    {
                        organization.Buildings.Add(building);
                    }
                }
            }

            foreach (Guid buildingWithBookableDesks in buildingsWithBookableDesks)
            {
                if (buildingsDict.TryGetValue(buildingWithBookableDesks, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.HasBookableDesks = true;
                        // break;  // should not break, because HasBookableDesks is with respect to the Building
                    }
                }
            }

            foreach (Guid buildingWithBookableMeetingRooms in buildingsWithBookableMeetingRooms)
            {
                if (buildingsDict.TryGetValue(buildingWithBookableMeetingRooms, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.HasBookableMeetingRooms = true;
                        // break;  // should not break, because HasBookableMeetingRooms is with respect to the Building
                    }
                }
            }

            foreach (Guid buildingWithBookableAssetSlots in buildingsWithBookableAssetSlots)
            {
                if (buildingsDict.TryGetValue(buildingWithBookableAssetSlots, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.HasBookableAssetSlots = true;
                        // break;  // should not break, because HasBookableAssetSlots is with respect to the Building
                    }
                }
            }

            foreach (UserData_PermanentSeat permanentSeat in permanentSeats)
            {
                if (buildingsDict.TryGetValue(permanentSeat.BuildingId, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.PermanentDeskId = permanentSeat.DeskId;
                    }
                }
            }

            foreach (UserData_AssetType assetType in assetTypes)
            {
                assetTypesDict.Add((assetType.BuildingId, assetType.Id), assetType);

                if (buildingsDict.TryGetValue(assetType.BuildingId, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.AssetTypes.Add(assetType);
                    }
                }
            }

            foreach (UserData_PermanentAsset permanentAsset in permanentAssets)
            {
                if (assetTypesDict.TryGetValue((permanentAsset.BuildingId, permanentAsset.AssetTypeId), out UserData_AssetType? assetType))
                {
                    if (assetType is not null)
                    {
                        assetType.PermanentAssetSlotId = permanentAsset.AssetSlotId;
                    }
                }
            }

            foreach (UserData_AdminFunction adminFunction in adminFunctions)
            {
                if (buildingsDict.TryGetValue(adminFunction.BuildingId, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.AdminFunctions.Add(adminFunction);
                    }
                }
            }

            foreach (UserData_AdminAssetType adminAssetType in adminAssetTypes)
            {
                if (buildingsDict.TryGetValue(adminAssetType.BuildingId, out UserData_Building? building))
                {
                    if (building is not null)
                    {
                        building.AdminAssetTypes.Add(adminAssetType);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves a user by email address.
        /// </summary>
        /// <param name="email">The user's email address.</param>
        /// <param name="cancellationToken">A token that may be used to cancel the database query.</param>
        /// <returns></returns>
        public async Task<UserData?> GetUserByEmailAsync(string email, bool updateLastAccessDate = false, bool includeOrganizationDisabled = false, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = "";

                sql += """
                    declare @_uid uniqueidentifier

                    select @_uid = Uid
                    from tblUsers
                    where Deleted = 0
                    and Email = @email

                    """;

                if (updateLastAccessDate)
                {
                    sql += """
                        update tblUsers
                        set LastAccessDateUtc = sysutcdatetime()
                        where Deleted = 0
                        and Uid = @_uid
                        and Disabled = 0
                        and UserSystemRole > 0

                        """;
                }

                sql += $"""
                    -- Query user data
                    select Uid
                          ,InsertDateUtc
                          ,UpdatedDateUtc
                          ,LastAccessDateUtc
                          ,LastPasswordChangeDateUtc
                          ,Email
                          ,HasPassword
                          ,TotpEnabled
                          ,UserSystemRole
                          ,DisplayName
                          ,FirstName
                          ,Surname
                          ,Timezone
                          ,AvatarUrl
                          ,AvatarThumbnailUrl
                          ,Disabled
                          ,ConcurrencyKey
                    from tblUsers
                    where Deleted = 0
                    and Uid = @_uid

                    if @@ROWCOUNT = 1
                    begin
                        -- Also query user's organization access
                        select tblUserOrganizationJoin.OrganizationId as id
                              ,tblOrganizations.Name
                              ,tblOrganizations.LogoImageUrl
                              ,tblOrganizations.CheckInEnabled
                              ,tblOrganizations.WorkplacePortalEnabled
                              ,tblOrganizations.WorkplaceAccessRequestsEnabled
                              ,tblOrganizations.WorkplaceInductionsEnabled
                              ,tblUserOrganizationJoin.UserOrganizationRole
                              ,tblUserOrganizationJoin.Note
                              ,tblUserOrganizationJoin.Contractor
                              ,tblUserOrganizationJoin.Visitor
                              ,tblUserOrganizationJoin.UserOrganizationDisabled
                              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserOrganizationJoin
                        inner join tblOrganizations
                        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
                        and tblOrganizations.Deleted = 0
                        and tblOrganizations.Disabled = 0
                        where tblUserOrganizationJoin.Uid = @_uid
                    """;

                if (!includeOrganizationDisabled)
                {
                    sql += """
                            and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
                            and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
                        """;
                }

                sql += $"""
                        order by tblOrganizations.Name

                        -- Also query user's last used building
                        select Uid
                              ,WebLastUsedOrganizationId
                              ,WebLastUsedBuildingId
                              ,MobileLastUsedOrganizationId
                              ,MobileLastUsedBuildingId
                        from tblUserLastUsedBuilding
                        where Uid = @_uid

                        -- Also query user's building access
                        select tblUserBuildingJoin.BuildingId as id
                              ,tblBuildings.Name
                              ,tblBuildings.OrganizationId
                              ,tblBuildings.Timezone
                              ,tblBuildings.CheckInEnabled
                              ,0 as HasBookableMeetingRooms -- Queried separately
                              ,0 as HasBookableAssetSlots -- Queried separately
                              ,tblUserBuildingJoin.FunctionId
                              ,tblFunctions.Name as FunctionName
                              ,tblFunctions.HtmlColor as FunctionHtmlColor
                              ,tblUserBuildingJoin.FirstAidOfficer
                              ,tblUserBuildingJoin.FireWarden
                              ,tblUserBuildingJoin.PeerSupportOfficer
                              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
                              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
                              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
                              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserBuildingJoin
                        inner join tblBuildings
                        on tblUserBuildingJoin.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        inner join tblFunctions
                        on tblUserBuildingJoin.FunctionId = tblFunctions.id
                        and tblFunctions.Deleted = 0
                        where tblUserBuildingJoin.Uid = @_uid
                        order by tblBuildings.Name

                        -- Also query user's buildings with bookable desks
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblDesks
                            inner join tblFloors
                            on tblDesks.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblDesks.Deleted = 0
                            and tblDesks.DeskType != {(int)DeskType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query user's buildings with bookable meeting rooms
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblMeetingRooms
                            inner join tblFloors
                            on tblMeetingRooms.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblMeetingRooms.Deleted = 0
                            and tblMeetingRooms.OfflineRoom = 0
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                            and
                            (
                                tblMeetingRooms.RestrictedRoom = 0
                                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
                            )
                        )

                        -- Also query user's buildings with bookable asset slots
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblAssetSlots
                            inner join tblAssetSections
                            on tblAssetSlots.AssetSectionId = tblAssetSections.id
                            and tblAssetSections.Deleted = 0
                            inner join tblAssetTypes
                            on tblAssetSections.AssetTypeId = tblAssetTypes.id
                            and tblAssetTypes.Deleted = 0
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblAssetSlots.Deleted = 0
                            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query the user's permanent seat
                        select tblDesks.id as DeskId
                              ,tblBuildings.id as BuildingId
                        from tblDesks
                        inner join tblFloors
                        on tblDesks.FloorId = tblFloors.id
                        and tblFloors.Deleted = 0
                        inner join tblBuildings
                        on tblFloors.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblDesks.Deleted = 0
                        and tblDesks.DeskType = {(int)DeskType.Permanent}
                        and tblDesks.PermanentOwnerUid = @_uid

                        -- Also query the user's asset types
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblUserAssetTypeJoin
                        inner join tblAssetTypes
                        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblUserAssetTypeJoin.Uid = @_uid

                        -- Also query the user's permanent assets
                        select tblAssetSlots.id as AssetSlotId
                              ,tblAssetSections.AssetTypeId
                              ,tblBuildings.id as BuildingId
                        from tblAssetSlots
                        inner join tblAssetSections
                        on tblAssetSlots.AssetSectionId = tblAssetSections.id
                        and tblAssetSections.Deleted = 0
                        inner join tblAssetTypes
                        on tblAssetSections.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblAssetSlots.Deleted = 0
                        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
                        and tblAssetSlots.PermanentOwnerUid = @_uid

                        -- Also query the user's admin functions if the user is an Admin,
                        -- or all functions if they are a Super Admin.
                        select tblFunctions.id
                              ,tblFunctions.Name
                              ,tblFunctions.BuildingId
                        from tblFunctions
                        where tblFunctions.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblFunctions.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminFunctions
                            on tblFunctions.id = tblUserAdminFunctions.FunctionId
                            and tblUserAdminFunctions.Uid = @_uid
                            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminFunctions.FunctionId is not null
                                )
                            )
                        )

                        -- Also query the user's admin asset types if the user is an Admin,
                        -- or all asset types if they are a Super Admin.
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblAssetTypes
                        where tblAssetTypes.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminAssetTypes
                            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
                            and tblUserAdminAssetTypes.Uid = @_uid
                            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminAssetTypes.AssetTypeId is not null
                                )
                            )
                        )
                    end
                    """;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                UserData? result = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                if (result is not null)
                {
                    result.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                    result.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                    FillExtendedDataOrganizations(result, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                }

                return result;
            }
        }

        /// <summary>
        /// Retrieves a user by email address, while validating that the given Azure TenantId and ObjectId are assigned to the user for Azure AD single sign on.
        /// </summary>
        /// <param name="tenantId"></param>
        /// <param name="objectId"></param>
        /// <param name="email"></param>
        /// <param name="updateLastAccessDate"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(bool azureTenantIdObjectIdValid, bool azureTenantIdObjectIdUnset, bool azureTenantIdObjectIdLinkedToOtherEmail, UserData?)> GetUserAndValidateAzureObjectIdAsync(Guid tenantId, Guid objectId, string email,
            bool updateLastAccessDate = false, string? loginSource = null, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = "";

                sql += """
                    declare @_uid uniqueidentifier
                    declare @_userTenantId uniqueidentifier
                    declare @_userObjectId uniqueidentifier
                    declare @_azureTenantIdObjectIdValid bit = 0
                    declare @_azureTenantIdObjectIdUnset bit = 0
                    declare @_azureTenantIdObjectIdLinkedToOtherEmail bit = 0

                    select @_uid = Uid
                    from tblUsers
                    where Deleted = 0
                    and Email = @email

                    select @_userTenantId = AzureTenantId
                          ,@_userObjectId = AzureObjectId
                    from tblUserAzureObjectId
                    where Uid = @_uid

                    if @_userTenantId is not null and @_userObjectId is not null
                        and @_userTenantId = @tenantId and @_userObjectId = @objectId
                    begin
                        set @_azureTenantIdObjectIdValid = 1
                    end
                    else if @_userTenantId is null or @_userObjectId is null
                    begin
                        set @_azureTenantIdObjectIdUnset = 1

                        -- As a double check - see if the TenantId/ObjectId combination belongs to a different email
                        select @_azureTenantIdObjectIdLinkedToOtherEmail = 1
                        from tblUserAzureObjectId
                        where AzureTenantId = @tenantId
                        and AzureObjectId = @objectId
                        and Uid != @_uid
                    end

                    """;

                if (updateLastAccessDate)
                {
                    sql += """
                        if @_azureTenantIdObjectIdValid = 1
                        begin
                            update tblUsers
                            set LastAccessDateUtc = sysutcdatetime()
                            where Deleted = 0
                            and Uid = @_uid
                            and Disabled = 0
                            and UserSystemRole > 0
                        end

                        """;
                }

                sql += $"""
                    select @_azureTenantIdObjectIdValid, @_azureTenantIdObjectIdUnset, @_azureTenantIdObjectIdLinkedToOtherEmail

                    -- Query user data
                    select Uid
                          ,InsertDateUtc
                          ,UpdatedDateUtc
                          ,LastAccessDateUtc
                          ,LastPasswordChangeDateUtc
                          ,Email
                          ,HasPassword
                          ,TotpEnabled
                          ,UserSystemRole
                          ,DisplayName
                          ,FirstName
                          ,Surname
                          ,Timezone
                          ,AvatarUrl
                          ,AvatarThumbnailUrl
                          ,Disabled
                          ,ConcurrencyKey
                    from tblUsers
                    where Deleted = 0
                    and Uid = @_uid

                    if @@ROWCOUNT = 1
                    begin
                        -- Also query user's organization access
                        select tblUserOrganizationJoin.OrganizationId as id
                              ,tblOrganizations.Name
                              ,tblOrganizations.LogoImageUrl
                              ,tblOrganizations.CheckInEnabled
                              ,tblOrganizations.WorkplacePortalEnabled
                              ,tblOrganizations.WorkplaceAccessRequestsEnabled
                              ,tblOrganizations.WorkplaceInductionsEnabled
                              ,tblUserOrganizationJoin.UserOrganizationRole
                              ,tblUserOrganizationJoin.Note
                              ,tblUserOrganizationJoin.Contractor
                              ,tblUserOrganizationJoin.Visitor
                              ,tblUserOrganizationJoin.UserOrganizationDisabled
                              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserOrganizationJoin
                        inner join tblOrganizations
                        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
                        and tblOrganizations.Deleted = 0
                        and tblOrganizations.Disabled = 0
                        where tblUserOrganizationJoin.Uid = @_uid
                        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
                        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
                        order by tblOrganizations.Name

                        -- Also query user's last used building
                        select Uid
                              ,WebLastUsedOrganizationId
                              ,WebLastUsedBuildingId
                              ,MobileLastUsedOrganizationId
                              ,MobileLastUsedBuildingId
                        from tblUserLastUsedBuilding
                        where Uid = @_uid

                        -- Also query user's building access
                        select tblUserBuildingJoin.BuildingId as id
                              ,tblBuildings.Name
                              ,tblBuildings.RegionId
                              ,tblRegions.Name as RegionName
                              ,tblBuildings.OrganizationId
                              ,tblBuildings.Timezone
                              ,tblBuildings.CheckInEnabled
                              ,0 as HasBookableMeetingRooms -- Queried separately
                              ,0 as HasBookableAssetSlots -- Queried separately
                              ,tblUserBuildingJoin.FunctionId
                              ,tblFunctions.Name as FunctionName
                              ,tblFunctions.HtmlColor as FunctionHtmlColor
                              ,tblUserBuildingJoin.FirstAidOfficer
                              ,tblUserBuildingJoin.FireWarden
                              ,tblUserBuildingJoin.PeerSupportOfficer
                              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
                              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
                              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
                              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserBuildingJoin
                        inner join tblBuildings
                        on tblUserBuildingJoin.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        inner join tblRegions
                        on tblBuildings.RegionId = tblRegions.id
                        and tblRegions.Deleted = 0
                        inner join tblFunctions
                        on tblUserBuildingJoin.FunctionId = tblFunctions.id
                        and tblFunctions.Deleted = 0
                        where tblUserBuildingJoin.Uid = @_uid
                        order by tblBuildings.Name

                        -- Also query user's buildings with bookable desks
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblDesks
                            inner join tblFloors
                            on tblDesks.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblDesks.Deleted = 0
                            and tblDesks.DeskType != {(int)DeskType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query user's buildings with bookable meeting rooms
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblMeetingRooms
                            inner join tblFloors
                            on tblMeetingRooms.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblMeetingRooms.Deleted = 0
                            and tblMeetingRooms.OfflineRoom = 0
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                            and
                            (
                                tblMeetingRooms.RestrictedRoom = 0
                                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
                            )
                        )

                        -- Also query user's buildings with bookable asset slots
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblAssetSlots
                            inner join tblAssetSections
                            on tblAssetSlots.AssetSectionId = tblAssetSections.id
                            and tblAssetSections.Deleted = 0
                            inner join tblAssetTypes
                            on tblAssetSections.AssetTypeId = tblAssetTypes.id
                            and tblAssetTypes.Deleted = 0
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblAssetSlots.Deleted = 0
                            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query the user's permanent seat
                        select tblDesks.id as DeskId
                              ,tblBuildings.id as BuildingId
                        from tblDesks
                        inner join tblFloors
                        on tblDesks.FloorId = tblFloors.id
                        and tblFloors.Deleted = 0
                        inner join tblBuildings
                        on tblFloors.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblDesks.Deleted = 0
                        and tblDesks.DeskType = {(int)DeskType.Permanent}
                        and tblDesks.PermanentOwnerUid = @_uid

                        -- Also query the user's asset types
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblUserAssetTypeJoin
                        inner join tblAssetTypes
                        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblUserAssetTypeJoin.Uid = @_uid

                        -- Also query the user's permanent assets
                        select tblAssetSlots.id as AssetSlotId
                              ,tblAssetSections.AssetTypeId
                              ,tblBuildings.id as BuildingId
                        from tblAssetSlots
                        inner join tblAssetSections
                        on tblAssetSlots.AssetSectionId = tblAssetSections.id
                        and tblAssetSections.Deleted = 0
                        inner join tblAssetTypes
                        on tblAssetSections.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblAssetSlots.Deleted = 0
                        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
                        and tblAssetSlots.PermanentOwnerUid = @_uid

                        -- Also query the user's admin functions if the user is an Admin,
                        -- or all functions if they are a Super Admin.
                        select tblFunctions.id
                              ,tblFunctions.Name
                              ,tblFunctions.BuildingId
                        from tblFunctions
                        where tblFunctions.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblFunctions.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminFunctions
                            on tblFunctions.id = tblUserAdminFunctions.FunctionId
                            and tblUserAdminFunctions.Uid = @_uid
                            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminFunctions.FunctionId is not null
                                )
                            )
                        )

                        -- Also query the user's admin asset types if the user is an Admin,
                        -- or all asset types if they are a Super Admin.
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblAssetTypes
                        where tblAssetTypes.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminAssetTypes
                            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
                            and tblUserAdminAssetTypes.Uid = @_uid
                            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminAssetTypes.AssetTypeId is not null
                                )
                            )
                        )
                    end
                    """;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@tenantId", tenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@objectId", objectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                (bool azureTenantIdObjectIdValid, bool azureTenantIdObjectIdUnset, bool azureTenantIdObjectIdLinkedToOtherEmail) = await gridReader.ReadFirstOrDefaultAsync<(bool, bool, bool)>();
                UserData? result = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                if (result is not null)
                {
                    result.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                    result.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                    FillExtendedDataOrganizations(result, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);

                    if (azureTenantIdObjectIdValid && !azureTenantIdObjectIdUnset)
                    {
                        // Update user's last access date.
                        (SqlQueryResult updateAccessDateResult, DateTime? newAccessDate) = await UpdateLastAccessDateForUserAsync(result.Uid, sqlConnection, true, loginSource, UserLoginType.SingleSignOn);

                        if (updateAccessDateResult == SqlQueryResult.Ok)
                        {
                            result.LastAccessDateUtc = newAccessDate;
                        }
                    }
                }

                return (azureTenantIdObjectIdValid, azureTenantIdObjectIdUnset, azureTenantIdObjectIdLinkedToOtherEmail, result);
            }
        }

        public async Task<UserDisplayNameData?> GetUserDisplayNameAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblUsers.Uid
      ,tblUsers.DisplayName
      ,tblUsers.FirstName
      ,tblUsers.Surname
      ,tblUsers.Email
      ,tblUsers.AvatarUrl
      ,tblUsers.AvatarThumbnailUrl
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
where tblUsers.Deleted = 0
and tblUsers.Uid = @uid
and tblUserOrganizationJoin.OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<UserDisplayNameData>(commandDefinition);
            }
        }

        public async Task<UserDisplayNameAndIsAssignedToBuildingData?> GetUserDisplayNameByEmailAndIsAssignedToBuildingAsync(string email, Guid organizationId, Guid buildingId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblUsers.Uid
      ,tblUsers.DisplayName
      ,tblUsers.FirstName
      ,tblUsers.Surname
      ,tblUsers.Email
      ,tblUsers.AvatarUrl
      ,tblUsers.AvatarThumbnailUrl

      ,tblUserOrganizationJoin.OrganizationId
      ,case when tblUserBuildingJoin.BuildingId is not null then 1 else 0 end as IsAssignedToBuilding
      ,tblUserBuildingJoin.BuildingId
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
and tblUserOrganizationJoin.OrganizationId = @organizationId
left join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
and tblUserBuildingJoin.BuildingId = @buildingId
where tblUsers.Deleted = 0
and tblUsers.Email = @email
";
                DynamicParameters parameters = new DynamicParameters();
                
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<UserDisplayNameAndIsAssignedToBuildingData>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Verfies an email/password combination and returns the user's data if valid.</para>
        /// </summary>
        /// <param name="email"></param>
        /// <param name="passwordPlainText"></param>
        /// <param name="cancellationToken">A token that may be used to cancel the database query.</param>
        /// <returns></returns>
        public async Task<(VerifyCredentialsResult, UserData?)> VerifyCredentialsAndGetUserAsync(string email, string passwordPlainText, string? totpCode,
            string? loginSource, CancellationToken cancellationToken = default)
        {
            UserData? userData;

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $"""
                    declare @_uid uniqueidentifier
                    
                    select @_uid = Uid
                    from tblUsers
                    where Deleted = 0
                    and Email = @email
                    
                    -- Query user data
                    select Uid
                          ,InsertDateUtc
                          ,UpdatedDateUtc
                          ,LastAccessDateUtc
                          ,LastPasswordChangeDateUtc
                          ,Email
                          ,HasPassword
                          ,PasswordHash
                          ,PasswordLockoutEndDateUtc
                          ,TotpEnabled
                          ,TotpSecret
                          ,TotpLockoutEndDateUtc
                          ,UserSystemRole
                          ,DisplayName
                          ,FirstName
                          ,Surname
                          ,Timezone
                          ,AvatarUrl
                          ,AvatarThumbnailUrl
                          ,Disabled
                          ,ConcurrencyKey
                    from tblUsers
                    where Deleted = 0
                    and Uid = @_uid
                    
                    if @@ROWCOUNT = 1
                    begin
                        -- Also query user's organization access
                        select tblUserOrganizationJoin.OrganizationId as id
                              ,tblOrganizations.Name
                              ,tblOrganizations.LogoImageUrl
                              ,tblOrganizations.CheckInEnabled
                              ,tblOrganizations.WorkplacePortalEnabled
                              ,tblOrganizations.WorkplaceAccessRequestsEnabled
                              ,tblOrganizations.WorkplaceInductionsEnabled
                              ,tblUserOrganizationJoin.UserOrganizationRole
                              ,tblUserOrganizationJoin.Note
                              ,tblUserOrganizationJoin.Contractor
                              ,tblUserOrganizationJoin.Visitor
                              ,tblUserOrganizationJoin.UserOrganizationDisabled
                              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserOrganizationJoin
                        inner join tblOrganizations
                        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
                        and tblOrganizations.Deleted = 0
                        and tblOrganizations.Disabled = 0
                        where tblUserOrganizationJoin.Uid = @_uid
                        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
                        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
                        order by tblOrganizations.Name

                        -- Also query user's last used building
                        select Uid
                              ,WebLastUsedOrganizationId
                              ,WebLastUsedBuildingId
                              ,MobileLastUsedOrganizationId
                              ,MobileLastUsedBuildingId
                        from tblUserLastUsedBuilding
                        where Uid = @_uid

                        -- Also query user's building access
                        select tblUserBuildingJoin.BuildingId as id
                              ,tblBuildings.Name
                              ,tblBuildings.RegionId
                              ,tblRegions.Name as RegionName
                              ,tblBuildings.OrganizationId
                              ,tblBuildings.Timezone
                              ,tblBuildings.CheckInEnabled
                              ,0 as HasBookableMeetingRooms -- Queried separately
                              ,0 as HasBookableAssetSlots -- Queried separately
                              ,tblUserBuildingJoin.FunctionId
                              ,tblFunctions.Name as FunctionName
                              ,tblFunctions.HtmlColor as FunctionHtmlColor
                              ,tblUserBuildingJoin.FirstAidOfficer
                              ,tblUserBuildingJoin.FireWarden
                              ,tblUserBuildingJoin.PeerSupportOfficer
                              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
                              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
                              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
                              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
                        from tblUserBuildingJoin
                        inner join tblBuildings
                        on tblUserBuildingJoin.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        inner join tblRegions
                        on tblBuildings.RegionId = tblRegions.id
                        and tblRegions.Deleted = 0
                        inner join tblFunctions
                        on tblUserBuildingJoin.FunctionId = tblFunctions.id
                        and tblFunctions.Deleted = 0
                        where tblUserBuildingJoin.Uid = @_uid
                        order by tblBuildings.Name

                        -- Also query user's buildings with bookable desks
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblDesks
                            inner join tblFloors
                            on tblDesks.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblDesks.Deleted = 0
                            and tblDesks.DeskType != {(int)DeskType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )
                    
                        -- Also query user's buildings with bookable meeting rooms
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblMeetingRooms
                            inner join tblFloors
                            on tblMeetingRooms.FloorId = tblFloors.id
                            and tblFloors.Deleted = 0
                            inner join tblBuildings
                            on tblFloors.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblMeetingRooms.Deleted = 0
                            and tblMeetingRooms.OfflineRoom = 0
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                            and
                            (
                                tblMeetingRooms.RestrictedRoom = 0
                                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
                            )
                        )

                        -- Also query user's buildings with bookable asset slots
                        select tblUserBuildingJoin.BuildingId
                        from tblUserBuildingJoin
                        where tblUserBuildingJoin.Uid = @_uid
                        and exists
                        (
                            select *
                            from tblAssetSlots
                            inner join tblAssetSections
                            on tblAssetSlots.AssetSectionId = tblAssetSections.id
                            and tblAssetSections.Deleted = 0
                            inner join tblAssetTypes
                            on tblAssetSections.AssetTypeId = tblAssetTypes.id
                            and tblAssetTypes.Deleted = 0
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            where tblAssetSlots.Deleted = 0
                            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
                            and tblBuildings.id = tblUserBuildingJoin.BuildingId
                        )

                        -- Also query the user's permanent seat
                        select tblDesks.id as DeskId
                              ,tblBuildings.id as BuildingId
                        from tblDesks
                        inner join tblFloors
                        on tblDesks.FloorId = tblFloors.id
                        and tblFloors.Deleted = 0
                        inner join tblBuildings
                        on tblFloors.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblDesks.Deleted = 0
                        and tblDesks.DeskType = {(int)DeskType.Permanent}
                        and tblDesks.PermanentOwnerUid = @_uid

                        -- Also query the user's asset types
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblUserAssetTypeJoin
                        inner join tblAssetTypes
                        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblUserAssetTypeJoin.Uid = @_uid

                        -- Also query the user's permanent assets
                        select tblAssetSlots.id as AssetSlotId
                              ,tblAssetSections.AssetTypeId
                              ,tblBuildings.id as BuildingId
                        from tblAssetSlots
                        inner join tblAssetSections
                        on tblAssetSlots.AssetSectionId = tblAssetSections.id
                        and tblAssetSections.Deleted = 0
                        inner join tblAssetTypes
                        on tblAssetSections.AssetTypeId = tblAssetTypes.id
                        and tblAssetTypes.Deleted = 0
                        inner join tblBuildings
                        on tblAssetTypes.BuildingId = tblBuildings.id
                        and tblBuildings.Deleted = 0
                        where tblAssetSlots.Deleted = 0
                        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
                        and tblAssetSlots.PermanentOwnerUid = @_uid

                        -- Also query the user's admin functions if the user is an Admin,
                        -- or all functions if they are a Super Admin.
                        select tblFunctions.id
                              ,tblFunctions.Name
                              ,tblFunctions.BuildingId
                        from tblFunctions
                        where tblFunctions.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblFunctions.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminFunctions
                            on tblFunctions.id = tblUserAdminFunctions.FunctionId
                            and tblUserAdminFunctions.Uid = @_uid
                            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminFunctions.FunctionId is not null
                                )
                            )
                        )

                        -- Also query the user's admin asset types if the user is an Admin,
                        -- or all asset types if they are a Super Admin.
                        select tblAssetTypes.id
                              ,tblAssetTypes.Name
                              ,tblAssetTypes.BuildingId
                              ,tblAssetTypes.LogoImageUrl
                        from tblAssetTypes
                        where tblAssetTypes.Deleted = 0
                        and exists
                        (
                            select *
                            from tblUserBuildingJoin
                            inner join tblBuildings
                            on tblAssetTypes.BuildingId = tblBuildings.id
                            and tblBuildings.Deleted = 0
                            inner join tblUserOrganizationJoin
                            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
                            and tblUserOrganizationJoin.Uid = @_uid
                            left join tblUserAdminAssetTypes
                            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
                            and tblUserAdminAssetTypes.Uid = @_uid
                            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
                            and tblUserBuildingJoin.Uid = @_uid
                            and
                            (
                                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                                or
                                (
                                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                                    and tblUserAdminAssetTypes.AssetTypeId is not null
                                )
                            )
                        )
                    end
                    """;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                // User did not exist
                if (userData is null)
                {
                    return (VerifyCredentialsResult.UserDidNotExist, null);
                }

                // Read extended data
                userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);

                // User does not have access to the system
                if (userData.Disabled || userData.UserSystemRole == UserSystemRole.NoAccess)
                {
                    return (VerifyCredentialsResult.NoAccess, null);
                }

                // User does not have a password (user must login using Single Sign On only)
                if (userData.PasswordHash is null)
                {
                    return (VerifyCredentialsResult.PasswordNotSet, null);
                }

                // Check if user has been locked out due to too many invalid password attempts
                if (userData.PasswordLockoutEndDateUtc > DateTime.UtcNow)
                {
                    return (VerifyCredentialsResult.PasswordLoginLockedOut, null);
                }

                // Check if 2fa is enabled
                if (userData.TotpEnabled)
                {
                    // Check if user has been locked out due to too many invalid 2fa attempts
                    if (userData.TotpLockoutEndDateUtc > DateTime.UtcNow)
                    {
                        return (VerifyCredentialsResult.TotpLockedOut, null);
                    }
                }
                else
                {
                    // Clear totp secret if there was one loaded from database
                    userData.TotpSecret = null;
                }

                // Validate credentials by recalculating the password hash and see if it matches
                // the one stored in the database.
                if (HMAC_Bcrypt.hmac_bcrypt_verify(passwordPlainText, userData.PasswordHash, _appSettings.Password.Pepper))
                {
                    // Perform additional verification if user's account has 2fa enabled
                    if (userData.TotpEnabled)
                    {
                        // Confirm totp secret is not null
                        if (string.IsNullOrEmpty(userData.TotpSecret))
                        {
                            // Should never happen - totp was enabled but user has no secret set in database
                            return (VerifyCredentialsResult.UnknownError, null);
                        }

                        // Check if 6-digit code was not supplied
                        if (string.IsNullOrEmpty(totpCode))
                        {
                            return (VerifyCredentialsResult.TotpCodeRequired, null);
                        }

                        // Verify the code
                        VerifyTotpCodeResult verifyTotpCodeResult = _totpHelpers.VerifyCode(userData.Uid, totpCode!, userData.TotpSecret);

                        // Clear totp secret from result before returning
                        userData.TotpSecret = null;

                        // Check totp verify result
                        if (verifyTotpCodeResult != VerifyTotpCodeResult.Ok)
                        {
                            // Totp verify failed, clear password hash from result before returning
                            userData.PasswordHash = null;

                            switch (verifyTotpCodeResult)
                            {
                                case VerifyTotpCodeResult.TotpCodeInvalid:
                                    // Since code was wrong, increment totp failure count
                                    (SqlQueryResult incrementTotpFailureCountResult, bool? isTotpLockedOut) = await IncrementTotpLoginFailureCountAsync(userData.Uid, sqlConnection, loginSource, UserLoginType.LocalPassword);

                                    if (incrementTotpFailureCountResult == SqlQueryResult.Ok && isTotpLockedOut.HasValue && isTotpLockedOut.Value)
                                    {
                                        // Account is now locked out after too many failed password attempts
                                        return (VerifyCredentialsResult.TotpLockedOut, null);
                                    }

                                    return (VerifyCredentialsResult.TotpCodeInvalid, null);
                                case VerifyTotpCodeResult.TotpCodeAlreadyUsed:
                                    // Reject duplicate codes, but don't count towards failures total
                                    return (VerifyCredentialsResult.TotpCodeAlreadyUsed, null);
                                default:
                                    // This should never happen
                                    return (VerifyCredentialsResult.UnknownError, null);
                            }
                        }
                    }

                    // If stored password hash's BcryptCost is different to appsetting.json's BcryptCost,
                    // recalculate the hash with the new cost value, and update it back to the database.
                    // This is so in future if we increase the cost in appsettings.json for better security,
                    // then the next time a user with a hash generated with a lower cost value logs in,
                    // we'll update their stored hash to a stronger one.

                    string[] sets = userData.PasswordHash.Split("$");
                    int currentHashCost = short.Parse(sets[2]);

                    // Uses != instead of < just in case for some reason we want to update existing hashes
                    // to a lower cost.
                    if (currentHashCost != _appSettings.Password.BcryptCost)
                    {
                        // Rehash the user's password. Also updates user's last access date.
                        await RehashPasswordForUserAsync(userData.Uid, passwordPlainText, sqlConnection);
                    }
                    else
                    {
                        // Update user's last access date.
                        (SqlQueryResult updateAccessDateResult, DateTime? newAccessDate) = await UpdateLastAccessDateForUserAsync(userData.Uid, sqlConnection, true, loginSource, UserLoginType.LocalPassword);

                        if (updateAccessDateResult == SqlQueryResult.Ok)
                        {
                            userData.LastAccessDateUtc = newAccessDate;
                        }
                    }

                    // Clear password hash from result before returning
                    userData.PasswordHash = null;

                    return (VerifyCredentialsResult.Ok, userData);
                }

                // Increment PasswordLoginFailureCount
                (SqlQueryResult incrementPasswordFailureCountResult, bool? isPasswordLockedOut) = await IncrementPasswordLoginFailureCountAsync(userData.Uid, sqlConnection, loginSource, UserLoginType.LocalPassword);

                if (incrementPasswordFailureCountResult == SqlQueryResult.Ok && isPasswordLockedOut.HasValue && isPasswordLockedOut.Value)
                {
                    // Account is now locked out after too many failed password attempts
                    return (VerifyCredentialsResult.PasswordLoginLockedOut, null);
                }

                // Invalid credentials
                return (VerifyCredentialsResult.PasswordInvalid, null);
            }
        }

        private async Task<(SqlQueryResult, bool? isLockedOut)> IncrementPasswordLoginFailureCountAsync(Guid uid, SqlConnection sqlConnection, string? loginSource, UserLoginType? userLoginType)
        {
            string sql = """
                declare @_now datetime2(3) = sysutcdatetime()
                declare @_result int = 0
                declare @_lockoutMinutes int = 5
                declare @_maxAttempts int = 5
                declare @_isLockedOut bit = 0

                -- Update failure count
                update tblUsers
                set PasswordLoginLastFailureDateUtc = @_now
                   ,PasswordLoginFailureCount = case when PasswordLoginLastFailureDateUtc is null or PasswordLoginLastFailureDateUtc < dateadd(minute, -@_lockoutMinutes, @_now)
                                                     then 1
                                                     else PasswordLoginFailureCount + 1
                                                end
                   ,PasswordLockoutEndDateUtc = case when PasswordLockoutEndDateUtc is null
                                                      and case when PasswordLoginLastFailureDateUtc is null or PasswordLoginLastFailureDateUtc < dateadd(minute, -@_lockoutMinutes, @_now)
                                                               then 1
                                                               else PasswordLoginFailureCount + 1
                                                          end >= @_maxAttempts
                                                     then dateadd(minute, @_lockoutMinutes, @_now)
                                                     else null
                                                end
                where Deleted = 0
                and Uid = @uid

                select @_isLockedOut = case when PasswordLockoutEndDateUtc > @_now
                                            then 1
                                            else 0
                                       end
                from tblUsers
                where Deleted = 0
                and Uid = @uid

                if @@ROWCOUNT > 0
                begin
                    set @_result = 1

                    insert into tblLoginHistory
                    (id
                    ,InsertDateUtc
                    ,Uid
                    ,Source
                    ,Success
                    ,FailReason
                    ,LoginType)
                    values
                    (@logId
                    ,@_now
                    ,@uid
                    ,@source
                    ,0 -- Success
                    ,'Invalid Password' -- FailReason
                    ,@userLoginType)
                end
                else
                begin
                    -- Record was not updated
                    set @_result = 2
                end

                select @_result, @_isLockedOut
                """;

            Guid logId = RT.Comb.Provider.Sql.Create();

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@source", loginSource, DbType.AnsiString, ParameterDirection.Input, 30);
            parameters.Add("@userLoginType", userLoginType?.ToString(), DbType.AnsiString, ParameterDirection.Input, 50);
            parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);

            (int resultCode, bool isLockedOut) = await sqlConnection.QueryFirstOrDefaultAsync<(int, bool)>(sql, parameters);

            switch (resultCode)
            {
                case 1:
                    return (SqlQueryResult.Ok, isLockedOut);
                case 2:
                    return (SqlQueryResult.RecordDidNotExist, null);
                default:
                    return (SqlQueryResult.UnknownError, null);
            }
        }

        private async Task<(SqlQueryResult, bool? isLockedOut)> IncrementTotpLoginFailureCountAsync(Guid uid, SqlConnection sqlConnection, string? loginSource, UserLoginType? userLoginType)
        {
            string sql = """
                declare @_now datetime2(3) = sysutcdatetime()
                declare @_result int = 0
                declare @_lockoutMinutes int = 5
                declare @_maxAttempts int = 5
                declare @_isLockedOut bit = 0

                -- Update failure count
                update tblUsers
                set TotpLastFailureDateUtc = @_now
                   ,TotpFailureCount = case when TotpLastFailureDateUtc is null or TotpLastFailureDateUtc < dateadd(minute, -@_lockoutMinutes, @_now)
                                            then 1
                                            else TotpFailureCount + 1
                                       end
                   ,TotpLockoutEndDateUtc = case when TotpLockoutEndDateUtc is null
                                                  and case when TotpLastFailureDateUtc is null or TotpLastFailureDateUtc < dateadd(minute, -@_lockoutMinutes, @_now)
                                                           then 1
                                                           else TotpFailureCount + 1
                                                      end >= @_maxAttempts
                                                 then dateadd(minute, @_lockoutMinutes, @_now)
                                                 else null
                                            end
                where Deleted = 0
                and Uid = @uid

                select @_isLockedOut = case when TotpLockoutEndDateUtc > @_now
                                            then 1
                                            else 0
                                       end
                from tblUsers
                where Deleted = 0
                and Uid = @uid

                if @@ROWCOUNT > 0
                begin
                    set @_result = 1

                    insert into tblLoginHistory
                    (id
                    ,InsertDateUtc
                    ,Uid
                    ,Source
                    ,Success
                    ,FailReason
                    ,LoginType)
                    values
                    (@logId
                    ,@_now
                    ,@uid
                    ,@source
                    ,0 -- Success
                    ,'Invalid Totp Code' -- FailReason
                    ,@userLoginType)
                end
                else
                begin
                    -- Record was not updated
                    set @_result = 2
                end

                select @_result, @_isLockedOut
                """;

            Guid logId = RT.Comb.Provider.Sql.Create();

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@source", loginSource, DbType.AnsiString, ParameterDirection.Input, 30);
            parameters.Add("@userLoginType", userLoginType?.ToString(), DbType.AnsiString, ParameterDirection.Input, 50);
            parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);

            (int resultCode, bool isLockedOut) = await sqlConnection.QueryFirstOrDefaultAsync<(int, bool)>(sql, parameters);

            switch (resultCode)
            {
                case 1:
                    return (SqlQueryResult.Ok, isLockedOut);
                case 2:
                    return (SqlQueryResult.RecordDidNotExist, null);
                default:
                    return (SqlQueryResult.UnknownError, null);
            }
        }

        private async Task<SqlQueryResult> UnlockAccountAsync(Guid uid)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = """
                update tblUsers
                set PasswordLoginFailureCount = 0
                   ,PasswordLoginLastFailureDateUtc = null
                   ,PasswordLockoutEndDateUtc = null
                   ,TotpFailureCount = 0
                   ,TotpLastFailureDateUtc = null
                   ,TotpLockoutEndDateUtc = null
                where Deleted = 0
                and Uid = @uid
                """;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                int rowsAffected = await sqlConnection.ExecuteAsync(sql, parameters);

                return rowsAffected > 0 ? SqlQueryResult.Ok : SqlQueryResult.RecordDidNotExist;
            }
        }

        /// <summary>
        /// Overwrites a user's password. Intended to be used when rehashing a password with a new BcrpytCost after successful login only.
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="passwordPlainText"></param>
        /// <returns></returns>
        private async Task<SqlQueryResult> RehashPasswordForUserAsync(Guid uid, string passwordPlainText, SqlConnection sqlConnection)
        {
            string passwordHash = HMAC_Bcrypt.hmac_bcrypt_hash(passwordPlainText, _bcryptSettings, _appSettings.Password.Pepper);

            string sql = """
                update tblUsers
                set PasswordHash = @passwordHash
                   ,PasswordLoginFailureCount = 0
                   ,PasswordLoginLastFailureDateUtc = null
                   ,PasswordLockoutEndDateUtc = null
                   ,TotpFailureCount = 0
                   ,TotpLastFailureDateUtc = null
                   ,TotpLockoutEndDateUtc = null
                   ,LastAccessDateUtc = sysutcdatetime()
                where Deleted = 0
                and Uid = @uid
                """;

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@passwordHash", passwordHash, DbType.AnsiString, ParameterDirection.Input, 115);

            int rowsAffected = await sqlConnection.ExecuteAsync(sql, parameters);

            return rowsAffected > 0 ? SqlQueryResult.Ok : SqlQueryResult.RecordDidNotExist;
        }

        /// <summary>
        /// <para>Changes a user's password if their current password matches.</para>
        /// <para>Returns: <see cref="VerifyCredentialsResult.Ok"/>, <see cref="VerifyCredentialsResult.UserDidNotExist"/>, <see cref="VerifyCredentialsResult.NoAccess"/>,
        /// <see cref="VerifyCredentialsResult.PasswordInvalid"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="passwordPlainText"></param>
        /// <returns></returns>
        private async Task<(VerifyCredentialsResult, DateTime? updatedLastPasswordChangedDate)> ChangePasswordAsync(Guid uid, string? currentPasswordPlainText, string newPasswordPlainText, SqlConnection sqlConnection)
        {
            string sqlVerify = """
                select Disabled, PasswordHash
                from tblUsers
                where Deleted = 0
                and Uid = @uid
                """;

            DynamicParameters parametersVerify = new DynamicParameters();
            parametersVerify.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

            (bool? disabled, string? currentPasswordHash) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, string)>(sqlVerify, parametersVerify);

            // If disabled is null, it means the above query returned 0 rows, i.e. user did not exist
            if (!disabled.HasValue)
            {
                return (VerifyCredentialsResult.UserDidNotExist, null);
            }

            // Check if user's account is disabled
            if (disabled.Value)
            {
                return (VerifyCredentialsResult.NoAccess, null);
            }

            // If user does not currently have a password, proceed so we allow them to set their password.
            // Otherwise if they have a password, we verify it. If invalid, we won't change it to the new one.
            if (currentPasswordHash is not null)
            {
                if (currentPasswordPlainText is null
                    || !HMAC_Bcrypt.hmac_bcrypt_verify(currentPasswordPlainText, currentPasswordHash, _appSettings.Password.Pepper))
                {
                    // Current password is invalid
                    return (VerifyCredentialsResult.PasswordInvalid, null);
                }
            }

            // Generate the hash for the new password
            string newPasswordHash = HMAC_Bcrypt.hmac_bcrypt_hash(newPasswordPlainText, _bcryptSettings, _appSettings.Password.Pepper);

            // Set the new hash in the database, update last password change date, and reset password failure counts
            string sql = """
                declare @_now datetime2(3) = sysutcdatetime()

                update tblUsers
                set LastPasswordChangeDateUtc = @_now
                   ,PasswordHash = @passwordHash
                   ,PasswordLoginFailureCount = 0
                   ,PasswordLoginLastFailureDateUtc = null
                   ,PasswordLockoutEndDateUtc = null
                   ,TotpFailureCount = 0
                   ,TotpLastFailureDateUtc = null
                   ,TotpLockoutEndDateUtc = null
                where Deleted = 0
                and Uid = @uid

                select @@ROWCOUNT, @_now
                """;

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@passwordHash", newPasswordHash, DbType.AnsiString, ParameterDirection.Input, 115);

            (int rowsAffected, DateTime updateLastPasswordChangeDateUtc) = await sqlConnection.QueryFirstOrDefaultAsync<(int, DateTime)>(sql, parameters);

            return rowsAffected > 0 ? (VerifyCredentialsResult.Ok, updateLastPasswordChangeDateUtc) : (VerifyCredentialsResult.UserDidNotExist, null);
        }

        /// <summary>
        /// Updates a user's last access date.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        private async Task<(SqlQueryResult, DateTime?)> UpdateLastAccessDateForUserAsync(Guid uid, SqlConnection sqlConnection, bool isNewLogin = false, string? loginSource = null, UserLoginType? userLoginType = null)
        {
            string sql = """
                declare @_now datetime2(3) = sysutcdatetime()
                declare @_result int = 0

                update tblUsers
                set LastAccessDateUtc = @_now
                   ,PasswordLoginFailureCount = 0
                   ,PasswordLoginLastFailureDateUtc = null
                   ,PasswordLockoutEndDateUtc = null
                   ,TotpFailureCount = 0
                   ,TotpLastFailureDateUtc = null
                   ,TotpLockoutEndDateUtc = null
                where Deleted = 0
                and Uid = @uid
                and Disabled = 0
                and UserSystemRole > 0

                if @@ROWCOUNT > 0
                begin
                    set @_result = 1
                """;

            if (isNewLogin)
            {
                sql += """
                        insert into tblLoginHistory
                        (id
                        ,InsertDateUtc
                        ,Uid
                        ,Source
                        ,Success
                        ,FailReason
                        ,LoginType)
                        values
                        (@logId
                        ,@_now
                        ,@uid
                        ,@source
                        ,1 -- Success
                        ,null -- FailReason
                        ,@userLoginType)
                    """;
            }

            sql += """
                end
                else
                begin
                    -- Record was not updated
                    set @_result = 2
                end

                select @_result, @_now
                """;

            Guid logId = RT.Comb.Provider.Sql.Create();

            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

            if (isNewLogin)
            {
                parameters.Add("@source", loginSource, DbType.AnsiString, ParameterDirection.Input, 30);
                parameters.Add("@userLoginType", userLoginType?.ToString(), DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
            }

            (int resultCode, DateTime now) = await sqlConnection.QueryFirstOrDefaultAsync<(int, DateTime)>(sql, parameters);

            switch (resultCode)
            {
                case 1:
                    return (SqlQueryResult.Ok, now);
                case 2:
                    return (SqlQueryResult.RecordDidNotExist, null);
                default:
                    return (SqlQueryResult.UnknownError, null);
            }
        }

        /// <summary>
        /// <para>Registers a new user account if an existing account with the same email address does not exist, and returns the user's data.</para>
        /// <para>Returns one of the following results: <see cref="UserSelfRegistrationResult.Ok"/>, <see cref="UserSelfRegistrationResult.RecordAlreadyExists"/>,
        /// <see cref="UserSelfRegistrationResult.LocalLoginDisabled"/>, <see cref="UserSelfRegistrationResult.RegisterTokenInvalid"/>,
        /// <see cref="UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization"/>, <see cref="UserSelfRegistrationResult.GetAppLockFailed"/>.</para>
        /// </summary>
        /// <param name="request">A <see cref="AuthCompleteRegisterRequest"/> containing the new user's details.</param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserSelfRegistrationResult, UserData?)> CompleteRegisterAsync(AuthCompleteRegisterRequest request, string? remoteIpAddress)
        {
            // First check the register token is valid
            (UserSelfRegistrationResult checkTokenResult, _) = await CheckRegisterTokenAsync(request.Email!, request.Token!, remoteIpAddress);

            if (checkTokenResult != UserSelfRegistrationResult.Ok)
            {
                return (checkTokenResult, null);
            }

            // Check if organization belonging to user's email domain has local login disabled
            LoginOptions loginOptions = await GetLoginOptionsAsync(request.Email!);

            if (loginOptions.OrganizationId is null)
            {
                return (UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization, null);
            }
            else if (loginOptions.DisableLocalLoginEnabled)
            {
                return (UserSelfRegistrationResult.LocalLoginDisabled, null);
            }
            else if (loginOptions.Uid is not null)
            {
                return (UserSelfRegistrationResult.RecordAlreadyExists, null);
            }

            string passwordHash = HMAC_Bcrypt.hmac_bcrypt_hash(request.LocalPassword!, _bcryptSettings, _appSettings.Password.Pepper);

            string logDescription = "User Self-Register";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_validatedRegionId uniqueidentifier = null
declare @_validatedBuildingId uniqueidentifier = null
declare @_validatedFunctionId uniqueidentifier = null
declare @_buildingTimezone varchar(50) = null

declare @_registerTokensData table
(
    Email nvarchar(254)
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
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
    declare @_organizationId uniqueidentifier

    -- Check if user's email domain belongs to an organization
    select @_organizationId = OrganizationId
    from tblOrganizationDomains
    inner join tblOrganizations
    on tblOrganizationDomains.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblOrganizationDomains.DomainName = @emailDomainName

    -- Check if provided region, building and function belongs to organization.
    -- Also retrieve the building timezone, which will be used for the new user's timezone.
    select @_validatedRegionId = tblRegions.id
          ,@_validatedBuildingId = tblBuildings.id
          ,@_validatedFunctionId = tblFunctions.id
          ,@_buildingTimezone = tblBuildings.Timezone
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblRegions
    on tblBuildings.RegionId = tblRegions.id
    and tblRegions.Deleted = 0
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @functionId
    and tblBuildings.id = @buildingId
    and tblRegions.id = @regionId
    and tblOrganizations.id = @_organizationId

    if @_organizationId is null
    begin
        -- User Email Domain does not belong to an existing organization
        set @_result = 3
        rollback
    end
    else if @_validatedRegionId is null or @_validatedBuildingId is null or @_validatedFunctionId is null
    begin
        -- Provided regionId or buildingId or functionId does not belong to matched organization
        set @_result = 4
        rollback
    end
    else
    begin
        -- Organization, Region, Building and Function check passed - attempt to create the user
        insert into tblUsers
        (Uid
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,LastPasswordChangeDateUtc
        ,Email
        ,PasswordHash
        ,UserSystemRole
        ,DisplayName
        ,FirstName
        ,Surname
        ,Timezone)
        select @uid
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,@_now -- LastPasswordChangeDateUtc
              ,@email
              ,@passwordHash
              ,{(int)UserSystemRole.User} -- User is added to the system as a normal user (system role)
              ,@displayName
              ,@firstName
              ,@surname
              ,@_buildingTimezone
        where not exists
        (
            select *
            from tblUsers
            where Deleted = 0
            and Email = @email
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            insert into tblUserOrganizationJoin
            (Uid
            ,OrganizationId
            ,InsertDateUtc
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled)
            select @uid
                  ,@_organizationId
                  ,@_now -- InsertDateUtc
                  ,{(int)UserOrganizationRole.User} -- User is added to the organization as a normal user (organization role)
                  ,null -- Note
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled

            insert into tblUserOrganizationJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinLogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@_organizationId
                  ,{(int)UserOrganizationRole.User} -- User is added to the organization as a normal user (organization role)
                  ,null -- Note
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserOrganizationJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserOrganizationJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc)
            select @userOrganizationJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@uid
                  ,@organizationId
                  ,{(int)UserOrganizationRole.User} -- UserOrganizationRole -- User is added to the organization as a normal user (organization role)
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user organization join history for the new user
            insert into tblUserOrganizationJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,UserOrganizationJoinHistoryId
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@userOrganizationJoinHistoryId
                  ,@uid
                  ,@organizationId
                  ,{(int)UserOrganizationRole.User} -- UserOrganizationRole -- User is added to the organization as a normal user (organization role)
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin

            insert into tblUserBuildingJoin
            (Uid
            ,BuildingId
            ,InsertDateUtc
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere)
            select @uid
                  ,@buildingId
                  ,@_now -- InsertDateUtc
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere

            insert into tblUserBuildingJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinLogid
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@_organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserBuildingJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserBuildingJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc)
            select @userBuildingJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user building join history for the new user
            insert into tblUserBuildingJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,UserBuildingJoinHistoryId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@userBuildingJoinHistoryId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            insert into tblUsers_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,Email
            ,UserSystemRole
            ,DisplayName
            ,FirstName
            ,Surname
            ,Timezone
            ,LogAction)
            select @logId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@email
                  ,{(int)UserSystemRole.User} -- User is added to the system as a normal user (system role)
                  ,@displayName
                  ,@firstName
                  ,@surname
                  ,@_buildingTimezone
                  ,'Insert' -- LogAction

            -- Remove all Register tokens for the user
            delete from tblRegisterTokens
            output deleted.Email
                  ,deleted.ExpiryDateUtc
                  ,deleted.Location
                  ,deleted.BrowserName
                  ,deleted.OSName
                  ,deleted.DeviceInfo
                  into @_registerTokensData
            where Email = @email

            -- Log removed tokens
            insert into tblRegisterTokens_Log
            (id
            ,InsertDateUtc
            ,UpdatedByIpAddress
            ,LogDescription
            ,Email
            ,ExpiryDateUtc
            ,Location
            ,BrowserName
            ,OSName
            ,DeviceInfo
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
                  ,@_now
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@email
                  ,d.ExpiryDateUtc
                  ,d.Location
                  ,d.BrowserName
                  ,d.OSName
                  ,d.DeviceInfo
                  ,'Delete' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_registerTokensData d

            commit
        end
        else
        begin
            -- Record already exists
            set @_result = 2
            rollback
        end
    end
end

select @_result

if @_result = 1
begin
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
        order by tblOrganizations.Name
                    
        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )
    end
end
";
                Guid uid = RT.Comb.Provider.Sql.Create();
                Guid logId = RT.Comb.Provider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Email!)));
                string displayName = $"{request.FirstName} {request.Surname}".Trim();
                string emailDomainName = Toolbox.GetDomainFromEmailAddress(request.Email!)!;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblUsers_Email_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@email", request.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@passwordHash", passwordHash, DbType.AnsiString, ParameterDirection.Input, 115);
                parameters.Add("@displayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@firstName", request.FirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", request.Surname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@emailDomainName", emailDomainName, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@regionId", request.RegionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionId", request.FunctionId, DbType.Guid, ParameterDirection.Input);

                // Histories
                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@userOrganizationJoinHistoryId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                    if (userData is not null)
                    {
                        // Read extended data
                        userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                        userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                        List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                        List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<UserData_PermanentSeat> permanentSeats = new List<UserData_PermanentSeat>();
                        List<UserData_AssetType> assetTypes = new List<UserData_AssetType>();
                        List<UserData_PermanentAsset> permanentAssets = new List<UserData_PermanentAsset>();
                        List<UserData_AdminFunction> adminFunctions = new List<UserData_AdminFunction>();
                        List<UserData_AdminAssetType> adminAssetTypes = new List<UserData_AdminAssetType>();

                        FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                    }
                }

                UserSelfRegistrationResult userSelfRegistrationResult;

                switch (resultCode)
                {
                    case 1:
                        userSelfRegistrationResult = UserSelfRegistrationResult.Ok;
                        break;
                    case 2:
                        userSelfRegistrationResult = UserSelfRegistrationResult.RecordAlreadyExists;
                        break;
                    case 3:
                        userSelfRegistrationResult = UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization;
                        break;
                    case 4:
                        userSelfRegistrationResult = UserSelfRegistrationResult.BuildingIdOrFunctionIdDoesNotBelongToMatchedOrganization;
                        break;
                    case 999:
                        // Database failed to immediately get an app lock. This means another query was
                        // running at exactly the same time which was trying to register a user with the same
                        // email address.
                        userSelfRegistrationResult = UserSelfRegistrationResult.GetAppLockFailed;
                        break;
                    default:
                        userSelfRegistrationResult = UserSelfRegistrationResult.UnknownError;
                        break;
                }

                return (userSelfRegistrationResult, userData);
            }
        }

        /// <summary>
        /// <para>Registers a new user account if an existing account with the same email address does not exist, and returns the user's data.</para>
        /// <para>Returns one of the following results: <see cref="UserSelfRegistrationResult.Ok"/>, <see cref="UserSelfRegistrationResult.RecordAlreadyExists"/>,
        /// <see cref="UserSelfRegistrationResult.SingleSignOnNotEnabled"/>, <see cref="UserSelfRegistrationResult.RegisterTokenInvalid"/>,
        /// <see cref="UserSelfRegistrationResult.GetAppLockFailed"/>.</para>
        /// </summary>
        /// <param name="request">A <see cref="AuthCompleteRegisterAzureADRequest"/> containing the new user's details.</param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserSelfRegistrationResult, UserData?)> CompleteRegisterAzureADTokenAsync(AuthCompleteRegisterAzureADRequest request, string? remoteIpAddress)
        {
            // First check the register token is valid
            (UserSelfRegistrationResult checkTokenResult, RegisterFormData? registerFormData) = await CheckRegisterAzureADTokenAsync(request.AzureTenantId!.Value, request.AzureObjectId!.Value, request.Token!, remoteIpAddress);

            if (checkTokenResult != UserSelfRegistrationResult.Ok)
            {
                return (checkTokenResult, null);
            }

            if (registerFormData is null)
            {
                return (UserSelfRegistrationResult.UnknownError, null);
            }

            string logDescription = "User Self-Register (Azure AD)";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_validatedRegionId uniqueidentifier = null
declare @_validatedBuildingId uniqueidentifier = null
declare @_validatedFunctionId uniqueidentifier = null
declare @_buildingTimezone varchar(50) = null

declare @_registerAzureTokensData table
(
    AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
   ,Email nvarchar(254)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,OrganizationId uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
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
    -- Check if provided region, building and function belongs to organization.
    -- Also retrieve the building timezone, which will be used for the new user's timezone.
    select @_validatedRegionId = tblRegions.id
          ,@_validatedBuildingId = tblBuildings.id
          ,@_validatedFunctionId = tblFunctions.id
          ,@_buildingTimezone = tblBuildings.Timezone
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblRegions
    on tblBuildings.RegionId = tblRegions.id
    and tblRegions.Deleted = 0
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.id = @functionId
    and tblBuildings.id = @buildingId
    and tblRegions.id = @regionId
    and tblOrganizations.id = @organizationId

    if @_validatedRegionId is null or @_validatedBuildingId is null or @_validatedFunctionId is null
    begin
        -- Provided regionId or buildingId or functionId does not belong to matched organization
        set @_result = 3
        rollback
    end
    else
    begin
        -- Organization, Region, Building and Function check passed - attempt to create the user
        insert into tblUsers
        (Uid
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,LastPasswordChangeDateUtc
        ,Email
        ,UserSystemRole
        ,DisplayName
        ,FirstName
        ,Surname
        ,Timezone)
        select @uid
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,@_now -- LastPasswordChangeDateUtc
              ,@email
              ,{(int)UserSystemRole.User} -- User is added to the system as a normal user (system role)
              ,@displayName
              ,@firstName
              ,@surname
              ,@_buildingTimezone
        where not exists
        (
            select *
            from tblUsers
            where Deleted = 0
            and Email = @email
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            insert into tblUserOrganizationJoin
            (Uid
            ,OrganizationId
            ,InsertDateUtc
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled)
            select @uid
                  ,@organizationId
                  ,@_now -- InsertDateUtc
                  ,{(int)UserOrganizationRole.User} -- User is added to the organization as a normal user (organization role)
                  ,null -- Note
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled

            insert into tblUserOrganizationJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinLogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@organizationId
                  ,{(int)UserOrganizationRole.User} -- UserOrganizationRole -- User is added to the organization as a normal user (organization role)
                  ,null -- Note
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserOrganizationJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserOrganizationJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc)
            select @userOrganizationJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@uid
                  ,@organizationId
                  ,{(int)UserOrganizationRole.User} -- UserOrganizationRole -- User is added to the organization as a normal user (organization role)
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user organization join history for the new user
            insert into tblUserOrganizationJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,UserOrganizationJoinHistoryId
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@userOrganizationJoinHistoryId
                  ,@uid
                  ,@organizationId
                  ,{(int)UserOrganizationRole.User} -- UserOrganizationRole -- User is added to the organization as a normal user (organization role)
                  ,0 -- Contractor
                  ,0 -- Visitor
                  ,0 -- UserOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin

            insert into tblUserBuildingJoin
            (Uid
            ,BuildingId
            ,InsertDateUtc
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere)
            select @uid
                  ,@buildingId
                  ,@_now -- InsertDateUtc
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere

            insert into tblUserBuildingJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinLogid
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserBuildingJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserBuildingJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc)
            select @userBuildingJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user building join history for the new user
            insert into tblUserBuildingJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,UserBuildingJoinHistoryId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@userBuildingJoinHistoryId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,0 -- FirstAidOfficer
                  ,0 -- FireWarden
                  ,0 -- PeerSupportOfficer
                  ,0 -- AllowBookingDeskForVisitor
                  ,0 -- AllowBookingRestrictedRooms
                  ,0 -- AllowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            insert into tblUsers_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,Email
            ,UserSystemRole
            ,DisplayName
            ,FirstName
            ,Surname
            ,Timezone
            ,LogAction)
            select @logId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@email
                  ,{(int)UserSystemRole.User} -- User is added to the system as a normal user (system role)
                  ,@displayName
                  ,@firstName
                  ,@surname
                  ,@_buildingTimezone
                  ,'Insert' -- LogAction

            -- Link Azure AD account
            insert into tblUserAzureObjectId
            (Uid
            ,AzureTenantId
            ,AzureObjectId
            ,InsertDateUtc)
            select @uid
                  ,@azureTenantId
                  ,@azureObjectId
                  ,@_now -- InsertDateUtc

            insert into tblUserAzureObjectId_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,AzureTenantId
            ,AzureObjectId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userAzureObjectIdLogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@azureTenantId
                  ,@azureObjectId
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Remove all RegisterAzure tokens for the user
            delete from tblRegisterAzureTokens
            output deleted.AzureTenantId
                  ,deleted.AzureObjectId
                  ,deleted.Email
                  ,deleted.FirstName
                  ,deleted.Surname
                  ,deleted.OrganizationId
                  ,deleted.ExpiryDateUtc
                  ,deleted.Location
                  ,deleted.BrowserName
                  ,deleted.OSName
                  ,deleted.DeviceInfo
                  ,deleted.AvatarUrl
                  ,deleted.AvatarImageStorageId
                  into @_registerAzureTokensData
            where AzureTenantId = @azureTenantId
            and AzureObjectId = @azureObjectId

            -- Log removed tokens
            insert into tblRegisterAzureTokens_Log
            (id
            ,InsertDateUtc
            ,UpdatedByIpAddress
            ,LogDescription
            ,AzureTenantId
            ,AzureObjectId
            ,Email
            ,FirstName
            ,Surname
            ,OrganizationId
            ,ExpiryDateUtc
            ,Location
            ,BrowserName
            ,OSName
            ,DeviceInfo
            ,AvatarUrl
            ,AvatarImageStorageId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
                  ,@_now
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@azureTenantId
                  ,@azureObjectId
                  ,d.Email
                  ,d.FirstName
                  ,d.Surname
                  ,d.OrganizationId
                  ,d.ExpiryDateUtc
                  ,d.Location
                  ,d.BrowserName
                  ,d.OSName
                  ,d.DeviceInfo
                  ,d.AvatarUrl
                  ,d.AvatarImageStorageId
                  ,'Delete' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_registerAzureTokensData d

            commit
        end
        else
        begin
            -- Record already exists
            set @_result = 2
            rollback
        end
    end
end

select @_result

if @_result = 1
begin
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
        order by tblOrganizations.Name
                    
        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )
    end
end
";
                Guid uid = RT.Comb.Provider.Sql.Create();
                Guid logId = RT.Comb.Provider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(registerFormData.Email)));
                string displayName = $"{request.FirstName} {request.Surname}".Trim();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@lockResourceName", $"tblUsers_Email_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@email", registerFormData.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@displayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@firstName", request.FirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", request.Surname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@avatarUrl", null, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@avatarThumbnailUrl", null, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@regionId", request.RegionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@functionId", request.FunctionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", registerFormData.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureTenantId", request.AzureTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureObjectId", request.AzureObjectId, DbType.Guid, ParameterDirection.Input);

                // Histories
                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@userOrganizationJoinHistoryId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userAzureObjectIdLogId", RT.Comb.Provider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                    if (userData is not null)
                    {
                        // Read extended data
                        userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                        userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                        List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                        List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<UserData_PermanentSeat> permanentSeats = new List<UserData_PermanentSeat>();
                        List<UserData_AssetType> assetTypes = new List<UserData_AssetType>();
                        List<UserData_PermanentAsset> permanentAssets = new List<UserData_PermanentAsset>();
                        List<UserData_AdminFunction> adminFunctions = new List<UserData_AdminFunction>();
                        List<UserData_AdminAssetType> adminAssetTypes = new List<UserData_AdminAssetType>();

                        FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                    }
                }

                UserSelfRegistrationResult userSelfRegistrationResult;

                switch (resultCode)
                {
                    case 1:
                        userSelfRegistrationResult = UserSelfRegistrationResult.Ok;

                        // If we have a profile photo from Azure, assign it to the user
                        if (userData is not null && registerFormData.AvatarUrl is not null && registerFormData.AvatarImageStorageId is not null)
                        {
                            ContentInspectorResultWithMemoryStream? contentInspectorResult = null;

                            string avatarUrlFilename = Path.GetFileName(registerFormData.AvatarUrl);

                            string avatarPhotoFilePath = Path.Combine(
                                _imageStorageRepository.GetFolderForImageTypeWithWebRootPath(registerFormData.OrganizationId, ImageStorageRelatedObjectType.AzureProfilePhoto),
                                avatarUrlFilename);

                            if (File.Exists(avatarPhotoFilePath))
                            {
                                using (FileStream fileStream = new FileStream(avatarPhotoFilePath, FileMode.Open))
                                {
                                    contentInspectorResult = await ImageStorageHelpers.CopyFormStreamAndInspectImageAsync(fileStream, avatarUrlFilename);
                                }

                                if (contentInspectorResult is not null
                                    && contentInspectorResult.InspectedExtension is not null
                                    && ImageStorageHelpers.IsValidImageExtension(contentInspectorResult.InspectedExtension))
                                {
                                    // Store avatar image file to disk
                                    (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                        await StoreAvatarImageAsync(sqlConnection,
                                            contentInspectorResult,
                                            uid, logId, uid, displayName, remoteIpAddress);

                                    if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                    {
                                        // Set image URL in response to be returned.
                                        userData.AvatarUrl = storedImageFile.FileUrl;
                                        userData.AvatarThumbnailUrl = thumbnailFile.FileUrl;

                                        // Remove the image from database and delete from disk if required
                                        if (registerFormData.AvatarImageStorageId is not null)
                                        {
                                            await _imageStorageRepository.DeleteImageAsync(registerFormData.AvatarImageStorageId.Value, "tblUsers", logId, uid, displayName, remoteIpAddress);
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case 2:
                        userSelfRegistrationResult = UserSelfRegistrationResult.RecordAlreadyExists;
                        break;
                    case 3:
                        userSelfRegistrationResult = UserSelfRegistrationResult.BuildingIdOrFunctionIdDoesNotBelongToMatchedOrganization;
                        break;
                    case 999:
                        // Database failed to immediately get an app lock. This means another query was
                        // running at exactly the same time which was trying to register a user with the same
                        // email address.
                        userSelfRegistrationResult = UserSelfRegistrationResult.GetAppLockFailed;
                        break;
                    default:
                        userSelfRegistrationResult = UserSelfRegistrationResult.UnknownError;
                        break;
                }

                return (userSelfRegistrationResult, userData);
            }
        }

        public async Task<UserData_MasterInfo> GetMasterInfoAsync(CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_anyOrganizationsExist bit = 0

-- Get total number of organizations in database
-- Note: The index 'IX_tblOrganizations_NameAsc' has a filter for Deleted = 0
select @_anyOrganizationsExist = case when convert(int, rows) > 0 then 1 else 0 end
from sys.partitions
where object_id = Object_Id('tblOrganizations')
and index_id = IndexProperty(Object_Id('tblOrganizations'), 'IX_tblOrganizations_NameAsc', 'IndexID')

select @_anyOrganizationsExist as AnyOrganizationsExist
";
                CommandDefinition commandDefinition = new CommandDefinition(sql, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstAsync<UserData_MasterInfo>(commandDefinition);
            }
        }

        public async Task<(VerifyCredentialsResult updateUserResult, VerifyCredentialsResult changePasswordResult, UserData? userData)> UpdateProfileAsync(UserUpdateProfileRequest request, ContentInspectorResultWithMemoryStream? contentInspectorResult, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update Profile";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,TotpEnabled bit
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
   ,OldDisplayName nvarchar(151)
   ,OldFirstName nvarchar(75)
   ,OldSurname nvarchar(75)
   ,OldTimezone varchar(50)
   ,OldAvatarUrl varchar(255)
   ,OldAvatarImageStorageId uniqueidentifier
   ,OldAvatarThumbnailUrl varchar(255)
   ,OldAvatarThumbnailStorageId uniqueidentifier
)

update tblUsers
set UpdatedDateUtc = @_now
   ,DisplayName = @displayName
   ,FirstName = @firstName
   ,Surname = @surname
   ,Timezone = @timezone
");
                if (request.AvatarImageChanged!.Value && request.AvatarImage is null)
                {
                    // Clear avatar image if it's being removed
                    sql.Append(@"
   ,AvatarUrl = null
   ,AvatarImageStorageId = null
   ,AvatarThumbnailUrl = null
   ,AvatarThumbnailStorageId = null
");
                }

                sql.Append($@"
output inserted.Email
      ,inserted.UserSystemRole
      ,inserted.TotpEnabled
      ,inserted.DisplayName
      ,inserted.FirstName
      ,inserted.Surname
      ,inserted.Timezone
      ,inserted.AvatarUrl
      ,inserted.AvatarImageStorageId
      ,inserted.AvatarThumbnailUrl
      ,inserted.AvatarThumbnailStorageId
      ,deleted.DisplayName
      ,deleted.FirstName
      ,deleted.Surname
      ,deleted.Timezone
      ,deleted.AvatarUrl
      ,deleted.AvatarImageStorageId
      ,deleted.AvatarThumbnailUrl
      ,deleted.AvatarThumbnailStorageId
      into @_data
where Deleted = 0
and Disabled = 0
and Uid = @uid

if @@ROWCOUNT = 1
begin
    set @_result = 1
    
    insert into tblUsers_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,Email
    ,UserSystemRole
    ,TotpEnabled
    ,DisplayName
    ,FirstName
    ,Surname
    ,Timezone
    ,AvatarUrl
    ,AvatarImageStorageId
    ,AvatarThumbnailUrl
    ,AvatarThumbnailStorageId
    ,Disabled
    ,Deleted
    ,OldEmail
    ,OldUserSystemRole
    ,OldTotpEnabled
    ,OldDisplayName
    ,OldFirstName
    ,OldSurname
    ,OldTimezone
    ,OldAvatarUrl
    ,OldAvatarImageStorageId
    ,OldAvatarThumbnailUrl
    ,OldAvatarThumbnailStorageId
    ,OldDisabled
    ,OldDeleted
    ,PasswordChanged
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.Email
          ,d.UserSystemRole
          ,d.TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- Disabled
          ,0 -- Deleted
          ,d.Email
          ,d.UserSystemRole
          ,d.TotpEnabled
          ,d.OldDisplayName
          ,d.OldFirstName
          ,d.OldSurname
          ,d.OldTimezone
          ,d.OldAvatarUrl
          ,d.OldAvatarImageStorageId
          ,d.OldAvatarThumbnailUrl
          ,d.OldAvatarThumbnailStorageId
          ,0 -- OldDisabled
          ,0 -- OldDeleted
          ,0 -- PasswordChanged
          ,'Update' -- LogAction
    from @_data d
");

                // Update user note if organization specified
                if (request.OrganizationId.HasValue)
                {
                    parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                    parameters.Add("@note", request.Note, DbType.String, ParameterDirection.Input, 500);

                    sql.Append($@"
    declare @_userOrganizationJoinData table
    (
        UserOrganizationRole int
       ,Note nvarchar(500)
       ,Contractor bit
       ,Visitor bit
       ,OldNote nvarchar(500)
    )

    update tblUserOrganizationJoin
    set Note = @note
    output inserted.UserOrganizationRole
          ,inserted.Note
          ,inserted.Contractor
          ,inserted.Visitor
          ,deleted.Note
          into @_userOrganizationJoinData
    where Uid = @uid
    and OrganizationId = @organizationId
    and UserOrganizationDisabled = 0

    if @@ROWCOUNT = 1
    begin
        insert into tblUserOrganizationJoin_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,Uid
        ,OrganizationId
        ,UserOrganizationRole
        ,Note
        ,Contractor
        ,Visitor
        ,UserOrganizationDisabled
        ,OldUserOrganizationRole
        ,OldNote
        ,OldContractor
        ,OldVisitor
        ,OldUserOrganizationDisabled
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select @userOrganizationJoinLogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@uid
              ,@organizationId
              ,d.UserOrganizationRole
              ,d.Note
              ,d.Contractor
              ,d.Visitor
              ,0 -- UserOrganizationDisabled
              ,d.UserOrganizationRole
              ,d.OldNote
              ,d.Contractor
              ,d.Visitor
              ,0 -- OldUserOrganizationDisabled
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_userOrganizationJoinData d
    end
");
                }

                sql.Append($@"
end
else
begin
    -- Record was not updated
    set @_result = 2
end

select @_result

-- Select old ImageStorageIds so we can delete off disk
select OldAvatarImageStorageId
      ,OldAvatarThumbnailStorageId
from @_data

-- Select row to return with the API result
select Uid
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,LastAccessDateUtc
      ,LastPasswordChangeDateUtc
      ,Email
      ,HasPassword
      ,TotpEnabled
      ,UserSystemRole
      ,DisplayName
      ,FirstName
      ,Surname
      ,Timezone
      ,AvatarUrl
      ,AvatarThumbnailUrl
      ,Disabled
      ,ConcurrencyKey
from tblUsers
where Deleted = 0
and Uid = @uid

if @@ROWCOUNT = 1
begin
    -- Also query user's organization access
    select tblUserOrganizationJoin.OrganizationId as id
          ,tblOrganizations.Name
          ,tblOrganizations.LogoImageUrl
          ,tblOrganizations.CheckInEnabled
          ,tblOrganizations.WorkplacePortalEnabled
          ,tblOrganizations.WorkplaceAccessRequestsEnabled
          ,tblOrganizations.WorkplaceInductionsEnabled
          ,tblUserOrganizationJoin.UserOrganizationRole
          ,tblUserOrganizationJoin.Note
          ,tblUserOrganizationJoin.Contractor
          ,tblUserOrganizationJoin.Visitor
          ,tblUserOrganizationJoin.UserOrganizationDisabled
          ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserOrganizationJoin
    inner join tblOrganizations
    on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblUserOrganizationJoin.Uid = @uid
    and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
    order by tblOrganizations.Name

    -- Also query user's last used building
    select Uid
          ,WebLastUsedOrganizationId
          ,WebLastUsedBuildingId
          ,MobileLastUsedOrganizationId
          ,MobileLastUsedBuildingId
    from tblUserLastUsedBuilding
    where Uid = @uid

    -- Also query user's building access
    select tblUserBuildingJoin.BuildingId as id
          ,tblBuildings.Name
          ,tblBuildings.OrganizationId
          ,tblBuildings.Timezone
          ,tblBuildings.CheckInEnabled
          ,0 as HasBookableMeetingRooms -- Queried separately
          ,0 as HasBookableAssetSlots -- Queried separately
          ,tblUserBuildingJoin.FunctionId
          ,tblFunctions.Name as FunctionName
          ,tblFunctions.HtmlColor as FunctionHtmlColor
          ,tblUserBuildingJoin.FirstAidOfficer
          ,tblUserBuildingJoin.FireWarden
          ,tblUserBuildingJoin.PeerSupportOfficer
          ,tblUserBuildingJoin.AllowBookingDeskForVisitor
          ,tblUserBuildingJoin.AllowBookingRestrictedRooms
          ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
          ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserBuildingJoin
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblFunctions
    on tblUserBuildingJoin.FunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblUserBuildingJoin.Uid = @uid
    order by tblBuildings.Name

    -- Also query user's buildings with bookable desks
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType != {(int)DeskType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query user's buildings with bookable meeting rooms
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblMeetingRooms
        inner join tblFloors
        on tblMeetingRooms.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblMeetingRooms.Deleted = 0
        and tblMeetingRooms.OfflineRoom = 0
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
        and
        (
            tblMeetingRooms.RestrictedRoom = 0
            or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
        )
    )

    -- Also query user's buildings with bookable asset slots
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query the user's permanent seat
    select tblDesks.id as DeskId
          ,tblBuildings.id as BuildingId
    from tblDesks
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblDesks.Deleted = 0
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid

    -- Also query the user's asset types
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblUserAssetTypeJoin.Uid = @uid

    -- Also query the user's permanent assets
    select tblAssetSlots.id as AssetSlotId
          ,tblAssetSections.AssetTypeId
          ,tblBuildings.id as BuildingId
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid

    -- Also query the user's admin functions if the user is an Admin,
    -- or all functions if they are a Super Admin.
    select tblFunctions.id
          ,tblFunctions.Name
          ,tblFunctions.BuildingId
    from tblFunctions
    where tblFunctions.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminFunctions
        on tblFunctions.id = tblUserAdminFunctions.FunctionId
        and tblUserAdminFunctions.Uid = @uid
        where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminFunctions.FunctionId is not null
            )
        )
    )

    -- Also query the user's admin asset types if the user is an Admin,
    -- or all asset types if they are a Super Admin.
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblAssetTypes
    where tblAssetTypes.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminAssetTypes
        on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
        and tblUserAdminAssetTypes.Uid = @uid
        where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminAssetTypes.AssetTypeId is not null
            )
        )
    )
end
");
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                string displayName = $"{request.FirstName} {request.Surname}".Trim();

                parameters.Add("@uid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@displayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@firstName", request.FirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", request.Surname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@timezone", request.Timezone, DbType.AnsiString, ParameterDirection.Input, 50);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinLogId", userOrganizationJoinLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                (Guid? oldAvatarImageStorageId, Guid? oldAvatarThumbnailStorageId) = await gridReader.ReadFirstOrDefaultAsync<(Guid?, Guid?)>();
                UserData? userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                // If update was successful, also get the data
                if (!gridReader.IsConsumed && userData is not null)
                {
                    // Read extended data
                    userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                    userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                    FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                }

                VerifyCredentialsResult queryResult;
                VerifyCredentialsResult changePasswordResult = VerifyCredentialsResult.Ok;

                switch (resultCode)
                {
                    case 1:
                        queryResult = VerifyCredentialsResult.Ok;

                        if (userData is not null)
                        {
                            // Check if password was set/changed
                            if (!string.IsNullOrEmpty(request.NewPassword))
                            {
                                // Change user's password
                                (changePasswordResult, DateTime? updatedLastPasswordChangedDate) = await ChangePasswordAsync(adminUserUid!.Value, request.CurrentPassword, request.NewPassword!, sqlConnection);

                                // If change password successful, set new last password change date in the response to be returned.
                                if (changePasswordResult == VerifyCredentialsResult.Ok)
                                {
                                    userData.HasPassword = true;
                                    userData.LastPasswordChangeDateUtc = updatedLastPasswordChangedDate;

                                    await SetPasswordChangedLogAsync(logId, true, sqlConnection);
                                }
                            }

                            // Check if avatar image was changed
                            if (request.AvatarImageChanged!.Value)
                            {
                                // Check if avatar image was deleted
                                if (request.AvatarImage is null)
                                {
                                    // Remove the image from database and delete from disk if required
                                    if (oldAvatarImageStorageId is not null)
                                    {
                                        await _imageStorageRepository.DeleteImageAsync(oldAvatarImageStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                    }

                                    // Remove the thumbnail from database and delete from disk if required
                                    if (oldAvatarThumbnailStorageId is not null)
                                    {
                                        await _imageStorageRepository.DeleteImageAsync(oldAvatarThumbnailStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                    }
                                }
                                // Otherwise, avatar image must have been replaced
                                else
                                {
                                    // Store avatar image file to disk
                                    (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                        await StoreAvatarImageAsync(sqlConnection,
                                            contentInspectorResult,
                                            adminUserUid!.Value, logId, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                    if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                    {
                                        // Set image URL in response to be returned.
                                        userData.AvatarUrl = storedImageFile.FileUrl;
                                        userData.AvatarThumbnailUrl = thumbnailFile.FileUrl;

                                        // Remove the image from database and delete from disk if required
                                        if (oldAvatarImageStorageId is not null)
                                        {
                                            await _imageStorageRepository.DeleteImageAsync(oldAvatarImageStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                        }

                                        // Remove the thumbnail from database and delete from disk if required
                                        if (oldAvatarThumbnailStorageId is not null)
                                        {
                                            await _imageStorageRepository.DeleteImageAsync(oldAvatarThumbnailStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                        }
                                    }
                                    else
                                    {
                                        if (storeImageResult == SqlQueryResult.Ok)
                                        {
                                            queryResult = VerifyCredentialsResult.Ok;
                                        }
                                        else
                                        {
                                            queryResult = VerifyCredentialsResult.UnknownError;
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case 2:
                        if (userData is null)
                        {
                            // Row did not exist
                            queryResult = VerifyCredentialsResult.UserDidNotExist;
                        }
                        else if (userData.Disabled)
                        {
                            // User exists but is disabled
                            queryResult = VerifyCredentialsResult.NoAccess;
                        }
                        else
                        {
                            // Should never happen
                            queryResult = VerifyCredentialsResult.UnknownError;
                        }
                        break;
                    default:
                        queryResult = VerifyCredentialsResult.UnknownError;
                        break;
                }

                return (queryResult, changePasswordResult, userData);
            }
        }

        private async Task SetPasswordChangedLogAsync(Guid logId, bool passwordChanged, SqlConnection sqlConnection)
        {
            string sql = @"
update tblUsers_Log
set PasswordChanged = @passwordChanged
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@passwordChanged", passwordChanged, DbType.Boolean, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);
        }

        private async Task<(SqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile)> StoreAvatarImageAsync(SqlConnection sqlConnection,
            ContentInspectorResultWithMemoryStream? contentInspectorResult,
            Guid uid, Guid logId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) = await _imageStorageRepository.WriteAvatarImageAsync(
                contentInspectorResult, uid, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);

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
update tblUsers
set AvatarUrl = @fileUrl
   ,AvatarImageStorageId = @imageStorageId
   ,AvatarThumbnailUrl = @thumbnailUrl
   ,AvatarThumbnailStorageId = @thumbnailStorageId
where Deleted = 0
and Uid = @uid

update tblUsers_Log
set AvatarUrl = @fileUrl
   ,AvatarImageStorageId = @imageStorageId
   ,AvatarThumbnailUrl = @thumbnailUrl
   ,AvatarThumbnailStorageId = @thumbnailStorageId
where id = @logId
";
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@fileUrl", storedImageFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@imageStorageId", storedImageFile.Id, DbType.Guid, ParameterDirection.Input);
            parameters.Add("@thumbnailUrl", thumbnailFile.FileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
            parameters.Add("@thumbnailStorageId", thumbnailFile.Id, DbType.Guid, ParameterDirection.Input);

            await sqlConnection.ExecuteAsync(sql, parameters);

            return (sqlQueryResult, storedImageFile, thumbnailFile);
        }

        public async Task<(UserEnableTotpResult, InitTwoFactorAuthenticationResponse?)> InitTwoFactorAuthenticationAsync(Guid uid, string userEmail)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user already has 2FA enabled or a TotpSecret set.
                string sql = @"
select 1, TotpEnabled, TotpSecret
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                (bool validUid, bool totpEnabled, string currentEncryptedTotpSecret) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, bool, string)>(sql, parameters);

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return (UserEnableTotpResult.UserInvalid, null);
                }

                // Check if 2fa is already enabled for the user.
                if (totpEnabled)
                {
                    return (UserEnableTotpResult.TotpAlreadyEnabled, null);
                }

                // Check if user already has a TotpSecret set (i.e. this endpoint was called before,
                // but user didn't complete enabling 2FA). If yes, just return URI using the existing secret.
                if (!string.IsNullOrEmpty(currentEncryptedTotpSecret))
                {
                    return (UserEnableTotpResult.Ok, _totpHelpers.GenerateInitTwoFactorAuthenticationResponse(userEmail, currentEncryptedTotpSecret));
                }

                // Generate a secret key
                string encryptedSecret = _totpHelpers.GenerateEncryptedSecretKey();

                // Assign encrypted secret to user in database
                sql = @"
update tblUsers
set TotpSecret = @encryptedSecret
where Uid = @uid
and Deleted = 0
and Disabled = 0
and TotpSecret is null
";
                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@encryptedSecret", encryptedSecret, DbType.AnsiString, ParameterDirection.Input, 156);

                int rowsAffected = await sqlConnection.ExecuteAsync(sql, parameters);

                if (rowsAffected == 0)
                {
                    // This should never happen except in the case of a race condition
                    // of two requests at once.
                    return (UserEnableTotpResult.UnknownError, null);
                }

                return (UserEnableTotpResult.Ok, _totpHelpers.GenerateInitTwoFactorAuthenticationResponse(userEmail, encryptedSecret));
            }
        }

        public async Task<(UserEnableTotpResult totpResult, byte[]? pngImage)> GetTwoFactorAuthenticationQRCodePngAsync(Guid uid, string userEmail)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user already has 2FA enabled or a TotpSecret set.
                string sql = @"
select 1, TotpEnabled, TotpSecret
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                (bool validUid, bool totpEnabled, string currentEncryptedTotpSecret) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, bool, string)>(sql, parameters);

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return (UserEnableTotpResult.UserInvalid, null);
                }

                // Check if 2fa is already enabled for the user.
                if (totpEnabled)
                {
                    return (UserEnableTotpResult.TotpAlreadyEnabled, null);
                }

                // Check if user already has a TotpSecret set (i.e. the init endpoint was called before,
                // but user didn't complete enabling 2FA).
                if (string.IsNullOrEmpty(currentEncryptedTotpSecret))
                {
                    return (UserEnableTotpResult.TotpSecretNotSet, null);
                }

                string qrCodeUri = _totpHelpers.GenerateQrCodeUri(userEmail, currentEncryptedTotpSecret);

                return (UserEnableTotpResult.Ok, QRCodeGeneratorHelpers.GeneratePngQRCode(qrCodeUri));
            }
        }

        public async Task<UserEnableTotpResult> EnableTwoFactorAuthenticationAsync(Guid uid, string totpCode, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user already has 2FA enabled or a TotpSecret set.
                string sql = @"
select 1, TotpEnabled, TotpSecret
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                (bool validUid, bool totpEnabled, string encryptedTotpSecret) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, bool, string)>(sql, parameters);

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return UserEnableTotpResult.UserInvalid;
                }

                // Check if 2fa is already enabled for the user.
                if (totpEnabled)
                {
                    return UserEnableTotpResult.TotpAlreadyEnabled;
                }

                // Check if totp secret is set. If not, the init endpoint needs to be called first.
                if (string.IsNullOrEmpty(encryptedTotpSecret))
                {
                    return UserEnableTotpResult.TotpSecretNotSet;
                }

                // Verify the user's totp code
                VerifyTotpCodeResult verifyTotpCodeResult = _totpHelpers.VerifyCode(uid, totpCode, encryptedTotpSecret);

                // If invalid for any reason, stop here
                if (verifyTotpCodeResult != VerifyTotpCodeResult.Ok)
                {
                    return UserEnableTotpResult.TotpCodeInvalid;
                }

                // Enable 2fa for the user
                sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
)

update tblUsers
set UpdatedDateUtc = @_now
   ,TotpEnabled = 1
   ,TotpFailureCount = 0
   ,TotpLastFailureDateUtc = null
   ,TotpLockoutEndDateUtc = null
output inserted.Email
      ,inserted.UserSystemRole
      ,inserted.DisplayName
      ,inserted.FirstName
      ,inserted.Surname
      ,inserted.Timezone
      ,inserted.AvatarUrl
      ,inserted.AvatarImageStorageId
      ,inserted.AvatarThumbnailUrl
      ,inserted.AvatarThumbnailStorageId
      into @_data
where Uid = @uid
and Deleted = 0
and Disabled = 0
and TotpEnabled = 0

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblUsers_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,Email
    ,UserSystemRole
    ,TotpEnabled
    ,DisplayName
    ,FirstName
    ,Surname
    ,Timezone
    ,AvatarUrl
    ,AvatarImageStorageId
    ,AvatarThumbnailUrl
    ,AvatarThumbnailStorageId
    ,Disabled
    ,Deleted
    ,OldEmail
    ,OldUserSystemRole
    ,OldTotpEnabled
    ,OldDisplayName
    ,OldFirstName
    ,OldSurname
    ,OldTimezone
    ,OldAvatarUrl
    ,OldAvatarImageStorageId
    ,OldAvatarThumbnailUrl
    ,OldAvatarThumbnailStorageId
    ,OldDisabled
    ,OldDeleted
    ,PasswordChanged
    ,LogAction)
    select @logId
          ,@_now
          ,@uid
          ,d.Displayname
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.Email
          ,d.UserSystemRole
          ,1 -- TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- Disabled
          ,0 -- Deleted
          ,d.Email
          ,d.UserSystemRole
          ,0 -- OldTotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- OldDisabled
          ,0 -- OldDeleted
          ,0 -- PasswordChanged
          ,'Update' -- LogAction
    from @_data d
end
else
begin
    -- Record was not updated
    set @_result = 2
end

select @_result
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Enable Two-Factor Authentication";

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                int result = await sqlConnection.QueryFirstOrDefaultAsync<int>(sql, parameters);

                UserEnableTotpResult userEnableTotpResult;

                switch (result)
                {
                    case 1:
                        userEnableTotpResult = UserEnableTotpResult.Ok;
                        break;
                    case 2:
                        userEnableTotpResult = UserEnableTotpResult.UserInvalid;
                        break;
                    default:
                        userEnableTotpResult = UserEnableTotpResult.UnknownError;
                        break;
                }

                return userEnableTotpResult;
            }
        }

        /// <summary>
        /// <para>Disables two-factor authentication for a user without validation, intended to be used by Master users.</para>
        /// <para>Returns one of the following results: <see cref="UserDisableTotpResult.Ok"/>, <see cref="UserDisableTotpResult.UserInvalid"/>,
        /// <see cref="UserDisableTotpResult.TotpNotEnabled"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<UserDisableTotpResult> MasterDisableTwoFactorAuthenticationAsync(Guid uid, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user exists and currently has 2FA enabled.
                string sql = @"
select 1, TotpEnabled
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                (bool validUid, bool totpEnabled) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, bool)>(sql, parameters);

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return UserDisableTotpResult.UserInvalid;
                }

                // Check if 2fa is enabled for the user.
                if (!totpEnabled)
                {
                    return UserDisableTotpResult.TotpNotEnabled;
                }

                // Disable 2fa for the user
                sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
)

update tblUsers
set UpdatedDateUtc = @_now
   ,TotpEnabled = 0
   ,TotpSecret = null
   ,TotpFailureCount = 0
   ,TotpLastFailureDateUtc = null
   ,TotpLockoutEndDateUtc = null
output inserted.Email
      ,inserted.UserSystemRole
      ,inserted.DisplayName
      ,inserted.FirstName
      ,inserted.Surname
      ,inserted.Timezone
      ,inserted.AvatarUrl
      ,inserted.AvatarImageStorageId
      ,inserted.AvatarThumbnailUrl
      ,inserted.AvatarThumbnailStorageId
where Uid = @uid
and Deleted = 0
and Disabled = 0
and TotpEnabled = 1

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblUsers_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,Email
    ,UserSystemRole
    ,TotpEnabled
    ,DisplayName
    ,FirstName
    ,Surname
    ,Timezone
    ,AvatarUrl
    ,AvatarImageStorageId
    ,AvatarThumbnailUrl
    ,AvatarThumbnailStorageId
    ,Disabled
    ,Deleted
    ,OldEmail
    ,OldUserSystemRole
    ,OldTotpEnabled
    ,OldDisplayName
    ,OldFirstName
    ,OldSurname
    ,OldTimezone
    ,OldAvatarUrl
    ,OldAvatarImageStorageId
    ,OldAvatarThumbnailUrl
    ,OldAvatarThumbnailStorageId
    ,OldDisabled
    ,OldDeleted
    ,PasswordChanged
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.Email
          ,d.UserSystemRole
          ,0 -- TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- Disabled
          ,0 -- Deleted
          ,d.Email
          ,d.UserSystemRole
          ,1 -- OldTotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- OldDisabled
          ,0 -- OldDeleted
          ,0 -- PasswordChanged
          ,'Update' -- LogAction
    from @_data d
end
else
begin
    -- Record was not updated
    set @_result = 2
end

select @_result
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Disable Two-Factor Authentication (Master)";

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                int result = await sqlConnection.QueryFirstOrDefaultAsync<int>(sql, parameters);

                UserDisableTotpResult disableTotpResult;

                switch (result)
                {
                    case 1:
                        disableTotpResult = UserDisableTotpResult.Ok;
                        break;
                    case 2:
                        // This should never happen except in the case of a race condition
                        // of two requests at once.
                        disableTotpResult = UserDisableTotpResult.UnknownError;
                        break;
                    default:
                        disableTotpResult = UserDisableTotpResult.UnknownError;
                        break;
                }

                return disableTotpResult;
            }
        }

        /// <summary>
        /// <para>Initializes disabling two-factor authentication for the user's own account.</para>
        /// <para>Returns one of the following results: <see cref="UserDisableTotpResult.Ok"/>, <see cref="UserDisableTotpResult.UserInvalid"/>,
        /// <see cref="UserDisableTotpResult.TotpNotEnabled"/>, <see cref="UserDisableTotpResult.PasswordInvalid"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="request"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<UserDisableTotpResult> InitDisableTwoFactorAuthenticationAsync(Guid uid, InitDisableTwoFactorAuthenticationRequest request, string? adminUserDisplayName, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user exists and currently has 2FA enabled.
                string sql = @"
select 1, TotpEnabled, Email, PasswordHash, FirstName, Timezone
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                (bool validUid, bool totpEnabled, string userEmail, string? passwordHash, string? userFirstName, string? userTimezone) =
                    await sqlConnection.QueryFirstOrDefaultAsync<(bool, bool, string, string?, string?, string? timezone)>(sql, parameters);

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return UserDisableTotpResult.UserInvalid;
                }

                // Check if 2fa is enabled for the user.
                if (!totpEnabled)
                {
                    return UserDisableTotpResult.TotpNotEnabled;
                }

                // If user does not currently have a password, proceed with allowing them to disable 2fa.
                // Otherwise if they have a password, we verify it. If invalid, we won't allow them to disable 2fa.
                if (passwordHash is not null)
                {
                    if (request.Password is null
                        || !HMAC_Bcrypt.hmac_bcrypt_verify(request.Password, passwordHash, _appSettings.Password.Pepper))
                    {
                        // Password is invalid
                        return UserDisableTotpResult.PasswordInvalid;
                    }
                }

                // Generate a token to be used in an email link for disabling 2fa
                string disableTotpToken = PasswordGenerator.GenerateAlphanumeric(96);
                string encryptedDisableTotpToken = StringCipherAesGcm.Encrypt(disableTotpToken, _appSettings.TwoFactorAuthentication.DisableTokenEncryptionKey);

                // Store token for later use
                sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_expiryDate datetime2(3) = dateadd(hour, 1, @_now)

insert into tblDisableTotpTokens
(Uid
,DisableTotpToken
,InsertDateUtc
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo)
select @uid
      ,@disableTotpToken
      ,@_now
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo

insert into tblDisableTotpTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select @logId
      ,@_now
      ,@adminUserUid
      ,@adminUserDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@uid
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo
      ,'Insert' -- LogAction

select @_now
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Init Disable Two-Factor Authentication";
                string? location = null;

                if (!string.IsNullOrEmpty(remoteIpAddress))
                {
                    location = await GetLocationStringForIpAddressAsync(remoteIpAddress);
                }

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@disableTotpToken", encryptedDisableTotpToken, DbType.AnsiString, ParameterDirection.Input, 205);
                parameters.Add("@location", location, DbType.String, ParameterDirection.Input, 130);
                parameters.Add("@browserName", request.UserAgentBrowserName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@osName", request.UserAgentOsName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@deviceInfo", request.UserAgentDeviceInfo, DbType.String, ParameterDirection.Input, 50);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                DateTime insertDateUtc = await sqlConnection.QueryFirstOrDefaultAsync<DateTime>(sql, parameters);

                if (userEmail is not null)
                {
                    /*
                    // Send email notification
                    DisableTotpEmailTemplateInfo disableTotpEmailTemplateInfo = new DisableTotpEmailTemplateInfo
                    {
                        Uid = uid,
                        Token = disableTotpToken,
                        InitLogId = logId,
                        UserFirstName = userFirstName,
                        UserDisplayName = adminUserDisplayName,
                        UserEmailAddress = userEmail,
                        RequestDateUtc = insertDateUtc,
                        IpAddress = remoteIpAddress,
                        Location = location,
                        DeviceInfo = request.UserAgentDeviceInfo,
                        OperatingSystem = request.UserAgentOsName,
                        Browser = request.UserAgentBrowserName,
                        Timezone = userTimezone,
                        ToAddress = userEmail
                    };

                    await _emailTemplateRepository.SendDisableTotpEmailNotificationAsync(disableTotpEmailTemplateInfo);
                    */
                }

                return UserDisableTotpResult.Ok;
            }
        }

        /// <summary>
        /// <para>Disables two-factor authentication for a user.</para>
        /// <para>Returns one of the following results: <see cref="UserDisableTotpResult.Ok"/>, <see cref="UserDisableTotpResult.UserInvalid"/>,
        /// <see cref="UserDisableTotpResult.TotpNotEnabled"/>, <see cref="UserDisableTotpResult.DisableTotpTokenInvalid"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<UserDisableTotpResult> DisableTwoFactorAuthenticationAsync(Guid uid, string token, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Check if the user exists and currently has 2FA enabled.
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_disableTotpTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblDisableTotpTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_disableTotpTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblDisableTotpTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,'Disable Two-Factor Authentication Token Expired' -- LogDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_disableTotpTokensData d

-- Get logs of deleted tokens
select id
from @_disableTotpTokensLogIds

-- Check if user exists and has 2fa enabled
select 1, TotpEnabled
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0

-- Get all of the current active tokens for this user
if @@ROWCOUNT = 1
begin
    select DisableTotpToken
    from tblDisableTotpTokens
    where Uid = @uid
    and ExpiryDateUtc >= @_now
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                (bool validUid, bool totpEnabled) = await reader.ReadFirstOrDefaultAsync<(bool, bool)>();
                List<string>? encryptedTokens = null;

                if (!reader.IsConsumed)
                {
                    encryptedTokens = (await reader.ReadAsync<string>()).AsList();
                }

                // Check if user exists and is not disabled
                if (!validUid)
                {
                    return UserDisableTotpResult.UserInvalid;
                }

                // Check if 2fa is enabled for the user.
                if (!totpEnabled)
                {
                    return UserDisableTotpResult.TotpNotEnabled;
                }

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens is null)
                {
                    return UserDisableTotpResult.DisableTotpTokenInvalid;
                }

                bool totpTokenFound = false;

                // Check stored tokens to see if the provided token is in the database
                foreach (string encryptedToken in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken, _appSettings.TwoFactorAuthentication.DisableTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        totpTokenFound = true;
                        break;
                    }
                }

                if (!totpTokenFound)
                {
                    return UserDisableTotpResult.DisableTotpTokenInvalid;
                }

                // Disable 2fa for the user
                sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_data table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
)

declare @_disableTotpTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Remove all tokens for this user from the database
delete from tblDisableTotpTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_disableTotpTokensData
where Uid = @uid

-- Log removed tokens
insert into tblDisableTotpTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_disableTotpTokensData d

update tblUsers
set UpdatedDateUtc = @_now
   ,TotpEnabled = 0
   ,TotpSecret = null
   ,TotpFailureCount = 0
   ,TotpLastFailureDateUtc = null
   ,TotpLockoutEndDateUtc = null
output inserted.Email
      ,inserted.UserSystemRole
      ,inserted.DisplayName
      ,inserted.FirstName
      ,inserted.Surname
      ,inserted.Timezone
      ,inserted.AvatarUrl
      ,inserted.AvatarImageStorageId
      ,inserted.AvatarThumbnailUrl
      ,inserted.AvatarThumbnailStorageId
      into @_data
where Uid = @uid
and Deleted = 0
and Disabled = 0
and TotpEnabled = 1

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblUsers_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,Email
    ,UserSystemRole
    ,TotpEnabled
    ,DisplayName
    ,FirstName
    ,Surname
    ,Timezone
    ,AvatarUrl
    ,AvatarImageStorageId
    ,AvatarThumbnailUrl
    ,AvatarThumbnailStorageId
    ,Disabled
    ,Deleted
    ,OldEmail
    ,OldUserSystemRole
    ,OldTotpEnabled
    ,OldDisplayName
    ,OldFirstName
    ,OldSurname
    ,OldTimezone
    ,OldAvatarUrl
    ,OldAvatarImageStorageId
    ,OldAvatarThumbnailUrl
    ,OldAvatarThumbnailStorageId
    ,OldDisabled
    ,OldDeleted
    ,PasswordChanged
    ,LogAction)
    select @logId
          ,@_now
          ,@uid -- UpdatedByUid
          ,d.Displayname -- UpdatedByDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.Email
          ,d.UserSystemRole
          ,0 -- TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- Disabled
          ,0 -- Deleted
          ,d.Email
          ,d.UserSystemRole
          ,1 -- OldTotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,0 -- OldDisabled
          ,0 -- OldDeleted
          ,0 -- PasswordChanged
          ,'Update' -- LogAction
    from @_data d
end
else
begin
    -- Record was not updated
    set @_result = 2
end

select @_result

select id
from @_disableTotpTokensLogIds
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Disable Two-Factor Authentication";

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader reader2 = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int result = await reader2.ReadFirstOrDefaultAsync<int>();

                UserDisableTotpResult disableTotpResult;

                switch (result)
                {
                    case 1:
                        disableTotpResult = UserDisableTotpResult.Ok;
                        break;
                    case 2:
                        // This should never happen except in the case of a race condition
                        // of two requests at once.
                        disableTotpResult = UserDisableTotpResult.UnknownError;
                        break;
                    default:
                        disableTotpResult = UserDisableTotpResult.UnknownError;
                        break;
                }

                return disableTotpResult;
            }
        }

        /// <summary>
        /// <para>Revokes all disables two-factor authentication tokens for a user.</para>
        /// <para>Returns one of the following results: <see cref="UserDisableTotpResult.Ok"/>, <see cref="UserDisableTotpResult.DisableTotpTokenInvalid"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="initLogId"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<UserDisableTotpResult> RevokeDisableTwoFactorAuthenticationAsync(Guid uid, Guid initLogId, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_disableTotpTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblDisableTotpTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_disableTotpTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblDisableTotpTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,'Disable Two-Factor Authentication Token Expired' -- LogDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_disableTotpTokensData d

-- Use init log ID as a token to expire all current disable totp tokens.
-- Use the original expiry date, so that the expiry date on revoking with this
-- token will be the same as the real token's expiry date.
-- This revoke token can be reused unlimited times until the expiry date but we don't mind.
select @_result = 1
from tblDisableTotpTokens_Log
where id = @initLogId
and ExpiryDateUtc > @_now

if @_result = 1
begin
    -- Clear table variable used for storing data to be used again.
    -- Note: We don't delete the log IDs from earlier as we still need those.
    delete from @_disableTotpTokensData

    -- Revoke remaining unexpired tokens by removing all tokens for this user from the database
    delete from tblDisableTotpTokens
    output deleted.Uid
          ,deleted.ExpiryDateUtc
          ,deleted.Location
          ,deleted.BrowserName
          ,deleted.OSName
          ,deleted.DeviceInfo
          into @_disableTotpTokensData
    where Uid = @uid

    -- Log revoked tokens
    insert into tblDisableTotpTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction)
    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
          ,@_now
          ,@uid -- UpdatedByUid
          ,@_userDisplayName -- UpdatedByDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.ExpiryDateUtc
          ,d.Location
          ,d.BrowserName
          ,d.OSName
          ,d.DeviceInfo
          ,'Delete' -- LogAction
    from @_disableTotpTokensData d
end
else
begin
    -- Invalid token
    set @_result = 2
end

select @_result
";
                string logDescription = "Revoke Disable Two-Factor Authentication Token";

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@initLogId", initLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int result = await reader.ReadFirstOrDefaultAsync<int>();

                UserDisableTotpResult revokeDisableTotpResult;

                switch (result)
                {
                    case 1:
                        revokeDisableTotpResult = UserDisableTotpResult.Ok;
                        break;
                    case 2:
                        revokeDisableTotpResult = UserDisableTotpResult.DisableTotpTokenInvalid;
                        break;
                    default:
                        revokeDisableTotpResult = UserDisableTotpResult.UnknownError;
                        break;
                }

                return revokeDisableTotpResult;
            }
        }

        /// <summary>
        /// Retrieves a list of users to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListWithImageResponse> ListUsersForDropdownAsync(Guid organizationId, string? searchTerm, long? requestCounter, bool includeDisabled = false, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select tblUsers.Uid as Value
      ,tblUsers.DisplayName as Text
      ,tblUsers.Email as SecondaryText
      ,tblUsers.AvatarThumbnailUrl as ImageUrl
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
";
                if (!includeDisabled)
                {
                    sql += $@"
and tblUsers.Disabled = 0";
                }

                sql += $@"{whereQuery}                
order by tblUsers.DisplayName
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListWithImageResponse selectListWithImageResponse = new SelectListWithImageResponse();
                selectListWithImageResponse.RequestCounter = requestCounter;
                selectListWithImageResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuidWithImage>(commandDefinition)).AsList();

                return selectListWithImageResponse;
            }
        }

        public async Task<bool> IsUserManageableByAdmin(Guid organizationId, Guid buildingId, Guid targetUserUid, Guid adminUserUid, CancellationToken cancellationToken = default)
        {
            UserOrganizationPermission? userOrganizationPermission = await _authCacheService.GetUserOrganizationPermissionAsync(adminUserUid, organizationId, cancellationToken);

            if (userOrganizationPermission is null)
            {
                return false;
            }

            switch (userOrganizationPermission.UserOrganizationRole)
            {
                case UserOrganizationRole.NoAccess:
                case UserOrganizationRole.User:
                case UserOrganizationRole.Tablet:
                    return false;
                case UserOrganizationRole.Admin:
                    // Need to query database below
                    break;
                case UserOrganizationRole.SuperAdmin:
                    return true;
                default:
                    throw new Exception($"Unknown UserOrganizationRole: {userOrganizationPermission.UserOrganizationRole}");
            }

            // Check admin user has access to building
            if (!userOrganizationPermission.BuildingPermissions.ContainsKey(buildingId))
            {
                return false;
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
select case when exists
(
    select *
    from tblUserAdminFunctions
    where Uid = @adminUserUid
    and exists
    (
        select *
        from tblUsers
        inner join tblUserOrganizationJoin
        on tblUsers.Uid = tblUserOrganizationJoin.Uid
        inner join tblUserBuildingJoin
        on tblUsers.Uid = tblUserBuildingJoin.Uid
        where tblUsers.Uid = @targetUserUid
        and tblUsers.Deleted = 0
        and tblUserOrganizationJoin.OrganizationId = @organizationId
        and tblUserBuildingJoin.BuildingId = @buildingId
        and tblUserAdminFunctions.FunctionId = tblUserBuildingJoin.FunctionId
    )
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@targetUserUid", targetUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserOrganizationRole", userOrganizationPermission.UserOrganizationRole, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Retrieves a list of users belonging to the specified building, to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListWithImageResponse> ListUsersInBuildingForDropdownAsync(Guid organizationId, Guid buildingId, string? searchTerm, long? requestCounter, bool includeDisabled = false, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select tblUsers.Uid as Value
      ,tblUsers.DisplayName as Text
      ,tblUsers.Email as SecondaryText
      ,tblUsers.AvatarThumbnailUrl as ImageUrl
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
inner join tblUserBuildingJoin
on tblUsers.Uid = tblUserBuildingJoin.Uid
where tblUsers.Deleted = 0
and tblUserOrganizationJoin.OrganizationId = @organizationId
and tblUserBuildingJoin.BuildingId = @buildingId
and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
";
                if (!includeDisabled)
                {
                    sql += @"
and tblUsers.Disabled = 0
and tblUserOrganizationJoin.UserOrganizationDisabled = 0";
                }

                sql += $@"
{whereQuery}
order by tblUsers.DisplayName
";
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListWithImageResponse selectListWithImageResponse = new SelectListWithImageResponse();
                selectListWithImageResponse.RequestCounter = requestCounter;
                selectListWithImageResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuidWithImage>(commandDefinition)).AsList();

                return selectListWithImageResponse;
            }
        }

        /// <summary>
        /// <para>Retrieves a filtered list of users, which the specified admin user has permission to manage, to be used for displaying a dropdown list.</para>
        /// <para>Since this uses tblUserAdminFunctions, it should be used for Admin users only.</para>
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="buildingId"></param>
        /// <param name="organizationId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListWithImageResponse> ListUsersInUserAdminFunctionsForDropdown(Guid userId, Guid organizationId, Guid buildingId, string? searchTerm, long? requestCounter, bool includeDisabled = false, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }

                string sql = $@"
select tblUsers.Uid as Value
      ,tblUsers.DisplayName as Text
      ,tblUsers.Email as SecondaryText
      ,tblUsers.AvatarThumbnailUrl as ImageUrl
from tblUserAdminFunctions
inner join tblUserBuildingJoin
on tblUserAdminFunctions.BuildingId = tblUserBuildingJoin.BuildingId
and tblUserAdminFunctions.FunctionId = tblUserBuildingJoin.FunctionId
inner join tblUserOrganizationJoin
on tblUserAdminFunctions.Uid = tblUserOrganizationJoin.Uid
and tblUserOrganizationJoin = @organizationId
inner join tblBuildings
on tblUserAdminFunctions.BuildingId = tblBuildings.id
and tblBuildings.Deleted = 0
inner join tblFunctions
on tblUserAdminFunctions.FunctionId = tblFunctions.id
and tblFunctions.Deleted = 0
inner join tblUsers
on tblUserBuildingJoin.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
";
                if (!includeDisabled)
                {
                    sql += @"
and tblUsers.Disabled = 0
and tblUserOrganizationJoin.UserOrganizationDisabled = 0";
                }

                sql += $@"
where tblUserAdminFunctions.Uid = @userId
and tblBuildings.id = @buildingId
and tblBuildings.OrganizationId = @organizationId
and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
{whereQuery}
order by tblUsers.Name
";
                parameters.Add("@userId", userId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListWithImageResponse selectListWithImageResponse = new SelectListWithImageResponse();
                selectListWithImageResponse.RequestCounter = requestCounter;
                selectListWithImageResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuidWithImage>(commandDefinition)).AsList();

                return selectListWithImageResponse;
            }
        }

        /// <summary>
        /// <para>Returns true if the specified admin user has permission to manage the function which the target user belongs to.</para>
        /// <para>Does not check whether the <paramref name="adminUserId"/> is an admin or super admin.</para>
        /// </summary>
        /// <param name="adminUserId"></param>
        /// <param name="targetUserUid"></param>
        /// <param name="buildingId"></param>
        /// <param name="organizationId"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsUserInUserAdminFunction(Guid adminUserId, Guid targetUserUid, Guid organizationId, Guid buildingId, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUserAdminFunctions
    inner join tblUserBuildingJoin
    on tblUserAdminFunctions.BuildingId = tblUserBuildingJoin.BuildingId
    and tblUserAdminFunctions.FunctionId = tblUserBuildingJoin.FunctionId
    inner join tblUserOrganizationJoin
    on tblUserAdminFunctions.Uid = tblUserOrganizationJoin.Uid
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    inner join tblBuildings
    on tblUserAdminFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblFunctions
    on tblUserAdminFunctions.FunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    inner join tblUsers
    on tblUserBuildingJoin.Uid = tblUsers.Uid
    and tblUsers.Deleted = 0
";
                if (!includeDisabled)
                {
                    sql += @"
    and tblUsers.Disabled = 0
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0";
                }

                sql += $@"
    where tblUserAdminFunctions.Uid = @adminUserUid
    and tblUsers.Uid = @targetUserUid
    and tblBuildings.id = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and tblUserOrganizationJoin.UserOrganizationRole != {(int)UserOrganizationRole.Tablet}
)
then 1 else 0 end
";
                CommandDefinition commandDefinition;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@adminUserUid", adminUserId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@targetUserUid", targetUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns true if the specified admin user has permission to manage at lease one assets which the target user belongs to, in specified bulding</para>
        /// <para>Does not check whether the <paramref name="adminUserId"/> is an admin or super admin.</para>
        /// </summary>
        /// <param name="adminUserId"></param>
        /// <param name="targetUserUid"></param>
        /// <param name="buildingId"></param>
        /// <param name="organizationId"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsUserInUserAdminAssetTypes(Guid adminUserId, Guid targetUserUid, Guid organizationId, Guid buildingId, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUserAdminAssetTypes
    inner join tblUserAssetTypeJoin
    on tblUserAssetTypeJoin.AssetTypeId = tblUserAdminAssetTypes.AssetTypeId
    inner join tblUserOrganizationJoin
    on tblUserOrganizationJoin.Uid = tblUserAdminAssetTypes.Uid
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    inner join tblBuildings
    on tblBuildings.id = tblUserAdminAssetTypes.BuildingId
    and tblBuildings.Deleted = 0
    inner join tblAssetTypes
    on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
    and tblAssetTypes.Deleted = 0
    inner join tblUsers
    on tblUsers.Uid = tblUserAssetTypeJoin.Uid
    and tblUsers.Deleted = 0
";
                if (!includeDisabled)
                {
                    sql += @"
    and tblUsers.Disabled = 0
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0";
                }

                sql += @"
    where tblUserAdminAssetTypes.Uid = @adminUserUid
    and tblUsers.Uid = @targetUserUid
    and tblBuildings.id = @buildingId
    and tblBuildings.OrganizationId = @organizationId
)
then 1 else 0 end
";
                CommandDefinition commandDefinition;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@adminUserUid", adminUserId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@targetUserUid", targetUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// <para>Returns true if the specified admin user has permission to manage the specified asset which the target user belongs to.</para>
        /// <para>Does not check whether the <paramref name="adminUserId"/> is an admin or super admin.</para>
        /// </summary>
        /// <param name="adminUserId"></param>
        /// <param name="targetUserUid"></param>
        /// <param name="organizationId"></param>
        /// <param name="assetTypeId"></param>
        /// <param name="includeDisabled"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsUserInUserAdminAssetType(Guid adminUserId, Guid targetUserUid, Guid organizationId, Guid assetTypeId, bool includeDisabled = false, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUserAdminAssetTypes
    inner join tblUserAssetTypeJoin
    on tblUserAssetTypeJoin.AssetTypeId = tblUserAdminAssetTypes.AssetTypeId
    inner join tblUserOrganizationJoin
    on tblUserOrganizationJoin.Uid = tblUserAdminAssetTypes.Uid
    and tblUserOrganizationJoin.OrganizationId = @organizationId
    inner join tblBuildings
    on tblBuildings.id = tblUserAdminAssetTypes.BuildingId
    and tblBuildings.Deleted = 0
    inner join tblAssetTypes
    on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
    and tblAssetTypes.Deleted = 0
    inner join tblUsers
    on tblUsers.Uid = tblUserAssetTypeJoin.Uid
    and tblUsers.Deleted = 0
";
                if (!includeDisabled)
                {
                    sql += @"
    and tblUsers.Disabled = 0
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0";
                }

                sql += @"
    where tblUsers.Uid = @targetUserUid
    and tblUserAdminAssetTypes.Uid = @adminUserUid
    and tblUserAdminAssetTypes.AssetTypeId = @assetTypeId
    and tblBuildings.OrganizationId = @organizationId
)
then 1 else 0 end
";
                CommandDefinition commandDefinition;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@adminUserUid", adminUserId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@targetUserUid", targetUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@assetTypeId", assetTypeId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Returns true if the user has permission to the specified asset type, or false if not.
        /// </summary>
        /// <param name="userUid"></param>
        /// <param name="organizationId"></param>
        /// <param name="assetTypeId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsUserHasPermissionToAssetType(Guid userUid, Guid organizationId, Guid assetTypeId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblUsers
    on tblUserAssetTypeJoin.Uid = tblUsers.Uid
    and tblUsers.Deleted = 0
    and tblUsers.Disabled = 0
    and tblUsers.UserSystemRole > 0
    inner join tblUserBuildingJoin
    on tblBuildings.id = tblUserBuildingJoin.BuildingId
    and tblUserBuildingJoin.Uid = tblUsers.Uid
    inner join tblUserOrganizationJoin
    on tblUserAssetTypeJoin.Uid = tblUserOrganizationJoin.Uid
    and tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
    and tblUserOrganizationJoin.UserOrganizationRole > 0
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0
    where tblUserAssetTypeJoin.Uid = @userUid
    and tblUserAssetTypeJoin.AssetTypeId = @assetTypeId
    and tblBuildings.OrganizationId = @organizationId
)
then 1 else 0 end
";
                CommandDefinition commandDefinition;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@userUid", userUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@assetTypeId", assetTypeId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Returns true if the user has permission to the specified admin asset type, or false if not.
        /// </summary>
        /// <param name="userUid"></param>
        /// <param name="organizationId"></param>
        /// <param name="assetTypeId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsUserHasPermissionToAdminAssetType(Guid userUid, Guid organizationId, Guid assetTypeId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblUserAdminAssetTypes
    inner join tblAssetTypes
    on tblUserAdminAssetTypes.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblUsers
    on tblUserAdminAssetTypes.Uid = tblUsers.Uid
    and tblUsers.Deleted = 0
    and tblUsers.Disabled = 0
    and tblUsers.UserSystemRole > 0
    inner join tblUserBuildingJoin
    on tblBuildings.id = tblUserBuildingJoin.BuildingId
    and tblUserBuildingJoin.Uid = tblUsers.Uid
    inner join tblUserOrganizationJoin
    on tblUserAdminAssetTypes.Uid = tblUserOrganizationJoin.Uid
    and tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
    and tblUserOrganizationJoin.UserOrganizationRole > 0
    and tblUserOrganizationJoin.UserOrganizationDisabled = 0
    where tblUserAdminAssetTypes.Uid = @userUid
    and tblUserAdminAssetTypes.AssetTypeId = @assetTypeId
    and tblBuildings.OrganizationId = @organizationId
)
then 1 else 0 end
";
                CommandDefinition commandDefinition;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@userUid", userUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@assetTypeId", assetTypeId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Retrieves a list of users to be used for displaying a dropdown list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="searchTerm"></param>
        /// <param name="requestCounter"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SelectListWithImageResponse> MasterListUsersForDropdownAsync(Guid? organizationId, string? searchTerm, long? requestCounter, bool includeDisabled = false, CancellationToken cancellationToken = default)
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
                            SqlTableName = "tblUsers",
                            SqlColumnName = "DisplayName",
                            DbType = DbType.String,
                            Size = 151
                        },
                        new SqlTableColumnParam
                        {
                            SqlTableName = "tblUsers",
                            SqlColumnName = "Email",
                            DbType = DbType.String,
                            Size = 254
                        }
                    };

                    whereQuery = SearchQueryBuilder.BuildSearchSqlStringWithParams(searchTerm, sqlTableColumnParams, SearchQueryStartType.StartWithAnd, parameters, "searchTerm");
                }
                string sql = $@"
select uid as Value
      ,DisplayName as Text
      ,Email as SecondaryText
      ,AvatarThumbnailUrl as ImageUrl
from tblUsers
where Deleted = 0
";
                if (!includeDisabled)
                {
                    sql += $@"
and Disabled = 0";
                }

                if (organizationId.HasValue)
                {
                    sql += $@"
and OrganizationId = @organizationId";
                }

                sql += $@"
{whereQuery}
order by DisplayName
";
                if (organizationId.HasValue)
                {
                    parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                }

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                SelectListWithImageResponse selectListWithImageResponse = new SelectListWithImageResponse();
                selectListWithImageResponse.RequestCounter = requestCounter;
                selectListWithImageResponse.Records = (await sqlConnection.QueryAsync<SelectListItemGuidWithImage>(commandDefinition)).AsList();

                return selectListWithImageResponse;
            }
        }

        public async Task<string> GetLocationStringForIpAddressAsync(string ipAddress, CancellationToken cancellationToken = default)
        {
            (int ipVersion, byte[]? ipAddressBytes) = Toolbox.IpAddressToBytes(ipAddress);

            if (ipVersion == 0 || ipAddressBytes is null)
            {
                return "";
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.DbIpLocation))
            {
                string sql = @"
select tblCountries.CountryName
      ,tblIpAddresses.Region
      ,tblIpAddresses.City
from DbIpLocation.dbo.tblIpAddresses
left join DbIpLocation.dbo.tblCountries
on tblIpAddresses.Country = tblCountries.CountryCode
where @ipAddressBytes between tblIpAddresses.StartIpAddressBytes and tblIpAddresses.EndIpAddressBytes
and tblIpAddresses.AddressFamily = @addressFamily
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@addressFamily", ipVersion, DbType.Byte, ParameterDirection.Input);
                parameters.Add("@ipAddressBytes", ipAddressBytes, DbType.Binary, ParameterDirection.Input, 16);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                (string countryName, string region, string city) = await sqlConnection.QueryFirstOrDefaultAsync<(string, string, string)>(commandDefinition);

                string result = "";

                // Append city
                if (!string.IsNullOrEmpty(city))
                {
                    result = city;
                }

                // Append region
                if (!string.IsNullOrEmpty(region))
                {
                    if (result != "")
                    {
                        result += ", ";
                    }

                    result += region;
                }

                // Append country name
                if (!string.IsNullOrEmpty(countryName))
                {
                    if (result != "")
                    {
                        result += ", ";
                    }

                    result += countryName;
                }

                return result;
            }
        }

        public async Task<LoginOptions> GetLoginOptionsAsync(string email, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_uid uniqueidentifier
declare @_hasPassword bit = 0
declare @_userDisabled bit = 0
declare @_azureADSingleSignOnEnabled bit = 0
declare @_azureADTenantId uniqueidentifier
declare @_useCustomAzureADApplication bit = 0
declare @_customAzureADSingleSignOnClientId uniqueidentifier
declare @_systemAzureADSingleSignOnClientId uniqueidentifier
declare @_organizationId uniqueidentifier
declare @_disableLocalLoginEnabled bit = 0

-- Get matching user info
select @_uid = Uid
      ,@_hasPassword = HasPassword
      ,@_userDisabled = case when Disabled = 1 or UserSystemRole = 0 then 1 else 0 end
from tblUsers
where Email = @email
and Deleted = 0

-- Get single sign on status based on email domain
select @_azureADSingleSignOnEnabled = ISNULL(tblOrganizationAzureSettings.AzureADSingleSignOnEnabled,0)
      ,@_azureADTenantId = tblOrganizationAzureSettings.AzureADTenantId
      ,@_useCustomAzureADApplication = tblOrganizationAzureSettings.UseCustomAzureADApplication
      ,@_customAzureADSingleSignOnClientId = case when tblOrganizationAzureSettings.UseCustomAzureADApplication = 1
                                                  then tblOrganizationAzureSettings.AzureADSingleSignOnClientID
                                                  else null
                                             end
      ,@_organizationId = tblOrganizations.id
      ,@_disableLocalLoginEnabled = tblOrganizations.DisableLocalLoginEnabled
from tblOrganizationDomains
inner join tblOrganizations
on tblOrganizationDomains.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0
left join tblOrganizationAzureSettings
on tblOrganizations.id = tblOrganizationAzureSettings.OrganizationId
where tblOrganizationDomains.DomainName = @emailDomainName

-- If the email domain has single sign on but are not using Custom Azure AD Application, then get the system one
if @_azureADSingleSignOnEnabled = 1 and @_useCustomAzureADApplication = 0
begin
    select @_systemAzureADSingleSignOnClientId = AzureADSingleSignOnClientId
    from tblSystemAzureSettings
end

select @_uid as Uid
      ,@_hasPassword as HasPassword
      ,@_userDisabled as UserDisabled
      ,@_organizationId as OrganizationId
      ,@_disableLocalLoginEnabled as DisableLocalLoginEnabled
      ,@_azureADSingleSignOnEnabled as AzureADSingleSignOnEnabled
      ,case when @_azureADSingleSignOnEnabled = 1 then @_azureADTenantId else null end as AzureADTenantId
      ,case when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 0 then @_systemAzureADSingleSignOnClientId
            when @_azureADSingleSignOnEnabled = 1 and ISNULL(@_useCustomAzureADApplication,0) = 1 then @_customAzureADSingleSignOnClientId
            else null
       end as AzureADSingleSignOnClientId
";
                string emailDomainName = Toolbox.GetDomainFromEmailAddress(email)!;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@emailDomainName", emailDomainName, DbType.String, ParameterDirection.Input, 252);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstAsync<LoginOptions>(commandDefinition);
            }
        }

        public async Task<UserForgotPasswordResult> InitForgotPasswordAsync(string email, AuthInitForgotPasswordRequest request, string? remoteIpAddress)
        {
            LoginOptions loginOptions = await GetLoginOptionsAsync(email);

            if (!loginOptions.Uid.HasValue)
            {
                return UserForgotPasswordResult.UserDidNotExist;
            }

            if (loginOptions.UserDisabled)
            {
                return UserForgotPasswordResult.NoAccess;
            }

            if (loginOptions.DisableLocalLoginEnabled)
            {
                return UserForgotPasswordResult.LocalLoginDisabled;
            }

            UserData? userData = await GetUserByUidAsync(loginOptions.Uid.Value);

            if (userData is null)
            {
                return UserForgotPasswordResult.UserDidNotExist;
            }

            // Generate a token to be used in an email link for resetting password
            string forgotPasswordToken = PasswordGenerator.GenerateAlphanumeric(96);
            string encryptedForgotPasswordToken = StringCipherAesGcm.Encrypt(forgotPasswordToken, _appSettings.Password.ForgotPasswordTokenEncryptionKey);

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Store token for later use
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_expiryDate datetime2(3) = dateadd(hour, 1, @_now)
declare @_adminUserDisplayName nvarchar(151)

insert into tblForgotPasswordTokens
(Uid
,ForgotPasswordToken
,InsertDateUtc
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo)
select @uid
      ,@forgotPasswordToken
      ,@_now
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo

insert into tblForgotPasswordTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select @logId
      ,@_now
      ,@adminUserUid
      ,@adminUserDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@uid
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo
      ,'Insert' -- LogAction

select @_now
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Init Forgot Password";
                string? location = null;

                if (!string.IsNullOrEmpty(remoteIpAddress))
                {
                    location = await GetLocationStringForIpAddressAsync(remoteIpAddress);
                }

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", loginOptions.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", loginOptions.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", userData.DisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@forgotPasswordToken", encryptedForgotPasswordToken, DbType.AnsiString, ParameterDirection.Input, 205);
                parameters.Add("@location", location, DbType.String, ParameterDirection.Input, 130);
                parameters.Add("@browserName", request.UserAgentBrowserName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@osName", request.UserAgentOsName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@deviceInfo", request.UserAgentDeviceInfo, DbType.String, ParameterDirection.Input, 50);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                DateTime insertDateUtc = await sqlConnection.QueryFirstOrDefaultAsync<DateTime>(sql, parameters);

                /*
                // Send email notification
                ForgotPasswordEmailTemplateInfo forgotPasswordEmailTemplateInfo = new ForgotPasswordEmailTemplateInfo
                {
                    Uid = userData.Uid,
                    Token = forgotPasswordToken,
                    InitLogId = logId,
                    UserFirstName = userData.FirstName,
                    UserDisplayName = userData.DisplayName,
                    UserEmailAddress = userData.Email,
                    RequestDateUtc = insertDateUtc,
                    IpAddress = remoteIpAddress,
                    Location = location,
                    DeviceInfo = request.UserAgentDeviceInfo,
                    OperatingSystem = request.UserAgentOsName,
                    Browser = request.UserAgentBrowserName,
                    Timezone = userData.Timezone,
                    ToAddress = userData.Email
                };

                await _emailTemplateRepository.SendForgotPasswordEmailNotificationAsync(forgotPasswordEmailTemplateInfo);
                */

                return UserForgotPasswordResult.Ok;
            }
        }

        public async Task<UserForgotPasswordResult> RevokeForgotPasswordAsync(Guid uid, Guid initLogId, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_forgotPasswordTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblForgotPasswordTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_forgotPasswordTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblForgotPasswordTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,'Disable Two-Factor Authentication Token Expired' -- LogDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_forgotPasswordTokensData d

-- Use init log ID as a token to expire all current disable totp tokens.
-- Use the original expiry date, so that the expiry date on revoking with this
-- token will be the same as the real token's expiry date.
-- This revoke token can be reused unlimited times until the expiry date but we don't mind.
select @_result = 1
from tblForgotPasswordTokens_Log
where id = @initLogId
and ExpiryDateUtc > @_now

if @_result = 1
begin
    -- Clear table variable used for storing data to be used again.
    -- Note: We don't delete the log IDs from earlier as we still need those.
    delete from @_forgotPasswordTokensData

    -- Revoke remaining unexpired tokens by removing all tokens for this user from the database
    delete from tblForgotPasswordTokens
    output deleted.Uid
          ,deleted.ExpiryDateUtc
          ,deleted.Location
          ,deleted.BrowserName
          ,deleted.OSName
          ,deleted.DeviceInfo
          into @_forgotPasswordTokensData
    where Uid = @uid

    -- Log revoked tokens
    insert into tblForgotPasswordTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction)
    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
          ,@_now
          ,@uid -- UpdatedByUid
          ,@_userDisplayName -- UpdatedByDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.ExpiryDateUtc
          ,d.Location
          ,d.BrowserName
          ,d.OSName
          ,d.DeviceInfo
          ,'Delete' -- LogAction
    from @_forgotPasswordTokensData d
end
else
begin
    -- Invalid token
    set @_result = 2
end

select @_result
";
                string logDescription = "Revoke Forgot Password Token";

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@initLogId", initLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int result = await reader.ReadFirstOrDefaultAsync<int>();

                UserForgotPasswordResult revokeForgotPasswordResult;

                switch (result)
                {
                    case 1:
                        revokeForgotPasswordResult = UserForgotPasswordResult.Ok;
                        break;
                    case 2:
                        revokeForgotPasswordResult = UserForgotPasswordResult.ForgotPasswordTokenInvalid;
                        break;
                    default:
                        revokeForgotPasswordResult = UserForgotPasswordResult.UnknownError;
                        break;
                }

                return revokeForgotPasswordResult;
            }
        }

        public async Task<UserForgotPasswordResult> CheckForgotPasswordTokenAsync(Guid uid, string token, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_forgotPasswordTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblForgotPasswordTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_forgotPasswordTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblForgotPasswordTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,'Forgot Password Token Expired' -- LogDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_forgotPasswordTokensData d

-- Check if user exists
select 1, Email
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0

-- Get all of the current active tokens for this user
if @@ROWCOUNT = 1
begin
    select ForgotPasswordToken
    from tblForgotPasswordTokens
    where Uid = @uid
    and ExpiryDateUtc >= @_now
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                (bool validUid, string? userEmail) = await reader.ReadFirstOrDefaultAsync<(bool, string?)>();
                List<string>? encryptedTokens = null;

                if (!reader.IsConsumed)
                {
                    encryptedTokens = (await reader.ReadAsync<string>()).AsList();
                }

                // Check if user exists and is not disabled
                if (!validUid || string.IsNullOrEmpty(userEmail))
                {
                    return UserForgotPasswordResult.UserDidNotExist;
                }

                // Check if organization belonging to user's email domain has local login disabled
                LoginOptions loginOptions = await GetLoginOptionsAsync(userEmail);

                if (loginOptions.DisableLocalLoginEnabled)
                {
                    return UserForgotPasswordResult.LocalLoginDisabled;
                }

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens is null)
                {
                    return UserForgotPasswordResult.ForgotPasswordTokenInvalid;
                }

                bool forgotPasswordTokenFound = false;

                // Check stored tokens to see if the provided token is in the database
                foreach (string encryptedToken in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken, _appSettings.Password.ForgotPasswordTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        forgotPasswordTokenFound = true;
                        break;
                    }
                }

                if (!forgotPasswordTokenFound)
                {
                    return UserForgotPasswordResult.ForgotPasswordTokenInvalid;
                }

                return UserForgotPasswordResult.Ok;
            }
        }

        public async Task<UserForgotPasswordResult> CompleteForgotPasswordAsync(Guid uid, string token, string newPasswordPlainText, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_forgotPasswordTokensData table
(
    Uid uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblForgotPasswordTokens
output deleted.Uid
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_forgotPasswordTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblForgotPasswordTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid -- UpdatedByUid
      ,@_userDisplayName -- UpdatedByDisplayName
      ,@remoteIpAddress
      ,'Forgot Password Token Expired' -- LogDescription
      ,@uid
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_forgotPasswordTokensData d

-- Check if user exists
select 1, Email
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0

-- Get all of the current active tokens for this user
if @@ROWCOUNT = 1
begin
    select ForgotPasswordToken
    from tblForgotPasswordTokens
    where Uid = @uid
    and ExpiryDateUtc >= @_now
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                (bool validUid, string? userEmail) = await reader.ReadFirstOrDefaultAsync<(bool, string?)>();
                List<string>? encryptedTokens = null;

                if (!reader.IsConsumed)
                {
                    encryptedTokens = (await reader.ReadAsync<string>()).AsList();
                }

                // Check if user exists and is not disabled
                if (!validUid || string.IsNullOrEmpty(userEmail))
                {
                    return UserForgotPasswordResult.UserDidNotExist;
                }

                // Check if organization belonging to user's email domain has local login disabled
                LoginOptions loginOptions = await GetLoginOptionsAsync(userEmail);

                if (loginOptions.DisableLocalLoginEnabled)
                {
                    return UserForgotPasswordResult.LocalLoginDisabled;
                }

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens is null)
                {
                    return UserForgotPasswordResult.ForgotPasswordTokenInvalid;
                }

                bool forgotPasswordTokenFound = false;

                // Check stored tokens to see if the provided token is in the database
                foreach (string encryptedToken in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken, _appSettings.Password.ForgotPasswordTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        forgotPasswordTokenFound = true;
                        break;
                    }
                }

                if (!forgotPasswordTokenFound)
                {
                    return UserForgotPasswordResult.ForgotPasswordTokenInvalid;
                }

                // Generate the hash for the new password
                string newPasswordHash = HMAC_Bcrypt.hmac_bcrypt_hash(newPasswordPlainText, _bcryptSettings, _appSettings.Password.Pepper);

                // Set the new hash in the database, update last password change date, and reset password failure counts
                sql = """
                declare @_result int = 0
                declare @_now datetime2(3) = sysutcdatetime()

                declare @_userDisplayName nvarchar(151)
                
                select @_userDisplayName = DisplayName
                from tblUsers
                where Uid = @uid
                and Deleted = 0
                and Disabled = 0

                declare @_forgotPasswordTokensData table
                (
                    Uid uniqueidentifier
                   ,ExpiryDateUtc datetime2(3)
                   ,Location nvarchar(130)
                   ,BrowserName nvarchar(40)
                   ,OSName nvarchar(40)
                   ,DeviceInfo nvarchar(50)
                )

                -- Remove all tokens for this user from the database
                delete from tblForgotPasswordTokens
                output deleted.Uid
                      ,deleted.ExpiryDateUtc
                      ,deleted.Location
                      ,deleted.BrowserName
                      ,deleted.OSName
                      ,deleted.DeviceInfo
                      into @_forgotPasswordTokensData
                where Uid = @uid

                -- Log removed tokens
                insert into tblForgotPasswordTokens_Log
                (id
                ,InsertDateUtc
                ,UpdatedByUid
                ,UpdatedByDisplayName
                ,UpdatedByIpAddress
                ,LogDescription
                ,Uid
                ,ExpiryDateUtc
                ,Location
                ,BrowserName
                ,OSName
                ,DeviceInfo
                ,LogAction)
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
                      ,@_now
                      ,@uid -- UpdatedByUid
                      ,@_userDisplayName -- UpdatedByDisplayName
                      ,@remoteIpAddress
                      ,@logDescription
                      ,@uid
                      ,d.ExpiryDateUtc
                      ,d.Location
                      ,d.BrowserName
                      ,d.OSName
                      ,d.DeviceInfo
                      ,'Delete' -- LogAction
                from @_forgotPasswordTokensData d

                declare @_data table
                (
                    Email nvarchar(254)
                   ,UserSystemRole int
                   ,TotpEnabled bit
                   ,DisplayName nvarchar(151)
                   ,FirstName nvarchar(75)
                   ,Surname nvarchar(75)
                   ,Timezone varchar(50)
                   ,AvatarUrl varchar(255)
                   ,AvatarImageStorageId uniqueidentifier
                   ,AvatarThumbnailUrl varchar(255)
                   ,AvatarThumbnailStorageId uniqueidentifier
                )

                update tblUsers
                set LastPasswordChangeDateUtc = @_now
                   ,PasswordHash = @passwordHash
                   ,PasswordLoginFailureCount = 0
                   ,PasswordLoginLastFailureDateUtc = null
                   ,PasswordLockoutEndDateUtc = null
                   ,TotpFailureCount = 0
                   ,TotpLastFailureDateUtc = null
                   ,TotpLockoutEndDateUtc = null
                output inserted.Email
                      ,inserted.UserSystemRole
                      ,inserted.TotpEnabled
                      ,inserted.DisplayName
                      ,inserted.FirstName
                      ,inserted.Surname
                      ,inserted.Timezone
                      ,inserted.AvatarUrl
                      ,inserted.AvatarImageStorageId
                      ,inserted.AvatarThumbnailUrl
                      ,inserted.AvatarThumbnailStorageId
                      into @_data
                where Deleted = 0
                and Disabled = 0
                and Uid = @uid

                if @@ROWCOUNT = 1
                begin
                    set @_result = 1

                    insert into tblUsers_Log
                    (id
                    ,InsertDateUtc
                    ,UpdatedByUid
                    ,UpdatedByDisplayName
                    ,UpdatedByIpAddress
                    ,LogDescription
                    ,Uid
                    ,Email
                    ,UserSystemRole
                    ,TotpEnabled
                    ,DisplayName
                    ,FirstName
                    ,Surname
                    ,Timezone
                    ,AvatarUrl
                    ,AvatarImageStorageId
                    ,AvatarThumbnailUrl
                    ,AvatarThumbnailStorageId
                    ,Disabled
                    ,Deleted
                    ,OldEmail
                    ,OldUserSystemRole
                    ,OldTotpEnabled
                    ,OldDisplayName
                    ,OldFirstName
                    ,OldSurname
                    ,OldTimezone
                    ,OldAvatarUrl
                    ,OldAvatarImageStorageId
                    ,OldAvatarThumbnailUrl
                    ,OldAvatarThumbnailStorageId
                    ,OldDisabled
                    ,OldDeleted
                    ,PasswordChanged
                    ,LogAction)
                    select @logId
                          ,@_now
                          ,@uid -- UpdatedByUid
                          ,@_userDisplayName
                          ,@remoteIpAddress
                          ,@logDescription
                          ,@uid
                          ,d.Email
                          ,d.UserSystemRole
                          ,d.TotpEnabled
                          ,d.DisplayName
                          ,d.FirstName
                          ,d.Surname
                          ,d.Timezone
                          ,d.AvatarUrl
                          ,d.AvatarImageStorageId
                          ,d.AvatarThumbnailUrl
                          ,d.AvatarThumbnailStorageId
                          ,0 -- Disabled
                          ,0 -- Deleted
                          ,d.Email
                          ,d.UserSystemRole
                          ,d.TotpEnabled
                          ,d.DisplayName
                          ,d.FirstName
                          ,d.Surname
                          ,d.Timezone
                          ,d.AvatarUrl
                          ,d.AvatarImageStorageId
                          ,d.AvatarThumbnailUrl
                          ,d.AvatarThumbnailStorageId
                          ,0 -- OldDisabled
                          ,0 -- OldDeleted
                          ,1 -- PasswordChanged
                          ,'Update'
                    from @_data d
                end
                else
                begin
                    -- Record was not updated
                    set @_result = 2
                end

                select @_result
                """;

                string logDescription = "Forgot Password";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@passwordHash", newPasswordHash, DbType.AnsiString, ParameterDirection.Input, 115);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader reader2 = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int result = await reader2.ReadFirstOrDefaultAsync<int>();

                UserForgotPasswordResult userForgotPasswordResult;

                switch (result)
                {
                    case 1:
                        userForgotPasswordResult = UserForgotPasswordResult.Ok;
                        break;
                    case 2:
                        userForgotPasswordResult = UserForgotPasswordResult.UserDidNotExist;
                        break;
                    default:
                        userForgotPasswordResult = UserForgotPasswordResult.UnknownError;
                        break;
                }

                return userForgotPasswordResult;
            }
        }

        public async Task<UserSelfRegistrationResult> InitRegisterAsync(AuthInitRegisterRequest request, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_uid uniqueidentifier

select @_uid = Uid
from tblUsers
where Email = @email
and Deleted = 0

declare @_organizationId uniqueidentifier

-- Check if user's email domain belongs to an organization
select @_organizationId = OrganizationId
from tblOrganizationDomains
inner join tblOrganizations
on tblOrganizationDomains.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0
where tblOrganizationDomains.DomainName = @emailDomainName

select @_uid, @_organizationId
";
                string emailDomainName = Toolbox.GetDomainFromEmailAddress(request.Email!)!;

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", request.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@emailDomainName", emailDomainName, DbType.String, ParameterDirection.Input, 254);

                (Guid? uid, Guid? organizationId) = await sqlConnection.QueryFirstOrDefaultAsync<(Guid?, Guid?)>(sql, parameters);

                if (uid is not null)
                {
                    return UserSelfRegistrationResult.RecordAlreadyExists;
                }

                if (organizationId is null)
                {
                    return UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization;
                }

                // Generate a token to be used in an email link for registering the user
                string registerToken = PasswordGenerator.GenerateAlphanumeric(96);
                string encryptedRegisterToken = StringCipherAesGcm.Encrypt(registerToken, _appSettings.Password.RegisterTokenEncryptionKey);

                // Store token for later use
                sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_expiryDate datetime2(3) = dateadd(hour, 1, @_now)

insert into tblRegisterTokens
(Email
,RegisterToken
,InsertDateUtc
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo)
select @email
      ,@registerToken
      ,@_now
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo

insert into tblRegisterTokens_Log
(id
,InsertDateUtc
,UpdatedByIpAddress
,LogDescription
,Email
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select @logId
      ,@_now
      ,@remoteIpAddress
      ,@logDescription
      ,@email
      ,@_expiryDate
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo
      ,'Insert' -- LogAction

select @_now
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Init Register";
                string? location = null;

                if (!string.IsNullOrEmpty(remoteIpAddress))
                {
                    location = await GetLocationStringForIpAddressAsync(remoteIpAddress);
                }

                parameters = new DynamicParameters();
                parameters.Add("@email", request.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@registerToken", encryptedRegisterToken, DbType.AnsiString, ParameterDirection.Input, 205);
                parameters.Add("@location", location, DbType.String, ParameterDirection.Input, 130);
                parameters.Add("@browserName", request.UserAgentBrowserName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@osName", request.UserAgentOsName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@deviceInfo", request.UserAgentDeviceInfo, DbType.String, ParameterDirection.Input, 50);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                DateTime insertDateUtc = await sqlConnection.QueryFirstOrDefaultAsync<DateTime>(sql, parameters);

                /*
                // Send email notification
                RegisterEmailTemplateInfo registerEmailTemplateInfo = new RegisterEmailTemplateInfo
                {
                    Email = request.Email!,
                    Token = registerToken,
                    RequestDateUtc = insertDateUtc,
                    IpAddress = remoteIpAddress,
                    Location = location,
                    DeviceInfo = request.UserAgentDeviceInfo,
                    OperatingSystem = request.UserAgentOsName,
                    Browser = request.UserAgentBrowserName,
                    ToAddress = request.Email!
                };

                await _emailTemplateRepository.SendRegisterEmailNotificationAsync(registerEmailTemplateInfo);
                */

                return UserSelfRegistrationResult.Ok;
            }
        }

        /// <summary>
        /// <para>Validates a register token.</para>
        /// <para>Returns one of the following results: <see cref="UserSelfRegistrationResult.Ok"/>, <see cref="UserSelfRegistrationResult.RecordAlreadyExists"/>,
        /// <see cref="UserSelfRegistrationResult.LocalLoginDisabled"/>, <see cref="UserSelfRegistrationResult.RegisterTokenInvalid"/>.</para>
        /// </summary>
        /// <param name="email"></param>
        /// <param name="token"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserSelfRegistrationResult, RegisterFormData?)> CheckRegisterTokenAsync(string email, string token, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_registerTokensData table
(
    Email nvarchar(254)
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the email to clean them up
delete from tblRegisterTokens
output deleted.Email
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_registerTokensData
where Email = @email
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblRegisterTokens_Log
(id
,InsertDateUtc
,UpdatedByIpAddress
,LogDescription
,Email
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@remoteIpAddress
      ,'Register Token Expired' -- LogDescription
      ,@email
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_registerTokensData d

-- Check if user exists
select 1
from tblUsers
where Email = @email
and Deleted = 0

-- Get all of the current active tokens for this email
select RegisterToken
from tblRegisterTokens
where Email = @email
and ExpiryDateUtc >= @_now
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                bool userExists = await reader.ReadFirstOrDefaultAsync<bool>();
                List<string> encryptedTokens = (await reader.ReadAsync<string>()).AsList();

                // Check if user with the specified email already exists
                if (userExists)
                {
                    return (UserSelfRegistrationResult.RecordAlreadyExists, null);
                }

                // Check if organization belonging to user's email domain has local login disabled
                LoginOptions loginOptions = await GetLoginOptionsAsync(email);

                if (loginOptions.OrganizationId is null)
                {
                    return (UserSelfRegistrationResult.EmailDomainDoesNotBelongToAnExistingOrganization, null);
                }

                if (loginOptions.DisableLocalLoginEnabled)
                {
                    return (UserSelfRegistrationResult.LocalLoginDisabled, null);
                }

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens.Count == 0)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                bool registerTokenFound = false;

                // Check stored tokens to see if the provided token is in the database
                foreach (string encryptedToken in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken, _appSettings.Password.RegisterTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        registerTokenFound = true;
                        break;
                    }
                }

                if (!registerTokenFound)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                // Query data needed for the register form
                sql = @"
select tblRegions.id as RegionId
      ,tblRegions.Name as RegionName
from tblRegions
inner join tblOrganizations
on tblRegions.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblRegions.OrganizationId = @organizationId
and tblRegions.Deleted = 0
order by tblRegions.Name

if @@ROWCOUNT > 0
begin
    select tblRegions.id as RegionId
          ,tblBuildings.id as BuildingId
          ,tblBuildings.Name as BuildingName
    from tblBuildings
    inner join tblRegions
    on tblBuildings.RegionId = tblRegions.id
    and tblRegions.Deleted = 0
    inner join tblOrganizations
    on tblRegions.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblRegions.OrganizationId = @organizationId
    and tblBuildings.Deleted = 0
    order by tblBuildings.Name

    if @@ROWCOUNT > 0
    begin
        select tblBuildings.id as BuildingId
              ,tblFunctions.id as FunctionId
              ,tblFunctions.Name as FunctionName
        from tblFunctions
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblRegions
        on tblBuildings.RegionId = tblRegions.id
        and tblRegions.Deleted = 0
        inner join tblOrganizations
        on tblRegions.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        where tblFunctions.Deleted = 0
        order by tblFunctions.Name
    end
end
";
                parameters = new DynamicParameters();
                parameters.Add("@organizationId", loginOptions.OrganizationId.Value, DbType.Guid, ParameterDirection.Input);

                using SqlMapper.GridReader reader2 = await sqlConnection.QueryMultipleAsync(sql, parameters);

                RegisterFormData registerFormData = new RegisterFormData
                {
                    Email = email,
                    OrganizationId = loginOptions.OrganizationId.Value,
                };

                registerFormData.Regions = (await reader2.ReadAsync<RegisterFormData_Region>()).AsList();

                if (!reader2.IsConsumed)
                {
                    List<RegisterFormData_Building> buildings = (await reader2.ReadAsync<RegisterFormData_Building>()).AsList();

                    if (buildings.Count > 0)
                    {
                        Dictionary<Guid, RegisterFormData_Region> regionsDict = new Dictionary<Guid, RegisterFormData_Region>();
                        Dictionary<Guid, RegisterFormData_Building> buildingsDict = new Dictionary<Guid, RegisterFormData_Building>();

                        foreach (RegisterFormData_Region region in registerFormData.Regions)
                        {
                            regionsDict.Add(region.RegionId, region);
                        }

                        foreach (RegisterFormData_Building building in buildings)
                        {
                            buildingsDict.Add(building.BuildingId, building);

                            if (regionsDict.TryGetValue(building.RegionId, out RegisterFormData_Region? region))
                            {
                                region!.Buildings.Add(building);
                            }
                        }

                        if (!reader2.IsConsumed)
                        {
                            List<RegisterFormData_Function> functions = (await reader2.ReadAsync<RegisterFormData_Function>()).AsList();

                            if (functions.Count > 0)
                            {
                                foreach (RegisterFormData_Function function in functions)
                                {
                                    if (buildingsDict.TryGetValue(function.BuildingId, out RegisterFormData_Building? building))
                                    {
                                        building!.Functions.Add(function);
                                    }
                                }
                            }
                        }
                    }
                }

                return (UserSelfRegistrationResult.Ok, registerFormData);
            }
        }

        /// <summary>
        /// <para>Validates a register token for creating an account when logging in using Azure Active Directory.</para>
        /// <para>Returns one of the following results: <see cref="UserSelfRegistrationResult.Ok"/>, <see cref="UserSelfRegistrationResult.RecordAlreadyExists"/>,
        /// <see cref="UserSelfRegistrationResult.SingleSignOnNotEnabled"/>, <see cref="UserSelfRegistrationResult.RegisterTokenInvalid"/>.</para>
        /// </summary>
        /// <param name="azureTenantId"></param>
        /// <param name="azureObjectId"></param>
        /// <param name="token"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserSelfRegistrationResult, RegisterFormData?)> CheckRegisterAzureADTokenAsync(Guid azureTenantId, Guid azureObjectId, string token, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_registerAzureTokensData table
(
    AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
   ,Email nvarchar(254)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,OrganizationId uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
)

-- Delete expired tokens for the email to clean them up
delete from tblRegisterAzureTokens
output deleted.AzureTenantId
      ,deleted.AzureObjectId
      ,deleted.Email
      ,deleted.FirstName
      ,deleted.Surname
      ,deleted.OrganizationId
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      ,deleted.AvatarUrl
      ,deleted.AvatarImageStorageId
      into @_registerAzureTokensData
where AzureTenantId = @azureTenantId
and AzureObjectId = @azureObjectId
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblRegisterAzureTokens_Log
(id
,InsertDateUtc
,UpdatedByIpAddress
,LogDescription
,AzureTenantId
,AzureObjectId
,Email
,FirstName
,Surname
,OrganizationId
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,AvatarUrl
,AvatarImageStorageId
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@remoteIpAddress
      ,'Register Token Expired (Azure AD)' -- LogDescription
      ,@azureTenantId
      ,@azureObjectId
      ,d.Email
      ,d.FirstName
      ,d.Surname
      ,d.OrganizationId
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,d.AvatarUrl
      ,d.AvatarImageStorageId
      ,'Delete' -- LogAction
from @_registerAzureTokensData d

-- Check if user exists with linked azure account
select 1
from tblUserAzureObjectId
inner join tblUsers
on tblUserAzureObjectId.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
where tblUserAzureObjectId.AzureTenantId = @azureTenantId
and tblUserAzureObjectId.AzureObjectId = @azureObjectId

if @@ROWCOUNT = 0
begin
    -- Get all of the current active tokens for this TenantId/ObjectId pair
    select AzureTenantId
          ,RegisterToken
          ,Email
          ,FirstName
          ,Surname
          ,OrganizationId
          ,AvatarUrl
          ,AvatarImageStorageId
    from tblRegisterAzureTokens
    where AzureTenantId = @azureTenantId
    and AzureObjectId = @azureObjectId
    and ExpiryDateUtc >= @_now
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@azureTenantId", azureTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureObjectId", azureObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                bool linkedAzureAccountUserExists = await reader.ReadFirstOrDefaultAsync<bool>();

                // Check if the given Azure TenantId/ObjectId pair is already linked to an existing user
                if (linkedAzureAccountUserExists)
                {
                    return (UserSelfRegistrationResult.RecordAlreadyExists, null);
                }

                List<RegisterAzureTokenDbRow> encryptedTokens = (await reader.ReadAsync<RegisterAzureTokenDbRow>()).AsList();

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens.Count == 0)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                RegisterAzureTokenDbRow? tokenData = null;

                // Check stored tokens to see if the provided token is in the database
                foreach (RegisterAzureTokenDbRow encryptedToken in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken.RegisterToken, _appSettings.Password.RegisterTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        tokenData = encryptedToken;
                        break;
                    }
                }

                if (tokenData is null)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                // Check that the tenant ID the token was created with matches the requested one
                if (tokenData.AzureTenantId != azureTenantId)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                // Query data needed for the register form
                sql = @"
declare @_organizationAzureADSingleSignOnEnabled bit = 0
declare @_organizationAzureADTenantId uniqueidentifier
declare @_userExists bit = 0

select @_organizationAzureADSingleSignOnEnabled = AzureADSingleSignOnEnabled
      ,@_organizationAzureADTenantId = AzureADTenantId
from tblOrganizations
inner join tblOrganizationAzureSettings
on tblOrganizations.id = tblOrganizationAzureSettings.OrganizationId
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0

-- Check if user exists with requested email
select @_userExists = 1
from tblUsers
where Email = @email
and Deleted = 0

select @_organizationAzureADSingleSignOnEnabled as SingleSignOnEnabled
      ,case when @_organizationAzureADSingleSignOnEnabled = 1 then @_organizationAzureADTenantId else null end as TenantId
      ,@_userExists as UserExists

if @_organizationAzureADSingleSignOnEnabled = 1 and @_organizationAzureADTenantId = @azureTenantId
begin
    select tblRegions.id as RegionId
          ,tblRegions.Name as RegionName
    from tblRegions
    inner join tblOrganizations
    on tblRegions.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblRegions.OrganizationId = @organizationId
    and tblRegions.Deleted = 0
    order by tblRegions.Name

    if @@ROWCOUNT > 0
    begin
        select tblRegions.id as RegionId
              ,tblBuildings.id as BuildingId
              ,tblBuildings.Name as BuildingName
        from tblBuildings
        inner join tblRegions
        on tblBuildings.RegionId = tblRegions.id
        and tblRegions.Deleted = 0
        inner join tblOrganizations
        on tblRegions.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        where tblRegions.OrganizationId = @organizationId
        and tblBuildings.Deleted = 0
        order by tblBuildings.Name

        if @@ROWCOUNT > 0
        begin
            select tblBuildings.id as BuildingId
                  ,tblFunctions.id as FunctionId
                  ,tblFunctions.Name as FunctionName
            from tblFunctions
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblRegions
            on tblBuildings.RegionId = tblRegions.id
            and tblRegions.Deleted = 0
            inner join tblOrganizations
            on tblRegions.OrganizationId = tblOrganizations.id
            and tblOrganizations.Deleted = 0
            where tblFunctions.Deleted = 0
            order by tblFunctions.Name
        end
    end
end
";
                parameters = new DynamicParameters();
                parameters.Add("@organizationId", tokenData.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureTenantId", tokenData.AzureTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@email", tokenData.Email, DbType.String, ParameterDirection.Input, 254);

                using SqlMapper.GridReader reader2 = await sqlConnection.QueryMultipleAsync(sql, parameters);

                (bool organizationSingleSignOnEnabled, Guid? organizationTenantId, bool emailUserExists) = await reader2.ReadFirstAsync<(bool, Guid?, bool)>();

                // Check whether another user already exists with the same email
                if (emailUserExists)
                {
                    return (UserSelfRegistrationResult.RecordAlreadyExists, null);
                }

                // Check that the organization has single sign on enabled
                if (!organizationSingleSignOnEnabled)
                {
                    return (UserSelfRegistrationResult.SingleSignOnNotEnabled, null);
                }

                // Check that the tenant ID for the organization matches the requested one
                if (organizationTenantId is null || organizationTenantId.Value != azureTenantId)
                {
                    return (UserSelfRegistrationResult.RegisterTokenInvalid, null);
                }

                RegisterFormData registerFormData = new RegisterFormData
                {
                    Email = tokenData.Email,
                    FirstName = tokenData.FirstName,
                    Surname = tokenData.Surname,
                    OrganizationId = tokenData.OrganizationId,
                    AvatarUrl = tokenData.AvatarUrl,
                    AvatarImageStorageId = tokenData.AvatarImageStorageId,
                };

                registerFormData.Regions = (await reader2.ReadAsync<RegisterFormData_Region>()).AsList();

                if (!reader2.IsConsumed)
                {
                    List<RegisterFormData_Building> buildings = (await reader2.ReadAsync<RegisterFormData_Building>()).AsList();

                    if (buildings.Count > 0)
                    {
                        Dictionary<Guid, RegisterFormData_Region> regionsDict = new Dictionary<Guid, RegisterFormData_Region>();
                        Dictionary<Guid, RegisterFormData_Building> buildingsDict = new Dictionary<Guid, RegisterFormData_Building>();

                        foreach (RegisterFormData_Region region in registerFormData.Regions)
                        {
                            regionsDict.Add(region.RegionId, region);
                        }

                        foreach (RegisterFormData_Building building in buildings)
                        {
                            buildingsDict.Add(building.BuildingId, building);

                            if (regionsDict.TryGetValue(building.RegionId, out RegisterFormData_Region? region))
                            {
                                region!.Buildings.Add(building);
                            }
                        }

                        if (!reader2.IsConsumed)
                        {
                            List<RegisterFormData_Function> functions = (await reader2.ReadAsync<RegisterFormData_Function>()).AsList();

                            if (functions.Count > 0)
                            {
                                foreach (RegisterFormData_Function function in functions)
                                {
                                    if (buildingsDict.TryGetValue(function.BuildingId, out RegisterFormData_Building? building))
                                    {
                                        building!.Functions.Add(function);
                                    }
                                }
                            }
                        }
                    }
                }

                return (UserSelfRegistrationResult.Ok, registerFormData);
            }
        }

        /// <summary>
        /// <para>Creates a user. Intended to be used on Master Settings Users page, as the request fails if the user already exists, without adding them to the new organization.</para>
        /// <para>Returns: <see cref="UserManagementResult.Ok"/>, <see cref="UserManagementResult.UserAlreadyExists"/>, <see cref="UserManagementResult.UserAssetTypesInvalid"/>,
        /// <see cref="UserManagementResult.UserAdminFunctionsInvalid"/>, <see cref="UserManagementResult.UserAdminAssetTypesInvalid"/>,
        /// <see cref="UserManagementResult.NewUserCreatedButStoreAvatarImageFailed"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="avatarContentInspectorResult"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<(UserManagementResult, UserData?)> MasterCreateUserAsync(MasterCreateUserRequest request,
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult,
            Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Create User (Master)";
            string displayName = $"{request.FirstName} {request.Surname}";

            string? passwordHash = null;
            bool passwordChanged = false;

            // Calculate PasswordHash if password was provided
            if (!string.IsNullOrEmpty(request.Password))
            {
                passwordChanged = true;
                passwordHash = HMAC_Bcrypt.hmac_bcrypt_hash(request.Password, _bcryptSettings, _appSettings.Password.Pepper);
            }

            DynamicParameters parameters = new DynamicParameters();

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                StringBuilder sql = new StringBuilder();

                sql.AppendLine(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_userAssetTypesValid bit = 1
declare @_userAdminFunctionsValid bit = 1
declare @_userAdminAssetTypesValid bit = 1

-- Auto-populate timezone if not specified
if @timezone is null or @timezone = ''
begin
    select @timezone = Timezone
    from tblBuildings
    where Deleted = 0
    and id = @buildingId
    and OrganizationId = @organizationId
end

declare @_userAssetTypesData table
(
    AssetTypeId uniqueidentifier
)

declare @_userAdminFunctionsData table
(
    FunctionId uniqueidentifier
)

declare @_userAdminAssetTypesData table
(
    AssetTypeId uniqueidentifier
)
");

                // Insert UserAssetTypes into query
                if (request.UserAssetTypes is not null && request.UserAssetTypes.Count > 0)
                {
                    sql.AppendLine(@"
insert into @_userAssetTypesData
(AssetTypeId)
values");
                    for (int i = 0; i < request.UserAssetTypes.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }

                        sql.AppendLine($"(@userAssetTypeId{i})");
                        parameters.Add($"@userAssetTypeId{i}", request.UserAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                    }
                }

                // For Admin, populate UserAdminFunctions and UserAdminAssetTypes into query 
                switch (request.UserOrganizationRole)
                {
                    case UserOrganizationRole.NoAccess:
                    case UserOrganizationRole.User:
                    case UserOrganizationRole.SuperAdmin:
                    case UserOrganizationRole.Tablet:
                        break;
                    case UserOrganizationRole.Admin:
                        // Insert UserAdminFunctions into query
                        if (request.UserAdminFunctions is not null && request.UserAdminFunctions.Count > 0)
                        {
                            sql.AppendLine(@"
insert into @_userAdminFunctionsData
(FunctionId)
values");
                            for (int i = 0; i < request.UserAdminFunctions.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminFunctionId{i})");
                                parameters.Add($"@userAdminFunctionId{i}", request.UserAdminFunctions[i], DbType.Guid, ParameterDirection.Input);
                            }
                        }

                        // Insert UserAdminAssetTypes into query
                        if (request.UserAdminAssetTypes is not null && request.UserAdminAssetTypes.Count > 0)
                        {
                            sql.AppendLine(@"
insert into @_userAdminAssetTypesData
(AssetTypeId)
values");
                            for (int i = 0; i < request.UserAdminAssetTypes.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminAssetTypeId{i})");
                                parameters.Add($"@userAdminAssetTypeId{i}", request.UserAdminAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                            }
                        }
                        break;
                    default:
                        throw new Exception($"Unknown UserOrganizationRole: {request.UserOrganizationRole}");
                }

                sql.AppendLine($@"
-- Validate UserAssetTypes
select top 1 @_userAssetTypesValid = 0
from @_userAssetTypesData d
where not exists
(
    select *
    from tblAssetTypes
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetTypes.Deleted = 0
    and tblAssetTypes.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.AssetTypeId = tblAssetTypes.id
)

-- Validate UserAdminFunctions
select top 1 @_userAdminFunctionsValid = 0
from @_userAdminFunctionsData d
where not exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.FunctionId = tblFunctions.id
)

-- Validate UserAdminAssetTypes
select top 1 @_userAdminAssetTypesValid = 0
from @_userAdminAssetTypesData d
where not exists
(
    select *
    from tblAssetTypes
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetTypes.Deleted = 0
    and tblAssetTypes.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.AssetTypeId = tblAssetTypes.id
)

if @_userAssetTypesValid = 0
begin
    -- At least one of UserAssetTypes is invalid
    set @_result = 3
end
else if @_userAdminFunctionsValid = 0
begin
    -- At least one of UserAdminFunctions is invalid
    set @_result = 4
end
else if @_userAdminAssetTypesValid = 0
begin
    -- At least one of UserAdminAssetTypes is invalid
    set @_result = 5
end
else
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
        insert into tblUsers
        (Uid
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,LastPasswordChangeDateUtc
        ,Email
        ,PasswordHash
        ,PasswordLoginFailureCount
        ,TotpEnabled
        ,TotpFailureCount
        ,UserSystemRole
        ,DisplayName
        ,FirstName
        ,Surname
        ,Timezone
        ,Disabled
        ,Deleted)
        select @uid
              ,@_now
              ,@_now
              ,case when @passwordHash is not null then @_now else null end
              ,@email
              ,@passwordHash
              ,0 -- PasswordLoginFailureCount
              ,0 -- TotpEnabled
              ,0 -- TotpFailureCount
              ,@userSystemRole
              ,@displayName
              ,@firstName
              ,@surname
              ,@timezone
              ,@disabled
              ,0 -- Deleted
        where not exists
        (
            select *
            from tblUsers
            where Email = @email
            and Deleted = 0
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            insert into tblUsers_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,Email
            ,UserSystemRole
            ,TotpEnabled
            ,DisplayName
            ,FirstName
            ,Surname
            ,Timezone
            ,Disabled
            ,Deleted
            ,PasswordChanged
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@email
                  ,@userSystemRole
                  ,0 -- TotpEnabled
                  ,@displayName
                  ,@firstName
                  ,@surname
                  ,@timezone
                  ,@disabled
                  ,0 -- Deleted
                  ,@passwordChanged
                  ,'Insert' -- LogAction

            insert into tblUserOrganizationJoin
            (Uid
            ,OrganizationId
            ,InsertDateUtc
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled)
            select @uid
                  ,@organizationId
                  ,@_now
                  ,@userOrganizationRole
                  ,@note
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled

            insert into tblUserOrganizationJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinLogId
                  ,@_now 
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@note
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserOrganizationJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserOrganizationJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc)
            select @userOrganizationJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user organization join history for the new user
            insert into tblUserOrganizationJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,UserOrganizationJoinHistoryId
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@userOrganizationJoinHistoryId
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            insert into tblUserBuildingJoin
            (Uid
            ,BuildingId
            ,InsertDateUtc
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere)
            select @uid
                  ,@buildingId
                  ,@_now
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere

            insert into tblUserBuildingJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinLogId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Insert a new row into tblUserBuildingJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserBuildingJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc)
            select @userBuildingJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user building join history for the new user
            insert into tblUserBuildingJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,UserBuildingJoinHistoryId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@userBuildingJoinHistoryId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId

            insert into tblUserAssetTypeJoin
            (Uid
            ,BuildingId
            ,AssetTypeId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,@_now
            from @_userAssetTypesData d

            -- Insert to log
            ;with logIds as (
                select ids.AssetTypeId, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_userAssetTypesData
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
                    from @_userAssetTypesData
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblUserAssetTypeJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,AssetTypeId
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
                  ,@uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_userAssetTypesData d
            left join logIds l
            on d.AssetTypeId = l.AssetTypeId

            insert into tblUserAdminFunctions
            (Uid
            ,BuildingId
            ,FunctionId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.FunctionId
                  ,@_now
            from @_userAdminFunctionsData d

            -- Insert to log
            ;with logIds as (
                select ids.FunctionId, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_userAdminFunctionsData
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by FunctionId) as RowNumber, FunctionId
                    from @_userAdminFunctionsData
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblUserAdminFunctions_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
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
                  ,@uid
                  ,@buildingId
                  ,d.FunctionId
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_userAdminFunctionsData d
            left join logIds l
            on d.FunctionId = l.FunctionId

            insert into tblUserAdminAssetTypes
            (Uid
            ,BuildingId
            ,AssetTypeId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,@_now
            from @_userAdminAssetTypesData d

            -- Insert to log
            ;with logIds as (
                select ids.AssetTypeId, combs.LogId
                from
                (
                    select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                    from
                    (
                        select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                        from @_userAdminAssetTypesData
                    ) combsInner
                ) combs
                inner join
                (
                    select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
                    from @_userAdminAssetTypesData
                ) ids
                on ids.RowNumber = combs.RowNumber
            )
            insert into tblUserAdminAssetTypes_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,AssetTypeId
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
                  ,@uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,'Insert' -- LogAction
                  ,'tblUsers' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_userAdminAssetTypesData d
            left join logIds l
            on d.AssetTypeId = l.AssetTypeId
        end
        else
        begin
            -- User already exists
            set @_result = 2
        end

        commit
    end
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        and tblUserOrganizationJoin.UserOrganizationRole > 0 -- Ignore organizations with no access
        and tblUserOrganizationJoin.UserOrganizationDisabled = 0 -- Ignore organizations user is banned from
        order by tblOrganizations.Name

        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query the user's permanent seat
        select tblDesks.id as DeskId
              ,tblBuildings.id as BuildingId
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType = {(int)DeskType.Permanent}
        and tblDesks.PermanentOwnerUid = @uid

        -- Also query the user's asset types
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblUserAssetTypeJoin
        inner join tblAssetTypes
        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblUserAssetTypeJoin.Uid = @uid

        -- Also query the user's permanent assets
        select tblAssetSlots.id as AssetSlotId
              ,tblAssetSections.AssetTypeId
              ,tblBuildings.id as BuildingId
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
        and tblAssetSlots.PermanentOwnerUid = @uid

        -- Also query the user's admin functions if the user is an Admin,
        -- or all functions if they are a Super Admin.
        select tblFunctions.id
              ,tblFunctions.Name
              ,tblFunctions.BuildingId
        from tblFunctions
        where tblFunctions.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminFunctions
            on tblFunctions.id = tblUserAdminFunctions.FunctionId
            and tblUserAdminFunctions.Uid = @uid
            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminFunctions.FunctionId is not null
                )
            )
        )

        -- Also query the user's admin asset types if the user is an Admin,
        -- or all asset types if they are a Super Admin.
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblAssetTypes
        where tblAssetTypes.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminAssetTypes
            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
            and tblUserAdminAssetTypes.Uid = @uid
            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminAssetTypes.AssetTypeId is not null
                )
            )
        )
    end
end
");
                Guid uid = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblUserOrganizationJoinHistories and tblUserOrganizationJoinHistories_Log
                // as well as tblUserBuildingJoinHistories and tblUserBuildingJoinHistories_Log
                Guid userOrganizationJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Email!.ToUpperInvariant())));

                parameters.Add("@lockResourceName", $"tblUsers_Email_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                // User details
                parameters.Add("@email", request.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@firstName", request.FirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", request.Surname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@passwordHash", passwordHash, DbType.AnsiString, ParameterDirection.Input, 115);
                parameters.Add("@passwordChanged", passwordChanged, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@displayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@userSystemRole", request.UserSystemRole, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@timezone", request.Timezone, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@disabled", request.Disabled, DbType.Boolean, ParameterDirection.Input);

                // Organization details
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationRole", request.UserOrganizationRole, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@contractor", request.Contractor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@visitor", request.Visitor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@note", request.Note, DbType.String, ParameterDirection.Input, 500);
                parameters.Add("@userOrganizationDisabled", request.UserOrganizationDisabled, DbType.Boolean, ParameterDirection.Input);

                // Building details
                parameters.Add("@functionId", request.FunctionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@firstAidOfficer", request.FirstAidOfficer, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@fireWarden", request.FireWarden, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@peerSupportOfficer", request.PeerSupportOfficer, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@allowBookingDeskForVisitor", request.AllowBookingDeskForVisitor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@allowBookingRestrictedRooms", request.AllowBookingRestrictedRooms, DbType.Boolean, ParameterDirection.Input);

                // Building Admin details
                parameters.Add("@allowBookingAnyoneAnywhere", request.AllowBookingAnyoneAnywhere, DbType.Boolean, ParameterDirection.Input);

                // Histories
                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@userOrganizationJoinHistoryId", userOrganizationJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryLogId", userOrganizationJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryId", userBuildingJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", userBuildingJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinLogId", userOrganizationJoinLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinLogId", userBuildingJoinLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? data = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    data = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                    if (!gridReader.IsConsumed && data is not null)
                    {
                        // Read extended data
                        data.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                        data.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                        List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                        List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                        List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                        List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                        List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                        List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                        FillExtendedDataOrganizations(data, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                    }
                }

                UserManagementResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = UserManagementResult.Ok;

                        if (data is not null && avatarContentInspectorResult is not null)
                        {
                            // Store avatar image file to disk
                            (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                await StoreAvatarImageAsync(sqlConnection,
                                    avatarContentInspectorResult,
                                    data.Uid, logId, adminUserUid, adminUserDisplayName, remoteIpAddress);

                            if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                            {
                                // Set image URLs and storage IDs in response to be returned.
                                data.AvatarUrl = storedImageFile.FileUrl;
                                data.AvatarImageStorageId = storedImageFile.Id;
                                data.AvatarThumbnailUrl = thumbnailFile.FileUrl;
                                data.AvatarThumbnailStorageId = thumbnailFile.Id;
                            }
                            else
                            {
                                queryResult = UserManagementResult.NewUserCreatedButStoreAvatarImageFailed; // Set error in case image is not uploaded
                                break;
                            }
                        }
                        break;
                    case 2:
                        queryResult = UserManagementResult.UserAlreadyExists;
                        break;
                    case 3:
                        queryResult = UserManagementResult.UserAssetTypesInvalid;
                        break;
                    case 4:
                        queryResult = UserManagementResult.UserAdminFunctionsInvalid;
                        break;
                    case 5:
                        queryResult = UserManagementResult.UserAdminAssetTypesInvalid;
                        break;
                    default:
                        queryResult = UserManagementResult.UnknownError;
                        break;
                }

                return (queryResult, data);
            }
        }

        /// <summary>
        /// <para>Update user details for master settings.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordAlreadyExists"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="avatarContentInspectorResult"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, UserData?)> MasterUpdateUserAsync(MasterUpdateUserRequest request,
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult,
            Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update User (Master)";
            bool passwordChanged = false;

            DynamicParameters parameters = new DynamicParameters();

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_lockResult int
declare @_emailExists bit = 0

declare @_data table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,TotpEnabled bit
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
   ,Disabled bit
   ,OldEmail nvarchar(254)
   ,OldUserSystemRole int
   ,OldDisplayName nvarchar(151)
   ,OldFirstName nvarchar(75)
   ,OldSurname nvarchar(75)
   ,OldTimezone varchar(50)
   ,OldAvatarUrl varchar(255)
   ,OldAvatarImageStorageId uniqueidentifier
   ,OldAvatarThumbnailUrl varchar(255)
   ,OldAvatarThumbnailStorageId uniqueidentifier
   ,OldDisabled bit
)

-- Check if other user exists with the same email
select top 1 @_emailExists = 1
from tblUsers
where Email = @email
and Deleted = 0
and Uid != @uid

if @_emailExists = 0
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
         update tblUsers
         set UpdatedDateUtc = @_now
            ,FirstName = @firstName
            ,Surname = @surname
            ,DisplayName = @displayName
            ,Email = @email
            ,Timezone = @timezone
            ,UserSystemRole = @userSystemRole
";
                if (!string.IsNullOrEmpty(request.NewPassword))
                {
                    passwordChanged = true;
                    string passwordHash = HMAC_Bcrypt.hmac_bcrypt_hash(request.NewPassword!, _bcryptSettings, _appSettings.Password.Pepper);
                    parameters.Add("@passwordHash", passwordHash, DbType.AnsiString, ParameterDirection.Input, 115);

                    // If NewPassword has a value, update the password hash and LastPasswordChangeDateUtc
                    sql += @"
            ,LastPasswordChangeDateUtc = @_now
            ,PasswordHash = @passwordHash";
                }

                if (request.AvatarImageChanged!.Value && request.AvatarImage is null)
                {
                    // Clear avatar image if it's being removed
                    sql += @"
            ,AvatarUrl = null
            ,AvatarImageStorageId = null
            ,AvatarThumbnailUrl = null
            ,AvatarThumbnailStorageId = null
";
                }

                sql += $@"
            ,Disabled = @disabled
        output inserted.Email
              ,inserted.UserSystemRole
              ,inserted.TotpEnabled
              ,inserted.DisplayName
              ,inserted.FirstName
              ,inserted.Surname
              ,inserted.TimeZone
              ,inserted.AvatarUrl
              ,inserted.AvatarImageStorageId
              ,inserted.AvatarThumbnailUrl
              ,inserted.AvatarThumbnailStorageId
              ,inserted.Disabled
              ,deleted.Email
              ,deleted.UserSystemRole
              ,deleted.DisplayName
              ,deleted.FirstName
              ,deleted.Surname
              ,deleted.Timezone
              ,deleted.AvatarUrl
              ,deleted.AvatarImageStorageId
              ,deleted.AvatarThumbnailUrl
              ,deleted.AvatarThumbnailStorageId
              ,deleted.Disabled
              into @_data
        where Deleted = 0
        and Uid = @uid
        and ConcurrencyKey = @concurrencyKey
        and not exists
        (
            select *
            from tblUsers
            where Email = @email
            and Deleted = 0
            and Uid != @uid
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            insert into tblUsers_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,Email
            ,UserSystemRole
            ,TotpEnabled
            ,DisplayName
            ,FirstName
            ,Surname
            ,Timezone
            ,AvatarUrl
            ,AvatarImageStorageId
            ,AvatarThumbnailUrl
            ,AvatarThumbnailStorageId
            ,Disabled
            ,Deleted
            ,OldEmail
            ,OldUserSystemRole
            ,OldTotpEnabled
            ,OldDisplayName
            ,OldFirstName
            ,OldSurname
            ,OldTimezone
            ,OldAvatarUrl
            ,OldAvatarImageStorageId
            ,OldAvatarThumbnailUrl
            ,OldAvatarThumbnailStorageId
            ,OldDisabled
            ,OldDeleted
            ,PasswordChanged
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,d.Email
                  ,d.UserSystemRole
                  ,d.TotpEnabled
                  ,d.DisplayName
                  ,d.FirstName
                  ,d.Surname
                  ,d.Timezone
                  ,d.AvatarUrl
                  ,d.AvatarImageStorageId
                  ,d.AvatarThumbnailUrl
                  ,d.AvatarThumbnailStorageId
                  ,d.Disabled
                  ,0 -- Deleted
                  ,d.OldEmail
                  ,d.OldUserSystemRole
                  ,d.TotpEnabled
                  ,d.OldDisplayName
                  ,d.OldFirstName
                  ,d.OldSurname
                  ,d.OldTimezone
                  ,d.OldAvatarUrl
                  ,d.OldAvatarImageStorageId
                  ,d.OldAvatarThumbnailUrl
                  ,d.OldAvatarThumbnailStorageId
                  ,d.OldDisabled
                  ,0 -- OldDeleted
                  ,@passwordChanged
                  ,'Update' -- LogAction
            from @_data d
        end
        else
        begin
            -- User did not exist
            set @_result = 3
        end

        commit
    end
end
else
begin
    -- Existing user with given email already exists
    set @_result = 2
end

select @_result

-- Select old ImageStorageIds so we can delete off disk
select AvatarImageStorageId
from @_data

-- Select row to return with the API result
select Uid
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,LastAccessDateUtc
      ,LastPasswordChangeDateUtc
      ,Email
      ,HasPassword
      ,TotpEnabled
      ,UserSystemRole
      ,DisplayName
      ,FirstName
      ,Surname
      ,Timezone
      ,AvatarUrl
      ,AvatarThumbnailUrl
      ,Disabled
      ,ConcurrencyKey
from tblUsers
where Deleted = 0
and Uid = @uid

if @@ROWCOUNT = 1
begin
    -- Also query user's organization access
    select tblUserOrganizationJoin.OrganizationId as id
          ,tblOrganizations.Name
          ,tblOrganizations.LogoImageUrl
          ,tblOrganizations.CheckInEnabled
          ,tblOrganizations.WorkplacePortalEnabled
          ,tblOrganizations.WorkplaceAccessRequestsEnabled
          ,tblOrganizations.WorkplaceInductionsEnabled
          ,tblUserOrganizationJoin.UserOrganizationRole
          ,tblUserOrganizationJoin.Note
          ,tblUserOrganizationJoin.Contractor
          ,tblUserOrganizationJoin.Visitor
          ,tblUserOrganizationJoin.UserOrganizationDisabled
          ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserOrganizationJoin
    inner join tblOrganizations
    on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblUserOrganizationJoin.Uid = @uid
    order by tblOrganizations.Name

    -- Also query user's last used building
    select Uid
          ,WebLastUsedOrganizationId
          ,WebLastUsedBuildingId
          ,MobileLastUsedOrganizationId
          ,MobileLastUsedBuildingId
    from tblUserLastUsedBuilding
    where Uid = @uid

    -- Also query user's building access
    select tblUserBuildingJoin.BuildingId as id
          ,tblBuildings.Name
          ,tblBuildings.OrganizationId
          ,tblBuildings.Timezone
          ,tblBuildings.CheckInEnabled
          ,0 as HasBookableMeetingRooms -- Queried separately
          ,0 as HasBookableAssetSlots -- Queried separately
          ,tblUserBuildingJoin.FunctionId
          ,tblFunctions.Name as FunctionName
          ,tblFunctions.HtmlColor as FunctionHtmlColor
          ,tblUserBuildingJoin.FirstAidOfficer
          ,tblUserBuildingJoin.FireWarden
          ,tblUserBuildingJoin.PeerSupportOfficer
          ,tblUserBuildingJoin.AllowBookingDeskForVisitor
          ,tblUserBuildingJoin.AllowBookingRestrictedRooms
          ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
          ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserBuildingJoin
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblFunctions
    on tblUserBuildingJoin.FunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblUserBuildingJoin.Uid = @uid
    order by tblBuildings.Name

    -- Also query user's buildings with bookable desks
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType != {(int)DeskType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query user's buildings with bookable meeting rooms
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblMeetingRooms
        inner join tblFloors
        on tblMeetingRooms.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblMeetingRooms.Deleted = 0
        and tblMeetingRooms.OfflineRoom = 0
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
        and
        (
            tblMeetingRooms.RestrictedRoom = 0
            or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
        )
    )

    -- Also query user's buildings with bookable asset slots
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query the user's permanent seat
    select tblDesks.id as DeskId
          ,tblBuildings.id as BuildingId
    from tblDesks
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblDesks.Deleted = 0
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid

    -- Also query the user's asset types
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblUserAssetTypeJoin.Uid = @uid

    -- Also query the user's permanent assets
    select tblAssetSlots.id as AssetSlotId
          ,tblAssetSections.AssetTypeId
          ,tblBuildings.id as BuildingId
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid

    -- Also query the user's admin functions if the user is an Admin,
    -- or all functions if they are a Super Admin.
    select tblFunctions.id
          ,tblFunctions.Name
          ,tblFunctions.BuildingId
    from tblFunctions
    where tblFunctions.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminFunctions
        on tblFunctions.id = tblUserAdminFunctions.FunctionId
        and tblUserAdminFunctions.Uid = @uid
        where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminFunctions.FunctionId is not null
            )
        )
    )

    -- Also query the user's admin asset types if the user is an Admin,
    -- or all asset types if they are a Super Admin.
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblAssetTypes
    where tblAssetTypes.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminAssetTypes
        on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
        and tblUserAdminAssetTypes.Uid = @uid
        where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminAssetTypes.AssetTypeId is not null
            )
        )
    )
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                string lockResourceHash = Convert.ToHexString(SHA1.HashData(Encoding.Unicode.GetBytes(request.Email!)));
                string displayName = $"{request.FirstName} {request.Surname}".Trim();

                parameters.Add("@lockResourceName", $"tblUsers_Email_{lockResourceHash}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@firstName", request.FirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", request.Surname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@email", request.Email, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@displayName", displayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@userSystemRole", request.UserSystemRole, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@timezone", request.Timezone, DbType.String, ParameterDirection.Input, 50);
                parameters.Add("@disabled", request.Disabled, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@passwordChanged", passwordChanged, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                (Guid? oldAvatarImageStorageId, Guid? oldAvatarThumbnailStorageId) = await gridReader.ReadFirstOrDefaultAsync<(Guid?, Guid?)>();
                UserData? userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                // If update was successful, also get the data
                if (!gridReader.IsConsumed && userData is not null)
                {
                    // Read extended data
                    userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                    userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                    FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        if (userData is not null)
                        {
                            if (userData.ExtendedData.Organizations is not null && userData.ExtendedData.Organizations.Count > 0)
                            {
                                // Invalidate cache so that if the user is currently logged in and using the system,
                                // the change will take effect right away.
                                List<Guid> organizationIds = new List<Guid>();

                                foreach (UserData_UserOrganizations organizationData in userData.ExtendedData.Organizations)
                                {
                                    organizationIds.Add(organizationData.Id);
                                }

                                await _authCacheService.InvalidateUserOrganizationPermissionCacheAsync(request.Uid!.Value, organizationIds);
                            }

                            // Check if avatar image was changed
                            if (request.AvatarImageChanged!.Value)
                            {
                                // Check if avatar image was deleted
                                if (request.AvatarImage is null)
                                {
                                    // Remove the image from database and delete from disk if required
                                    if (oldAvatarImageStorageId is not null)
                                    {
                                        await _imageStorageRepository.DeleteImageAsync(oldAvatarImageStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                    }

                                    // Remove the thumbnail from database and delete from disk if required
                                    if (oldAvatarThumbnailStorageId is not null)
                                    {
                                        await _imageStorageRepository.DeleteImageAsync(oldAvatarThumbnailStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                    }
                                }
                                // Otherwise, avatar image must have been replaced
                                else
                                {
                                    // Store avatar image file to disk
                                    (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile) =
                                        await StoreAvatarImageAsync(sqlConnection,
                                            avatarContentInspectorResult,
                                            request.Uid!.Value, logId, adminUserUid, adminUserDisplayName, remoteIpAddress);

                                    if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null && thumbnailFile is not null)
                                    {
                                        // Set image URL in response to be returned.
                                        userData.AvatarUrl = storedImageFile.FileUrl;
                                        userData.AvatarThumbnailUrl = thumbnailFile.FileUrl;

                                        // Remove the image from database and delete from disk if required
                                        if (oldAvatarImageStorageId is not null)
                                        {
                                            await _imageStorageRepository.DeleteImageAsync(oldAvatarImageStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                        }

                                        // Remove the thumbnail from database and delete from disk if required
                                        if (oldAvatarThumbnailStorageId is not null)
                                        {
                                            await _imageStorageRepository.DeleteImageAsync(oldAvatarThumbnailStorageId.Value, "tblUsers", logId, adminUserUid, adminUserDisplayName, remoteIpAddress);
                                        }
                                    }
                                    else
                                    {
                                        queryResult = storeImageResult;
                                    }
                                }
                            }
                        }
                        break;
                    case 2:
                        // Another user with the specified email already exists.
                        queryResult = SqlQueryResult.RecordAlreadyExists;
                        break;
                    case 3:
                        if (userData is null)
                        {
                            // Row did not exist
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(userData.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // Row exists but concurrency key was invalid
                            queryResult = SqlQueryResult.ConcurrencyKeyInvalid;
                        }
                        else
                        {
                            // Concurrency key matches, assume that user does not belong to organization.
                            // (Could also be that the email already exists if an unlikely race condition occurred).
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return (queryResult, userData);
            }
        }

        public async Task<UserSelfRegistrationResult> InitRegisterAzureADAsync(Guid azureTenantId, Guid azureObjectId, Guid organizationId, string azureEmail, string? azureUserPrincipalName,
            string? azureFirstName, string? azureSurname, string? azureDisplayName, string? userAgentBrowserName, string? userAgentOsName, string? userAgentDeviceInfo,
            string? azureAvatarUrl, Guid? azureAvatarImageStorageId, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_uid uniqueidentifier
declare @_organizationAzureADSingleSignOnEnabled bit = 0
declare @_organizationAzureADTenantId uniqueidentifier

select @_uid = Uid
from tblUsers
where Email = @email
and Deleted = 0

select @_organizationAzureADSingleSignOnEnabled = AzureADSingleSignOnEnabled
      ,@_organizationAzureADTenantId = AzureADTenantId
from tblOrganizations
inner join tblOrganizationAzureSettings
on tblOrganizations.id = tblOrganizationAzureSettings.OrganizationId
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0

select @_uid
      ,@_organizationAzureADSingleSignOnEnabled as SingleSignOnEnabled
      ,case when @_organizationAzureADSingleSignOnEnabled = 1 then @_organizationAzureADTenantId else null end as TenantId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@email", azureEmail, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                (Guid? uid, bool organizationSingleSignOnEnabled, Guid? organizationAzureTenantId) = await sqlConnection.QueryFirstOrDefaultAsync<(Guid?, bool, Guid?)>(sql, parameters);

                if (uid is not null)
                {
                    return UserSelfRegistrationResult.RecordAlreadyExists;
                }

                // If the organization does not have single sign on enabled, stop here
                if (!organizationSingleSignOnEnabled)
                {
                    return UserSelfRegistrationResult.SingleSignOnNotEnabled;
                }

                // If the organization's azure tenant ID does not match the requested one, stop here
                if (organizationAzureTenantId != azureTenantId)
                {
                    return UserSelfRegistrationResult.SingleSignOnNotEnabled;
                }

                // Generate a token to be used in an email link for registering the user
                string registerToken = PasswordGenerator.GenerateAlphanumeric(96);
                string encryptedRegisterToken = StringCipherAesGcm.Encrypt(registerToken, _appSettings.Password.RegisterTokenEncryptionKey);

                // Store token for later use
                sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_expiryDate datetime2(3) = dateadd(hour, 1, @_now)

insert into tblRegisterAzureTokens
(AzureTenantId
,AzureObjectId
,RegisterToken
,InsertDateUtc
,Email
,FirstName
,Surname
,OrganizationId
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,AvatarUrl
,AvatarImageStorageId)
select @azureTenantId
      ,@azureObjectId
      ,@registerToken
      ,@_now -- InsertDateUtc
      ,@email
      ,@firstName
      ,@surname
      ,@organizationId
      ,@_expiryDate -- ExpiryDateUtc
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo
      ,@avatarUrl
      ,@avatarImageStorageId

insert into tblRegisterAzureTokens_Log
(id
,InsertDateUtc
,UpdatedByIpAddress
,LogDescription
,AzureTenantId
,AzureObjectId
,Email
,FirstName
,Surname
,OrganizationId
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,AvatarUrl
,AvatarImageStorageId
,LogAction)
select @logId
      ,@_now -- InsertDateUtc
      ,@remoteIpAddress
      ,@logDescription
      ,@azureTenantId
      ,@azureObjectId
      ,@email
      ,@firstName
      ,@surname
      ,@organizationId
      ,@_expiryDate -- ExpiryDateUtc
      ,@location
      ,@browserName
      ,@osName
      ,@deviceInfo
      ,@avatarUrl
      ,@avatarImageStorageId
      ,'Insert' -- LogAction

select @_now
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Init Register (Azure AD)";
                string? location = null;

                if (!string.IsNullOrEmpty(remoteIpAddress))
                {
                    location = await GetLocationStringForIpAddressAsync(remoteIpAddress);
                }

                parameters = new DynamicParameters();
                parameters.Add("@email", azureEmail, DbType.String, ParameterDirection.Input, 254);
                parameters.Add("@firstName", azureFirstName, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@surname", azureSurname, DbType.String, ParameterDirection.Input, 75);
                parameters.Add("@azureTenantId", azureTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureObjectId", azureObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@registerToken", encryptedRegisterToken, DbType.AnsiString, ParameterDirection.Input, 205);
                parameters.Add("@location", location, DbType.String, ParameterDirection.Input, 130);
                parameters.Add("@browserName", userAgentBrowserName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@osName", userAgentOsName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@deviceInfo", userAgentDeviceInfo, DbType.String, ParameterDirection.Input, 50);
                parameters.Add("@avatarUrl", azureAvatarUrl, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@avatarImageStorageId", azureAvatarImageStorageId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                DateTime insertDateUtc = await sqlConnection.QueryFirstOrDefaultAsync<DateTime>(sql, parameters);

                /*
                // Send email notification
                RegisterAzureADEmailTemplateInfo registerEmailTemplateInfo = new RegisterAzureADEmailTemplateInfo
                {
                    AzureEmail = azureEmail,
                    AzureFirstName = azureFirstName,
                    AzureTenantId = azureTenantId,
                    AzureObjectId = azureObjectId,
                    AzureUserPrincipalName = azureUserPrincipalName,
                    AzureDisplayName = azureDisplayName,
                    Token = registerToken,
                    RequestDateUtc = insertDateUtc,
                    IpAddress = remoteIpAddress,
                    Location = location,
                    DeviceInfo = userAgentDeviceInfo,
                    OperatingSystem = userAgentOsName,
                    Browser = userAgentBrowserName,
                    ToAddress = azureEmail
                };

                await _emailTemplateRepository.SendRegisterAzureADEmailNotificationAsync(registerEmailTemplateInfo);
                */

                return UserSelfRegistrationResult.Ok;
            }
        }

        public async Task<SqlQueryResult> InitLinkAccountAzureADSingleSignOnConfirmation(Guid uid, Guid azureTenantId, Guid azureObjectId, string sspAccountEmail, string? sspAccountFirstName,
            string? sspAccountDisplayName, string? azureUserPrincipalName, string? azureDisplayName, string? userAgentBrowserName, string? userAgentOsName, string? userAgentDeviceInfo, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Generate a token to be used in an email link for confirming linking azure AD account
                string confirmToken = PasswordGenerator.GenerateAlphanumeric(96);
                string encryptedConfirmToken = StringCipherAesGcm.Encrypt(confirmToken, _appSettings.Password.LinkAccountTokenEncryptionKey);

                // Store token for later use
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_expiryDate datetime2(3) = dateadd(hour, 1, @_now)
declare @_linkedAzureADAccountExists bit = 0

select @_linkedAzureADAccountExists = case when exists
(
    select *
    from tblUserAzureObjectId
    where Uid = @uid
)
then 1 else 0 end

select @_linkedAzureADAccountExists, @_now

-- If a linked Azure AD account does not yet exist for the user,
-- store confirm token to be included in an email sent to the user to confirm
-- linking the SSP account to the given Azure AD account.
if @_linkedAzureADAccountExists = 0
begin
    insert into tblLinkUserAccountAzureADConfirmTokens
    (Uid
    ,ConfirmToken
    ,InsertDateUtc
    ,AzureTenantId
    ,AzureObjectId
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo)
    select @uid
          ,@confirmToken
          ,@_now -- InsertDateUtc
          ,@azureTenantId
          ,@azureObjectId
          ,@_expiryDate -- ExpiryDateUtc
          ,@location
          ,@browserName
          ,@osName
          ,@deviceInfo

    insert into tblLinkUserAccountAzureADConfirmTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,AzureTenantId
    ,AzureObjectId
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction)
    select @logId
          ,@_now -- InsertDateUtc
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,@azureTenantId
          ,@azureObjectId
          ,@_expiryDate -- ExpiryDateUtc
          ,@location
          ,@browserName
          ,@osName
          ,@deviceInfo
          ,'Insert' -- LogAction
end
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Init Link Azure AD Account Confirmation";
                string? location = null;

                if (!string.IsNullOrEmpty(remoteIpAddress))
                {
                    location = await GetLocationStringForIpAddressAsync(remoteIpAddress);
                }

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureTenantId", azureTenantId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureObjectId", azureObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@confirmToken", encryptedConfirmToken, DbType.AnsiString, ParameterDirection.Input, 205);
                parameters.Add("@location", location, DbType.String, ParameterDirection.Input, 130);
                parameters.Add("@browserName", userAgentBrowserName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@osName", userAgentOsName, DbType.String, ParameterDirection.Input, 40);
                parameters.Add("@deviceInfo", userAgentDeviceInfo, DbType.String, ParameterDirection.Input, 50);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                (bool linkedAzureADAccountExists, DateTime insertDateUtc) = await sqlConnection.QueryFirstOrDefaultAsync<(bool, DateTime)>(sql, parameters);

                if (linkedAzureADAccountExists)
                {
                    return SqlQueryResult.RecordAlreadyExists;
                }

                /*
                // Send email notification
                LinkAccountAzureADConfirmEmailTemplateInfo confirmEmailTemplateInfo = new LinkAccountAzureADConfirmEmailTemplateInfo
                {
                    Uid = uid,
                    UserEmail = sspAccountEmail,
                    UserDisplayName = sspAccountDisplayName,
                    Token = confirmToken,
                    RequestDateUtc = insertDateUtc,
                    IpAddress = remoteIpAddress,
                    Location = location,
                    DeviceInfo = userAgentDeviceInfo,
                    OperatingSystem = userAgentOsName,
                    Browser = userAgentBrowserName,
                    ToAddress = sspAccountEmail,
                    FirstName = sspAccountFirstName,
                    AzureUserPrincipalName = azureUserPrincipalName,
                    AzureDisplayName = azureDisplayName,
                };

                await _emailTemplateRepository.SendLinkAccountAzureADConfirmEmailNotificationAsync(confirmEmailTemplateInfo);
                */

                return SqlQueryResult.Ok;
            }
        }

        /// <summary>
        /// <para>Completes linking an Azure AD acccount to a Smart Space Pro account by consuming the given token.</para>
        /// <para>Returns one of the following results: <see cref="UserLinkAccountResult.Ok"/>, <see cref="UserLinkAccountResult.SingleSignOnNotEnabled"/>,
        /// <see cref="UserLinkAccountResult.RegisterTokenInvalid"/>.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="token"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<UserLinkAccountResult> CompleteLinkAccountAzureADTokenAsync(Guid uid, string token, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Remove expired tokens and revoke active ones
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_userExists bit = 0
declare @_emailDomainName nvarchar(254)

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_linkAccountTokensData table
(
    Uid uniqueidentifier
   ,AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Delete expired tokens for the user to clean them up
delete from tblLinkUserAccountAzureADConfirmTokens
output deleted.Uid
      ,deleted.AzureTenantId
      ,deleted.AzureObjectId
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_linkAccountTokensData
where Uid = @uid
and ExpiryDateUtc <= @_now

-- Log expired tokens
insert into tblLinkUserAccountAzureADConfirmTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,AzureTenantId
,AzureObjectId
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid
      ,@_userDisplayName
      ,@remoteIpAddress
      ,'Link Azure AD Account Token Expired' -- LogDescription
      ,@uid
      ,d.AzureTenantId
      ,d.AzureObjectId
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_linkAccountTokensData d

-- Check if user exists
select @_userExists = 1
      ,@_emailDomainName = substring(Email, charindex('@', Email) + 1, len(Email))
from tblUsers
where Uid = @uid
and Deleted = 0

select @_userExists

if @_userExists = 1
begin
    -- Get all of the current active tokens for this user
    select ConfirmToken
          ,AzureTenantId
          ,AzureObjectId
    from tblLinkUserAccountAzureADConfirmTokens
    where Uid = @uid
    and ExpiryDateUtc >= @_now

    declare @_organizationId uniqueidentifier

    -- Check if user's email domain belongs to an organization
    select @_organizationId = OrganizationId
    from tblOrganizationDomains
    inner join tblOrganizations
    on tblOrganizationDomains.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblOrganizationDomains.DomainName = @_emailDomainName

    -- Select whether single sign on is enabled, and the organization's tenant ID
    select tblOrganizationAzureSettings.AzureADTenantId
          ,tblOrganizationAzureSettings.AzureADSingleSignOnEnabled
    from tblOrganizationAzureSettings
    inner join tblOrganizations
    on tblOrganizationAzureSettings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblOrganizationAzureSettings.OrganizationId = @_organizationId
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                using SqlMapper.GridReader reader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                bool userExists = await reader.ReadFirstOrDefaultAsync<bool>();
                List<(string encryptedToken, Guid tokenTenantId, Guid tokenObjectId)> encryptedTokens = (await reader.ReadAsync<(string, Guid, Guid)>()).AsList();
                (Guid? organizationTenantId, bool? singleSignOnEnabled) = await reader.ReadFirstOrDefaultAsync<(Guid?, bool?)>();

                // If user doesn't exist, stop here
                if (!userExists)
                {
                    return UserLinkAccountResult.UserInvalid;
                }

                // If single sign on isn't enabled for the organization, stop here
                if (!singleSignOnEnabled.HasValue || !singleSignOnEnabled.Value || !organizationTenantId.HasValue)
                {
                    return UserLinkAccountResult.SingleSignOnNotEnabled;
                }

                // If no tokens are available in the database, they must have all
                // expired, so we can't proceed.
                if (encryptedTokens.Count == 0)
                {
                    return UserLinkAccountResult.LinkAccountTokenInvalid;
                }

                bool confirmTokenFound = false;
                Guid? requestedTenantId = null;
                Guid? requestedObjectId = null;

                // Check stored tokens to see if the provided token is in the database
                foreach ((string encryptedToken, Guid tokenTenantId, Guid tokenObjectId) in encryptedTokens!)
                {
                    string decryptedToken = StringCipherAesGcm.Decrypt(encryptedToken, _appSettings.Password.LinkAccountTokenEncryptionKey);

                    if (token == decryptedToken)
                    {
                        // If token found, verify that the Tenant ID of the Azure AD account requesting
                        // to be linked to the user's account matches the tenant ID of the organization
                        if (tokenTenantId != organizationTenantId.Value)
                        {
                            return UserLinkAccountResult.LinkAccountTokenInvalid;
                        }

                        requestedTenantId = tokenTenantId;
                        requestedObjectId = tokenObjectId;

                        confirmTokenFound = true;
                        break;
                    }
                }

                if (!confirmTokenFound)
                {
                    return UserLinkAccountResult.LinkAccountTokenInvalid;
                }

                // Valid token is found. Link account and revoke all active tokens
                sql = @"
declare @_now datetime2(3) = sysutcdatetime()

declare @_userDisplayName nvarchar(151)

select @_userDisplayName = DisplayName
from tblUsers
where Uid = @uid

declare @_linkAccountTokensData table
(
    Uid uniqueidentifier
   ,AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

-- Remove all tokens for this user from the database
delete from tblLinkUserAccountAzureADConfirmTokens
output deleted.Uid
      ,deleted.AzureTenantId
      ,deleted.AzureObjectId
      ,deleted.ExpiryDateUtc
      ,deleted.Location
      ,deleted.BrowserName
      ,deleted.OSName
      ,deleted.DeviceInfo
      into @_linkAccountTokensData
where Uid = @uid

-- Log removed tokens
insert into tblLinkUserAccountAzureADConfirmTokens_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,AzureTenantId
,AzureObjectId
,ExpiryDateUtc
,Location
,BrowserName
,OSName
,DeviceInfo
,LogAction)
select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier)
      ,@_now
      ,@uid
      ,@_userDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@uid
      ,d.AzureTenantId
      ,d.AzureObjectId
      ,d.ExpiryDateUtc
      ,d.Location
      ,d.BrowserName
      ,d.OSName
      ,d.DeviceInfo
      ,'Delete' -- LogAction
from @_linkAccountTokensData d

-- Link Azure AD account
insert into tblUserAzureObjectId
(Uid
,AzureTenantId
,AzureObjectId
,InsertDateUtc)
select @uid
      ,@azureTenantId
      ,@azureObjectId
      ,@_now -- InsertDateUtc

-- Insert to log
insert into tblUserAzureObjectId_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,Uid
,AzureTenantId
,AzureObjectId
,LogAction)
select @logId
      ,@_now -- InsertDateUtc
      ,@uid
      ,@_userDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@uid
      ,@azureTenantId
      ,@azureObjectId
      ,'Insert' -- LogAction
";
                Guid logId = RT.Comb.Provider.Sql.Create();
                string logDescription = "Link Azure AD Account";

                parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureTenantId", requestedTenantId!.Value, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@azureObjectId", requestedObjectId!.Value, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                await sqlConnection.ExecuteAsync(sql, parameters);

                return UserLinkAccountResult.Ok;
            }
        }

        /// <summary>
        /// <para>Deletes the specified user.</para>
        /// <para>Returns: <see cref="SqlQueryResult.Ok"/>, <see cref="SqlQueryResult.RecordDidNotExist"/>, <see cref="SqlQueryResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, UserData?)> MasterDeleteUserAsync(MasterDeleteUserRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete User (Master)";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Get timezones for all buildings the user belongs to
                string sql = @"
select tblBuildings.id
      ,tblBuildings.Timezone
from tblUserBuildingJoin
inner join tblBuildings
on tblUserBuildingJoin.BuildingId = tblBuildings.id
where tblUserBuildingJoin.Uid = @uid
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);

                List<(Guid buildingId, string timezone)> buildingTimezones = (await sqlConnection.QueryAsync<(Guid, string)>(sql, parameters)).AsList();

                // Build SQL query to store all building timezone UtcOffsetMinutes in table variable
                StringBuilder timezoneSql = new StringBuilder(@"
declare @_buildingTimezones table
(
    BuildingId uniqueidentifier
   ,UtcOffsetMinutes int
   ,NowLocal datetime2(3)
   ,Last15MinuteIntervalLocal datetime2(3)
   ,EndOfTheWorldLocal datetime2(3)
)
");
                parameters = new DynamicParameters();

                if (buildingTimezones.Count > 0)
                {
                    timezoneSql.AppendLine(@"
insert into @_buildingTimezones
(BuildingId
,UtcOffsetMinutes
,NowLocal
,Last15MinuteIntervalLocal
,EndOfTheWorldLocal)
values");

                    for (int i = 0; i < buildingTimezones.Count; ++i)
                    {
                        if (i > 0)
                        {
                            timezoneSql.Append(',');
                        }

                        TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(buildingTimezones[i].timezone);
                        timezoneSql.AppendLine($"(@buildingId{i},@utcOffsetMinutes{i},null,null,null)");
                        parameters.Add($"@buildingId{i}", buildingTimezones[i].buildingId, DbType.Guid, ParameterDirection.Input); ;
                        parameters.Add($"@utcOffsetMinutes{i}", (int)timeZoneInfo.BaseUtcOffset.TotalMinutes, DbType.Int32, ParameterDirection.Input); ;
                    }

                    timezoneSql.AppendLine(@"
update @_buildingTimezones
set NowLocal = dateadd(minute, UtcOffsetMinutes, @_now)
   ,Last15MinuteIntervalLocal = dateadd(minute, datediff(minute, '2000-01-01', dateadd(minute, UtcOffsetMinutes, @_now)) / 15 * 15, '2000-01-01')
   ,EndOfTheWorldLocal = dateadd(minute, UtcOffsetMinutes, @endOfTheWorldUtc)
");
                }

                sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_nowPlus1 datetime2(3) = dateadd(millisecond, 1, sysutcdatetime())
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')

{timezoneSql}

declare @_userData table
(
    Email nvarchar(254)
   ,UserSystemRole int
   ,TotpEnabled bit
   ,DisplayName nvarchar(151)
   ,FirstName nvarchar(75)
   ,Surname nvarchar(75)
   ,Timezone varchar(50)
   ,AvatarUrl varchar(255)
   ,AvatarImageStorageId uniqueidentifier
   ,AvatarThumbnailUrl varchar(255)
   ,AvatarThumbnailStorageId uniqueidentifier
   ,Disabled bit
)

declare @_userAzureObjectIdData table
(
    AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
)

declare @_linkUserAccountAzureADConfirmTokensData table
(
    ConfirmToken varchar(205)
   ,AzureTenantId uniqueidentifier
   ,AzureObjectId uniqueidentifier
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

declare @_forgotPasswordTokensData table
(
    ForgotPasswordToken varchar(205)
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

declare @_disableTotpTokensData table
(
    DisableTotpToken varchar(205)
   ,ExpiryDateUtc datetime2(3)
   ,Location nvarchar(130)
   ,BrowserName nvarchar(40)
   ,OSName nvarchar(40)
   ,DeviceInfo nvarchar(50)
)

declare @_userOrganizationJoinData table
(
    OrganizationId uniqueidentifier
   ,UserOrganizationRole int
   ,Note nvarchar(500)
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
)

declare @_userOrganizationJoinHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,UserOrganizationRole int
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_userBuildingJoinData table
(
    OrganizationId uniqueidentifier
   ,BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
   ,FirstAidOfficer bit
   ,FireWarden bit
   ,PeerSupportOfficer bit
   ,AllowBookingDeskForVisitor bit
   ,AllowBookingRestrictedRooms bit
   ,AllowBookingAnyoneAnywhere bit
)

declare @_userBuildingJoinHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
   ,FirstAidOfficer bit
   ,FireWarden bit
   ,PeerSupportOfficer bit
   ,AllowBookingDeskForVisitor bit
   ,AllowBookingRestrictedRooms bit
   ,AllowBookingAnyoneAnywhere bit
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_userAssetTypesLogData table
(
    OrganizationId uniqueidentifier
   ,BuildingId uniqueidentifier
   ,AssetTypeId uniqueidentifier
)

declare @_userAdminFunctionsLogData table
(
    OrganizationId uniqueidentifier
   ,BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
)

declare @_userAdminAssetTypesLogData table
(
    OrganizationId uniqueidentifier
   ,BuildingId uniqueidentifier
   ,AssetTypeId uniqueidentifier
)

declare @_deskData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,XAxis float
   ,YAxis float
   ,OldDeskType int
   ,OldPermanentOwnerUid uniqueidentifier
)

declare @_deskHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,DeskId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,OldEndDateUtc datetime2(3)
   ,OldEndDateLocal datetime2(3)
)

declare @_newDeskHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,DeskId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
)

declare @_permanentDeskHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,DeskId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousPermanentDesksAvailabilityData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,DeskId uniqueidentifier
   ,AvailabilityCreatorUid uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
)

declare @_previousDeskBookingsData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,DeskId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousLocalMeetingRoomBookingsData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,MeetingRoomId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_assetSlotData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,PermanentOwnerUid uniqueidentifier
   ,XAxis float
   ,YAxis float
)

declare @_assetSlotHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,OldEndDateUtc datetime2(3)
   ,OldEndDateLocal datetime2(3)
)

declare @_newAssetSlotHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
)

declare @_previousAssetSlotBookingsData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousPermanentAssetSlotsData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,XAxis float
   ,YAxis float
)

declare @_previousPermanentAssetSlotsAvailabilityData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,AvailabilityCreatorUid uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
)

declare @_permanentAssetSlotHistoryData table
(
    id uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

update tblUsers
set UpdatedDateUtc = @_now
   ,Deleted = 1
output inserted.Email
      ,inserted.UserSystemRole
      ,inserted.TotpEnabled
      ,inserted.DisplayName
      ,inserted.FirstName
      ,inserted.Surname
      ,inserted.Timezone
      ,inserted.AvatarUrl
      ,inserted.AvatarImageStorageId
      ,inserted.AvatarThumbnailUrl
      ,inserted.AvatarThumbnailStorageId
      ,inserted.Disabled
      into @_userData
where Uid = @uid
and Deleted = 0
and ConcurrencyKey = @concurrencyKey

if @@ROWCOUNT = 1
begin
    set @_result = 1

    -- Insert to log
    insert into tblUsers_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,Email
    ,UserSystemRole
    ,TotpEnabled
    ,DisplayName
    ,FirstName
    ,Surname
    ,Timezone
    ,AvatarUrl
    ,AvatarImageStorageId
    ,AvatarThumbnailUrl
    ,AvatarThumbnailStorageId
    ,Disabled
    ,Deleted
    ,OldEmail
    ,OldUserSystemRole
    ,OldTotpEnabled
    ,OldDisplayName
    ,OldFirstName
    ,OldSurname
    ,OldTimezone
    ,OldAvatarUrl
    ,OldAvatarImageStorageId
    ,OldAvatarThumbnailUrl
    ,OldAvatarThumbnailStorageId
    ,OldDisabled
    ,OldDeleted
    ,PasswordChanged
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.Email
          ,d.UserSystemRole
          ,d.TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,d.Disabled
          ,1 -- Deleted
          ,d.Email
          ,d.UserSystemRole
          ,d.TotpEnabled
          ,d.DisplayName
          ,d.FirstName
          ,d.Surname
          ,d.Timezone
          ,d.AvatarUrl
          ,d.AvatarImageStorageId
          ,d.AvatarThumbnailUrl
          ,d.AvatarThumbnailStorageId
          ,d.Disabled
          ,0 -- OldDeleted
          ,0 -- PasswordChanged
          ,'Delete' -- LogAction
    from @_userData d

    -- Delete refresh tokens (no logging required)
    delete from tblRefreshTokens
    where Uid = @uid

    -- Delete linked Azure accounts
    delete from tblUserAzureObjectId
    output deleted.AzureTenantId
          ,deleted.AzureObjectId
          into @_userAzureObjectIdData
    where Uid = @uid

    -- Insert to tblUserAzureObjectId_Log
    ;with logIds as (
        select ids.AzureTenantId, ids.AzureObjectId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAzureObjectIdData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by AzureTenantId, AzureObjectId) as RowNumber, AzureTenantId, AzureObjectId
            from @_userAzureObjectIdData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAzureObjectId_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,AzureTenantId
    ,AzureObjectId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.AzureTenantId
          ,d.AzureObjectId
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAzureObjectIdData d
    left join logIds l
    on d.AzureTenantId = l.AzureTenantId
    and d.AzureObjectId = l.AzureObjectId

    -- Delete from tblLinkUserAccountAzureADConfirmTokens
    delete from tblLinkUserAccountAzureADConfirmTokens
    output deleted.ConfirmToken -- ConfirmToken not logged, just used to uniquify the row for generating log ids
          ,deleted.AzureTenantId
          ,deleted.AzureObjectId
          ,deleted.ExpiryDateUtc
          ,deleted.Location
          ,deleted.BrowserName
          ,deleted.OSName
          ,deleted.DeviceInfo
          into @_linkUserAccountAzureADConfirmTokensData
    where Uid = @uid

    -- Insert to tblLinkUserAccountAzureADConfirmTokens_Log
    ;with logIds as (
        select ids.ExpiryDateUtc, ids.ConfirmToken, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_linkUserAccountAzureADConfirmTokensData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by ExpiryDateUtc, ConfirmToken) as RowNumber, ExpiryDateUtc, ConfirmToken
            from @_linkUserAccountAzureADConfirmTokensData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblLinkUserAccountAzureADConfirmTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,AzureTenantId
    ,AzureObjectId
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.AzureTenantId
          ,d.AzureObjectId
          ,d.ExpiryDateUtc
          ,d.Location
          ,d.BrowserName
          ,d.OSName
          ,d.DeviceInfo
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_linkUserAccountAzureADConfirmTokensData d
    left join logIds l
    on d.ExpiryDateUtc = l.ExpiryDateUtc
    and d.ConfirmToken = l.ConfirmToken

    -- Delete from tblForgotPasswordTokens
    delete from tblForgotPasswordTokens
    output deleted.ForgotPasswordToken -- ForgotPasswordToken not logged, just used to uniquify the row for generating log ids
          ,deleted.ExpiryDateUtc
          ,deleted.Location
          ,deleted.BrowserName
          ,deleted.OSName
          ,deleted.DeviceInfo
          into @_forgotPasswordTokensData
    where Uid = @uid

    -- Insert to tblForgotPasswordTokens_Log
    ;with logIds as (
        select ids.ExpiryDateUtc, ids.ForgotPasswordToken, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_forgotPasswordTokensData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by ExpiryDateUtc, ForgotPasswordToken) as RowNumber, ExpiryDateUtc, ForgotPasswordToken
            from @_forgotPasswordTokensData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblForgotPasswordTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.ExpiryDateUtc
          ,d.Location
          ,d.BrowserName
          ,d.OSName
          ,d.DeviceInfo
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_forgotPasswordTokensData d
    left join logIds l
    on d.ExpiryDateUtc = l.ExpiryDateUtc
    and d.ForgotPasswordToken = l.ForgotPasswordToken

    -- Delete from tblDisableTotpTokens
    delete from tblDisableTotpTokens
    output deleted.DisableTotpToken -- DisableTotpToken not logged, just used to uniquify the row for generating log ids
          ,deleted.ExpiryDateUtc
          ,deleted.Location
          ,deleted.BrowserName
          ,deleted.OSName
          ,deleted.DeviceInfo
          into @_disableTotpTokensData
    where Uid = @uid

    -- Insert to tblDisableTotpTokens_Log
    ;with logIds as (
        select ids.ExpiryDateUtc, ids.DisableTotpToken, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_disableTotpTokensData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by ExpiryDateUtc, DisableTotpToken) as RowNumber, ExpiryDateUtc, DisableTotpToken
            from @_disableTotpTokensData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblDisableTotpTokens_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,ExpiryDateUtc
    ,Location
    ,BrowserName
    ,OSName
    ,DeviceInfo
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.ExpiryDateUtc
          ,d.Location
          ,d.BrowserName
          ,d.OSName
          ,d.DeviceInfo
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_disableTotpTokensData d
    left join logIds l
    on d.ExpiryDateUtc = l.ExpiryDateUtc
    and d.DisableTotpToken = l.DisableTotpToken

    -- Remove the user from all organizations
    delete from tblUserOrganizationJoin
    output deleted.OrganizationId
          ,deleted.UserOrganizationRole
          ,deleted.Note
          ,deleted.Contractor
          ,deleted.Visitor
          ,deleted.UserOrganizationDisabled
          into @_userOrganizationJoinData
    from tblUserOrganizationJoin
    where tblUserOrganizationJoin.Uid = @uid

    -- Insert to tblUserOrganizationJoin_Log
    ;with logIds as (
        select ids.OrganizationId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userOrganizationJoinData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by OrganizationId) as RowNumber, OrganizationId
            from @_userOrganizationJoinData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserOrganizationJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Note
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,OldUserOrganizationRole
    ,OldNote
    ,OldContractor
    ,OldVisitor
    ,OldUserOrganizationDisabled
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,d.OrganizationId
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userOrganizationJoinData d
    left join logIds l
    on d.OrganizationId = l.OrganizationId

    -- Update the old row in tblUserOrganizationJoinHistories with updated EndDateUtc
    update tblUserOrganizationJoinHistories
    set UpdatedDateUtc = @_now
       ,EndDateUtc = @_last15MinuteIntervalUtc
    output inserted.id -- UserOrganizationJoinHistoryId
          ,inserted.OrganizationId
          ,inserted.UserOrganizationRole
          ,inserted.Contractor
          ,inserted.Visitor
          ,inserted.UserOrganizationDisabled
          ,inserted.StartDateUtc
          ,inserted.EndDateUtc
          ,deleted.EndDateUtc
          into @_userOrganizationJoinHistoryData
    where Uid = @uid
    and EndDateUtc > @_last15MinuteIntervalUtc

    -- Insert to tblUserOrganizationJoinHistories_Log
    ;with logIds as (
        select ids.OrganizationId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userOrganizationJoinHistoryData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by OrganizationId) as RowNumber, OrganizationId
            from @_userOrganizationJoinHistoryData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserOrganizationJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,UserOrganizationJoinHistoryId
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,StartDateUtc
    ,EndDateUtc
    ,OldEndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,h.id -- UserOrganizationJoinHistoryId
          ,@uid
          ,h.OrganizationId
          ,h.UserOrganizationRole
          ,h.Contractor
          ,h.Visitor
          ,h.UserOrganizationDisabled
          ,h.StartDateUtc
          ,h.EndDateUtc
          ,h.OldEndDateUtc
          ,'Update' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userOrganizationJoinHistoryData h
    left join logIds l
    on h.OrganizationId = l.OrganizationId

    -- Remove buildings for the user in all organizations
    delete from tblUserBuildingJoin
    output tblBuildings.OrganizationId
          ,deleted.BuildingId
          ,deleted.FunctionId
          ,deleted.FirstAidOfficer
          ,deleted.FireWarden
          ,deleted.PeerSupportOfficer
          ,deleted.AllowBookingDeskForVisitor
          ,deleted.AllowBookingRestrictedRooms
          ,deleted.AllowBookingAnyoneAnywhere
          into @_userBuildingJoinData
    from tblUserBuildingJoin
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    where tblUserBuildingJoin.Uid = @uid

    -- Insert to tblUserBuildingJoin_Log
    ;with logIds as (
        select ids.BuildingId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userBuildingJoinData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by BuildingId) as RowNumber, BuildingId
            from @_userBuildingJoinData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserBuildingJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,FirstAidOfficer
    ,FireWarden
    ,PeerSupportOfficer
    ,AllowBookingDeskForVisitor
    ,AllowBookingRestrictedRooms
    ,AllowBookingAnyoneAnywhere
    ,OldFunctionId
    ,OldFirstAidOfficer
    ,OldFireWarden
    ,OldPeerSupportOfficer
    ,OldAllowBookingDeskForVisitor
    ,OldAllowBookingRestrictedRooms
    ,OldAllowBookingAnyoneAnywhere
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,@uid
          ,d.BuildingId
          ,d.FunctionId
          ,d.FirstAidOfficer
          ,d.FireWarden
          ,d.PeerSupportOfficer
          ,d.AllowBookingDeskForVisitor
          ,d.AllowBookingRestrictedRooms
          ,d.AllowBookingAnyoneAnywhere
          ,d.FunctionId
          ,d.FirstAidOfficer
          ,d.FireWarden
          ,d.PeerSupportOfficer
          ,d.AllowBookingDeskForVisitor
          ,d.AllowBookingRestrictedRooms
          ,d.AllowBookingAnyoneAnywhere
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userBuildingJoinData d
    left join logIds l
    on d.BuildingId = l.BuildingId

    -- Update the old row in tblUserBuildingJoinHistories with updated EndDateUtc
    update tblUserBuildingJoinHistories
    set UpdatedDateUtc = @_now
       ,EndDateUtc = @_last15MinuteIntervalUtc
    output inserted.id -- UserBuildingJoinHistoryId
          ,tblBuildings.OrganizationId
          ,inserted.BuildingId
          ,inserted.FunctionId
          ,inserted.FirstAidOfficer
          ,inserted.FireWarden
          ,inserted.PeerSupportOfficer
          ,inserted.AllowBookingDeskForVisitor
          ,inserted.AllowBookingRestrictedRooms
          ,inserted.AllowBookingAnyoneAnywhere
          ,inserted.StartDateUtc
          ,inserted.EndDateUtc
          ,deleted.EndDateUtc
          into @_userBuildingJoinHistoryData
    from tblUserBuildingJoinHistories
    inner join tblBuildings
    on tblUserBuildingJoinHistories.BuildingId = tblBuildings.id
    where tblUserBuildingJoinHistories.Uid = @uid
    and tblUserBuildingJoinHistories.EndDateUtc > @_last15MinuteIntervalUtc

    -- Insert to tblUserBuildingJoinHistories_Log
    ;with logIds as (
        select ids.BuildingId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userBuildingJoinHistoryData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by BuildingId) as RowNumber, BuildingId
            from @_userBuildingJoinHistoryData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserBuildingJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,UserBuildingJoinHistoryId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,FirstAidOfficer
    ,FireWarden
    ,PeerSupportOfficer
    ,AllowBookingDeskForVisitor
    ,AllowBookingRestrictedRooms
    ,AllowBookingAnyoneAnywhere
    ,StartDateUtc
    ,EndDateUtc
    ,OldEndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,h.OrganizationId
          ,h.id -- UserBuildingJoinHistoryId
          ,@uid
          ,h.BuildingId
          ,h.FunctionId
          ,h.FirstAidOfficer
          ,h.FireWarden
          ,h.PeerSupportOfficer
          ,h.AllowBookingDeskForVisitor
          ,h.AllowBookingRestrictedRooms
          ,h.AllowBookingAnyoneAnywhere
          ,h.StartDateUtc
          ,h.EndDateUtc
          ,h.OldEndDateUtc
          ,'Update' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userBuildingJoinHistoryData h
    left join logIds l
    on h.BuildingId = l.BuildingId

    -- Remove user asset types for the user for all buildings in all organizations
    delete from tblUserAssetTypeJoin
    output tblBuildings.OrganizationId
          ,deleted.BuildingId
          ,deleted.AssetTypeId
          into @_userAssetTypesLogData
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    where tblUserAssetTypeJoin.Uid = @uid

    -- Insert to tblUserAssetTypeJoin_Log
    ;with logIds as (
        select ids.AssetTypeId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAssetTypesLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
            from @_userAssetTypesLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAssetTypeJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,AssetTypeId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,@uid
          ,d.BuildingId
          ,d.AssetTypeId
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Remove user admin functions for the user for all buildings in all organizations
    delete from tblUserAdminFunctions
    output tblBuildings.OrganizationId
          ,deleted.BuildingId
          ,deleted.FunctionId
          into @_userAdminFunctionsLogData
    from tblUserAdminFunctions
    inner join tblFunctions
    on tblUserAdminFunctions.FunctionId = tblFunctions.id
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    where tblUserAdminFunctions.Uid = @uid

    -- Insert to tblUserAdminFunctions_Log
    ;with logIds as (
        select ids.FunctionId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAdminFunctionsLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by FunctionId) as RowNumber, FunctionId
            from @_userAdminFunctionsLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAdminFunctions_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,@uid
          ,d.BuildingId
          ,d.FunctionId
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminFunctionsLogData d
    left join logIds l
    on d.FunctionId = l.FunctionId

    -- Remove user admin asset types for the user for all buildings in all organizations
    delete from tblUserAdminAssetTypes
    output tblBuildings.OrganizationId
          ,deleted.BuildingId
          ,deleted.AssetTypeId
          into @_userAdminAssetTypesLogData
    from tblUserAdminAssetTypes
    inner join tblAssetTypes
    on tblUserAdminAssetTypes.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    where tblUserAdminAssetTypes.Uid = @uid

    -- Insert to tblUserAdminAssetTypes_Log
    ;with logIds as (
        select ids.AssetTypeId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAdminAssetTypesLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
            from @_userAdminAssetTypesLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAdminAssetTypes_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,AssetTypeId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,@uid
          ,d.BuildingId
          ,d.AssetTypeId
          ,'Delete' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Revoke the user's permanent desk in all organizations
    update tblDesks
    set UpdatedDateUtc = @_now
       ,DeskType = {(int)DeskType.Flexi}
       ,PermanentOwnerUid = null
    output inserted.id -- DeskId
          ,tblBuildings.OrganizationId
          ,inserted.Name
          ,inserted.FloorId
          ,inserted.DeskType
          ,inserted.FunctionType
          ,inserted.FunctionId
          ,inserted.PermanentOwnerUid
          ,inserted.XAxis
          ,inserted.YAxis
          ,deleted.DeskType
          ,deleted.PermanentOwnerUid
          into @_deskData
    from tblDesks
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblDesks.Deleted = 0
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid

    -- If user had any permanent desks in any organization, more steps to be taken
    if @@ROWCOUNT > 0
    begin
        -- Insert to desks log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_deskData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDesks_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,PermanentOwnerUid
        ,XAxis
        ,YAxis
        ,Deleted
        ,OldName
        ,OldDeskType
        ,OldFunctionType
        ,OldFunctionId
        ,OldPermanentOwnerUid
        ,OldXAxis
        ,OldYAxis
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- DeskId
              ,d.Name
              ,d.FloorId
              ,d.DeskType
              ,d.FunctionType
              ,d.FunctionId
              ,d.PermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- Deleted
              ,d.Name
              ,d.OldDeskType
              ,d.FunctionType
              ,d.FunctionId
              ,d.OldPermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- OldDeleted
              ,'Delete' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_deskData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblDeskHistories with updated EndDateUtc
        update tblDeskHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = tz.Last15MinuteIntervalLocal
        output inserted.id -- DeskHistoryId
              ,tblBuildings.OrganizationId
              ,inserted.DeskId
              ,inserted.Name
              ,inserted.FloorId
              ,inserted.DeskType
              ,inserted.FunctionType
              ,inserted.FunctionId
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,deleted.EndDateUtc
              ,deleted.EndDateLocal
              into @_deskHistoryData
        from tblDeskHistories
        inner join @_deskData d
        on tblDeskHistories.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblDeskHistories.EndDateUtc > @_last15MinuteIntervalUtc

        -- Insert to log for the old row in tblDeskHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_deskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskHistoryId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,OldEndDateUtc
        ,OldEndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,h.OrganizationId
              ,h.id -- DeskHistoryId
              ,h.DeskId
              ,h.Name
              ,h.FloorId
              ,h.DeskType
              ,h.FunctionType
              ,h.FunctionId
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,h.OldEndDateUtc
              ,h.OldEndDateLocal
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_deskHistoryData h
        left join logIds l
        on h.id = l.id

        -- Insert a new row into tblDeskHistories for the desk we just updated,
        -- using the last 15 minute interval for StartDateUtc and StartDateLocal
        ;with generatedIds as (
            select ids.id, combs.GeneratedId
            from
            (
                select ROW_NUMBER() over (order by GeneratedId) as RowNumber, GeneratedId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as GeneratedId
                    from @_deskData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,OrganizationId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal)
        output inserted.id
              ,inserted.OrganizationId
              ,inserted.DeskId
              ,inserted.Name
              ,inserted.FloorId
              ,inserted.DeskType
              ,inserted.FunctionType
              ,inserted.FunctionId
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              into @_newDeskHistoryData
        select l.GeneratedId
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,d.OrganizationId
              ,d.id -- DeskId
              ,d.Name
              ,d.FloorId
              ,d.DeskType
              ,d.FunctionType
              ,d.FunctionId
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,tz.Last15MinuteIntervalLocal -- StartDateLocal
              ,tz.EndOfTheWorldLocal -- EndDateLocal
        from @_deskData d
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        left join generatedIds l
        on d.id = l.id

        -- Write to log for the desk history for the new row
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_nowPlus1) as binary(6)) as uniqueidentifier) as LogId
                    from @_newDeskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_newDeskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskHistoryId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,h.OrganizationId
              ,h.id -- DeskHistoryId
              ,h.DeskId -- DeskId
              ,h.Name
              ,h.FloorId
              ,h.DeskType
              ,h.FunctionType
              ,h.FunctionId
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,'Insert' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newDeskHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent desk availabilities, as they are no longer needed now that the
        -- desk has been changed to flexi.
        update tblPermanentDeskAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = tz.NowLocal
           ,Cancelled = 1
        output inserted.id
              ,tblBuildings.OrganizationId
              ,inserted.DeskId
              ,inserted.AvailabilityCreatorUid
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,inserted.CancelledDateUtc
              ,inserted.CancelledDateLocal
              into @_previousPermanentDesksAvailabilityData
        from tblPermanentDeskAvailabilities
        inner join @_deskData d
        on tblPermanentDeskAvailabilities.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentDeskAvailabilities.Deleted = 0
        and tblPermanentDeskAvailabilities.Cancelled = 0
        and tblPermanentDeskAvailabilities.EndDateUtc > @_last15MinuteIntervalUtc -- Only update availabilities that are current or in the future

        -- Insert cancelled permanent desk availabilities for previous permanent desks into log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_previousPermanentDesksAvailabilityData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_previousPermanentDesksAvailabilityData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentDeskAvailabilities_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentDeskAvailabilityId
        ,DeskId
        ,AvailabilityCreatorUid
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,CancelledByUid
        ,CancelledDateUtc
        ,CancelledDateLocal
        ,Cancelled
        ,Deleted
        ,OldCancelled
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- PermanentDeskAvailabilityId
              ,d.DeskId
              ,d.AvailabilityCreatorUid
              ,d.StartDateUtc
              ,d.EndDateUtc
              ,d.StartDateLocal
              ,d.EndDateLocal
              ,@adminUserUid -- CancelledByUid
              ,d.CancelledDateUtc
              ,d.CancelledDateLocal
              ,1 -- Cancelled
              ,0 -- Deleted
              ,0 -- OldCancelled
              ,0 -- OldDeleted
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentDesksAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentDeskHistories (used for reporting/dashboard only)
        -- for this desk by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentDeskHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = tz.Last15MinuteIntervalLocal
        output inserted.id
              ,tblBuildings.OrganizationId
              ,inserted.DeskId
              ,inserted.PermanentOwnerUid
              ,inserted.BookingStartUtc
              ,inserted.BookingEndUtc
              ,inserted.BookingStartLocal
              ,inserted.BookingEndLocal
              ,deleted.BookingEndUtc
              ,deleted.BookingEndLocal
              into @_permanentDeskHistoryData
        from tblPermanentDeskHistories
        inner join @_deskData d
        on tblPermanentDeskHistories.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentDeskHistories.BookingEndUtc > @_last15MinuteIntervalUtc

        -- Insert to log for updates made to tblPermanentDeskHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_permanentDeskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_permanentDeskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentDeskHistoryId
        ,DeskId
        ,PermanentOwnerUid
        ,BookingStartUtc
        ,BookingEndUtc
        ,BookingStartLocal
        ,BookingEndLocal
        ,OldBookingEndUtc
        ,OldBookingEndLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- PermanentDeskHistoryId
              ,d.DeskId
              ,d.PermanentOwnerUid
              ,d.BookingStartUtc
              ,d.BookingEndUtc
              ,d.BookingStartLocal
              ,d.BookingEndLocal
              ,d.OldBookingEndUtc
              ,d.OldBookingEndLocal
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentDeskHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current desk bookings where the user is the booking owner in all organizations
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.DeskId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousDeskBookingsData
    from tblDeskBookings
    inner join tblDesks
    on tblDeskBookings.DeskId = tblDesks.id
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblDeskBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblDeskBookings.BookingStartUtc and @_now < tblDeskBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0

    -- Cancel future desk bookings where the user is the booking owner in all organizations
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.DeskId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousDeskBookingsData
    from tblDeskBookings
    inner join tblDesks
    on tblDeskBookings.DeskId = tblDesks.id
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblDeskBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblDeskBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in all organizations
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousDeskBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousDeskBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblDeskBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,DeskBookingId
    ,DeskId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,d.id -- DeskBookingId
          ,d.DeskId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousDeskBookingsData d
    left join logIds l
    on d.id = l.id

    -- Truncate current local meeting room bookings where the user is the booking owner in all organizations
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.MeetingRoomId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousLocalMeetingRoomBookingsData
    from tblMeetingRoomBookings
    inner join tblMeetingRooms
    on tblMeetingRoomBookings.MeetingRoomId = tblMeetingRooms.id
    inner join tblFloors
    on tblMeetingRooms.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblMeetingRoomBookings.BookingStartUtc and @_now < tblMeetingRoomBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0

    -- Cancel future local meeting room bookings where the user is the booking owner in all organizations
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.MeetingRoomId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousLocalMeetingRoomBookingsData
    from tblMeetingRoomBookings
    inner join tblMeetingRooms
    on tblMeetingRoomBookings.MeetingRoomId = tblMeetingRooms.id
    inner join tblFloors
    on tblMeetingRooms.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblMeetingRoomBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in all organizations
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousLocalMeetingRoomBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousLocalMeetingRoomBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblMeetingRoomBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,MeetingRoomBookingId
    ,MeetingRoomId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,d.id -- MeetingRoomBookingId
          ,d.MeetingRoomId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousLocalMeetingRoomBookingsData d
    left join logIds l
    on d.id = l.id

    -- Revoke the user's permanent asset slots if they have any in all organizations
    update tblAssetSlots
    set UpdatedDateUtc = @_now
       ,AssetSlotType = {(int)AssetSlotType.Flexi}
       ,PermanentOwnerUid = null
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.Name
          ,inserted.AssetSectionId
          ,inserted.AssetSlotType
          ,inserted.PermanentOwnerUid
          ,inserted.XAxis
          ,inserted.YAxis
          into @_assetSlotData
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid

    -- If user had a permanent asset slot in any organizations, more steps to be taken
    if @@ROWCOUNT > 0
    begin
        -- Insert to asset slots log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_assetSlotData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlots_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,PermanentOwnerUid
        ,XAxis
        ,YAxis
        ,Deleted
        ,OldName
        ,OldXAxis
        ,OldYAxis
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- AssetSlotId
              ,d.Name
              ,d.AssetSectionId
              ,d.AssetSlotType
              ,d.PermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- Deleted
              ,d.Name
              ,d.XAxis
              ,d.YAxis
              ,0 -- OldDeleted
              ,'Delete' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_assetSlotData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblAssetSlotHistories with updated EndDateUtc
        update tblAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = tz.Last15MinuteIntervalLocal
        output inserted.id -- AssetSlotHistoryId
              ,tblBuildings.OrganizationId
              ,inserted.AssetSlotId
              ,inserted.Name
              ,inserted.AssetSectionId
              ,inserted.AssetSlotType
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,deleted.EndDateUtc
              ,deleted.EndDateLocal
              into @_assetSlotHistoryData
        from tblAssetSlotHistories
        inner join @_assetSlotData d
        on tblAssetSlotHistories.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblAssetSlotHistories.EndDateUtc > @_last15MinuteIntervalUtc

        -- Insert to log for the old row in tblAssetSlotHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_assetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotHistoryId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,OldEndDateUtc
        ,OldEndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,h.OrganizationId
              ,h.id -- AssetSlotHistoryId
              ,h.AssetSlotId
              ,h.Name
              ,h.AssetSectionId
              ,h.AssetSlotType
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,h.OldEndDateUtc
              ,h.OldEndDateLocal
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_assetSlotHistoryData h
        left join logIds l
        on h.id = l.id

        -- Insert a new row into tblAssetSlotHistories for the asset slot we just updated,
        -- using the last 15 minute interval for StartDateUtc and StartDateLocal
        ;with generatedIds as (
            select ids.id, combs.GeneratedId
            from
            (
                select ROW_NUMBER() over (order by GeneratedId) as RowNumber, GeneratedId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as GeneratedId
                    from @_assetSlotData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,OrganizationId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal)
        output inserted.id
              ,inserted.OrganizationId
              ,inserted.AssetSlotId
              ,inserted.Name
              ,inserted.AssetSectionId
              ,inserted.AssetSlotType
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              into @_newAssetSlotHistoryData
        select l.GeneratedId
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,d.OrganizationId
              ,d.id -- AssetSlotId
              ,d.Name
              ,d.AssetSectionId
              ,d.AssetSlotType
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,tz.Last15MinuteIntervalLocal -- StartDateLocal
              ,tz.EndOfTheWorldLocal -- EndDateLocal
        from @_assetSlotData d
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        left join generatedIds l
        on d.id = l.id

        -- Write to log for the asset slot history for the new row
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_nowPlus1) as binary(6)) as uniqueidentifier) as LogId
                    from @_newAssetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_newAssetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotHistoryId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,h.OrganizationId
              ,h.id -- AssetSlotHistoryId
              ,h.AssetSlotId
              ,h.Name
              ,h.AssetSectionId
              ,h.AssetSlotType
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,'Insert' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newAssetSlotHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent asset slot availabilities, as they are no longer needed now that the
        -- asset slots have been changed to flexi.
        update tblPermanentAssetSlotAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = tz.NowLocal
           ,Cancelled = 1
        output inserted.id
              ,tblBuildings.OrganizationId
              ,inserted.AssetSlotId
              ,inserted.AvailabilityCreatorUid
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,inserted.CancelledDateUtc
              ,inserted.CancelledDateLocal
              into @_previousPermanentAssetSlotsAvailabilityData
        from tblPermanentAssetSlotAvailabilities
        inner join @_assetSlotData d
        on tblPermanentAssetSlotAvailabilities.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentAssetSlotAvailabilities.Deleted = 0
        and tblPermanentAssetSlotAvailabilities.Cancelled = 0
        and tblPermanentAssetSlotAvailabilities.EndDateUtc > @_last15MinuteIntervalUtc -- Only update availabilities that are current or in the future

        -- Insert cancelled permanent asset slot availabilities into log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_previousPermanentAssetSlotsAvailabilityData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_previousPermanentAssetSlotsAvailabilityData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentAssetSlotAvailabilities_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentAssetSlotAvailabilityId
        ,AssetSlotId
        ,AvailabilityCreatorUid
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,CancelledByUid
        ,CancelledDateUtc
        ,CancelledDateLocal
        ,Cancelled
        ,Deleted
        ,OldCancelled
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- PermanentAssetSlotAvailabilityId
              ,d.AssetSlotId
              ,d.AvailabilityCreatorUid
              ,d.StartDateUtc
              ,d.EndDateUtc
              ,d.StartDateLocal
              ,d.EndDateLocal
              ,@adminUserUid -- CancelledByUid
              ,d.CancelledDateUtc
              ,d.CancelledDateLocal
              ,1 -- Cancelled
              ,0 -- Deleted
              ,0 -- OldCancelled
              ,0 -- OldDeleted
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentAssetSlotsAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentAssetSlotHistories (used for reporting/dashboard only)
        -- for this asset slot by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = tz.Last15MinuteIntervalLocal
        output inserted.id
              ,tblBuildings.OrganizationId
              ,inserted.AssetSlotId
              ,inserted.PermanentOwnerUid
              ,inserted.BookingStartUtc
              ,inserted.BookingEndUtc
              ,inserted.BookingStartLocal
              ,inserted.BookingEndLocal
              ,deleted.BookingEndUtc
              ,deleted.BookingEndLocal
              into @_permanentAssetSlotHistoryData
        from tblPermanentAssetSlotHistories
        inner join @_assetSlotData d
        on tblPermanentAssetSlotHistories.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentAssetSlotHistories.BookingEndUtc > @_last15MinuteIntervalUtc

        -- Insert to log for updates made to tblPermanentAssetSlotHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_permanentAssetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_permanentAssetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentAssetSlotHistoryId
        ,AssetSlotId
        ,PermanentOwnerUid
        ,BookingStartUtc
        ,BookingEndUtc
        ,BookingStartLocal
        ,BookingEndLocal
        ,OldBookingEndUtc
        ,OldBookingEndLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.OrganizationId
              ,d.id -- PermanentAssetSlotHistoryId
              ,d.AssetSlotId
              ,d.PermanentOwnerUid
              ,d.BookingStartUtc
              ,d.BookingEndUtc
              ,d.BookingStartLocal
              ,d.BookingEndLocal
              ,d.OldBookingEndUtc
              ,d.OldBookingEndLocal
              ,'Update' -- LogAction
              ,'tblUsers' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentAssetSlotHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current asset slot bookings where the user is the booking owner in all organizations
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.AssetSlotId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousAssetSlotBookingsData
    from tblAssetSlotBookings
    inner join tblAssetSlots
    on tblAssetSlotBookings.AssetSlotId = tblAssetSlots.id
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblAssetSlotBookings.BookingStartUtc and @_now < tblAssetSlotBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0

    -- Cancel future asset slot bookings where the user is the booking owner in all organizations
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,tblBuildings.OrganizationId
          ,inserted.AssetSlotId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousAssetSlotBookingsData
    from tblAssetSlotBookings
    inner join tblAssetSlots
    on tblAssetSlotBookings.AssetSlotId = tblAssetSlots.id
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblAssetSlotBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in all organizations
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousAssetSlotBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousAssetSlotBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblAssetSlotBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,AssetSlotBookingId
    ,AssetSlotId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,d.OrganizationId
          ,d.id -- AssetSlotBookingId
          ,d.AssetSlotId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUsers' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousAssetSlotBookingsData d
    left join logIds l
    on d.id = l.id
end
else
begin
    -- User either did not exist, did not belong to organization, or ConcurrencyKey was invalid
    set @_result = 2
end

select @_result

if @_result != 1
begin
    -- Select row to return with the API result
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        order by tblOrganizations.Name

        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query the user's permanent seat
        select tblDesks.id as DeskId
              ,tblBuildings.id as BuildingId
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType = {(int)DeskType.Permanent}
        and tblDesks.PermanentOwnerUid = @uid

        -- Also query the user's asset types
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblUserAssetTypeJoin
        inner join tblAssetTypes
        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblUserAssetTypeJoin.Uid = @uid

        -- Also query the user's permanent assets
        select tblAssetSlots.id as AssetSlotId
              ,tblAssetSections.AssetTypeId
              ,tblBuildings.id as BuildingId
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
        and tblAssetSlots.PermanentOwnerUid = @uid

        -- Also query the user's admin functions if the user is an Admin,
        -- or all functions if they are a Super Admin.
        select tblFunctions.id
              ,tblFunctions.Name
              ,tblFunctions.BuildingId
        from tblFunctions
        where tblFunctions.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminFunctions
            on tblFunctions.id = tblUserAdminFunctions.FunctionId
            and tblUserAdminFunctions.Uid = @uid
            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminFunctions.FunctionId is not null
                )
            )
        )

        -- Also query the user's admin asset types if the user is an Admin,
        -- or all asset types if they are a Super Admin.
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblAssetTypes
        where tblAssetTypes.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminAssetTypes
            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
            and tblUserAdminAssetTypes.Uid = @uid
            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminAssetTypes.AssetTypeId is not null
                )
            )
        )
    end
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = null;

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
                            userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                            // If insert was successful, also get the data
                            if (!gridReader.IsConsumed)
                            {
                                if (!gridReader.IsConsumed && userData is not null)
                                {
                                    // Read extended data
                                    userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                                    userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                                    List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                                    List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                                    List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                                    List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                                    List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                                    List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                                    List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                                    List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                                    List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                                    FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                                }
                            }
                        }

                        if (userData is null)
                        {
                            // User did not exist
                            queryResult = SqlQueryResult.RecordDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(userData.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // User exists but concurrency key was invalid
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

                return (queryResult, userData);
            }
        }

    }
}
