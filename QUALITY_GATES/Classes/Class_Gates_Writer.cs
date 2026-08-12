using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Threading;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;


namespace QUALITY_GATES.Classes
{
    public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
    {
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
                                                                       string userId,
                                                                       CancellationToken cancellationToken = default)
        {
            var keyProcess = RoleDeliverable == DeliverableRolesEstructure.Responsible ? "DEL_RESP" : "DEL_ACC";

            var validationResult = await _db.GetDatatableFromSelectAsync(
                SQL_Validate_Deliverable_Status(),
                new[] { new SqlParameter("@StatusIdToUpdate", STATUS_ID_TO_UPDATE), 
                       new SqlParameter("@KeyProcess", keyProcess) },
                cancellationToken: cancellationToken);

            if (!validationResult.Success || validationResult.DTResults == null)
                return OperationResult<bool>.Fail($"Error validating status: {validationResult.Message}");

            if (Convert.ToInt32(validationResult.DTResults.Rows[0][0]) == 0)
                return OperationResult<bool>.Fail($"Status '{STATUS_ID_TO_UPDATE}' is not valid for role '{RoleDeliverable}'.");

            var updateSql = RoleDeliverable == DeliverableRolesEstructure.Responsible
                ? SQL_Update_Deliverable_Responsible_Status()
                : SQL_Update_Deliverable_Accountable_Status();

            var updateParams = new[]
            {
                new SqlParameter("@StatusIdToUpdate", STATUS_ID_TO_UPDATE),
                new SqlParameter("@OppLineId",        oppLineId),
                new SqlParameter("@StatusId",         statusId),
                new SqlParameter("@SgateId",          sgateId),
                new SqlParameter("@DelId",            delId),
                new SqlParameter("@UserId",           userId)
            };

            var result = await _db.NonQueryDataToSQLServer(updateSql, updateParams, cancellationToken: cancellationToken);

            if (!result.Success)
                return OperationResult<bool>.Fail($"Error updating deliverable status: {result.Message}");

            if (result.RecordsAffected == 0)
                return OperationResult<bool>.Fail($"Deliverable '{oppLineId}/{statusId}/{sgateId}/{delId}' not found.");

            return OperationResult<bool>.Ok(true);
        }

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
                                                                       string userId,
                                                                       CancellationToken cancellationToken = default)
        {
            // TODO: validar que userId tiene permiso para modificar la prioridad del proyecto antes de ejecutar el UPDATE

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

    }
}
