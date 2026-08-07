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
using  UtilidadesFichero;


public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
{
    public async Task<OperationResult<List<DELIVERABLES_COMMENTS>>> Get_Comments_Per_Deliverable(string Opp_Line_ID,
                                                                                            string GateId,
                                                                                            string StatusId,
                                                                                            string DelivId)
    {
        CancellationToken cancellationToken = default;
        DELIVERABLES_COMMENTS DevComments = new DELIVERABLES_COMMENTS();
        List<DELIVERABLES_COMMENTS> CurList = new List<DELIVERABLES_COMMENTS>();
        var paramsFile = new[] { new SqlParameter("@oppLineId", Opp_Line_ID),
                                 new SqlParameter("@GateId", GateId),
                                 new SqlParameter("@StatusId", StatusId),
                                 new SqlParameter("@DelivID", DelivId)};
        var queryResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Deliverable_Comments_Per_Deliverable(), paramsFile, cancellationToken: cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<List<DELIVERABLES_COMMENTS>>.Fail($"Error: {queryResult.Message}");

        try
        {
            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                DevComments = new DELIVERABLES_COMMENTS()
                {
                    OppLineId = Opp_Line_ID,
                    Gate_Id = GateId,
                    StatusiD  = StatusId,
                    DeliverableID  = Convert.ToInt32(DelivId),
                    Id= Guid.Parse(GetString(row, "COMMENT_ID")),
                    COMMENT_TEXT = GetString(row, "COMMENT_TEXT"),
                    USER = GetString(row, "COMMENT_BY"),
                    COMMENT_DATE = GetDateTime(row, "COMMENT_DATE")
                };
                CurList.Add(DevComments);
            }
            return OperationResult<List<DELIVERABLES_COMMENTS>>.Ok(_db.DeepCopyList(CurList));

        }

        catch (Exception ex)
        {
            return OperationResult<List<DELIVERABLES_COMMENTS>>.Fail($"Error: {ex.Message}");
            throw;
        }


    }

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
                    OPP_LINE_PRIORITY=PriorityExtensions.FromValue(GetInt(row,"Priority")),
                    OPP_ID = GetString(row, "OPPORTUNITY_ID"),
                    OPP_LINE_ID = GetString(row, "OPPORTUNITY_LINE_ID"),
                    OPP_NAME = GetString(row, "OPPORTUNITY_NAME"),
                    OPP_LINE_NAME = GetString(row, "OPPORTUNITY_LNE_NAME"),
                    SALES_ORGANIZATION = GetString(row, "SALES_ORGANIZATION"),
                    BUSINESS_UNIT = GetString(row, "BUSINESS_UNIT"),
                    PRODUCT_CATEGORY = GetString(row, "PRODUCT_CATEGORY"),
                    OWNER = GetString(row, "OWNER_SF"),
                    PROJECT_SALES_FORCE_LINK = GetString(row, "SF_LINK"),
                    RELEASED_DATE = GetDateOnly(row, "RELEASED_DATE"),
                    AGEING_DAYS = DateOnly.FromDateTime(DateTime.Today).DayNumber - GetDateOnly(row, "RELEASED_DATE").DayNumber,
                    CUST_NAME = GetString(row, "CUST_NAME"),
                    CUST_SAP_CODE = GetString(row, "SAP_CUSTOMER"),
                    CUST_PARENT = GetString(row, "PARENT_NAME"),
                    SOP = GetDateOnly(row, "SOP"),
                    CURRENT_GATE_ID = GetString(row, "CURRENT_QG_STATUS"),
                    TOTAL_DELIVERABLES = GetInt(row, "#_DELIVERABLES"),
                    TOTAL_DELIVERABLES_PENDING_OWNER = GetInt(row, "PENDING_RESPONSIBLE"),
                    TOTAL_DELIVERABLES_PENDING_ACCOUNTANT = GetInt(row, "PENDING_ACCOUNTANT"),
                    PARTS_1Y = GetInt(row, "PIECES_1Y"),
                    VALUE_1Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_1Y") * TargetPrice)),
                    PARTS_2Y = GetInt(row, "PIECES_2Y"),
                    VALUE_2Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_2Y") * TargetPrice)),
                    PARTS_3Y = GetInt(row, "PIECES_3Y"),
                    VALUE_3Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_3Y") * TargetPrice)),
                    PARTS_4Y = GetInt(row, "PIECES_4Y"),
                    VALUE_4Y_EUR = Convert.ToInt32((GetInt(row, "PIECES_4Y") * TargetPrice)),
                    VALUE_TOTAL_ALL_EUR = Convert.ToInt32((GetInt(row, "PIECES_1Y") * TargetPrice)) + Convert.ToInt32((GetInt(row, "PIECES_2Y") * TargetPrice)) + Convert.ToInt32((GetInt(row, "PIECES_3Y") * TargetPrice)) + Convert.ToInt32((GetInt(row, "PIECES_4Y") * TargetPrice))
                };
                // From here we have to recover Gate Status Availables for each project and add it currentprojectList
                const string sqlNextGate = @"SELECT T1.STATUS_ID,STATUS_DESCRIPTION,STATUS_SEQUENCE,T2.STATUS_ID,ISNULL(CURRENT_STATUS,
                                                    'NO_GEN')AS CURRENT_STATUS ,T2.GENERATION_DATE,T2.GENERATION_USER,T2.TIMES_REOPENED, 
                                                       ISNULL(T3.GATE_STATUS_DESC,'No Generated') AS CURSTATUS_DESC,[CLOSING_DATE],[CLOSING_USER],
                                                       T3.IS_FINAL_STATE,T3.IS_PENDING_REVIEW,T3.IS_INITIAL_STATE,T3.IS_IN_PROGRESS,
                                                       SUM (CASE WHEN T3.GATE_STATUS_DESC IS NULL THEN 0 ELSE 1 END )AS #_DELIVERABLES,
                                                       ((SUM (CASE WHEN T3.GATE_STATUS_DESC IS NULL THEN 0 ELSE 1 END )) - 
                                                         SUM(CASE T5.IS_FINAL_STATE WHEN 1 THEN 1 ELSE 0 END))PENDING_RESPONSIBLE,
                                                       ((SUM (CASE WHEN T3.GATE_STATUS_DESC IS NULL THEN 0 ELSE 1 END ))  - 
                                                        SUM(CASE T6.IS_FINAL_STATE WHEN 1 THEN 1 ELSE 0 END))PENDING_ACCOUNTANT
                                             FROM [SRM].[dbo].[MAS_STATUS] T1
                                             LEFT JOIN  [SRM].[dbo].[TRA_PROJECTS_STATUS] T2 ON  T2.MODULE_ID=T1.STATUS_MODULE AND T1.STATUS_ID=T2.STATUS_ID AND T2.OPP_LINE_ID=@OppLineId
                                             LEFT JOIN [SRM].[dbo].MAS_GATE_STATUS T3 ON T3.MODULE_ID= 'Q_GATES'  AND T3.KEY_PROCESS='GATE_ST' AND T3.GATE_STATUS_ID=T2.CURRENT_STATUS
                                             LEFT JOIN dbo.TRA_PROJECTS_DELIVERABLES T4 ON T4.OPP_LINE_ID=@OppLineId AND T4.STATUS_ID=T1.STATUS_ID
                                             LEFT JOIN dbo.MAS_GATE_STATUS T5 ON T5.GATE_STATUS_ID=T4.DELIVERABLE_STATUS_ID AND T5.MODULE_ID='Q_GATES' AND T5.KEY_PROCESS='DEL_RESP'
                                             LEFT JOIN dbo.MAS_GATE_STATUS T6 ON T6.GATE_STATUS_ID=T4.ACCOUNTABLE_STATUS_ID AND T6.MODULE_ID='Q_GATES' AND T6.KEY_PROCESS='DEL_ACC'   
                                             WHERE T1.STATUS_MODULE='Q_GATES' AND T1.IsDeleted=0
                                             GROUP BY T1.STATUS_ID,STATUS_DESCRIPTION,STATUS_SEQUENCE,T2.STATUS_ID,ISNULL(CURRENT_STATUS,'NO_GEN')
                                                   ,T2.GENERATION_DATE,T2.GENERATION_USER,T2.TIMES_REOPENED, 
                                                   ISNULL(T3.GATE_STATUS_DESC,'No Generated') ,[CLOSING_DATE],[CLOSING_USER],
                                                   T3.IS_FINAL_STATE,T3.IS_PENDING_REVIEW,T3.IS_INITIAL_STATE,T3.IS_IN_PROGRESS
                                             ORDER BY T1.STATUS_SEQUENCE";

      
                var parametersNextGate = new[]
                      { new SqlParameter("@ModuleId", MODULE_ID),
                        new SqlParameter("@OppLineId", GetString(row, "OPPORTUNITY_LINE_ID"))};
                var queryResultSt = await _db.GetDatatableFromSelectAsync(sqlNextGate, parametersNextGate, cancellationToken: cancellationToken);
                if (!queryResultSt.Success || queryResult.DTResults == null)
                { return OperationResult<List<PROJECTS_HEADER>>.Fail($"Error inesperado: {queryResultSt.Message}"); }
                PROJECT_STATUS CurStatus;
              
                    foreach (DataRow rowSt in queryResultSt.DTResults.Rows)
                    {
                        CurStatus = new PROJECT_STATUS
                        {
                            GATE_STATUS_ID = GetString(rowSt,"STATUS_ID"),
                            GATE_STATUS_DESCRIPTION = GetString(rowSt,"STATUS_DESCRIPTION"),
                            GATE_TIME_GENERATION = GetDateTime(rowSt, "GENERATION_DATE"),
                            GENERATION_USER_ID = GetString(rowSt,"GENERATION_USER"),
                            CURRENT_STATUS_ID = GetString(rowSt,"CURRENT_STATUS"),
                            CURRENT_STATUS_DESCRIPTION_ID = GetString(rowSt, "CURSTATUS_DESC"),
                            GATE_TIME_CLOSED = GetDateTime(rowSt , "CLOSING_DATE"),
                            CLOSING_USER_ID = GetString(rowSt, "CLOSING_USER"),
                            TIMES_REOPENED = GetInt(rowSt ,"TIMES_REOPENED"),
                            GATE_GENERATED = rowSt["GENERATION_DATE"] != DBNull.Value && rowSt["GENERATION_DATE"] != null,
                            GATE_CLOSED= rowSt["CLOSING_DATE"] != DBNull.Value && rowSt["CLOSING_DATE"] != null,
                            IS_CURRENT_STATUS_FINAL = GetBoolean(rowSt, "IS_FINAL_STATE"),
                            IS_CURRENT_STATUS_INITIAL = GetBoolean(rowSt, "IS_INITIAL_STATE"),
                            IS_CURRENT_STATUS_IN_PROGRESS = GetBoolean(rowSt, "IS_IN_PROGRESS"),
                            TOTAL_DELIVERABLES = GetInt(rowSt, "#_DELIVERABLES"),
                            TOTAL_DELIVERABLES_PENDING_ACCOUNTANT = GetInt(rowSt, "PENDING_ACCOUNTANT"),
                            TOTAL_DELIVERABLES_PENDING_OWNER = GetInt(rowSt, "PENDING_RESPONSIBLE")
                        };
                       project.List_Project_Status.Add( CurStatus );
                    }
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
        ORDER BY STATUS_ID,ISNULL(GATE_SEQUENCE, 0)";

    private static string SQL_Projects_Deliverables() => @"
      SELECT    T1.[DELIVERABLE_ID]
               ,[DELIVERABLE_STATUS_ID]
               ,[ACCOUNTABLE_STATUS_ID]
               ,T4.GATE_STATUS_DESC DEL_STATUS_DESC
               ,T5.GATE_STATUS_DESC ACC_STATUS_DESC
               ,T4.IS_FINAL_STATE RESP_STATUS_iSFINAL 
               ,T5.IS_FINAL_STATE ACC_STATUS_iSFINAL
               ,T1.[DELIVERABLE_CREATION_TYPE]
               ,T1.[DELIVERABLE_NAME]
               ,T1.[DELIVERABLE_ACEPTANCE_CRITERIA]
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
               ,T6.SAMPLE_LINK
               ,T6.INSTRUCTION_LINK
               ,T1.USER_TEXT
     FROM  [dbo].[TRA_PROJECTS_DELIVERABLES] T1
     LEFT JOIN dbo.MAS_JOB_TITLES T2 ON T2.JOB_TITLE_ID=T1.RESPONSIBLE_JOB_ID
     LEFT JOIN dbo.MAS_JOB_TITLES T3 ON T3.JOB_TITLE_ID=T1.ACCOUNTABLE_JOB_ID
     LEFT JOIN dbo.MAS_GATE_STATUS T4 ON T4.GATE_STATUS_ID=T1.DELIVERABLE_STATUS_ID AND T4.KEY_PROCESS='DEL_RESP'  
     LEFT JOIN dbo.MAS_GATE_STATUS T5 ON T5.GATE_STATUS_ID=T1.ACCOUNTABLE_STATUS_ID AND T5.KEY_PROCESS='DEL_ACC' 
     LEFT JOIN dbo.MAS_DELIVERABLES T6 ON T6.MODULE_ID='Q_GATES' AND T6.SEQUENCE=T1.DELIVERABLE_ID AND T6.SGATE_ID=T1.SGATE_ID AND T6.STATUS_ID=T1.STATUS_ID
     WHERE T1.OPP_LINE_ID = @OppLineId AND T1.STATUS_ID = @GateId AND T1.SGATE_ID = @ActionId
     ORDER BY DELIVERABLE_ID";


    private string SQL_TRA_PROJECTS_DETAIL()
    {
        return $@"SELECT OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                      CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                      PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF
               FROM dbo.TRA_PROJECTS T1
               WHERE CURRENT_QG_STATUS IN ('PCA','FEAS') AND [IsGeneratedInitial]=1 AND OPPORTUNITY_LINE_ID= @OppLineId";
    //TODO:IMPROVE HARDCODING QUALITY GATES FILTE
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
                         Priority,
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
                 LEFT JOIN dbo.MAS_GATE_STATUS T3 ON T3.GATE_STATUS_ID=T2.DELIVERABLE_STATUS_ID AND T3.MODULE_ID='QGATES' AND T3.KEY_PROCESS='DEL_RESP'
                 LEFT JOIN dbo.MAS_GATE_STATUS T4 ON T4.GATE_STATUS_ID=T2.ACCOUNTABLE_STATUS_ID AND T4.MODULE_ID='QGATES' AND T4.KEY_PROCESS='DEL_ACC'   
                 WHERE CURRENT_QG_STATUS IN ('PCA', 'FEAS') AND [IsGeneratedInitial]=1
                 GROUP BY OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                          CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                          PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,PARENT_NAME,SF_LINK,Priority";
        //TODO: AVOID HARDCODING GAYES & MODULE_ID (Module Id is in cosntatnt that should be called when we instance class
        //TODO: DELIVERABLE FIGURES MUST BE ACCORDING CURRENT GATE
    }
    /// <summary>
    /// Brings from DB maximum detail for an especif Project PPROJECT->STATUS->GATES->DELIVERABLES->DELIVERABLES FILES-> DELIVERABLES COMMENTS    
    /// </summary>
    /// <param name="Opp_Line_ID,">Unique Sales Forve ID of the project to display data</param>
    /// <param name="Gate_Id">Gate ID we want to display gates by default display FEAS</param>
    /// <returns>Project detail object</returns>
    public async Task<OperationResult<PROJECT_DETAIL>> Get_Project_Max_Details(string Opp_Line_ID, string Gate_Id = "PCA")
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
                    //DELIVERABLE_FILE CurFile;
                    List<DELIVERABLE_FILE> ListFiles = new List<DELIVERABLE_FILE>();
                    if (FilesFilteredList.Count > 0)
                     {
                        foreach (DELIVERABLE_FILE_FROM_DMS item in FilesFilteredList)
                        {
                            ListFiles.Add(new DELIVERABLE_FILE
                            {
                                OppLineId = Opp_Line_ID,
                                Gate_Id= Gate_Id,
                                StatusiD = curAction.ACTION_ID,
                                DeliverableID  = delivId,
                                Id_Trans=item.Id_Trans,
                                Id_File = item.Id_File,
                                FileName =item.FileName,
                                FilePath = item.FilePath,
                                FileType = item.FileType,
                                UserUpload  = item.UserUpload,
                                UploadDate = item.UploadDate,
                                METADADATA_OS_Creation_Date=item.METADADATA_Creation_Date,
                                METADATA_OS_User_Creation=item.METADATA_User_Creation,
                                METADADATA_OS_Modification_Date=item.METADADATA_Creation_Date,
                                METADATA_OS_User_Modification=item.METADATA_User_Modification,
                            });
                        }
                    }


                    
                    var commentKey = (curAction.ACTION_ID.Trim(), delivId);
                    var delivComments = commentMap.TryGetValue(commentKey, out var foundComments)
                        ? new List<DELIVERABLES_COMMENTS>(foundComments)
                        : new List<DELIVERABLES_COMMENTS>();
                    curListDeliverable.Add(new GATES_DELIVERABLES
                    {
                        OPP_LINE_ID = Opp_Line_ID,
                        STATUS_ID= Gate_Id,// FEAS
                        ACTION_ID = curAction.ACTION_ID, // FEAS_1
                        DELIVERABLE_SEQUENCE            = delivId,
                        DELIVERABLE_DESCRIPTION         = GetString(rowDel, "DELIVERABLE_NAME"),
                        DELIVERABLE_TYPE_GENERATION     = GetString(rowDel, "DELIVERABLE_CREATION_TYPE"),
                        DELIVERABLE_ACCEPTANCE_CRITERIA = GetString(rowDel, "DELIVERABLE_ACEPTANCE_CRITERIA"),
                        DELIVERABLE_STATUS_ID           = GetString(rowDel, "DELIVERABLE_STATUS_ID"),
                        DELIVERABLE_STATUS_DESCRIPTION  = GetString(rowDel, "DEL_STATUS_DESC"),
                        IS_DELIVERABLE_RESPONSIBLE_FINISH = GetBoolean(rowDel, "RESP_STATUS_iSFINAL"),
                        IS_DELIVERABLE_ACCOUNTED_FINISH = GetBoolean(rowDel, "ACC_STATUS_iSFINAL"),
                        ACCOUNTED_STATUS_ID             = GetString(rowDel, "ACCOUNTABLE_STATUS_ID"),
                        ACCOUNTED_STATUS_DESCRIPTION    = GetString(rowDel, "ACC_STATUS_DESC"),
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
                        LINK_TO_TEMPLATE                 = GetString(rowDel, "SAMPLE_LINK"),
                        LINK_TO_INSTRUCTION_TO_FOLLOW    = GetString(rowDel, "INSTRUCTION_LINK"),
                        DELIVERABLE_USER_TEXT            = GetString(rowDel, "USER_TEXT"),
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
        WHERE C.OPP_LINE_ID =@OppLineId  AND C.STATUS_ID = @GateId AND KEY_PROCESS='DELIV'
        ORDER BY C.SGATE_ID, C.DEL_ID, C.COMMENT_DATE ASC";
    private static string SQL_Projects_Deliverable_Comments_Per_Deliverable() => @"
         SELECT C.OPP_LINE_ID,
                C.STATUS_ID,
               C.SGATE_ID,
               C.DEL_ID,
               C.COMMENT_ID,
               C.COMMENT_TEXT,
               C.COMMENT_BY,
               C.COMMENT_DATE
        FROM  dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS C
        WHERE C.OPP_LINE_ID =@OppLineId  AND C.STATUS_ID = @GateId AND C.SGATE_ID= @StatusId AND C.DEL_ID=@DelivID AND KEY_PROCESS='DELIV'
        ORDER BY C.SGATE_ID, C.DEL_ID, C.COMMENT_DATE ASC";

}
