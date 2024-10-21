using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisits.CreateWorkplaceVisit;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class WorkplaceVisitsRepository
    {
        private readonly AppSettings _appSettings;

        public WorkplaceVisitsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        public async Task<SqlQueryResult> InsertVisitorAsync(CreateWorkplaceVisitRequest request, Guid? userid, string adminUserDisplayName, string? remoteIpAddress)
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
                                    EndDateUtc, EndDateLocal, PurposeOfVisit, Company, Cancelled, Truncated, Deleted)
                                VALUES 
                                    (@id, @InsertDateUtc, @InsertDateUtc, @BuildingId, 1, 
                                    @FormCompletedByUid, @HostUid, @StartDateUtc, @StartDateLocal, 
                                    @EndDateUtc, @EndDateLocal, @PurposeOfVisit, @Company, 0, 0, 0);
                            ";
                        Guid WorkplaceVisitId = Guid.NewGuid();
                        Guid? FormCompletedByUid = userid;
                        DateTime InsertDateUtc = DateTime.UtcNow;

                        Guid Uid = Guid.NewGuid();
                        int userResultCode = 0, resultCode = 0;

                        var visitParameters = new DynamicParameters();
                        visitParameters.Add("@id", WorkplaceVisitId, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@BuildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@FormCompletedByUid", FormCompletedByUid, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@HostUid", request.HostUid, DbType.Guid, ParameterDirection.Input);
                        visitParameters.Add("@StartDateUtc", request.StartDate.ToUniversalTime(), DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@StartDateLocal", request.StartDate, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@EndDateUtc", request.EndDate.ToUniversalTime(), DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@EndDateLocal", request.EndDate, DbType.DateTime2, ParameterDirection.Input, 3);
                        visitParameters.Add("@PurposeOfVisit", RemoveSpecialCharacters(request.Purpose), DbType.String, ParameterDirection.Input, 4000);
                        visitParameters.Add("@Company", RemoveSpecialCharacters(request.Company), DbType.String, ParameterDirection.Input, 4000);

                        resultCode = await sqlConnection.ExecuteAsync(insertVisitSql, visitParameters, transaction);
                        // Log the visit insertion
                        await LogVisitAsync(sqlConnection, transaction, WorkplaceVisitId, request.OrganizationId, request.HostUid, request.BuildingId, FormCompletedByUid, adminUserDisplayName, remoteIpAddress, "Insert", request.StartDate.ToUniversalTime(), request.EndDate.ToUniversalTime(), request.Purpose);


                        foreach (var user in request.Users)
                        {

                            // SQL query to check if the email already exists and get the User ID
                            string checkEmailSql = "SELECT Uid FROM [dbo].[tblUsers] WHERE Email = @Email";

                            // Query the database to check if the email exists
                            var existingUserId = await sqlConnection.QueryFirstOrDefaultAsync<Guid?>(checkEmailSql, new { Email = user.Email }, transaction);

                            if (existingUserId != null)
                            {
                                Uid = existingUserId.Value;

                            }
                            else
                            {
                                // Proceed with inserting the new user if the email doesn't exist
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
                                    NULL, NULL, 0, 0);";

                                // Map parameters for tblUsers
                                DynamicParameters userParameters = new DynamicParameters();
                                userParameters.Add("@uid", Uid, DbType.Guid, ParameterDirection.Input);
                                userParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                                userParameters.Add("@Email", user.Email, DbType.String, ParameterDirection.Input, 254);
                                userParameters.Add("@DisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                                userParameters.Add("@FirstName", RemoveSpecialCharacters(user.FirstName), DbType.String, ParameterDirection.Input, 75);
                                userParameters.Add("@Surname", RemoveSpecialCharacters(user.Surname), DbType.String, ParameterDirection.Input, 75);
                                userParameters.Add("@Timezone", "UTC", DbType.String, ParameterDirection.Input, 50);

                                // Execute the SQL to insert the user
                                await sqlConnection.ExecuteAsync(insertUserSql, userParameters, transaction);
                            }

                            // Insert into tblWorkplaceVisitUserJoin
                            string insertUserJoinSql = @"
                                    INSERT INTO [dbo].[tblWorkplaceVisitUserJoin] 
                                        (WorkplaceVisitId, Uid, InsertDateUtc, FirstName, Surname, 
                                        Email, MobileNumber, SignInDateUtc, SignOutDateUtc)
                                    VALUES 
                                        (@workplaceVisitId, @uid, @InsertDateUtc, @firstName, @surname, 
                                        @Email, @MobileNumber, null, null);
                                ";

                            DynamicParameters userJoinParameters = new DynamicParameters();
                            userJoinParameters.Add("@workplaceVisitId", WorkplaceVisitId, DbType.Guid, ParameterDirection.Input);
                            userJoinParameters.Add("@uid", Uid, DbType.Guid, ParameterDirection.Input);
                            userJoinParameters.Add("@InsertDateUtc", InsertDateUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                            userJoinParameters.Add("@firstName", RemoveSpecialCharacters(user.FirstName), DbType.String, ParameterDirection.Input, 75);
                            userJoinParameters.Add("@surname", RemoveSpecialCharacters(user.Surname), DbType.String, ParameterDirection.Input, 75);
                            userJoinParameters.Add("@Email", user.Email, DbType.String, ParameterDirection.Input, 254);
                            userJoinParameters.Add("@MobileNumber", user.MobileNumber, DbType.String, ParameterDirection.Input, 30);

                            // Execute the SQL and retrieve the number of affected rows for UserJoin
                            userResultCode = await sqlConnection.ExecuteAsync(insertUserJoinSql, userJoinParameters, transaction);
                            // Log the user join insertion
                            await LogUserJoinAsync(sqlConnection, transaction, Uid, WorkplaceVisitId, request.OrganizationId, user, adminUserDisplayName, remoteIpAddress, "Insert");


                            Uid = Guid.NewGuid();
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
        private async Task LogVisitAsync(SqlConnection sqlConnection, IDbTransaction transaction, Guid workplaceVisitId, Guid buildingId, Guid hostUid, Guid OrganizationId, Guid? formCompletedByUid, string adminUserDisplayName, string? remoteIpAddress, string logAction, DateTime startDateUtc, DateTime endDateUtc, string purposeOfVisit)
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
            logParameters.Add("@UpdatedByIpAddress", remoteIpAddress, DbType.String, ParameterDirection.Input, 45);
            logParameters.Add("@LogDescription", "Visitor registered: " + purposeOfVisit, DbType.String, ParameterDirection.Input, 4000);
            logParameters.Add("@OrganizationId", OrganizationId, DbType.Guid);
            logParameters.Add("@WorkplaceVisitId", workplaceVisitId, DbType.Guid);
            logParameters.Add("@BuildingId", buildingId, DbType.Guid);
            logParameters.Add("@RegisteredByVisitor", 1, DbType.Int32);
            logParameters.Add("@FormCompletedByUid", formCompletedByUid, DbType.Guid);
            logParameters.Add("@HostUid", hostUid, DbType.Guid);
            logParameters.Add("@StartDateUtc", startDateUtc, DbType.DateTime2);
            logParameters.Add("@StartDateLocal", startDateUtc, DbType.DateTime2);
            logParameters.Add("@EndDateUtc", endDateUtc, DbType.DateTime2);
            logParameters.Add("@EndDateLocal", endDateUtc, DbType.DateTime2);
            logParameters.Add("@PurposeOfVisit", purposeOfVisit, DbType.String, ParameterDirection.Input, 4000);
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
            logParameters.Add("@LogAction", logAction, DbType.String, ParameterDirection.Input, 50);
            logParameters.Add("@CascadeFrom", null, DbType.String, ParameterDirection.Input, 50);
            logParameters.Add("@CascadeLogId", null, DbType.Guid);

            await sqlConnection.ExecuteAsync(logSql, logParameters, transaction);
        }

        // Log user join insertion
        private async Task LogUserJoinAsync(SqlConnection sqlConnection, IDbTransaction transaction, Guid Uid, Guid workplaceVisitId, Guid OrganizationId, UserInfo user, string adminUserDisplayName, string? remoteIpAddress, string logAction)
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
            userJoinLogParameters.Add("@UpdatedByDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
            userJoinLogParameters.Add("@UpdatedByIpAddress", remoteIpAddress, DbType.String, ParameterDirection.Input, 45);
            userJoinLogParameters.Add("@LogDescription", "User added to visit: " + user.FirstName + " " + user.Surname, DbType.String, ParameterDirection.Input, 4000);
            userJoinLogParameters.Add("@WorkplaceVisitId", workplaceVisitId, DbType.Guid);
            userJoinLogParameters.Add("@OrganizationId", OrganizationId, DbType.Guid);
            userJoinLogParameters.Add("@Uid", Uid, DbType.Guid);
            userJoinLogParameters.Add("@FirstName", RemoveSpecialCharacters(user.FirstName), DbType.String, ParameterDirection.Input, 75);
            userJoinLogParameters.Add("@Surname", RemoveSpecialCharacters(user.Surname), DbType.String, ParameterDirection.Input, 75);
            userJoinLogParameters.Add("@Email", user.Email, DbType.String, ParameterDirection.Input, 254);
            userJoinLogParameters.Add("@MobileNumber", user.MobileNumber, DbType.String, ParameterDirection.Input, 30);
            userJoinLogParameters.Add("@SignInDateUtc", null, DbType.DateTime2);
            userJoinLogParameters.Add("@SignOutDateUtc", null, DbType.DateTime2);
            userJoinLogParameters.Add("@LogAction", logAction, DbType.String, ParameterDirection.Input, 50);

            await sqlConnection.ExecuteAsync(logUserJoinSql, userJoinLogParameters, transaction);
        }
        public string RemoveSpecialCharacters(string input)
        {
            return Regex.Replace(input, @"[^a-zA-Z0-9\s]", "");
        }

    }
}
