using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.VisitorTablet.Models;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public class GetVisitorsRepository
    {
        private readonly AppSettings _appSettings;

        public GetVisitorsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        public async Task<IEnumerable<VisitorDto>> GetVisitorsAsync(Guid hostUid, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
                select u.FirstName, u.Surname, u.WorkplaceVisitId
                from dbo.tblWorkplaceVisitUserJoin u
                join dbo.tblWorkplaceVisits w on u.WorkplaceVisitId = w.id
                where w.hostUid = @hostUid";

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@hostUid", hostUid, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryAsync<VisitorDto>(commandDefinition);
            }
        }
    }
}