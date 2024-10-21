using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignIn;
using VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignOut;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletWorkplaceVisitUserJoinRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletWorkplaceVisitUserJoinRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Attempts to sign in a user by updating the SignInDateUtc for a specific Host and user.
        /// First, it checks if a record exists with the given HostUid and Uid. 
        /// If the record exists, it updates the SignInDateUtc; otherwise, it returns an error result.
        /// </summary>
        /// <param name="HostUid">The ID of the Host.</param>
        /// <param name="uid">The unique identifier of the user.</param>
        /// <param name="signInDateUtc">The sign-in date and time in UTC.</param>
        /// <returns>Returns a SqlQueryResult indicating success, record not found, or an unknown error.</returns>

        public async Task<SqlQueryResult> SignInAsync(Guid uid, UpdateWorkplaceVisitUserSignInRequest req)
        {

            // Use connection string from app settings
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                await sqlConnection.OpenAsync();

                // First, check if the record exists
                var checkQuery = @"
                SELECT Id
                FROM [dbo].[tblWorkplaceVisits]
                WHERE HostUid = @HostUid 
                ORDER BY InsertDateUtc DESC";

                var workplaceVisitIds = (await sqlConnection.QueryAsync<Guid>(checkQuery, new { HostUid = req.HostUid })).ToList();

                // If record exists, update the SignInDateUtc
                if(workplaceVisitIds.Any())
                {
                    var updateQuery = @"
                        UPDATE [dbo].[tblWorkplaceVisitUserJoin]
                        SET [SignInDateUtc] = @SignInDateUtc,
                            [SignInDateLocal] = @SignInDateLocal
                        WHERE [Uid] = @Uid
                        AND [Uid] IN 
                        (
                            SELECT u.Uid
                            FROM dbo.tblWorkplaceVisitUserJoin u 
                            JOIN dbo.tblWorkplaceVisits w ON u.WorkplaceVisitId = w.id
                            WHERE w.HostUid = @HostUid
                        )";

                    var signParameters = new DynamicParameters();
                    signParameters.Add("@SignInDateLocal", req.SignInDate, DbType.DateTime2); 
                    signParameters.Add("@SignInDateUtc", req.SignInDate.ToUniversalTime(), DbType.DateTime2); 
                    signParameters.Add("@HostUid", req.HostUid, DbType.Guid); 
                    signParameters.Add("@Uid", uid, DbType.Guid); 

                   int result = await sqlConnection.ExecuteAsync(updateQuery, signParameters);
                    if(result > 0)
                    {
                        return SqlQueryResult.Ok;
                    }
                }
                return SqlQueryResult.RecordDidNotExist;
            }
        }

        /// <summary>
        /// Attempts to sign out a user by updating the SignOutDateUtc for a specific Host and user.
        /// First, it checks if a record exists with the given HostUid and Uid. 
        /// If the record exists, it updates the SignOutDateUtc; otherwise, it returns an error result.
        /// </summary>
        /// <param name="HostUid">The ID of the Host.</param>
        /// <param name="uid">The unique identifier of the user.</param>
        /// <param name="signOutDateUtc">The sign-in date and time in UTC.</param>
        /// <returns>Returns a SqlQueryResult indicating success or failure.</returns>
        public async Task<SqlQueryResult> SignOutAsync(UpdateWorkplaceVisitUserSignOutRequest req, Guid uid)
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

                var workplaceVisitIds = (await sqlConnection.QueryAsync<Guid>(checkQuery, new { HostUid = req.HostUid })).ToList();

                if(workplaceVisitIds.Any())
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
                    signParameters.Add("@SignOutDateLocal", req.SignOutDate, DbType.DateTime2);
                    signParameters.Add("@SignOutDateUtc", req.SignOutDate.ToUniversalTime(), DbType.DateTime2);
                    signParameters.Add("@HostUid", req.HostUid, DbType.Guid);
                    signParameters.Add("@Uid", uid, DbType.Guid);

                    int result = await sqlConnection.ExecuteAsync(updateQuery, signParameters);
                    if (result > 0)
                    {
                        return SqlQueryResult.Ok;
                    }
                }

                return SqlQueryResult.RecordDidNotExist;
            }
        }
    }
}
