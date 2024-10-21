using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignOut;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletVisitorRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletVisitorRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Handles cancelling or truncating a visit based on the current time.
        /// If cancelled before the visit starts, the visit is cancelled.
        /// If the visit is in progress, it is truncated (ends early).
        /// If the visit has already ended, it is marked as cancelled.
        /// </summary>
        /// <param name="visitId">The ID of the workplace visit.</param>
        /// <param name="currentTimeUtc">The current time in UTC.</param>
        /// <param name="currentTimeLocal">The current time in the local timezone.</param>
        /// <returns>Returns a SqlQueryResult indicating success or failure.</returns>
        public async Task<SqlQueryResult> CancelOrTruncateVisitAsync(SignOutRequest req, Guid uid)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                await sqlConnection.OpenAsync();

                // First, check if the record exists
                var checkQuery = @"
            SELECT Id
            FROM [dbo].[tblWorkplaceVisits]
            WHERE HostUid = @HostUid
            ORDER BY InsertDateUtc DESC";
                //WHERE [WorkplaceVisitId] = @WorkplaceVisitId AND [Uid] = @Uid";


                var workplaceVisitIds = (await sqlConnection.QueryAsync<Guid>(checkQuery, new { HostUid = req.HostUid })).ToList();

                //// If record doesn't exist, return appropriate result
                //if (exists == 0)
                //{
                //    return SqlQueryResult.RecordDidNotExist;
                //}

                // If record exists, update the SignInDateUtc
                foreach (var workplaceVisitId in workplaceVisitIds)
                {

                    var updateQuery = @"
UPDATE [dbo].[tblWorkplaceVisitUserJoin]
SET [SignOutDateUtc] = @SignOutDateUtc,
    [SignOutDateLocal] = @SignOutDateLocal
WHERE [Uid] = @Uid
AND [Uid] IN 
(
    SELECT u.Uid
    FROM dbo.tblWorkplaceVisitUserJoin u 
    JOIN dbo.tblWorkplaceVisits w ON u.WorkplaceVisitId = w.id
    WHERE w.HostUid = @HostUid
)";



                    var signParameters = new DynamicParameters();
                    signParameters.Add("@SignOutDateLocal", req.SignOutDate, DbType.DateTime2); // No length needed
                    signParameters.Add("@SignOutDateUtc", req.SignOutDate.ToUniversalTime(), DbType.DateTime2); // No length needed
                    signParameters.Add("@HostUid", req.HostUid, DbType.Guid); // Adjust as necessary
                    signParameters.Add("@Uid", uid, DbType.Guid); // Adjust as necessary

                    await sqlConnection.ExecuteAsync(updateQuery, signParameters);

                }

                return SqlQueryResult.Ok;


            }
        }
    }
}
