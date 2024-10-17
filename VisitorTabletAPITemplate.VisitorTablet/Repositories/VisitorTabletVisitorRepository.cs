using Azure;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Register;
using static Dapper.SqlMapper;
namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public class VisitorTabletVisitorRepository
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

        public async Task<SqlQueryResult> InsertVisitorAsync(RegisterRequest request, string adminUserDisplayName, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                await sqlConnection.OpenAsync(); // Open the connection
                // Start a transaction
                using (var transaction = sqlConnection.BeginTransaction())
                {
                    try
                    {
                        // Insert into tblWorkplaceVisits
                        string insertVisitSql = @"
            INSERT INTO [dbo].[tblWorkplaceVisits] 
                (id, InsertDateUtc, UpdatedDateUtc, BuildingId, RegisteredByVisitor, 
                FormCompletedByUid, HostUid, StartDateUtc, StartDateLocal, 
                EndDateUtc, EndDateLocal, PurposeOfVisit, Cancelled, Truncated, Deleted)
            VALUES 
                (@id, @InsertDateUtc, @InsertDateUtc, @BuildingId, 1, 
                @FormCompletedByUid, @HostUid, @StartDateUtc, @StartDateLocal, 
                @EndDateUtc, @EndDateLocal, @PurposeOfVisit, 0, 0, 0);
        ";
                        Guid WorkplaceVisitId = Guid.NewGuid();
                        Guid FormCompletedByUid = Guid.NewGuid();
                        DateTime InsertDateUtc = DateTime.UtcNow;
                         
                        var visitParameters = new DynamicParameters();
                        visitParameters.Add("@id", WorkplaceVisitId, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@BuildingId", Guid.NewGuid(), DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@FormCompletedByUid", FormCompletedByUid, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@HostUid", Guid.NewGuid(), DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@StartDateUtc", request.StartDate.ToUniversalTime(), DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@StartDateLocal", request.StartDate, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@EndDateUtc", request.EndDate.ToUniversalTime(), DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@EndDateLocal", request.EndDate, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@PurposeOfVisit", RemoveSpecialCharacters(request.Purpose), DbType.String, ParameterDirection.Input, 4000);

                        await sqlConnection.ExecuteAsync(insertVisitSql, visitParameters, transaction);
                        // Log the visit insertion
                        await LogVisitAsync(sqlConnection, transaction, FormCompletedByUid, WorkplaceVisitId,request.OrganizationId, FormCompletedByUid, adminUserDisplayName, remoteIpAddress, "Insert", request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime(), request.Purpose);

                        int userResultCode = 0 , resultCode = 0;
                        foreach (var user in request.Users)
                        {
                            // Insert into tblWorkplaceVisitUserJoin
                            string insertUserJoinSql = @"
            INSERT INTO [dbo].[tblWorkplaceVisitUserJoin] 
                (WorkplaceVisitId, Uid, InsertDateUtc, FirstName, Surname, 
                Email, MobileNumber, SignInDateUtc, SignOutDateUtc)
            VALUES 
                (@workplaceVisitId, @uid, @InsertDateUtc, @firstName, @surname, 
                @Email, @MobileNumber, null, null);
        ";
                            Guid Uid = FormCompletedByUid;
                            DynamicParameters userJoinParameters = new DynamicParameters();
                            userJoinParameters.Add("@workplaceVisitId", WorkplaceVisitId, DbType.Guid, ParameterDirection.Input);
                            userJoinParameters.Add("@uid", Uid, DbType.Guid, ParameterDirection.Input);
                            userJoinParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                            userJoinParameters.Add("@firstName", RemoveSpecialCharacters(user.FirstName), DbType.String, ParameterDirection.Input, 75);
                            userJoinParameters.Add("@surname", RemoveSpecialCharacters(user.Surname), DbType.String, ParameterDirection.Input, 75);
                            userJoinParameters.Add("@Email", user.Email, DbType.String, ParameterDirection.Input, 254);
                            userJoinParameters.Add("@MobileNumber", user.MobileNumber, DbType.String, ParameterDirection.Input, 30);

                            // Execute the SQL and retrieve the number of affected rows for UserJoin
                            resultCode = await sqlConnection.ExecuteAsync(insertUserJoinSql, userJoinParameters, transaction);
                            // Log the user join insertion
                            await LogUserJoinAsync(sqlConnection, transaction, Uid, WorkplaceVisitId, request.OrganizationId, user, adminUserDisplayName, remoteIpAddress, "Insert");

                            // Insert into tblUsers
                            string insertUserSql = @"
                            INSERT INTO [dbo].[tblUsers] 
                            (Uid, InsertDateUtc, UpdatedDateUtc, LastAccessDateUtc, 
                            LastPasswordChangeDateUtc, Email, 
                            PasswordHash, PasswordLoginFailureCount, 
                            PasswordLoginLastFailureDateUtc, PasswordLockoutEndDateUtc, 
                            TotpEnabled, TotpSecret, TotpFailureCount, 
                            TotpLastFailureDateUtc, TotpLockoutEndDateUtc, 
                            UserSystemRole, DisplayName, FirstName, 
                            Surname, Timezone, AvatarUrl, AvatarImageStorageId, 
                            AvatarThumbnailUrl, AvatarThumbnailStorageId, 
                            Disabled, Deleted)
                            VALUES 
                            (@uid, @InsertDateUtc, @InsertDateUtc, NULL, 
                            NULL, @Email, 
                            NULL, 0, NULL, 
                            NULL, 0, NULL, 0, 
                            NULL, NULL, 
                            1, @DisplayName, @FirstName, 
                            @Surname, @Timezone, NULL, NULL, 
                            NULL, NULL, 0, 0);                            
                            ";

                            // Map parameters for tblUsers
                            DynamicParameters userParameters = new DynamicParameters();
                            userParameters.Add("@uid", Uid, DbType.Guid, ParameterDirection.Input);
                            userParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                            userParameters.Add("@Email", user.Email, DbType.String, ParameterDirection.Input, 254);
                            userParameters.Add("@DisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151); // Adjusted to match nvarchar(151)
                            userParameters.Add("@FirstName", RemoveSpecialCharacters(user.FirstName), DbType.String, ParameterDirection.Input, 75);
                            userParameters.Add("@Surname", RemoveSpecialCharacters(user.Surname), DbType.String, ParameterDirection.Input, 75);
                            userParameters.Add("@Timezone", "UTC", DbType.String, ParameterDirection.Input, 50); // Adjusted to match varchar(50)

                            // Execute the SQL for Users
                             userResultCode = await sqlConnection.ExecuteAsync(insertUserSql, userParameters, transaction);
                        }
                        // Check the result code for UserJoin
                        if (resultCode > 0 && userResultCode > 0)
                        {
                            transaction.Commit();
                            return SqlQueryResult.Ok;
                        }
                        transaction.Rollback();
                        return SqlQueryResult.RecordDidNotExist;

                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return SqlQueryResult.UnknownError;
                    }
                }

            }
        }

        // Log visit insertion
        private async Task LogVisitAsync(SqlConnection sqlConnection, IDbTransaction transaction,Guid Uid, Guid workplaceVisitId,Guid OrganizationId, Guid formCompletedByUid, string adminUserDisplayName, string? remoteIpAddress, string logAction, DateTime startDateUtc, DateTime endDateUtc, string purposeOfVisit)
        {
            string logSql = @"
INSERT INTO [dbo].[tblWorkplaceVisits_Log] 
    (id, InsertDateUtc, UpdatedByUid, UpdatedByDisplayName, 
    UpdatedByIpAddress, LogDescription, OrganizationId, 
    WorkplaceVisitId, BuildingId, RegisteredByVisitor, 
    FormCompletedByUid, HostUid, StartDateUtc, 
    StartDateLocal, EndDateUtc, EndDateLocal, 
    PurposeOfVisit, CancelledDateUtc, CancelledDateLocal, 
    Cancelled, Truncated, Deleted, OldEndDateUtc, 
    OldEndDateLocal, OldCancelled, OldTruncated, 
    OldDeleted, LogAction, CascadeFrom, CascadeLogId)
VALUES 
    (@id, @InsertDateUtc, @UpdatedByUid, @UpdatedByDisplayName, 
    @UpdatedByIpAddress, @LogDescription, @OrganizationId, 
    @WorkplaceVisitId, @BuildingId, @RegisteredByVisitor, 
    @FormCompletedByUid, @HostUid, @StartDateUtc, 
    @StartDateLocal, @EndDateUtc, @EndDateLocal, 
    @PurposeOfVisit, @CancelledDateUtc, @CancelledDateLocal, 
    @Cancelled, @Truncated, @Deleted, @OldEndDateUtc, 
    @OldEndDateLocal, @OldCancelled, @OldTruncated, 
    @OldDeleted, @LogAction, @CascadeFrom, @CascadeLogId);
";

            var logParameters = new DynamicParameters();
            logParameters.Add("@id", Guid.NewGuid(), DbType.Guid);
            logParameters.Add("@InsertDateUtc", DateTime.UtcNow, DbType.DateTime2);
            logParameters.Add("@UpdatedByUid", formCompletedByUid, DbType.Guid);
            logParameters.Add("@UpdatedByDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
            logParameters.Add("@UpdatedByIpAddress", remoteIpAddress, DbType.String,  ParameterDirection.Input,45);
            logParameters.Add("@LogDescription", "Visitor registered: " + purposeOfVisit, DbType.String,  ParameterDirection.Input,4000);
            logParameters.Add("@OrganizationId", OrganizationId, DbType.Guid);
            logParameters.Add("@WorkplaceVisitId", workplaceVisitId, DbType.Guid);
            logParameters.Add("@BuildingId", Guid.NewGuid(), DbType.Guid);
            logParameters.Add("@RegisteredByVisitor", 1, DbType.Int32);
            logParameters.Add("@FormCompletedByUid", formCompletedByUid, DbType.Guid);
            logParameters.Add("@HostUid", Guid.NewGuid(), DbType.Guid);
            logParameters.Add("@StartDateUtc", startDateUtc, DbType.DateTime2);
            logParameters.Add("@StartDateLocal", startDateUtc, DbType.DateTime2);
            logParameters.Add("@EndDateUtc", endDateUtc, DbType.DateTime2);
            logParameters.Add("@EndDateLocal", endDateUtc, DbType.DateTime2);
            logParameters.Add("@PurposeOfVisit", purposeOfVisit, DbType.String,  ParameterDirection.Input,4000);
            logParameters.Add("@CancelledDateUtc", null, DbType.DateTime2);
            logParameters.Add("@CancelledDateLocal", null, DbType.DateTime2);
            logParameters.Add("@Cancelled", false, DbType.Boolean);
            logParameters.Add("@Truncated", false, DbType.Boolean);
            logParameters.Add("@Deleted", false, DbType.Boolean);
            logParameters.Add("@OldEndDateUtc", null, DbType.DateTime2);
            logParameters.Add("@OldEndDateLocal", null, DbType.DateTime2);
            logParameters.Add("@OldCancelled", false, DbType.Boolean);
            logParameters.Add("@OldTruncated", false, DbType.Boolean);
            logParameters.Add("@OldDeleted", false, DbType.Boolean);
            logParameters.Add("@LogAction", logAction, DbType.String,  ParameterDirection.Input,50);
            logParameters.Add("@CascadeFrom", null, DbType.String,  ParameterDirection.Input,50);
            logParameters.Add("@CascadeLogId", null, DbType.Guid);

            await sqlConnection.ExecuteAsync(logSql, logParameters, transaction);
        }

        // Log user join insertion
        private async Task LogUserJoinAsync(SqlConnection sqlConnection, IDbTransaction transaction,Guid Uid, Guid workplaceVisitId, Guid OrganizationId, UserInfo user, string adminUserDisplayName, string? remoteIpAddress, string logAction)
        {
            string logUserJoinSql = @"
INSERT INTO [dbo].[tblWorkplaceVisitUserJoin_Log] 
    (id, InsertDateUtc, UpdatedByUid, UpdatedByDisplayName, 
    UpdatedByIpAddress, LogDescription, OrganizationId, WorkplaceVisitId, 
    Uid, FirstName, Surname, Email, MobileNumber, 
    SignInDateUtc, SignOutDateUtc, LogAction)
VALUES 
    (@id, @InsertDateUtc, @UpdatedByUid, @UpdatedByDisplayName, 
    @UpdatedByIpAddress, @LogDescription, @OrganizationId,@WorkplaceVisitId, 
    @Uid, @FirstName, @Surname, @Email, @MobileNumber, 
    @SignInDateUtc, @SignOutDateUtc, @LogAction);
";

            var userJoinLogParameters = new DynamicParameters();
            userJoinLogParameters.Add("@id", Guid.NewGuid(), DbType.Guid);
            userJoinLogParameters.Add("@InsertDateUtc", DateTime.UtcNow, DbType.DateTime2);
            userJoinLogParameters.Add("@UpdatedByUid", Uid, DbType.Guid);
            userJoinLogParameters.Add("@UpdatedByDisplayName", adminUserDisplayName, DbType.String,  ParameterDirection.Input,151);
            userJoinLogParameters.Add("@UpdatedByIpAddress", remoteIpAddress, DbType.String,  ParameterDirection.Input,45);
            userJoinLogParameters.Add("@LogDescription", "User added to visit: " + user.FirstName + " " + user.Surname, DbType.String,  ParameterDirection.Input,4000);
            userJoinLogParameters.Add("@WorkplaceVisitId", workplaceVisitId, DbType.Guid);
            userJoinLogParameters.Add("@OrganizationId", OrganizationId, DbType.Guid);
            userJoinLogParameters.Add("@Uid", Uid, DbType.Guid);
            userJoinLogParameters.Add("@FirstName", RemoveSpecialCharacters(user.FirstName), DbType.String,  ParameterDirection.Input,75);
            userJoinLogParameters.Add("@Surname", RemoveSpecialCharacters(user.Surname), DbType.String,  ParameterDirection.Input,75);
            userJoinLogParameters.Add("@Email", user.Email, DbType.String,  ParameterDirection.Input,254);
            userJoinLogParameters.Add("@MobileNumber", user.MobileNumber, DbType.String,  ParameterDirection.Input,30);
            userJoinLogParameters.Add("@SignInDateUtc", null, DbType.DateTime2);
            userJoinLogParameters.Add("@SignOutDateUtc", null, DbType.DateTime2);
            userJoinLogParameters.Add("@LogAction", logAction, DbType.String,  ParameterDirection.Input,50);

            await sqlConnection.ExecuteAsync(logUserJoinSql, userJoinLogParameters, transaction);
        }
        public string RemoveSpecialCharacters(string input)
        {
            return Regex.Replace(input, @"[^a-zA-Z0-9\s]", "");
        }

    }
}
