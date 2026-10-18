

using Microsoft.Data.SqlClient;
using QUALITY_GATES.Models;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using System.Data;
using System.Data.Common;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.ComponentModel.Design;

namespace QUALITY_GATES.Classes;
public partial class Class_Projects_Quality_Gates
{
    public async Task<SqlActionResult> Generate_Next_Default_Gate(string OPP_LINE_ID, string USER_ID_WHO_REQUEST = "System",CancellationToken cancellationToken = default)
    {
        // Check if requester user has right to do it
        if (USER_ID_WHO_REQUEST != "System") // System always allowed to generate next gate, no matter the job title
        {
            var result = await Get_KeyProcess_Job_title_Restriction("@GATE_GEN", USER_ID_WHO_REQUEST, cancellationToken);
            if (!result.Success)
                return new SqlActionResult { Success = false, Message = result.ErrorMessage };
        }
        // Determine which is the next gate to generate: first one not yet created for this project
        const string sqlNextGate = """
              SELECT T1.STATUS_ID,STATUS_SEQUENCE,T2.STATUS_ID,ISNULL(CURRENT_STATUS,'NO_GEN') AS CURRENT_STATUS
              FROM [SRM].[dbo].[MAS_STATUS] T1
              LEFT JOIN  [SRM].[dbo].[TRA_PROJECTS_STATUS] T2 ON  T2.MODULE_ID=T1.STATUS_MODULE AND T1.STATUS_ID=T2.STATUS_ID AND T2.OPP_LINE_ID=@OppLineId
              WHERE T1.STATUS_MODULE=@ModuleId AND T1.IsDeleted=0
              ORDER BY T1.STATUS_SEQUENCE
              """;
        var parametersNextGate = new[]
                   {
                      new SqlParameter("@ModuleId", _MODULE_ID),
                      new SqlParameter("@OppLineId", OPP_LINE_ID),
                   };
        var queryResult = await _db.SelectAsync(sqlNextGate, parametersNextGate, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return queryResult;

        // Find the first status not yet generated for this project
        DataRow? nextGateRow = null;
        foreach (DataRow row in queryResult.DTResults.Rows)
        {
            if (row["CURRENT_STATUS"]?.ToString()?.Trim() == "NO_GEN")
            {
                nextGateRow = row;
                break;
            }
        }

        if (nextGateRow == null)
        {
            return new SqlActionResult
            {
                Success = true,
                Message = "Next gate is already open",
                RecordsAffected = 0
            };
        }

        // Column 0 is T1.STATUS_ID (the master status, not the transactional one)
        string GateID = nextGateRow[0].ToString()!.Trim();

        const string sqlActions = """
            SELECT MODULE_ID,
                   STATUS_ID,
                   SGATE_ID,
                   SEQUENCE,
                   GATE_TARGET,
                   RESPONSIBLE,
                   ACCOUNTABLE,
                   SUPPORTING,
                   INFORMED,
                   ORIGINAL_START_DATE,
                   ORIGINAL_END_DATE,
                   PROCESS_DAYS,
                   IsDeleted
            FROM dbo.MAS_ACTIONS
            WHERE STATUS_ID = @GateID  AND (IsDeleted IS NULL OR IsDeleted = 0)
            """;
        const string sqlDeliverables = """
            SELECT MODULE_ID,
                   STATUS_ID,
                   SGATE_ID,
                   SEQUENCE,
                   DELIVERABLE_TYPE,
                   DELIVERABLE_ACTION,
                   DELIVERABLE_ACEPTANCE_CRITERIA,
                   INSTRUCTION_LINK,
                   SAMPLE_LINK,
                   DEFAULT_LEAD_TIME_DAYS,
                   RESPONSIBLE_JOB_TITLE,
                   ACCOUNTABLE_JOB_TITLE,
                   SUPORTING_JOB_TITLE,
                   IsDeleted,
                   GENERATES_NEXT_GATE,
                   PATH_TO_SAVE
            FROM dbo.MAS_DELIVERABLES
            WHERE STATUS_ID= @GateID  AND (IsDeleted IS NULL OR IsDeleted = 0)
            """;

        var parametersActGate = new[] { new SqlParameter("@GateID", GateID) };
        queryResult = await _db.SelectAsync(sqlActions, parametersActGate, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return queryResult;
        DataTable dtActions = queryResult.DTResults;

        var parametersDelGate = new[] { new SqlParameter("@GateID", GateID) };
        queryResult = await _db.SelectAsync(sqlDeliverables, parametersDelGate, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return queryResult;
        DataTable dtDeliverables = queryResult.DTResults;

        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        try
        {
            // Default Current for FEASIBILITY Status  WAIT_GPM  untill GPM will be created then NOTSTARTED
            String _Default_Status = "NOTSTARTED";
            if (GateID == "FEAS")
            {
                _Default_Status = "WAIT_GPM";
            }

            var parametersStatus = new[]
            {
                new SqlParameter("@ModuleId", _MODULE_ID),
                new SqlParameter("@OppLineId", OPP_LINE_ID),
                new SqlParameter("@StatusId", GateID),
                new SqlParameter("@GenerationDate", DateTime.UtcNow),
                new SqlParameter("@GenerationUser", USER_ID_WHO_REQUEST),
                new SqlParameter("@CurrentStatus", _Default_Status),
                new SqlParameter("@User", USER_ID_WHO_REQUEST)
            };
            queryResult = await _db.NonQueryAsync(GetInsertDefaulGateStatus(), parametersStatus, tx);
            if (!queryResult.Success)
            {
                tx.Rollback();
                return queryResult;
            }

            foreach (DataRow rowA in dtActions.Rows)
            {
                var parametersAct = new[]
                {
                    new SqlParameter("@OppLineId", OPP_LINE_ID),
                    new SqlParameter("@StatusId", rowA["STATUS_ID"]),
                    new SqlParameter("@SGateId", rowA["SGATE_ID"]),
                    new SqlParameter("@Sequence", rowA["SEQUENCE"]),
                    new SqlParameter("@GateTarget", rowA["GATE_TARGET"]),
                    new SqlParameter("@User", USER_ID_WHO_REQUEST ),
                    new SqlParameter("@PlanStart", DateTime.Now)
                };
                queryResult = await _db.NonQueryAsync(GetInsertDefaulGateActions(GateID), parametersAct, tx);
                if (!queryResult.Success)
                {
                    tx.Rollback();
                    return queryResult;
                }
            }

            foreach (DataRow rowD in dtDeliverables.Rows)
            {
                var parametersDel = new[]
                {
                    new SqlParameter("@OppLineId", OPP_LINE_ID),
                    new SqlParameter("@StatusId", rowD["STATUS_ID"].ToString()),
                    new SqlParameter("@SGateId", rowD["SGATE_ID"].ToString()),
                    new SqlParameter("@DeliverableId", rowD["SEQUENCE"].ToString()),
                    new SqlParameter("@DeliverableName", rowD["DELIVERABLE_ACTION"].ToString()),
                    new SqlParameter("@DeliverableAcepCriteria", rowD["DELIVERABLE_ACEPTANCE_CRITERIA"].ToString()),
                    new SqlParameter("@PlannedStartDate", DateTime.Now),
                    new SqlParameter("@PlannedEndDate", DateTime.Now),
                    new SqlParameter("@ActualStartDate", DateTime.Now),
                    new SqlParameter("@ActualEndDate", DateTime.Now),
                    new SqlParameter("@UseStartDate", DateTime.Now),
                    new SqlParameter("@UserEndDate", DateTime.Now),
                    new SqlParameter("@RESP_Job_Id", rowD["RESPONSIBLE_JOB_TITLE"].ToString()),
                    new SqlParameter("@ACC_Job_Id", rowD["ACCOUNTABLE_JOB_TITLE"].ToString()),
                    new SqlParameter("@RESP_User_Id", ""),
                    new SqlParameter("@ACC_User_Id", ""),
                    new SqlParameter("@User", USER_ID_WHO_REQUEST ),
                    new SqlParameter("@FolderToSave", ""),
                    new SqlParameter("@Generates_Next_Gate",GetBoolean(rowD, "GENERATES_NEXT_GATE"))
 
                     
                };
                queryResult = await _db.NonQueryAsync(GetInsertDefaultGateDeliverables(GateID), parametersDel, tx);
                if (!queryResult.Success)
                {
                    tx.Rollback();
                    return queryResult;
                }
            }

            // Update project's current status to the newly generated gate
            var parametersUpdtProject = new[]
            {
                new SqlParameter("@Project_Id", OPP_LINE_ID),
                new SqlParameter("@Current_Status", GateID)
            };
            queryResult = await _db.NonQueryAsync(GetUpdateProjectCurrentQGStatus(), parametersUpdtProject, tx);
            if (!queryResult.Success)
            {
                tx.Rollback();
                return queryResult;
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            tx.Rollback();
            queryResult = new SqlActionResult
            {
                Success = false,
                Message = $"Error generating next default gate: {ex.Message}",
                RecordsAffected = 0
            };
            return queryResult;
        }

        return queryResult;
    }
    public async Task<SqlActionResult> Generate_Default_Gate_FromInitial(string GateID, CancellationToken cancellationToken = default)
    {
        string _FolderToSave = string.Empty;
        const string sqlProjects = """ 
            SELECT
                OPPORTUNITY_ID,
                OPPORTUNITY_LINE_ID,
                OPPORTUNITY_NAME,
                OPPORTUNITY_LNE_NAME,
                OWNER_SF,
                MATNR
            FROM [dbo].[TRA_PROJECTS]
            WHERE Is_Q_GATES=1 AND IsGeneratedInitial = 0
            """; // For opportunity management
        const string sqlProjects_PR = """
            SELECT
                OPPORTUNITY_ID,
                OPPORTUNITY_LINE_ID,
                OPPORTUNITY_NAME,
                OPPORTUNITY_LNE_NAME,
                OWNER_SF,
                MATNR
            FROM [dbo].[TRA_PROJECTS]
            WHERE Is_Q_GATES_PR=1 AND IsGeneratedInitial_PR = 0
            """; // For Projects
        const string sqlActions = """
            SELECT MODULE_ID,
                   STATUS_ID,
                   SGATE_ID,
                   SEQUENCE,
                   GATE_TARGET,
                   RESPONSIBLE,
                   ACCOUNTABLE,
                   SUPPORTING,
                   INFORMED,
                   ORIGINAL_START_DATE,
                   ORIGINAL_END_DATE,
                   PROCESS_DAYS,
                   IsDeleted
            FROM dbo.MAS_ACTIONS
            WHERE STATUS_ID = @GateID  AND (IsDeleted IS NULL OR IsDeleted = 0)
            """;
        const string sqlDeliverables = """
            SELECT MODULE_ID,
                   STATUS_ID,
                   SGATE_ID,
                   SEQUENCE,
                   DELIVERABLE_TYPE,
                   DELIVERABLE_ACTION,
                   DELIVERABLE_ACEPTANCE_CRITERIA,
                   INSTRUCTION_LINK,
                   SAMPLE_LINK,
                   DEFAULT_LEAD_TIME_DAYS,
                   RESPONSIBLE_JOB_TITLE,
                   ACCOUNTABLE_JOB_TITLE,
                   SUPORTING_JOB_TITLE,
                   IsDeleted,
                   GENERATES_NEXT_GATE,
                   PATH_TO_SAVE
            FROM dbo.MAS_DELIVERABLES
            WHERE STATUS_ID= @GateID  AND (IsDeleted IS NULL OR IsDeleted = 0)         
            """;

        // First recover all projects pending to process depending MODULE_ID
        string _SqlProject = string.Empty;
        switch (_MODULE_ID_ENUM)
        {
            case Module_ID.Q_GATES:
                _SqlProject = sqlProjects;
                break;
            case Module_ID.Q_GATES_PR:
                _SqlProject = sqlProjects_PR;
                break;
            default:
                _SqlProject = string.Empty;
                break;
        }
        var queryResult = await _db.SelectAsync(_SqlProject, null, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; } // Error generating SQL query 
        if (queryResult.Success & queryResult.RecordsAffected == 0)
        { return queryResult; } // No records to process

        DataTable dtProjects = queryResult.DTResults;
      
        // Second recover default actions
        var parametersActGate = new[]
                   {new SqlParameter("@GateID", GateID) };
        queryResult = await _db.SelectAsync(sqlActions, parametersActGate, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; }
        DataTable dtActions = queryResult.DTResults;
        var parametersDelGate = new[]
                  {new SqlParameter("@GateID", GateID) };
        queryResult = await _db.SelectAsync(sqlDeliverables, parametersDelGate, cancellationToken);
        // Third th recover default deliverables
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; }
        DataTable dtDeliverables = queryResult.DTResults;

        // Open transaction
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (DataRow rowP in dtProjects.Rows) // Iterate for each new project generate default actions + Deliverables
            {
                              
                // Insert Gate status in TRA_PROJECTS_STATUS
                var parametersStatus = new[]
                    {
                      new SqlParameter("@ModuleId",_MODULE_ID  ),
                      new SqlParameter("@OppLineId", rowP["OPPORTUNITY_LINE_ID"]),
                      new SqlParameter("@StatusId", GateID),
                      new SqlParameter("@GenerationDate", DateTime.UtcNow),
                      new SqlParameter("@GenerationUser","System"),
                      new SqlParameter("@CurrentStatus","NOTSTARTED"),
                      new SqlParameter("@User", "System")
                    };
                queryResult = await _db.NonQueryAsync(GetInsertDefaulGateStatus(), parametersStatus, tx);
                if (!queryResult.Success)
                {
                    tx.Rollback();
                    return queryResult;
                }
                foreach (DataRow rowA in dtActions.Rows) // Iterate for each new project generate default actions + Deliverables
                {
                    // Actions
                    var parametersAct = new[]
                    {
                      new SqlParameter("@OppLineId", rowP["OPPORTUNITY_LINE_ID"]),
                      new SqlParameter("@StatusId", rowA["STATUS_ID"]),
                      new SqlParameter("@SGateId", rowA["SGATE_ID"]),
                      new SqlParameter("@Sequence",rowA["SEQUENCE"]),
                      new SqlParameter("@GateTarget", rowA["GATE_TARGET"]),
                      new SqlParameter("@User", "System"),
                      new SqlParameter("@PlanStart", DateTime.Now)
                    };
                    queryResult = await _db.NonQueryAsync(GetInsertDefaulGateActions(GateID), parametersAct, tx);
                    if (!queryResult.Success)
                    {
                        tx.Rollback();
                        return queryResult;
                    }
                }
                foreach (DataRow rowD in dtDeliverables.Rows) // Iterate for each new project generate default actions + Deliverables
                {
                    //If gate PCA then we have to create deliverables in an especicic address
                    switch (GateID)
                    {
                        case "PCA":
                            _FolderToSave = "https://grupopremo.sharepoint.com/sites/GlobalProjectManagement/OpportunitiesSFDocs/" + rowP["OPPORTUNITY_LINE_ID"].ToString();
                            break;
                        case "DE_VAL":
                            _FolderToSave = @$"https://grupopremo.sharepoint.com/sites/GlobalProjectManagement/LinkedProjects/{rowP["MATNR"].ToString().Trim()}"+ rowD["PATH_TO_SAVE"].ToString();
                            break;
                        default:
                            _FolderToSave = string.Empty;
                            break;

                    }

                    // Deliverables
                    var parametersDel = new[]
                  {
                      new SqlParameter("@OppLineId", rowP["OPPORTUNITY_LINE_ID"].ToString()),
                      new SqlParameter("@StatusId", rowD["STATUS_ID"].ToString()),
                      new SqlParameter("@SGateId", rowD["SGATE_ID"].ToString()),
                      new SqlParameter("@DeliverableId", rowD["SEQUENCE"].ToString()),
                      new SqlParameter("@DeliverableName",rowD["DELIVERABLE_ACTION"].ToString()),
                      new SqlParameter("@DeliverableAcepCriteria",rowD["DELIVERABLE_ACEPTANCE_CRITERIA"].ToString()),
                      new SqlParameter("@PlannedStartDate", DateTime.Now),
                      new SqlParameter("@PlannedEndDate", DateTime.Now),
                      new SqlParameter("@ActualStartDate", DateTime.Now),
                      new SqlParameter("@ActualEndDate", DateTime.Now),
                      new SqlParameter("@UseStartDate", DateTime.Now),
                      new SqlParameter("@UserEndDate", DateTime.Now),
                      new SqlParameter("@RESP_Job_Id", GetString( rowD, "RESPONSIBLE_JOB_TITLE")),
                      new SqlParameter("@ACC_Job_Id", rowD["ACCOUNTABLE_JOB_TITLE"].ToString()),
                      new SqlParameter("@RESP_User_Id", ""),
                      new SqlParameter("@ACC_User_Id", ""),
                      new SqlParameter("@User", "System"),
                      new SqlParameter("@FolderToSave",  _FolderToSave ),
                      new SqlParameter("@Generates_Next_Gate",GetBoolean(rowD, "GENERATES_NEXT_GATE"))
                    };
                    queryResult = await _db.NonQueryAsync(GetInsertDefaultGateDeliverables(GateID), parametersDel, tx);
                    if (!queryResult.Success)
                    {
                        tx.Rollback();
                        return queryResult;
                    }

                }
                // Finally we have to update Projects as generated = true
                var parametersUpdtProject = new[]
                 { new SqlParameter("@Project_Id", rowP["OPPORTUNITY_LINE_ID"].ToString()),
                   new SqlParameter("@Current_Status", GateID) 
                 };
                queryResult = await _db.NonQueryAsync(GetUpdateProjectAsFeasiilityGenerated(_MODULE_ID_ENUM), parametersUpdtProject, tx);
                if (!queryResult.Success)
                {
                    tx.Rollback();
                    return queryResult;
                }
                
                
            }
            tx.Commit(); // Commit transaction if all operations succeed
            // Assign owner to deliverables whose job title is KEY_ACC_M
            const string sqlUpdateKeyAccM = """
                UPDATE [SRM].[dbo].[TRA_PROJECTS_DELIVERABLES]
                SET RESPONSIBLE_USER_ID = CASE WHEN RESPONSIBLE_JOB_ID = 'KEY_ACC_M' THEN @Owner ELSE RESPONSIBLE_USER_ID END,
                    ACCOUNTABLE_USER_ID = CASE WHEN ACCOUNTABLE_JOB_ID = 'KEY_ACC_M' THEN @Owner ELSE ACCOUNTABLE_USER_ID END,
                    MODIFIED_BY         = 'System',
                    MODIFIED_DATE       = GETDATE()
                WHERE OPP_LINE_ID = @OppLineId
                  AND STATUS_ID   = @StatusId
                  AND (RESPONSIBLE_JOB_ID = 'KEY_ACC_M' OR ACCOUNTABLE_JOB_ID = 'KEY_ACC_M')
                """;
            foreach (DataRow rowP in dtProjects.Rows)
            {
                string ownerSf = GetString(rowP, "OWNER_SF");
                if (string.IsNullOrWhiteSpace(ownerSf))
                    continue;
             
                var paramsUpdate = new[]
                {
                    new SqlParameter("@Owner",     ownerSf),
                    new SqlParameter("@OppLineId", GetString(rowP, "OPPORTUNITY_LINE_ID")),
                    new SqlParameter("@StatusId",  GateID)
                };
                queryResult = await _db.NonQueryAsync(sqlUpdateKeyAccM, paramsUpdate, ct: cancellationToken);
                if (!queryResult.Success)
                    return queryResult;
            }
        }
        catch (Exception ex)
        {
            tx.Rollback();
            queryResult = new SqlActionResult();
            queryResult.Success = false;
            queryResult.Message = $"Error generating default gates and deliverables: {ex.Message}";
            queryResult.RecordsAffected = 0;
            return queryResult;
            throw;

        }

        return queryResult;
    }
    private string GetUpdateProjectCurrentQGStatus()
    {
        return $@"UPDATE [SRM].[dbo].[TRA_PROJECTS]
                     SET CURRENT_QG_STATUS = @Current_Status
                   WHERE OPPORTUNITY_LINE_ID = @Project_Id";
    }
    /// <summary>
    /// Returns the SQL update statement used to mark a project as generated for feasibility, based on the specified
    /// module.
    /// </summary>
    /// <remarks>The returned SQL statement is intended for use with the <see cref="Module_ID.Q_GATES"/>
    /// module. For unsupported modules, the method returns an empty string.</remarks>
    /// <param name="CurrentModule">The module identifier that determines which SQL update statement to return.</param>
    /// <returns>A SQL update statement as a string if the specified module is supported; otherwise, an empty string.</returns>
    private string GetUpdateProjectAsFeasiilityGenerated(Module_ID CurrentModule)
    {
        switch (CurrentModule)
        {
            case Module_ID.Q_GATES:
                return $@" UPDATE [SRM].[dbo].[TRA_PROJECTS]
                           SET IsGeneratedInitial = 1,
                               CURRENT_QG_STATUS=@Current_Status,
                               RELEASED_DATE= GETDATE()
                           WHERE  OPPORTUNITY_LINE_ID= @Project_Id  ";
            case Module_ID.Q_GATES_PR:
                return $@" UPDATE [SRM].[dbo].[TRA_PROJECTS]
                           SET IsGeneratedInitial_PR = 1,
                               CURRENT_QG_STATUS=@Current_Status,
                               RELEASED_DATE= GETDATE()
                           WHERE  OPPORTUNITY_LINE_ID= @Project_Id  ";
            default:
                return string.Empty;
        }
  
                          
    }
    private string GetInsertDefaulGateStatus()
    {
        return $@"INSERT INTO [SRM].[dbo].[TRA_PROJECTS_STATUS]
                          ([MODULE_ID]
                          ,[OPP_LINE_ID]
                          ,[STATUS_ID]
                          ,[GENERATION_DATE]
                          ,[GENERATION_USER]
                          ,[CURRENT_STATUS]
                          ,[TIMES_REOPENED])
                   VALUES(
                          @ModuleId,
                          @OppLineId,
                          @StatusId,
                          @GenerationDate,
                          @GenerationUser,
                          @CurrentStatus, 
                          0)";
    }
    private string GetInsertDefaulGateActions(string GateID)
    {
        return $@"INSERT INTO [SRM].[dbo].[TRA_PROJECTS_GATES]
                          ([OPP_LINE_ID],
                          [STATUS_ID],
                          [SGATE_ID],
                          GATE_SEQUENCE,
                          [GATE_TYPE],
                          [GATE_STATUS_ID],
                          [GATE_TEXT_EXPLANATION],
                          [RESPONSIBLE_USER_ID],
                          [PLANNED_START_DATE],
                          [PLANNED_END_DATE],
                          [ACTUAL_START_DATE],
                          [ACTUAL_END_DATE],
                          [CREATED_BY],
                          [CREATED_DATE],
                          [MODIFIED_BY],
                          [MODIFIED_DATE])
                   VALUES(
                          @OppLineId,
                          @StatusId,
                          @SGateId,
                          @Sequence,
                          'DEFAULT',
                          'NOTSTARTED', 
                          @GateTarget,
                          '',
                          @PlanStart,
                          '2026-07-20',  --PLANNED_END_DATE
                          GETDATE(),    --ACTUAL_START_DATE
                          '2026-07-20', --ACTUAL_END_DATE
                          @User,
                          GETDATE(),    --CREATED_DATE
                          @User,        --MODIIFIED_BY
                          GETDATE());   --MODIFIED_DATE";
    }
    private string GetInsertDefaultGateDeliverables(string GateID)
    {
        return $@"INSERT INTO [SRM].[dbo].[TRA_PROJECTS_DELIVERABLES]
                             ([OPP_LINE_ID]
                             ,[STATUS_ID]
                             ,[SGATE_ID]
                             ,[DELIVERABLE_ID]
                             ,[DELIVERABLE_STATUS_ID]
                             ,[ACCOUNTABLE_STATUS_ID]
                             ,[DELIVERABLE_CREATION_TYPE]
                             ,[DELIVERABLE_NAME]
                             ,[DELIVERABLE_ACEPTANCE_CRITERIA]
                             ,[PLANNED_START_DATE]
                             ,[PLANNED_END_DATE]
                             ,[ACTUAL_START_DATE]
                             ,[ACTUAL_END_DATE]
                             ,[USER_START_DATE]
                             ,[USER_FINISH_DATE]
                             ,[RESPONSIBLE_JOB_ID]
                             ,[ACCOUNTABLE_JOB_ID]
                             ,[RESPONSIBLE_USER_ID]
                             ,[ACCOUNTABLE_USER_ID]
                             ,[CREATED_BY]
                             ,[CREATED_DATE]
                             ,[MODIFIED_BY]
                             ,[MODIFIED_DATE]
                             ,PATH_TO_SAVE
                             ,NEXT_GATE_TRIGGERS)
                   VALUES(
                          @OppLineId,
                          @StatusId,
                          @SGateId,
                          @DeliverableId,
                          'NOTSTARTED', 
                          'NOTSTARTED', 
                          'DEFAULT',
                          @DeliverableName,
                          @DeliverableAcepCriteria,
                          @PlannedStartDate,
                          @PlannedEndDate,
                          @ActualStartDate,
                          @ActualEndDate,
                          @UseStartDate,
                          @UserEndDate,
                          @RESP_Job_Id,
                          @ACC_Job_Id,
                          @RESP_User_Id,
                          @ACC_User_Id,
                          @User,
                          GETDATE(),    --CREATED_DATE
                          @User,        --MODIIFIED_BY
                          GETDATE(),   --MODIFIED_DATE
                          @FolderToSave,
                          @Generates_Next_Gate)"; 
    }
}
 
