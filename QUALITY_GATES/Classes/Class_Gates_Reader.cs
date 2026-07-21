namespace QUALITY_GATES.Classes;

using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Threading;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;

public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
{
    
    public async Task<OperationResult<List<PROJECTS_HEADER>>> Get_Projects_Header_Details()
   
    {
        CancellationToken cancellationToken = default;
        OperationResult<List<PROJECTS_HEADER>> resultado = new OperationResult<List<PROJECTS_HEADER>>();

        List<PROJECTS_HEADER> currentprojectList = new List<PROJECTS_HEADER>();
        try
        {
            var queryResult = await _db.GetDatatableFromSelectAsync(SQL_TRA_PROJECTS(), null, cancellationToken: cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
            { return OperationResult<List<PROJECTS_HEADER>>.Fail($"Error : {queryResult.Message}");};
            // Here we need to feed objects
            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                // Price logical now works with Target Price on opp_line this data will be mandatory.
                double TargetPrice = GetDouble(row, "PRICE_1Y");
                if (TargetPrice == 0)
                {
                    TargetPrice = 1;// Target price will be mandatory on salesforce but now it is notg and can arrive 0 Value
                  
                }
                PROJECTS_HEADER project = new PROJECTS_HEADER
                {
                    OPP_ID = GetString(row, "OPPORTUNITY_ID"),
                    OPP_LINE_ID = GetString(row, "OPPORTUNITY_LINE_ID"),
                    OPP_NAME = GetString(row, "OPPORTUNITY_NAME"),
                    OPP_LINE_NAME = GetString(row, "OPPORTUNITY_LNE_NAME"),
                    SALES_ORGANIZATION = GetString(row,"SALES_ORGANIZATION"),
                    BUSINESS_UNIT = GetString(row, "BUSINESS_UNIT"),
                    PRODUCT_CATEGORY = GetString(row, "PRODUCT_CATEGORY"),
                    OWNER = GetString(row, "OWNER_SF"),
                    PROJECT_SALES_FORCE_LINK= GetString(row, "SF_LINK"),
                    RELEASED_DATE= GetDateOnly(row, "RELEASED_DATE"),
                    AGEING_DAYS=  DateOnly.FromDateTime(DateTime.Today).DayNumber - GetDateOnly(row, "RELEASED_DATE").DayNumber ,
                    CUST_NAME = GetString(row, "CUST_NAME"),
                    CUST_SAP_CODE = GetString(row, "SAP_CUSTOMER"),
                    CUST_PARENT= GetString(row, "PARENT_NAME"),
                    SOP = GetDateOnly(row, "SOP"),
                    CURRENT_GATE_ID = GetString(row, "CURRENT_QG_STATUS"),
                    TOTAL_DELIVERABLES = GetInt(row, "#_DELIVERABLES"),
                    TOTAL_DELIVERABLES_PENDING_OWNER = GetInt(row, "PENDING_RESPONSIBLE"),
                    TOTAL_DELIVERABLES_PENDING_ACCOUNTANT = GetInt(row, "PENDING_ACCOUNTANT"),
                    PARTS_1Y = GetInt(row, "PIECES_1Y"),
                    VALUE_1Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_1Y") * TargetPrice)),
                    PARTS_2Y= GetInt(row, "PIECES_2Y"),
                    VALUE_2Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_2Y") * TargetPrice)),
                    PARTS_3Y = GetInt(row, "PIECES_3Y"),
                    VALUE_3Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_3Y") * TargetPrice)),
                    PARTS_4Y = GetInt(row, "PIECES_4Y"),
                    VALUE_4Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_4Y") * TargetPrice)),
                    VALUE_TOTAL_ALL_EUR = Convert.ToInt32((GetInt(row, "PIECES_1Y") * TargetPrice))+ Convert.ToInt32((GetInt(row, "PIECES_2Y") * TargetPrice))+ Convert.ToInt32((GetInt(row, "PIECES_3Y") * TargetPrice))+ Convert.ToInt32((GetInt(row, "PIECES_4Y") * TargetPrice))
                };
                currentprojectList.Add(project);
            }
            return OperationResult<List<PROJECTS_HEADER>>.Ok(_db.DeepCopyList(currentprojectList));
        }
        catch (Exception ex)
        {
            return OperationResult<List<PROJECTS_HEADER>>.Fail($"Error inesperado: {ex.Message}");
            throw;
        }

       
        
    }
   
       private static string SQL_Projects_Gates() => @"
        SELECT
            SGATE_ID,
            GATE_SEQUENCE,
            GATE_TYPE,
            GATE_TEXT_EXPLANATION,
            GATE_STATUS_ID,
            RESPONSIBLE_USER_ID,
            [PLANNED_START_DATE],
            [PLANNED_END_DATE],
            [ACTUAL_START_DATE],
            [ACTUAL_END_DATE]
        FROM  [dbo].[TRA_PROJECTS_GATES]
        WHERE OPP_LINE_ID = @OppLineId AND STATUS_ID = @GateId
        ORDER BY ISNULL(GATE_SEQUENCE, 0)";

    private static string SQL_Projects_Deliverables() => @"
      SELECT    [DELIVERABLE_ID]
               ,[DELIVERABLE_STATUS_ID]
               ,[ACCOUNTABLE_STATUS_ID]
               ,T4.GATE_STATUS_DESC DEL_STATUS_DESC
               ,T5.GATE_STATUS_DESC ACC_STATUS_DESC
               ,T4.IS_FINAL_STATE RESP_STATUS_iSFINAL 
               ,T5.IS_FINAL_STATE ACC_STATUS_iSFINAL
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
               ,T2.JOB_TITLE_DESCRIPTION AS RESP_DESCR
               ,T3.JOB_TITLE_DESCRIPTION AS ACC_DESCR
               ,[RESPONSIBLE_USER_ID]
               ,[ACCOUNTABLE_USER_ID]
     FROM  [dbo].[TRA_PROJECTS_DELIVERABLES] T1
     LEFT JOIN dbo.MAS_JOB_TITLES T2 ON T2.JOB_TITLE_ID=T1.RESPONSIBLE_JOB_ID
     LEFT JOIN dbo.MAS_JOB_TITLES T3 ON T3.JOB_TITLE_ID=T1.ACCOUNTABLE_JOB_ID
     LEFT JOIN dbo.MAS_GATE_STATUS T4 ON T4.GATE_STATUS_ID=T1.DELIVERABLE_STATUS_ID
     LEFT JOIN dbo.MAS_GATE_STATUS T5 ON T5.GATE_STATUS_ID=T1.ACCOUNTABLE_STATUS_ID
     WHERE T1.OPP_LINE_ID = @OppLineId AND T1.STATUS_ID = @GateId AND T1.SGATE_ID = @ActionId
     ORDER BY DELIVERABLE_ID";


    private string SQL_TRA_PROJECTS_DETAIL()
    {
        return $@"SELECT OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                      CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                      PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF
               FROM dbo.TRA_PROJECTS T1
               WHERE CURRENT_QG_STATUS='FEAS' AND [IsGeneratedFeasibility]=1 AND OPPORTUNITY_LINE_ID= @OppLineId";
    
    }
    private string SQL_TRA_PROJECTS()
    {
        return $@"SELECT OPPORTUNITY_ID,
                         OPPORTUNITY_LINE_ID,
                         OPPORTUNITY_NAME,
                         OPPORTUNITY_LNE_NAME,
                         MATNR,DESCRIPTION,
                         SAP_CUSTOMER,
                         CUST_NAME,
                         SALES_ORGANIZATION,
                         PRODUCT_CATEGORY,
                         BUSINESS_UNIT,
                         CURRENT_QG_STATUS,
                         RELEASED_DATE,
                         PIECES_1Y,
                         PIECES_2Y,
                         PIECES_3Y,
                         PIECES_4Y,
                         PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,
                         PARENT_NAME,SF_LINK,
                         MIN(PLANNED_START_DATE)MIN_PLAN,
                         MAX(PLANNED_END_DATE)MAX_PLAN,
                         MIN(ACTUAL_START_DATE)MIN_ACTUAL,
                         MAX(ACTUAL_END_DATE)MAX_ACTUAL,
                         MIN(USER_START_DATE) MIN_USER,
                         MAX(USER_FINISH_DATE)MAX_USER,
                         COUNT(*) AS #_DELIVERABLES,
                         (COUNT(*) - SUM(CASE T3.IS_FINAL_STATE WHEN 1 THEN 1 ELSE 0 END))PENDING_RESPONSIBLE,
                         (COUNT(*)  - SUM(CASE T4.IS_FINAL_STATE WHEN 1 THEN 1 ELSE 0 END))PENDING_ACCOUNTANT
                 FROM dbo.TRA_PROJECTS T1
                 LEFT JOIN dbo.TRA_PROJECTS_DELIVERABLES T2 ON T2.OPP_LINE_ID=OPPORTUNITY_LINE_ID
                 LEFT JOIN dbo.MAS_GATE_STATUS T3 ON T3.GATE_STATUS_ID=T2.DELIVERABLE_STATUS_ID
                 LEFT JOIN dbo.MAS_GATE_STATUS T4 ON T4.GATE_STATUS_ID=T2.ACCOUNTABLE_STATUS_ID
                 WHERE CURRENT_QG_STATUS='FEAS' AND [IsGeneratedFeasibility]=1
                 GROUP BY OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                          CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                          PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,PARENT_NAME,SF_LINK";
    }
    /// <summary>
    /// Brings from DB maximum detail for an especif Project PPROJECT->STATUS->GATES->DELIVERABLES->DELIVERABLES FILES-> DELIVERABLES COMMENTS    
    /// </summary>
    /// <param name="Opp_Line_ID,">Unique Sales Forve ID of the project to display data</param>
    /// <param name="Gate_Id">Gate ID we want to display gates by default display FEAS</param>
    /// <returns>Project detail object</returns>
    public async Task<OperationResult<PROJECT_DETAIL>> Get_Project_Max_Details(string Opp_Line_ID, string Gate_Id = "FEAS")
    {
        PROJECT_DETAIL project = new PROJECT_DETAIL();
        List<GATES_ACTIONS> CurListAction = new List<GATES_ACTIONS>();
        try
        {
            CancellationToken cancellationToken = default;

            // 1 — Cabecera del proyecto
            var paramsPro = new[] { new SqlParameter("@OppLineId", Opp_Line_ID) };
            var queryResult = await _db.GetDatatableFromSelectAsync(SQL_TRA_PROJECTS_DETAIL(), paramsPro, cancellationToken: cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<PROJECT_DETAIL>.Fail($"Error: {queryResult.Message}");
            if (queryResult.RecordsAffected != 1)
                return OperationResult<PROJECT_DETAIL>.Fail("Error: More than one project record from datatable");

            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                project = new PROJECT_DETAIL
                {
                    OPP_ID          = GetString(row, "OPPORTUNITY_ID"),
                    OPP_LINE_ID     = GetString(row, "OPPORTUNITY_LINE_ID"),
                    OPP_NAME        = GetString(row, "OPPORTUNITY_NAME"),
                    OPP_LINE_NAME   = GetString(row, "OPPORTUNITY_LNE_NAME"),
                    CURRENT_GATE_ID = GetString(row, "CURRENT_QG_STATUS"),
                };
            }
            // 2.- Retry all files related to this project in one query, grouped in memory
            var FilesOfProject = await Get_DMS_FilesPerproject(Opp_Line_ID);
            if (!FilesOfProject.Success )
            {
                return OperationResult<PROJECT_DETAIL>.Fail($"Error: {FilesOfProject.ErrorMessage}");
            }
                // 2 — Todos los comentarios del gate en una sola query, agrupados en memoria
                var paramsComments = new[]
            {
                new SqlParameter("@OppLineId", Opp_Line_ID),
                new SqlParameter("@GateId",    Gate_Id)
            };
            var commentsResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Deliverable_Comments(), paramsComments, cancellationToken: cancellationToken);
            var commentMap = new Dictionary<(string sgateId, int delivId), List<DELIVERABLES_COMMENTS>>();
            if (commentsResult.Success && commentsResult.DTResults != null)
            {
                foreach (DataRow row in commentsResult.DTResults.Rows)
                {
                    var key = (GetString(row, "SGATE_ID").Trim(), GetInt(row, "DEL_ID"));
                    if (!commentMap.ContainsKey(key))
                        commentMap[key] = new List<DELIVERABLES_COMMENTS>();
                    commentMap[key].Add(new DELIVERABLES_COMMENTS
                    {
                        Id           = new Guid(GetString(row, "COMMENT_ID")),
                        COMMENT_TEXT = GetString(row, "COMMENT_TEXT"),
                        USER         = GetString(row, "COMMENT_BY"),
                        COMMENT_DATE = GetDateTime(row, "COMMENT_DATE"),
                    });
                }
            }

            // 3 — Actions del gate
            var paramsGate = new[]
            {
                new SqlParameter("@OppLineId", Opp_Line_ID),
                new SqlParameter("@GateId",    Gate_Id)
            };
            var actionsResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Gates(), paramsGate, cancellationToken: cancellationToken);
            if (!actionsResult.Success || actionsResult.DTResults == null)
                return OperationResult<PROJECT_DETAIL>.Fail($"Error querying actions: {actionsResult.Message}");

            foreach (DataRow row in actionsResult.DTResults.Rows)
            {
                var curAction = new GATES_ACTIONS
                {
                    ACTION_ID                    = GetString(row, "SGATE_ID"),
                    ACTION_SEQUENCE              = GetInt(row, "GATE_SEQUENCE"),
                    ACTION_TARGET                = GetString(row, "GATE_TEXT_EXPLANATION"),
                    ACTION_GENERATION_TYPE       = GetString(row, "GATE_TYPE"),
                    
                    
                };

                // 4 — Deliverables de cada action
                var paramsDeliv = new[]
                {
                    new SqlParameter("@OppLineId", Opp_Line_ID),
                    new SqlParameter("@GateId",    Gate_Id),
                    new SqlParameter("@ActionId",  curAction.ACTION_ID)
                };
                var delivResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Deliverables(), paramsDeliv, cancellationToken: cancellationToken);
                if (!delivResult.Success || delivResult.DTResults == null)
                    return OperationResult<PROJECT_DETAIL>.Fail($"Error querying deliverables: {delivResult.Message}");

                var curListDeliverable = new List<GATES_DELIVERABLES>();
                foreach (DataRow rowDel in delivResult.DTResults.Rows)
                {
                    var delivId = GetInt(rowDel, "DELIVERABLE_ID");
                    // para cada deliverable tenemos que construir la lista de Ficheros asociados
                    var FilesFilteredList = FilterFilesDeliverables(
                                                            FilesOfProject.Data,
                                                            Opp_Line_ID,
                                                            Gate_Id,
                                                            curAction.ACTION_ID,
                                                            delivId);
                    // Then from filtered list we can create list of files asociated to deliverable
                    DELIVERABLE_FILE CurFile;
                    List<DELIVERABLE_FILE> ListFiles = new List<DELIVERABLE_FILE>();
                    if (FilesFilteredList.Count > 0)
                     {
                        foreach (DELIVERABLE_FILE_FROM_DMS item in FilesFilteredList)
                        {
                            ListFiles.Add(new DELIVERABLE_FILE
                            {
                                Id=item.Id,
                                FileName =item.FileName,
                                FilePath = item.FilePath,
                                FileType = item.FileType,
                                UserUpload  = item.UserUpload,
                                UploadDate = item.UploadDate,
                            });
                        }
                    }


                    
                    var commentKey = (curAction.ACTION_ID.Trim(), delivId);
                    var delivComments = commentMap.TryGetValue(commentKey, out var foundComments)
                        ? new List<DELIVERABLES_COMMENTS>(foundComments)
                        : new List<DELIVERABLES_COMMENTS>();
                    curListDeliverable.Add(new GATES_DELIVERABLES
                    {
                        DELIVERABLE_SEQUENCE            = delivId,
                        DELIVERABLE_DESCRIPTION         = GetString(rowDel, "DELIVERABLE_NAME"),
                        DELIVERABLE_TYPE_GENERATION     = GetString(rowDel, "DELIVERABLE_CREATION_TYPE"),
                        DELIVERABLE_ACCEPTANCE_CRITERIA = GetString(rowDel, "DELIVERABLE_ACEPTANCE_CRITERIA"),
                        DELIVERABLE_STATUS_ID           = GetString(rowDel, "DELIVERABLE_STATUS_ID"),
                        DELIVERABLE_STATUS_DESCRIPTION  = GetString(rowDel, "DEL_STATUS_DESC"),
                        IS_DELIVERABLE_RESPONSIBLE_FINISH = GetBoolean(rowDel, "RESP_STATUS_iSFINAL"),
                        IS_DELIVERABLE_ACCOUNTED_FINISH = GetBoolean(rowDel, "ACC_STATUS_iSFINAL"),
                        ACCOUNTED_STATUS_ID             = GetString(rowDel, "ACC_STATUS_DESC"),
                        DELIVERABLE_TYPE                = GetString(rowDel, "DELIVERABLE_CREATION_TYPE"),
                        PLANNED_START_DATE              = GetDateOnly(rowDel, "PLANNED_START_DATE"),
                        PLANNED_END_DATE                = GetDateOnly(rowDel, "PLANNED_END_DATE"),
                        ACTUAL_START_DATE               = GetDateOnly(rowDel, "ACTUAL_START_DATE"),
                        ACTUAL_END_DATE                 = GetDateOnly(rowDel, "ACTUAL_END_DATE"),
                        USER_START_DATE                 = GetDateOnly(rowDel, "USER_START_DATE"),
                        USER_END_DATE                   = GetDateOnly(rowDel, "USER_FINISH_DATE"),
                        ACCOUNTABLE_JOB_ID               = GetString(rowDel, "ACCOUNTABLE_JOB_ID"),
                        ACCOUNTABLE_JOB_NAME             = GetString(rowDel, "ACC_DESCR"),
                        RESPONSIBLE_JOB_ID               = GetString(rowDel, "RESPONSIBLE_JOB_ID"),
                        RESPONSIBLE_JOB_NAME             = GetString(rowDel, "RESP_DESCR"),
                        RESPONSIBLE_USER_ID              = GetString(rowDel, "RESPONSIBLE_USER_ID"),
                        RESPONSIBLE_USER_NAME            = String.Empty,  // We need to implement this
                        ACCOUNTABLE_USER_ID              = GetString(rowDel, "ACCOUNTABLE_USER_ID"),
                        ACCOUNTABLE_USER_NAME            = String.Empty,  // We need to implement this
                        DeliverableComments              = delivComments,
                        DeliverableFiles                 = ListFiles
                    });
                }
                curAction.List_Deliverables = curListDeliverable;
                CurListAction.Add(curAction);
            }

            project.List_Actions = CurListAction;
            return OperationResult<PROJECT_DETAIL>.Ok(project);
        }
        catch (Exception ex)
        {
            return OperationResult<PROJECT_DETAIL>.Fail($"Error: {ex.Message}");
        }
    }
    /// <summary>
    /// Funtion recovers all files from a given project here we filter by specific deliverable file within a given project
    /// </summary>
    /// <returns>Filtered list for adding by a given deliverable</returns>
    internal List<DELIVERABLE_FILE_FROM_DMS> FilterFilesDeliverables(
                                                            List<DELIVERABLE_FILE_FROM_DMS> lista,
                                                            string? oppLineId = null,
                                                            string? gateId = null,
                                                            string? statusId = null,
                                                            int? deliverableId = null)
    {
        var query = lista.AsEnumerable();

        if (!string.IsNullOrEmpty(oppLineId))
            query = query.Where(x => x.OppLineId.ToString().Trim() == oppLineId.ToString().Trim());

        if (!string.IsNullOrEmpty(gateId))
            query = query.Where(x => x.Gate_Id.ToString().Trim() == gateId.ToString().Trim());

        if (!string.IsNullOrEmpty(statusId))
            query = query.Where(x => x.StatusiD.ToString().Trim() == statusId.ToString().Trim());

        if (deliverableId.HasValue)
            query = query.Where(x => x.DeliverableID == deliverableId.Value);

        return query.ToList();
    }

    private static string SQL_Projects_Deliverable_Comments() => @"
         SELECT C.SGATE_ID,
               C.DEL_ID,
               C.COMMENT_ID,
               C.COMMENT_TEXT,
               C.COMMENT_BY,
               C.COMMENT_DATE
        FROM  dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS C
        WHERE C.OPP_LINE_ID =@OppLineId  AND C.STATUS_ID = @GateId
        ORDER BY C.SGATE_ID, C.DEL_ID, C.COMMENT_DATE ASC";

  
}
