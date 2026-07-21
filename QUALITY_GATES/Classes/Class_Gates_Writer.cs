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
                                                                       int delId)
        {
            await Task.CompletedTask;
            return OperationResult<bool>.Ok(true);
        }
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
                      (OPP_LINE_ID, STATUS_ID, SGATE_ID, DEL_ID, COMMENT_ID, COMMENT_TEXT, COMMENT_BY,COMMENT_DATE)
                VALUES
                      (@OppLineId, @StatusId, @SgateId, @DelId, @CommentId, @CommentText, @CommentBy,SYSDATETIME() )";

    }


}
