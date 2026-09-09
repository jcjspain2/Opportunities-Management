using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Runtime.InteropServices;
using System.Threading;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;


namespace QUALITY_GATES.Classes
{
    public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
    {

        /// <summary>
        /// Adds a Users for working wiyh my Tasks as collaborative
        /// </summary>
        /// <param name="oppLineId">Unique Sales Forve ID of the project to display data</param>
        /// <param name="statusId">Status ID deliverable belongs to example FEAS</param>
        /// <param name="sgateId">Gate action ID, deliverable belongs to example FEAS_1</param>
        /// <param name="delId">Delivery ID, where cooment will be allocated </param>
        /// <param name="User_Who_Request">User Id who makes request for this function should be responsible of the deliverable</param>
        /// <param name="ListUsersToCollaborate">List Of Users to Collaborate</param>
        /// <returns>True adds , false error </returns>
        public async Task<OperationResult<bool>> AddCollaborator_To_Deliverable(string oppLineId,
                                                                       string statusId,
                                                                       string sgateId,
                                                                       int delId,
                                                                       string User_Who_Request,
                                                                       List<USERS_COLLABORATIVE> ListUsersToCollaborate,
                                                                       CancellationToken cancellationToken = default)

        {
            foreach (var user in ListUsersToCollaborate)
            {
                if (string.IsNullOrEmpty(user.UserId) || string.IsNullOrEmpty(user.Responsible_Task_Comment))
                    return OperationResult<bool>.Fail($"User '{user.UserId}' has empty required fields.");
            }

            var existingResult = await _db.GetDatatableFromSelectAsync(
                SQL_Get_Existing_Collaborators(),
                new[]
                {
                    new SqlParameter("@OppLineId", oppLineId),
                    new SqlParameter("@StatusId",  statusId),
                    new SqlParameter("@SgateId",   sgateId),
                    new SqlParameter("@DelId",     delId)
                },
                cancellationToken: cancellationToken);

            if (!existingResult.Success || existingResult.DTResults == null)
                return OperationResult<bool>.Fail($"Error checking existing collaborators: {existingResult.Message}");

            var existingUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in existingResult.DTResults.Rows)
                existingUserIds.Add(GetString(row, "User_ID"));

            foreach (var user in ListUsersToCollaborate)
            {
                if (existingUserIds.Contains(user.UserId))
                    return OperationResult<bool>.Fail($"User '{user.UserId}' is already a collaborator for this deliverable.");
            }

            var userMails = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var user in ListUsersToCollaborate)
            {
                var mailResult = await _db.GetDatatableFromSelectAsync(
                    SQL_Get_User_Mail_By_SamAccount(),
                    new[] { new SqlParameter("@UserId", user.UserId) },
                    cancellationToken: cancellationToken);

                if (!mailResult.Success || mailResult.DTResults == null || mailResult.DTResults.Rows.Count == 0)
                    return OperationResult<bool>.Fail($"User '{user.UserId}' not found in AD directory.");

                userMails[user.UserId] = GetString(mailResult.DTResults.Rows[0], "EmailAddress");
            }

