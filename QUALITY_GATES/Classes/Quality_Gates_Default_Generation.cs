namespace QUALITY_GATES.Classes;

using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using System.Data;

public partial class Class_Projects_Quality_Gates
{
    public async Task<Return_SQL_Action> Generate_Default_Gate(
        string GateID,
        CancellationToken cancellationToken = default)
    {
        //PROJECTS CurProject = new PROJECTS();

        const string sqlProjects = """
            SELECT
                OPPORTUNITY_ID,
                OPPORTUNITY_LINE_ID,
                OPPORTUNITY_NAME,
                OPPORTUNITY_LNE_NAME
            FROM [dbo].[TRA_PROJECTS]
            WHERE IsGeneratedFeasibility = 0
            """;
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
            WHERE STATUS_ID = 'FEAS'  AND (IsDeleted IS NULL OR IsDeleted = 0)
            """;
        const string sqlDeliverables = """
            SELECT MODULE_ID,
                   STATUS_ID,
                   SGATE_ID,
                   SEQUENCE,
                   DELIVERABLE_TYPE,
                   DELIVERABLE_ACTION,
                   DELIVERABLE_ACEPTANCE_CRITERIA,
                   DELIVERABLE_LINK,
                   DELIVERABLE_TEXT_USER,
                   INSTRUCTION_LINK,
                   SAMPLE_LINK,
                   DEFAULT_LEAD_TIME_DAYS,
                   RESPONSIBLE_JOB_TITLE,
                   ACCOUNTABLE_JOB_TITLE,
                   SUPORTING_JOB_TITLE,
                   IsDeleted
            FROM dbo.MAS_DELIVERABLES
            WHERE STATUS_ID='FEAS'  AND (IsDeleted IS NULL OR IsDeleted = 0)         
            """;

        // First recover all projects pending to process.
        var queryResult = await _db.GetDatatableFromSelectAsync(sqlProjects, null, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; } // Error generating SQL query 
        if (queryResult.Success & queryResult.RecordsAffected == 0)
        { return queryResult; } // No records to process
        DataTable dtProjects = queryResult.DTResults;
        // Second recover default actions
        queryResult = await _db.GetDatatableFromSelectAsync(sqlActions, null, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; }
        DataTable dtActions = queryResult.DTResults;
        queryResult = await _db.GetDatatableFromSelectAsync(sqlDeliverables, null, cancellationToken);
        // Third recover default deliverables
        if (!queryResult.Success || queryResult.DTResults == null)
        { return queryResult; }
        DataTable dtDeliverables = queryResult.DTResults;

        // Open transaction
        await using var conn = await _db.CreateOpenConnectionAsync();
        await using var tx = conn.BeginTransaction();
        try
        {
            foreach (DataRow rowP in dtProjects.Rows) // Iterate for each new project generate default actions + Deliverables
            {
                foreach (DataRow rowA in dtActions.Rows) // Iterate for each new project generate default actions + Deliverables
                {
                    // Actions
                    var parametersAct = new[]
                    {
                      new SqlParameter("@OppLineId", rowP["OPPORTUNITY_LINE_ID"]),
                      new SqlParameter("@StatusId", rowA["STATUS_ID"]),
                      new SqlParameter("@SGateId", rowA["SGATE_ID"]),
                      new SqlParameter("@GateTarget", rowA["GATE_TARGET"]),
                      new SqlParameter("@User", "System"),
                      new SqlParameter("@PlanStart", DateTime.Now)
                    };
                    queryResult = await _db.NonQueryDataToSQLServer(GetInsertDefaulGateActions(GateID), parametersAct, transaction: tx);
                    if (!queryResult.Success)
                    {
                        tx.Rollback();
                        return queryResult;
                    }
                }
                foreach (DataRow rowD in dtDeliverables.Rows) // Iterate for each new project generate default actions + Deliverables
                {
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
                      new SqlParameter("@RESP_Job_Id", rowD["RESPONSIBLE_JOB_TITLE"].ToString()),
                      new SqlParameter("@ACC_Job_Id", rowD["ACCOUNTABLE_JOB_TITLE"].ToString()),
                      new SqlParameter("@RESP_User_Id", ""),
                      new SqlParameter("@ACC_User_Id", ""),
                      new SqlParameter("@User", "System")

                    };
                    queryResult = await _db.NonQueryDataToSQLServer(GetInsertDefaultGateDeliverables(GateID), parametersDel, transaction: tx);
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
                queryResult = await _db.NonQueryDataToSQLServer(GetUpdateProjectAsFeasiilityGenerated(), parametersUpdtProject, transaction: tx);
                if (!queryResult.Success)
                {
                    tx.Rollback();
                    return queryResult;
                }
                
                
            }
            tx.Commit(); // Commit transaction if all operations succeed
        }
        catch (Exception ex)
        {
            tx.Rollback();
            queryResult = new Return_SQL_Action();
            queryResult.Success = false;
            queryResult.Message = $"Error generating default gates and deliverables: {ex.Message}";
            queryResult.RecordsAffected = 0;
            return queryResult;
            throw;

        }

        return queryResult;
    }
    private string GetUpdateProjectAsFeasiilityGenerated()
    {
        return $@" UPDATE [SRM].[dbo].[TRA_PROJECTS]
                          SET IsGeneratedFeasibility = 1,
                              CURRENT_QG_STATUS=@Current_Status,
                              RELEASED_DATE= GETDATE()
                     WHERE  OPPORTUNITY_LINE_ID= @Project_Id  ";
                          
    }
    private string GetInsertDefaulGateActions(string GateID)
    {
        return $@"INSERT INTO [SRM].[dbo].[TRA_PROJECTS_GATES]
                          ([OPP_LINE_ID],
                          [STATUS_ID],
                          [SGATE_ID],
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
                             ,[MODIFIED_DATE])
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
                          GETDATE());   --MODIFIED_DATE";
    }
}
 
