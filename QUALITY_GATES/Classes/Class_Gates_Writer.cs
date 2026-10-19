using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Models;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Runtime.InteropServices;
using System.Threading;
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

            var existingResult = await _db.SelectAsync(
                SQL_Get_Existing_Collaborators(),
                new[]
                {
                    new SqlParameter("@OppLineId", oppLineId),
                    new SqlParameter("@StatusId",  statusId),
                    new SqlParameter("@SgateId",   sgateId),
                    new SqlParameter("@DelId",     delId)
                },
                cancellationToken);

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
                var mailResult = await _db.SelectAsync(
                    SQL_Get_User_Mail_By_SamAccount(),
                    new[] { new SqlParameter("@UserId", user.UserId) },
                    cancellationToken);

                if (!mailResult.Success || mailResult.DTResults == null || mailResult.DTResults.Rows.Count == 0)
                    return OperationResult<bool>.Fail($"User '{user.UserId}' not found in AD directory.");

                userMails[user.UserId] = GetString(mailResult.DTResults.Rows[0], "EmailAddress");
            }

            await using var conn = new SqlConnection(_db.ConnectionString);
            await conn.OpenAsync(cancellationToken);
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

                    var insertResult = await _db.NonQueryAsync(
                        SQL_Insert_Collaborator(), parameters, tx, cancellationToken);

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

            var result = await _db.NonQueryAsync(SQL_Insert_Deliverable_Comment(), parameters);

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

            var result = await _db.NonQueryAsync(SQL_Insert_Deliverable_Comment_Accountant(), parameters);

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

            var result = await _db.NonQueryAsync(SQL_Update_Deliverable_UserText(), parameters, ct: cancellationToken);

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

            var result = await _db.NonQueryAsync(SQL_Update_Project_Priority(), parameters, ct: cancellationToken);

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
            bool isAccountableApproved = false; // used for next gate generation 
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
                            isAccountableApproved = true; // Ok for checking next gate generation
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
            // Check if is Deliverable that generates NEXT gate
            const string sqlDeliverableNextgate = @"
                  SELECT 1
                  FROM dbo.TRA_PROJECTS_DELIVERABLES
                  WHERE OPP_LINE_ID = @OppLineId AND STATUS_ID = @StatusId AND SGATE_ID=@SGateId AND DELIVERABLE_ID=@DelId AND
                        NEXT_GATE_TRIGGERS=1";
            if (isAccountableApproved)
            {
                var resultNextGate = await _db.SelectAsync(sqlDeliverableNextgate,
                   new[] { new SqlParameter("@OppLineId",        oppLineId),
                       new SqlParameter("@StatusId",         statusId),
                       new SqlParameter("@SgateId",          sgateId),
                       new SqlParameter("@DelId",            delId) },
                   cancellationToken);

                if (!resultNextGate.Success || resultNextGate.DTResults == null)
                    return OperationResult<bool>.Fail($"Error checking if is deliverable next gate:  '{oppLineId}/{statusId}/{sgateId}/{delId} + {resultNextGate.Message}'");
                if (resultNextGate.RecordsAffected == 1)
                {
                    var result = await Generate_Next_Default_Gate(oppLineId);
                    if (!result.Success)
                        return OperationResult<bool>.Fail($"Error generating next gate: {result.Message}");
                }
            }
            
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

            await using var conn = new SqlConnection(_db.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var tx = conn.BeginTransaction();
            try
            {
                var result = await _db.NonQueryAsync(SQL_Update_Deliverable_Responsible_Status(), ParamsResp(), tx, cancellationToken);

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

                var accResult = await _db.NonQueryAsync(
                        SQL_Update_New_Deliverable_Accountable_Status(), ParamsAcc(), tx, cancellationToken);

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
                                                 AND DELIVERABLE_STATUS_ID=GATE_STATUS_ID
                  where OPP_LINE_ID = @OppLineId AND STATUS_ID = @StatusId ";


                SqlParameter[] ParamsDel() => new[]
                                          { new SqlParameter("@OppLineId",oppLineId),
                                            new SqlParameter("@StatusId", statusId),
                                          };

                var resultDelNotFinalState = await _db.SelectAsync(sqlDelFinalState, ParamsDel(), cancellationToken);

                if (!resultDelNotFinalState.Success || resultDelNotFinalState.DTResults == null)
                    return OperationResult<bool>.Fail($"Error: {result.Message}");


                if (Convert.ToInt32(resultDelNotFinalState.DTResults.Rows[0]["NOT_FINAL_STATE_DELIVERBLES"]) == 0)
                {
                    STATUS_ID_GATE = "COMPLETED";
                }
                else
                {
                    STATUS_ID_GATE = "INPROGRESS";
                }
                    SqlParameter[] ParamsStatus() => new[]
                                             { new SqlParameter("@StatusIdToUpdate",STATUS_ID_GATE ),
                                               new SqlParameter("@OppLineId",oppLineId),
                                               new SqlParameter("@StatusId",statusId),
                                              };
                var StatusResult = await _db.NonQueryAsync( SQL_Update_Status(), ParamsStatus(), ct: cancellationToken);
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
        public async Task<OperationResult<Manual_Creation_Action>> Create_Manual_Action(Manual_Creation_Action ActionToCreate,
                                                                                        string UserRequester = "System",
                                                                                        CancellationToken cancellationToken = default)
        {
            // Step 1: Input validation
            if (string.IsNullOrWhiteSpace(ActionToCreate.OPP_LINE_ID))
                return OperationResult<Manual_Creation_Action>.Fail("OPP_LINE_ID is required.");
            if (string.IsNullOrWhiteSpace(ActionToCreate.STATUS_ID))
                return OperationResult<Manual_Creation_Action>.Fail("STATUS_ID is required.");
            if (string.IsNullOrWhiteSpace(ActionToCreate.ACTION_TARGET))
                return OperationResult<Manual_Creation_Action>.Fail("ACTION_TARGET is required.");
            if (ActionToCreate.LIST_DELIVERABLES.Count == 0)
                return OperationResult<Manual_Creation_Action>.Fail("At least one deliverable is required.");
            if (ActionToCreate.PLANNED_START_DATE != DateOnly.MinValue &&
                ActionToCreate.PLANNED_END_DATE   != DateOnly.MinValue &&
                ActionToCreate.PLANNED_START_DATE  > ActionToCreate.PLANNED_END_DATE)
                return OperationResult<Manual_Creation_Action>.Fail("PLANNED_START_DATE cannot be after PLANNED_END_DATE.");

            // Step 2: Verify the gate has been generated for this project
            var gateCheckResult = await _db.SelectAsync(
                SQL_Check_Gate_Exists_For_Project(),
                new[]
                {
                    new SqlParameter("@OppLineId", ActionToCreate.OPP_LINE_ID),
                    new SqlParameter("@StatusId",  ActionToCreate.STATUS_ID)
                },
                cancellationToken);

            if (!gateCheckResult.Success || gateCheckResult.DTResults == null)
                return OperationResult<Manual_Creation_Action>.Fail($"Error checking gate existence: {gateCheckResult.Message}");

            if (Convert.ToInt32(gateCheckResult.DTResults.Rows[0][0]) == 0)
                return OperationResult<Manual_Creation_Action>.Fail(
                    $"Gate '{ActionToCreate.STATUS_ID}' has not been generated for project '{ActionToCreate.OPP_LINE_ID}'.");

            // Steps 3 + 4: SGATE_ID generation and inserts are inside one transaction
            // to prevent duplicate SGATE_IDs under concurrent requests.
            DateTime planStart = ActionToCreate.PLANNED_START_DATE == DateOnly.MinValue
                ? DateTime.Now
                : ActionToCreate.PLANNED_START_DATE.ToDateTime(TimeOnly.MinValue);
            DateTime planEnd = ActionToCreate.PLANNED_END_DATE == DateOnly.MinValue
                ? DateTime.Now
                : ActionToCreate.PLANNED_END_DATE.ToDateTime(TimeOnly.MinValue);

            await using var conn = new SqlConnection(_db.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var tx = conn.BeginTransaction();
            try
            {
                // Generate SGATE_ID inside the transaction — atomic with the INSERT
                var countResult = await _db.SelectAsync(
                    SQL_Count_Manual_Actions_For_Gate(),
                    new[]
                    {
                        new SqlParameter("@OppLineId", ActionToCreate.OPP_LINE_ID),
                        new SqlParameter("@StatusId",  ActionToCreate.STATUS_ID)
                    },
                    cancellationToken);

                if (!countResult.Success || countResult.DTResults == null)
                {
                    tx.Rollback();
                    return OperationResult<Manual_Creation_Action>.Fail($"Error generating action ID: {countResult.Message}");
                }

                int manualCount = Convert.ToInt32(countResult.DTResults.Rows[0][0]);
                ActionToCreate.ACTION_ID = $"{ActionToCreate.STATUS_ID}_M{(manualCount + 1):D2}";

                var actionParams = new[]
                {
                    new SqlParameter("@OppLineId",  ActionToCreate.OPP_LINE_ID),
                    new SqlParameter("@StatusId",   ActionToCreate.STATUS_ID),
                    new SqlParameter("@SGateId",    ActionToCreate.ACTION_ID),
                    new SqlParameter("@Sequence",   ActionToCreate.ACTION_SEQUENCE),
                    new SqlParameter("@GateTarget", ActionToCreate.ACTION_TARGET),
                    new SqlParameter("@PlanStart",  planStart),
                    new SqlParameter("@PlanEnd",    planEnd),
                    new SqlParameter("@User",       UserRequester)
                };

                var actionResult = await _db.NonQueryAsync(SQL_Insert_Manual_Action(), actionParams, tx, cancellationToken);
                if (!actionResult.Success)
                {
                    tx.Rollback();
                    return OperationResult<Manual_Creation_Action>.Fail($"Error inserting action: {actionResult.Message}");
                }

                // DELIVERABLE_ID assigned by index (×10) — never relies on caller's DEL_SEQUENCE
                // to avoid PK violations when multiple deliverables share the default value of 10.
                for (int i = 0; i < ActionToCreate.LIST_DELIVERABLES.Count; i++)
                {
                    var del = ActionToCreate.LIST_DELIVERABLES[i];
                    int deliverableId = (i + 1) * 10;

                    var delParams = new[]
                    {
                        new SqlParameter("@OppLineId",      ActionToCreate.OPP_LINE_ID),
                        new SqlParameter("@StatusId",       ActionToCreate.STATUS_ID),
                        new SqlParameter("@SGateId",        ActionToCreate.ACTION_ID),
                        new SqlParameter("@DeliverableId",  deliverableId),
                        new SqlParameter("@DelName",        del.DELIVERABLE_ACTION),
                        new SqlParameter("@AccepCriteria",  del.DELIVERABLE_ACEPTANCE_CRITERIA),
                        new SqlParameter("@PlanStart",      planStart),
                        new SqlParameter("@PlanEnd",        planEnd),
                        new SqlParameter("@RespJobId",      del.RESPONSIBLE_JOB_TITLE),
                        new SqlParameter("@AccJobId",       del.ACCOUNTABLE_JOB_TITLE),
                        new SqlParameter("@PathToSave",     del.PATH_TO_SAVE),
                        new SqlParameter("@User",           UserRequester)
                    };

                    var delResult = await _db.NonQueryAsync(SQL_Insert_Manual_Deliverable(), delParams, tx, cancellationToken);
                    if (!delResult.Success)
                    {
                        tx.Rollback();
                        return OperationResult<Manual_Creation_Action>.Fail(
                            $"Error inserting deliverable {deliverableId}: {delResult.Message}");
                    }
                }

                tx.Commit();
                return OperationResult<Manual_Creation_Action>.Ok(ActionToCreate);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static string SQL_Check_Gate_Exists_For_Project() => @"
            SELECT COUNT(1)
            FROM   dbo.TRA_PROJECTS_STATUS
            WHERE  OPP_LINE_ID = @OppLineId
              AND  STATUS_ID   = @StatusId";

        private static string SQL_Count_Manual_Actions_For_Gate() => @"
            SELECT COUNT(*)
            FROM   dbo.TRA_PROJECTS_GATES
            WHERE  OPP_LINE_ID = @OppLineId
              AND  STATUS_ID   = @StatusId
              AND  GATE_TYPE   = 'MANUAL'";

        private static string SQL_Insert_Manual_Action() => @"
            INSERT INTO dbo.TRA_PROJECTS_GATES
                   (OPP_LINE_ID, STATUS_ID, SGATE_ID, GATE_SEQUENCE, GATE_TYPE,
                    GATE_STATUS_ID, GATE_TEXT_EXPLANATION, RESPONSIBLE_USER_ID,
                    PLANNED_START_DATE, PLANNED_END_DATE, ACTUAL_START_DATE, ACTUAL_END_DATE,
                    CREATED_BY, CREATED_DATE, MODIFIED_BY, MODIFIED_DATE)
            VALUES (@OppLineId, @StatusId, @SGateId, @Sequence, 'MANUAL',
                    'NOTSTARTED', @GateTarget, '',
                    @PlanStart, @PlanEnd, GETDATE(), GETDATE(),
                    @User, GETDATE(), @User, GETDATE())";

        private static string SQL_Insert_Manual_Deliverable() => @"
            INSERT INTO dbo.TRA_PROJECTS_DELIVERABLES
                   (OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID,
                    DELIVERABLE_STATUS_ID, ACCOUNTABLE_STATUS_ID, DELIVERABLE_CREATION_TYPE,
                    DELIVERABLE_NAME, DELIVERABLE_ACEPTANCE_CRITERIA,
                    PLANNED_START_DATE, PLANNED_END_DATE, ACTUAL_START_DATE, ACTUAL_END_DATE,
                    USER_START_DATE, USER_FINISH_DATE,
                    RESPONSIBLE_JOB_ID, ACCOUNTABLE_JOB_ID,
                    RESPONSIBLE_USER_ID, ACCOUNTABLE_USER_ID,
                    CREATED_BY, CREATED_DATE, MODIFIED_BY, MODIFIED_DATE,
                    PATH_TO_SAVE, NEXT_GATE_TRIGGERS)
            VALUES (@OppLineId, @StatusId, @SGateId, @DeliverableId,
                    'NOTSTARTED', 'NOTSTARTED', 'MANUAL',
                    @DelName, @AccepCriteria,
                    @PlanStart, @PlanEnd, GETDATE(), GETDATE(),
                    GETDATE(), GETDATE(),
                    @RespJobId, @AccJobId,
                    '', '',
                    @User, GETDATE(), @User, GETDATE(),
                    @PathToSave, 0)";
        /// <summary>
        /// Adds a deliverable request into an existing action
        /// </summary>
        /// <param name="DeliverableToCreate"></param>
        /// <param name="UserRequester"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<OperationResult<bool>> Add_Deliverable_To_Existing_Action(Manual_Creation_Deliverable_From_Existing_Action DeliverableToCreate,
                                                                                    string UserRequester = "System",
                                                                                    CancellationToken cancellationToken = default)
        {
            // Step 1: Field validation — no DB
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.OPP_LINE_ID))
                return OperationResult<bool>.Fail("OPP_LINE_ID is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.STATUS_ID))
                return OperationResult<bool>.Fail("STATUS_ID is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.ACTION_ID))
                return OperationResult<bool>.Fail("ACTION_ID is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.DELIVERABLE_ACTION))
                return OperationResult<bool>.Fail("DELIVERABLE_ACTION is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.DELIVERABLE_ACEPTANCE_CRITERIA))
                return OperationResult<bool>.Fail("DELIVERABLE_ACEPTANCE_CRITERIA is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.RESPONSIBLE_JOB_TITLE))
                return OperationResult<bool>.Fail("RESPONSIBLE_JOB_TITLE is required.");
            if (string.IsNullOrWhiteSpace(DeliverableToCreate.ACCOUNTABLE_JOB_TITLE))
                return OperationResult<bool>.Fail("ACCOUNTABLE_JOB_TITLE is required.");

            // Step 2: Verify OPP_LINE_ID exists in TRA_PROJECTS
            var projectCheck = await _db.SelectAsync(
                SQL_Check_Project_Exists(),
                new[] { new SqlParameter("@OppLineId", DeliverableToCreate.OPP_LINE_ID) },
                cancellationToken);

            if (!projectCheck.Success || projectCheck.DTResults == null)
                return OperationResult<bool>.Fail($"Error checking project: {projectCheck.Message}");
            if (Convert.ToInt32(projectCheck.DTResults.Rows[0][0]) == 0)
                return OperationResult<bool>.Fail($"Project '{DeliverableToCreate.OPP_LINE_ID}' not found.");

            // Step 3: Verify both job titles exist in MAS_JOB_TITLES — two independent queries,
            // both always run so all errors are reported together.
            var jobErrors = new List<string>();

            var respJobCheck = await _db.SelectAsync(
                SQL_Check_Job_Title_Exists(),
                new[] { new SqlParameter("@JobTitleId", DeliverableToCreate.RESPONSIBLE_JOB_TITLE) },
                cancellationToken);

            if (!respJobCheck.Success || respJobCheck.DTResults == null)
                return OperationResult<bool>.Fail($"Error checking RESPONSIBLE_JOB_TITLE: {respJobCheck.Message}");
            if (Convert.ToInt32(respJobCheck.DTResults.Rows[0][0]) == 0)
                jobErrors.Add($"RESPONSIBLE_JOB_TITLE '{DeliverableToCreate.RESPONSIBLE_JOB_TITLE}' is not a valid job title.");

            var accJobCheck = await _db.SelectAsync(
                SQL_Check_Job_Title_Exists(),
                new[] { new SqlParameter("@JobTitleId", DeliverableToCreate.ACCOUNTABLE_JOB_TITLE) },
                cancellationToken);

            if (!accJobCheck.Success || accJobCheck.DTResults == null)
                return OperationResult<bool>.Fail($"Error checking ACCOUNTABLE_JOB_TITLE: {accJobCheck.Message}");
            if (Convert.ToInt32(accJobCheck.DTResults.Rows[0][0]) == 0)
                jobErrors.Add($"ACCOUNTABLE_JOB_TITLE '{DeliverableToCreate.ACCOUNTABLE_JOB_TITLE}' is not a valid job title.");

            if (jobErrors.Count > 0)
                return OperationResult<bool>.Fail(string.Join(" | ", jobErrors));

            // Step 4: Verify action exists in TRA_PROJECTS_GATES
            var actionCheck = await _db.SelectAsync(SQL_Check_Action_Exists(),
                                                    new[]
                                                    {
                                                        new SqlParameter("@OppLineId", DeliverableToCreate.OPP_LINE_ID),
                                                        new SqlParameter("@StatusId",  DeliverableToCreate.STATUS_ID),
                                                        new SqlParameter("@ActionId",  DeliverableToCreate.ACTION_ID)
                                                    },
                                                    cancellationToken);

            if (!actionCheck.Success || actionCheck.DTResults == null)
                return OperationResult<bool>.Fail($"Error checking action: {actionCheck.Message}");
            if (Convert.ToInt32(actionCheck.DTResults.Rows[0][0]) == 0)
                return OperationResult<bool>.Fail($"Action '{DeliverableToCreate.ACTION_ID}' not found for project '{DeliverableToCreate.OPP_LINE_ID}' / gate '{DeliverableToCreate.STATUS_ID}'.");

            // Steps 5+6: MAX + INSERT inside one transaction — atomic to prevent duplicate DELIVERABLE_ID
            await using var conn = new SqlConnection(_db.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var tx = conn.BeginTransaction();
            try
            {
                var lastDelResult = await _db.SelectAsync(  SQL_Get_Last_Deliverable_Row(),
                                                            new[]
                                                            {
                                                                new SqlParameter("@OppLineId", DeliverableToCreate.OPP_LINE_ID),
                                                                new SqlParameter("@StatusId",  DeliverableToCreate.STATUS_ID),
                                                                new SqlParameter("@ActionId",  DeliverableToCreate.ACTION_ID)
                                                            },
                                                            cancellationToken);

                if (!lastDelResult.Success || lastDelResult.DTResults == null)
                {
                    tx.Rollback();
                    return OperationResult<bool>.Fail($"Error retrieving last deliverable: {lastDelResult.Message}");
                }

                if (lastDelResult.DTResults.Rows.Count == 0)
                {
                    tx.Rollback();
                    return OperationResult<bool>.Fail($"No deliverables found for action '{DeliverableToCreate.ACTION_ID}'. Cannot add a deliverable to an empty action.");
                }

                int newDeliverableId = GetInt(lastDelResult.DTResults.Rows[0], "DELIVERABLE_ID") + 10;
                string pathToSave    = GetString(lastDelResult.DTResults.Rows[0], "PATH_TO_SAVE");

                var insertParams = new[]
                                        {
                                            new SqlParameter("@OppLineId",     DeliverableToCreate.OPP_LINE_ID),
                                            new SqlParameter("@StatusId",      DeliverableToCreate.STATUS_ID),
                                            new SqlParameter("@ActionId",      DeliverableToCreate.ACTION_ID),
                                            new SqlParameter("@DeliverableId", newDeliverableId),
                                            new SqlParameter("@DelName",       DeliverableToCreate.DELIVERABLE_ACTION),
                                            new SqlParameter("@AccepCriteria", DeliverableToCreate.DELIVERABLE_ACEPTANCE_CRITERIA),
                                            new SqlParameter("@RespJobId",     DeliverableToCreate.RESPONSIBLE_JOB_TITLE),
                                            new SqlParameter("@AccJobId",      DeliverableToCreate.ACCOUNTABLE_JOB_TITLE),
                                            new SqlParameter("@PathToSave",    pathToSave),
                                            new SqlParameter("@User",          UserRequester)
                                        };

                var insertResult = await _db.NonQueryAsync(SQL_Insert_Single_Manual_Deliverable(), insertParams, tx, cancellationToken);
                if (!insertResult.Success)
                {
                    tx.Rollback();
                    return OperationResult<bool>.Fail($"Error inserting deliverable: {insertResult.Message}");
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

        private static string SQL_Check_Project_Exists() => @"
            SELECT COUNT(1)
            FROM   dbo.TRA_PROJECTS
            WHERE  OPPORTUNITY_LINE_ID = @OppLineId";

        private static string SQL_Check_Job_Title_Exists() => @"
            SELECT COUNT(1)
            FROM   dbo.MAS_JOB_TITLES
            WHERE  JOB_TITLE_ID = @JobTitleId";

        private static string SQL_Check_Action_Exists() => @"
            SELECT COUNT(1)
            FROM   dbo.TRA_PROJECTS_GATES
            WHERE  OPP_LINE_ID = @OppLineId
              AND  STATUS_ID   = @StatusId
              AND  SGATE_ID    = @ActionId";

        private static string SQL_Get_Last_Deliverable_Row() => @"
            SELECT TOP 1 DELIVERABLE_ID, PATH_TO_SAVE
            FROM   dbo.TRA_PROJECTS_DELIVERABLES
            WHERE  OPP_LINE_ID = @OppLineId
              AND  STATUS_ID   = @StatusId
              AND  SGATE_ID    = @ActionId
            ORDER BY DELIVERABLE_ID DESC";

        private static string SQL_Insert_Single_Manual_Deliverable() => @"
            INSERT INTO dbo.TRA_PROJECTS_DELIVERABLES
                   (OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID,
                    DELIVERABLE_STATUS_ID, ACCOUNTABLE_STATUS_ID, DELIVERABLE_CREATION_TYPE,
                    DELIVERABLE_NAME, DELIVERABLE_ACEPTANCE_CRITERIA,
                    PLANNED_START_DATE, PLANNED_END_DATE, ACTUAL_START_DATE, ACTUAL_END_DATE,
                    USER_START_DATE, USER_FINISH_DATE,
                    RESPONSIBLE_JOB_ID, ACCOUNTABLE_JOB_ID,
                    RESPONSIBLE_USER_ID, ACCOUNTABLE_USER_ID,
                    CREATED_BY, CREATED_DATE, MODIFIED_BY, MODIFIED_DATE,
                    PATH_TO_SAVE, NEXT_GATE_TRIGGERS)
            VALUES (@OppLineId, @StatusId, @ActionId, @DeliverableId,
                    'NOTSTARTED', 'NOTSTARTED', 'MANUAL',
                    @DelName, @AccepCriteria,
                    GETDATE(), GETDATE(), GETDATE(), GETDATE(),
                    GETDATE(), GETDATE(),
                    @RespJobId, @AccJobId,
                    '', '',
                    @User, GETDATE(), @User, GETDATE(),
                    @PathToSave, 0)";

        private async Task<(bool IsValid, string? ErrorMessage)> ValidateDeliverableStatusExistsAsync(
                                                                                                        string STATUS_ID_TO_UPDATE,
                                                                                                        string keyProcess,
                                                                                                        CancellationToken cancellationToken = default)
                                                                                                                                                         {
            var result = await _db.SelectAsync(
                SQL_Validate_Deliverable_Status(),
                new[] { new SqlParameter("@StatusIdToUpdate", STATUS_ID_TO_UPDATE),
                       new SqlParameter("@KeyProcess", keyProcess) },
                cancellationToken);

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