            await using var conn = await _db.CreateOpenConnectionAsync(cancellationToken: cancellationToken);
            await using var tx = conn.BeginTransaction();
            try
            {
                foreach (var user in ListUsersToCollaborate)
                {
                    var parameters = new[]
                    {
                        new SqlParameter("@OppLineId",   oppLineId),
                        new SqlParameter("@StatusId",    statusId),
                        new SqlParameter("@SgateId",     sgateId),
                        new SqlParameter("@DelId",       delId),
                        new SqlParameter("@UserId",      user.UserId),
                        new SqlParameter("@UserMail",    userMails[user.UserId]),
                        new SqlParameter("@RespComment", user.Responsible_Task_Comment),
                        new SqlParameter("@GeneratedBy", User_Who_Request)
                    };

                    var insertResult = await _db.NonQueryDataToSQLServer(
                        SQL_Insert_Collaborator(), parameters, transaction: tx, cancellationToken: cancellationToken);

                    if (!insertResult.Success)
                    {
                        tx.Rollback();
                        return OperationResult<bool>.Fail($"Error inserting collaborator '{user.UserId}': {insertResult.Message}");
                    }
                }

                tx.Commit();
                return OperationResult<bool>.Ok(true);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static string SQL_Get_Existing_Collaborators() => @"
            SELECT User_ID
            FROM   dbo.TRA_PROJECTS_DELIVERABLE_COOPERATION
            WHERE  OPP_LINE_ID    = @OppLineId
              AND  STATUS_ID      = @StatusId
              AND  SGATE_ID       = @SgateId
              AND  DELIVERABLE_ID = @DelId";

        private static string SQL_Get_User_Mail_By_SamAccount() => @"
            SELECT EmailAddress
            FROM   dbo.MAS_AD_Users
            WHERE  samaccountname = @UserId";

        private static string SQL_Insert_Collaborator() => @"
            INSERT INTO dbo.TRA_PROJECTS_DELIVERABLE_COOPERATION
                  (OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID, User_ID, User_Mail, RESP_COMMENT, Generataed_At, Generated_By)
            VALUES
                  (@OppLineId, @StatusId, @SgateId, @DelId, @UserId, @UserMail, @RespComment, SYSDATETIME(), @GeneratedBy)";
        private static string SQL_Validate_Deliverable_Status() => @"
            SELECT COUNT(1)
            FROM   dbo.MAS_GATE_STATUS
            WHERE  GATE_STATUS_ID   = @StatusIdToUpdate
              AND  KEY_PROCESS = @KeyProcess";

        private static string SQL_Update_Deliverable_Responsible_Status() => @"
            UPDATE dbo.TRA_PROJECTS_DELIVERABLES
            SET    DELIVERABLE_STATUS_ID = @StatusIdToUpdate,
                   MODIFIED_DATE        = SYSDATETIME(),
                   MODIFIED_BY          = @UserId
            WHERE  OPP_LINE_ID   = @OppLineId
              AND  STATUS_ID     = @StatusId
              AND  SGATE_ID      = @SgateId
              AND  DELIVERABLE_ID = @DelId";

        private static string SQL_Update_Deliverable_Accountable_Status() => @"
            UPDATE dbo.TRA_PROJECTS_DELIVERABLES
            SET    ACCOUNTABLE_STATUS_ID = @StatusIdToUpdate,
                   MODIFIED_DATE         = SYSDATETIME()
            WHERE  OPP_LINE_ID   = @OppLineId
              AND  STATUS_ID     = @StatusId
              AND  SGATE_ID      = @SgateId
              AND  DELIVERABLE_ID = @DelId";
        /// <summary>
        /// Adds a comment for a given deliverable 
        /// </summary>
        /// <param name="oppLineId">Unique Sales Forve ID of the project to display data</param>
        /// <param name="statusId">Status ID deliverable belongs to example FEAS</param>
        /// <param name="sgateId">Gate action ID, deliverable belongs to example FEAS_1</param>
        /// <param name="delId">Delivery ID, where cooment will be allocated </param>
        /// <param name="commentText">Text for the comment coming from UI</param>
        /// <param name="commentBy">User ID who made comment</param>
        /// <returns>Return unique identificator in datatable</returns>
        public async Task<OperationResult<Guid>> AddDeliverableComment(string oppLineId,
                                                                       string statusId,
                                                                       string sgateId,
                                                                       int delId,
                                                                       string commentText,
                                                                       string commentBy,
                                                                       CancellationToken cancellationToken = default)                                                                             {
            var newCommentId = Guid.NewGuid();
            var parameters = new[]
            {
            new SqlParameter("@OppLineId",   oppLineId),
            new SqlParameter("@StatusId",    statusId),
            new SqlParameter("@SgateId",     sgateId),
            new SqlParameter("@DelId",       delId),
            new SqlParameter("@CommentId",   newCommentId),
            new SqlParameter("@CommentText", commentText),
            new SqlParameter("@CommentBy",   commentBy),
        };

            var result = await _db.NonQueryDataToSQLServer(SQL_Insert_Deliverable_Comment(), parameters);

            return result.Success
                ? OperationResult<Guid>.Ok(newCommentId)
                : OperationResult<Guid>.Fail($"Error inserting comment: {result.Message}");
        }

        private static string SQL_Insert_Deliverable_Comment() => @"
                INSERT INTO dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS
                      (OPP_LINE_ID, STATUS_ID, SGATE_ID, DEL_ID, COMMENT_ID, COMMENT_TEXT, COMMENT_BY,COMMENT_DATE,KEY_PROCESS)
                VALUES
                      (@OppLineId, @StatusId, @SgateId, @DelId, @CommentId, @CommentText, @CommentBy,SYSDATETIME(),'DELIV' )";


        public async Task<OperationResult<Guid>> AddDeliverableCommentAccountant(string oppLineId,
                                                               string statusId,
                                                               string sgateId,
                                                               int delId,
                                                               string commentText,
                                                               string commentBy,
                                                               CancellationToken cancellationToken = default)
        {
            var newCommentId = Guid.NewGuid();
            var parameters = new[]
            {
            new SqlParameter("@OppLineId",   oppLineId),
            new SqlParameter("@StatusId",    statusId),
            new SqlParameter("@SgateId",     sgateId),
            new SqlParameter("@DelId",       delId),
            new SqlParameter("@CommentId",   newCommentId),
            new SqlParameter("@CommentText", commentText),
            new SqlParameter("@CommentBy",   commentBy),
        };

            var result = await _db.NonQueryDataToSQLServer(SQL_Insert_Deliverable_Comment_Accountant(), parameters);

            return result.Success
                ? OperationResult<Guid>.Ok(newCommentId)
                : OperationResult<Guid>.Fail($"Error inserting comment: {result.Message}");
        }
        private static string SQL_Insert_Deliverable_Comment_Accountant() => @"
                INSERT INTO dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS
                      (OPP_LINE_ID, STATUS_ID, SGATE_ID, DEL_ID, COMMENT_ID, COMMENT_TEXT, COMMENT_BY,COMMENT_DATE,KEY_PROCESS)
                VALUES
                      (@OppLineId, @StatusId, @SgateId, @DelId, @CommentId, @CommentText, @CommentBy,SYSDATETIME(),'ACC' )";
        /// <summary>
        /// Update deliverable User Text for a given deliverable
        /// </summary>
        public async Task<OperationResult<bool>> UpdateDeliverableUserText(string oppLineId,
                                                                           string statusId,
                                                                           string sgateId,
                                                                           int delId,
                                                                           string USER_TEXT_TO_UPDATE,
                                                                           CancellationToken cancellationToken = default)
        {
            var parameters = new[]
            {
                new SqlParameter("@UserText",  USER_TEXT_TO_UPDATE),
                new SqlParameter("@OppLineId", oppLineId),
                new SqlParameter("@StatusId",  statusId),
                new SqlParameter("@SgateId",   sgateId),
                new SqlParameter("@DelId",     delId)
            };

            var result = await _db.NonQueryDataToSQLServer(SQL_Update_Deliverable_UserText(), parameters, cancellationToken: cancellationToken);

            if (!result.Success)
                return OperationResult<bool>.Fail($"Error updating deliverable user text: {result.Message}");

            if (result.RecordsAffected == 0)
                return OperationResult<bool>.Fail($"Deliverable '{oppLineId}/{statusId}/{sgateId}/{delId}' not found.");

            return OperationResult<bool>.Ok(true);
        }

        private static string SQL_Update_Deliverable_UserText() => @"
            UPDATE dbo.TRA_PROJECTS_DELIVERABLES
            SET    USER_TEXT     = @UserText,
                   MODIFIED_DATE = SYSDATETIME()
            WHERE  OPP_LINE_ID    = @OppLineId
              AND  STATUS_ID      = @StatusId
              AND  SGATE_ID       = @SgateId
              AND  DELIVERABLE_ID = @DelId";

        public async Task<OperationResult<bool>> UpdateProjectPriority(string oppLineId,
                                                                       Priority Project_Priority,
                                                                       string   USER_ID_WHO_REQUEST,
                                                                       CancellationToken cancellationToken = default)
        {
            // Check if the user has the required job title to perform this action
            if (USER_ID_WHO_REQUEST != "System") // System always allowed to generate next gate, no matter the job title
            {
                var resultJob = await Get_KeyProcess_Job_title_Restriction("@PRIORITY", USER_ID_WHO_REQUEST, cancellationToken);
                if (!resultJob.Success)
                    return OperationResult<bool>.Fail($"Error: {resultJob.ErrorMessage}");
          
            }

            var parameters = new[]
            {
                new SqlParameter("@Priority",  (int)Project_Priority),
                new SqlParameter("@OppLineId", oppLineId)
            };

            var result = await _db.NonQueryDataToSQLServer(SQL_Update_Project_Priority(), parameters, cancellationToken: cancellationToken);

            if (!result.Success)
                return OperationResult<bool>.Fail($"Error updating project priority: {result.Message}");

            if (result.RecordsAffected == 0)
                return OperationResult<bool>.Fail($"Project '{oppLineId}' not found.");

            return OperationResult<bool>.Ok(true);
        }

        private static string SQL_Update_Project_Priority() => @"
            UPDATE dbo.TRA_PROJECTS
            SET    PRIORITY      = @Priority
            WHERE  OPPORTUNITY_LINE_ID = @OppLineId";


        /// <summary>
        /// Adds a comment for a given deliverable 
        /// </summary>
        /// <param name="oppLineId">Unique Sales Forve ID of the project to display data</param>
        /// <param name="statusId">Status ID deliverable belongs to example FEAS</param>
        /// <param name="sgateId">Gate action ID, deliverable belongs to example FEAS_1</param>
        /// <param name="delId">Delivery ID, where cooment will be allocated </param>
        /// <returns>TO BUIOLD</returns>

        public async Task<OperationResult<bool>> UpdateDeliverableStatus(string oppLineId,
                                                                             string statusId,
                                                                             string sgateId,
                                                                             int delId,
                                                                             DeliverableRolesEstructure RoleDeliverable,
                                                                             string STATUS_ID_TO_UPDATE,
                                                                             string USER_ID_WHO_REQUEST,
                                                                             CancellationToken cancellationToken = default)
        {
            var keyProcess = RoleDeliverable == DeliverableRolesEstructure.Responsible ? "DEL_RESP" : "DEL_ACC";
            string STATUS_ID_RESPONSIBLE=string.Empty;
            string STATUS_ID_ACCOUNTABLE=string.Empty;
            string STATUS_ID_GATE = "INPROGRESS";
            //TODO: Need to determine GATE ststus when all deliverable finsih create code
            switch (RoleDeliverable)
            {
                case DeliverableRolesEstructure.Responsible:
                    switch (STATUS_ID_TO_UPDATE)
                    {
                        case "PENDING_A":
                            STATUS_ID_RESPONSIBLE = "PENDING_A";
                            STATUS_ID_ACCOUNTABLE = "PENDING_A";
                            break;
                        case "INPROGRESS":
                            STATUS_ID_RESPONSIBLE = "INPROGRESS";
                            STATUS_ID_ACCOUNTABLE = "INPROGRESS";
                             break;
                        case "WAIVED":
                            STATUS_ID_RESPONSIBLE = "WAIVED";
                            STATUS_ID_ACCOUNTABLE = "PENDING_A";
                            break;
                    }                    
                    break;
                case DeliverableRolesEstructure.Accountable:
                    switch (STATUS_ID_TO_UPDATE)
                    {
                        case "PENDING_R":
                            STATUS_ID_RESPONSIBLE = "PENDING_R";
                            STATUS_ID_ACCOUNTABLE = "PENDING_R";
                            break;
                        case "APPROVED":
                            STATUS_ID_RESPONSIBLE = "COMPLETED";
                            STATUS_ID_ACCOUNTABLE = "APPROVED";
                            break;
                    }
                    
                    break;
            }

            // Check Status ID Exists in Datatable
            var (isValidResp, validationErrorResp) = await ValidateDeliverableStatusExistsAsync(STATUS_ID_RESPONSIBLE, "DEL_RESP", cancellationToken);
            if (!isValidResp)
                return OperationResult<bool>.Fail(validationErrorResp!);
            var (isValidAcc, validationErrorAcc) = await ValidateDeliverableStatusExistsAsync(STATUS_ID_ACCOUNTABLE, "DEL_ACC", cancellationToken);
            if (!isValidAcc)
                return OperationResult<bool>.Fail(validationErrorAcc!);


            bool alsoUpdateAccountable = keyProcess == "DEL_RESP" && STATUS_ID_TO_UPDATE == "PENDING_A";

            SqlParameter[] ParamsResp() => new[]
            {
                new SqlParameter("@StatusIdToUpdate", STATUS_ID_RESPONSIBLE),
                new SqlParameter("@OppLineId",        oppLineId),
                new SqlParameter("@StatusId",         statusId),
                new SqlParameter("@SgateId",          sgateId),
                new SqlParameter("@DelId",            delId),
                new SqlParameter("@UserId",           USER_ID_WHO_REQUEST)
            };

            await using var conn = await _db.CreateOpenConnectionAsync(cancellationToken: cancellationToken);
            await using var tx = conn.BeginTransaction();
            try
            {
                var result = await _db.NonQueryDataToSQLServer(SQL_Update_Deliverable_Responsible_Status(), ParamsResp(), transaction: tx, cancellationToken: cancellationToken);

                if (!result.Success)
                {
                    tx.Rollback();
                    return OperationResult<bool>.Fail($"Error updating deliverable status: {result.Message}");
                }

                if (result.RecordsAffected == 0)
                {
                    tx.Rollback();
                    return OperationResult<bool>.Fail($"Deliverable '{oppLineId}/{statusId}/{sgateId}/{delId}' not found.");
                }
                SqlParameter[] ParamsAcc() => new[]
           {
                new SqlParameter("@StatusIdToUpdate", STATUS_ID_ACCOUNTABLE),
                new SqlParameter("@OppLineId",        oppLineId),
                new SqlParameter("@StatusId",         statusId),
                new SqlParameter("@SgateId",          sgateId),
                new SqlParameter("@DelId",            delId),
                new SqlParameter("@UserId",           USER_ID_WHO_REQUEST)
            };

                var accResult = await _db.NonQueryDataToSQLServer(
                        SQL_Update_New_Deliverable_Accountable_Status(), ParamsAcc(), transaction: tx, cancellationToken: cancellationToken);

                    if (!accResult.Success)
                    {
                        tx.Rollback();
                        return OperationResult<bool>.Fail($"Error updating accountable status: {accResult.Message}");
                    }
               
                tx.Commit();
                // Also we need to update Gate Status after commit deliverable status otherways is not trustable
                // First we need to check if all deliverables are in final status from accountable perpective.
                const string sqlDelFinalState = @"
                  SELECT COUNT(*) AS NOT_FINAL_STATE_DELIVERBLES
                  FROM dbo.TRA_PROJECTS_DELIVERABLES
                  JOIN dbo.MAS_GATE_STATUS T2 ON T2.MODULE_ID = 'Q_GATES' AND T2.KEY_PROCESS = 'DEL_ACC' AND IS_FINAL_STATE<> 1
                  where OPP_LINE_ID = @OppLineId AND STATUS_ID = @StatusId ";


                SqlParameter[] ParamsStatus() => new[]
      {
                new SqlParameter("@StatusIdToUpdate",STATUS_ID_GATE ),
                new SqlParameter("@OppLineId",        oppLineId),
                new SqlParameter("@StatusId",         statusId),
                new SqlParameter("@UserId",           USER_ID_WHO_REQUEST)
            };

                var resultDelNotFinalState = await _db.GetDatatableFromSelectAsync(sqlDelFinalState, ParamsStatus(), cancellationToken: cancellationToken);

                if (!resultDelNotFinalState.Success)
                    return OperationResult<bool>.Fail($"Error: {result.Message}");
                   

                if (Convert.ToInt32(resultDelNotFinalState.DTResults.Rows[0]["NOT_FINAL_STATE_DELIVERBLES"]) == 0)
                {
                    STATUS_ID_GATE = "COMPLETED";
                }
                var StatusResult = await _db.NonQueryDataToSQLServer(
                       SQL_Update_Status(), ParamsStatus(), cancellationToken: cancellationToken);
                if (!StatusResult.Success)
                {
                    return OperationResult<bool>.Fail($"Error updating Gate status: {StatusResult.Message}");
                }

                return OperationResult<bool>.Ok(true);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private async Task<(bool IsValid, string? ErrorMessage)> ValidateDeliverableStatusExistsAsync(
            string STATUS_ID_TO_UPDATE,
            string keyProcess,
            CancellationToken cancellationToken = default)
        {
            var result = await _db.GetDatatableFromSelectAsync(
                SQL_Validate_Deliverable_Status(),
                new[] { new SqlParameter("@StatusIdToUpdate", STATUS_ID_TO_UPDATE),
                       new SqlParameter("@KeyProcess", keyProcess) },
                cancellationToken: cancellationToken);

            if (!result.Success || result.DTResults == null)
                return (false, $"Error validating status: {"Error trying to update " + STATUS_ID_TO_UPDATE + ": " + result.Message}");

            if (Convert.ToInt32(result.DTResults.Rows[0][0]) == 0)
                return (false, $"Status '{STATUS_ID_TO_UPDATE}' is not valid.");

            return (true, null);
        }

        private static string SQL_Update_New_Deliverable_Accountable_Status() => @"
            UPDATE dbo.TRA_PROJECTS_DELIVERABLES
            SET    ACCOUNTABLE_STATUS_ID = @StatusIdToUpdate,
                   MODIFIED_DATE         = SYSDATETIME(),
                   MODIFIED_BY           = @UserId
            WHERE  OPP_LINE_ID    = @OppLineId
              AND  STATUS_ID      = @StatusId
              AND  SGATE_ID       = @SgateId
              AND  DELIVERABLE_ID = @DelId";

        private static string SQL_Update_Status() => @"
            UPDATE dbo.TRA_PROJECTS_STATUS
            SET    CURRENT_STATUS = @StatusIdToUpdate
            WHERE  OPP_LINE_ID    = @OppLineId
              AND  STATUS_ID      = @StatusId ";

        private static string SQL_Update_Status_To_Close() => @"
            UPDATE dbo.TRA_PROJECTS_STATUS
            SET    CURRENT_STATUS = @StatusId,
                   CLOSING_DATE   = SYSDATETIME(),
                   CLOSING_USER   = @UserId
            WHERE  OPP_LINE_ID    = @OppLineId
              AND  STATUS_ID      = @StatusId ";

    }



}
