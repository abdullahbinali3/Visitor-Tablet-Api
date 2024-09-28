using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.VisitorTablet.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Models;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletExamplesRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletExamplesRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// <para>Retrieves the specified example from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<Example?> GetExampleAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default)
        {
            /*
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.MyConnectionString))
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
            */

            return new Example
            {
                id = id,
                OrganizationId = organizationId,
                ExampleType = ExampleType.Example,
            };
        }
    }
}
