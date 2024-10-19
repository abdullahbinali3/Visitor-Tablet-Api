using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletBuildingsRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletBuildingsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Retrieves a list of buildings that the logged in user has access to, to be used for displaying a list.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<(SqlQueryResult, List<SelectListItemGuid>?)> ListBuildingsAsync(Guid organizationId, Guid adminUserUid, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_validOrganization bit = 0
declare @_result int = 0

select @_validOrganization = 1
from tblOrganizations
inner join tblUserOrganizationJoin
on tblOrganizations.id = tblUserOrganizationJoin.OrganizationId
and tblUserOrganizationJoin.Uid = @adminUserUid
where tblOrganizations.id = @organizationId
and tblOrganizations.Deleted = 0

select @_validOrganization

if @_validOrganization = 1
begin
    -- Select buildings in the organization which the logged in user has access to
    select tblBuildings.id as value
          ,tblBuildings.Name as text
    from tblBuildings
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    inner join tblUserBuildingJoin
    on tblUserBuildingJoin.Uid = @adminUserUid
    and tblBuildings.id = tblUserBuildingJoin.BuildingId
    where tblBuildings.OrganizationId = @organizationId
    and tblBuildings.Deleted = 0
    order by tblBuildings.Name

    if @@ROWCOUNT > 0
    begin
        set @_result = 1
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
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using GridReader reader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                bool organizationValid = await reader.ReadFirstOrDefaultAsync<bool>();
                List<SelectListItemGuid>? data = null;

                if (organizationValid)
                {
                    data = (await reader.ReadAsync<SelectListItemGuid>()).AsList();
                }
                
                int resultCode = await reader.ReadFirstOrDefaultAsync<int>();

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

                return (queryResult, data);
            }
        }
    }
}
