using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
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
        /// Attempts to sign in a user by updating the SignInDateUtc for a specific workplace visit and user.
        /// First, it checks if a record exists with the given WorkplaceVisitId and Uid. 
        /// If the record exists, it updates the SignInDateUtc; otherwise, it returns an error result.
        /// </summary>
        /// <param name="workplaceVisitId">The ID of the workplace visit.</param>
        /// <param name="uid">The unique identifier of the user.</param>
        /// <param name="signInDateUtc">The sign-in date and time in UTC.</param>
        /// <returns>Returns a SqlQueryResult indicating success, record not found, or an unknown error.</returns>

        public async Task<SqlQueryResult> SignInAsync(Guid workplaceVisitId, Guid uid, DateTime? signInDateUtc)
        {
            // Use connection string from app settings
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                await sqlConnection.OpenAsync();

                // First, check if the record exists
                var checkQuery = @"
            SELECT COUNT(1)
            FROM [dbo].[tblWorkplaceVisitUserJoin]
            WHERE [WorkplaceVisitId] = @WorkplaceVisitId AND [Uid] = @Uid";

                var exists = await sqlConnection.ExecuteScalarAsync<int>(checkQuery, new { WorkplaceVisitId = workplaceVisitId, Uid = uid });

                // If record doesn't exist, return appropriate result
                if (exists == 0)
                {
                    return SqlQueryResult.RecordDidNotExist;
                }

                // If record exists, update the SignInDateUtc
                var updateQuery = @"
            UPDATE [dbo].[tblWorkplaceVisitUserJoin]
            SET [SignInDateUtc] = @SignInDateUtc
            WHERE [WorkplaceVisitId] = @WorkplaceVisitId AND [Uid] = @Uid";

                var rowsAffected = await sqlConnection.ExecuteAsync(updateQuery, new { SignInDateUtc = signInDateUtc, WorkplaceVisitId = workplaceVisitId, Uid = uid });

                return rowsAffected > 0 ? SqlQueryResult.Ok : SqlQueryResult.UnknownError;
            }
        }
    }
}
