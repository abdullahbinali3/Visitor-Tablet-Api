using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using VisitorTabletAPITemplate.VisitorTablet.Models;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public class UserRepository
    {
        private readonly AppSettings _appSettings;

        public UserRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
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

        public async Task<IEnumerable<Visitor>> GetVisitorsAsync(Guid hostUid, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
                select u.FirstName, u.Surname, u.Uid
                from dbo.tblWorkplaceVisitUserJoin u
                join dbo.tblWorkplaceVisits w on u.WorkplaceVisitId = w.id
                where w.hostUid = @hostUid";

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@hostUid", hostUid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryAsync<Visitor>(commandDefinition);
            }
        }

    }
}
