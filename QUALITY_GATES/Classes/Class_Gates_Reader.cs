namespace QUALITY_GATES.Classes;

using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Threading;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;

public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
{
    public async Task<OperationResult<PROJECT_DETAIL>> Get_Project_Max_Details(string Opp_Line_ID,string Gate_Id="FEAS" )
    {
        PROJECT_DETAIL project= new PROJECT_DETAIL(); // ROJECT_DETAIL->List<GATES_ACTIONS> ->List_Deliverables-> List Files -> List comments
        GATES_ACTIONS  CurAction = new GATES_ACTIONS();
        GATES_DELIVERABLES CurDeliverable = new GATES_DELIVERABLES();

        List < GATES_ACTIONS > CurListAction = new List<GATES_ACTIONS> ();
        List<GATES_DELIVERABLES> CurListDeliverable = new List<GATES_DELIVERABLES>();
        try
        {
            CancellationToken cancellationToken = default;
            // recover Project Detail
            var parametersPro = new[]
                   {new SqlParameter("@OppLineId", Opp_Line_ID) };
            var queryResult = await _db.GetDatatableFromSelectAsync(SQL_TRA_PROJECTS_DETAIL(),parametersPro, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
            { return OperationResult<PROJECT_DETAIL>.Fail($"Error : {queryResult.Message}"); }
            if (queryResult.RecordsAffected != 1)
            { return OperationResult<PROJECT_DETAIL>.Fail($"Error :  More than one project record from datatable "); }
            // Populate project detail into object
            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                project = new PROJECT_DETAIL
                {
                    OPP_ID = GetString(row, "OPPORTUNITY_ID"),
                    OPP_LINE_ID= GetString(row, "OPPORTUNITY_LINE_ID"),
                    OPP_NAME= GetString(row, "OPPORTUNITY_NAME"),
                    OPP_LINE_NAME= GetString(row, "OPPORTUNITY_LNE_NAME"),
                    CURRENT_GATE_ID= GetString(row, "CURRENT_QG_STATUS"),
                };

            }
            var paramsGate = new[]
             {
                new SqlParameter("@OppLineId", Opp_Line_ID),
                new SqlParameter("@GateId",    Gate_Id)
             };
            var actionsResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Gates(), paramsGate, cancellationToken);
            if (!actionsResult.Success || actionsResult.DTResults == null)
            { return OperationResult<PROJECT_DETAIL>.Fail($"Error querying actions: {actionsResult.Message}"); }

            var actionMap = new Dictionary<string, GATES_ACTIONS>();
            foreach (DataRow row in actionsResult.DTResults.Rows)
            {
                var sgateId = GetString(row, "SGATE_ID");
                CurAction = new GATES_ACTIONS
                {
                    ACTION_ID = GetString(row, "SGATE_ID"),
                    ACTION_SEQUENCE = GetInt(row, "SEQUENCE"),
                    ACTION_TARGET = GetString(row, "GATE_TEXT_EXPLANATION"),
                    ACTION_GENERATION_TYPE = GetString(row, "GATE_TYPE"),
                    RESPONSIBLE_JOB_DESCRIPTION = GetString(row, "RESPONSIBLE"),
                    ACCOUNTABLE_JOB_DESCRIPTION = GetString(row, "ACCOUNTABLE"),
                    SUPPORTING_JOB_DESCRIPTION = GetString(row, "SUPPORTING"),
                    INFORMED_JOB_DESCRIPTION = GetString(row, "INFORMED"),
                    RESPONSIBLE_USER = GetString(row, "RESPONSIBLE_USER_ID"),
                    ACCOUNTABLE_USER = string.Empty,
                    SUPPORTING_USER = string.Empty,
                    INFORMED_USER = string.Empty,
                };
              
                // Within each action we have to ger deliverables asociated to it
                var paramsDeliverable = new[]
                   {
                      new SqlParameter("@OppLineId", Opp_Line_ID),
                      new SqlParameter("@GateId",    Gate_Id),
                      new SqlParameter("@ActionId",  CurAction.ACTION_ID)
                   };
                var delivResult = await _db.GetDatatableFromSelectAsync(SQL_Projects_Deliverables(), paramsDeliverable, cancellationToken);
                if (!delivResult.Success || delivResult.DTResults == null)
                { return OperationResult<PROJECT_DETAIL>.Fail($"Error querying deliverables: {delivResult.Message}"); }
                CurListDeliverable = new List<GATES_DELIVERABLES>(); 
                foreach (DataRow rowDel in delivResult.DTResults.Rows)
                    {
                        var deliverable = new GATES_DELIVERABLES
                        {
                            DELIVERABLE_SEQUENCE = GetInt(rowDel, "DELIVERABLE_ID"),
                            DELIVERABLE_DESCRIPTION = GetString(rowDel, "DELIVERABLE_NAME"),
                            DELIVERABLE_TYPE_GENERATION = GetString(rowDel, "DELIVERABLE_CREATION_TYPE"),
                            DELIVERABLE_ACCEPTANCE_CRITERIA = GetString(rowDel, "DELIVERABLE_ACEPTANCE_CRITERIA"),
                            DELIVERABLE_TYPE = GetString(rowDel, "DELIVERABLE_TYPE"),
                        };
                        CurListDeliverable.Add(deliverable);
                    }
                // Add Lo Action Object to list of actions
                CurAction.List_Deliverables = CurListDeliverable;
                CurListAction.Add(CurAction);
            }
            // Here add list actions to project object
            project.List_Actions = CurListAction;
            return OperationResult<PROJECT_DETAIL>.Ok(project );
        }
        catch (Exception ex)
        {
            return OperationResult<PROJECT_DETAIL>.Fail($"Error : {ex.Message}");
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
            var queryResult = await _db.GetDatatableFromSelectAsync(SQL_TRA_PROJECTS(), null, cancellationToken);
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
                    TOTAL_DELIVERABLES_PENDING_OWNER = 0,
                    TOTAL_DELIVERABLES_PENDING_ACCOUNTANT = 0,
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
            G.SGATE_ID,
            G.GATE_TYPE,
            G.GATE_TEXT_EXPLANATION,
            G.GATE_STATUS_ID,
            G.RESPONSIBLE_USER_ID,
            ISNULL(A.SEQUENCE,    0)  AS SEQUENCE,
            ISNULL(A.RESPONSIBLE, '') AS RESPONSIBLE,
            ISNULL(A.ACCOUNTABLE, '') AS ACCOUNTABLE,
            ISNULL(A.SUPPORTING,  '') AS SUPPORTING,
            ISNULL(A.INFORMED,    '') AS INFORMED
        FROM  dbo.TRA_PROJECTS_GATES G
        LEFT JOIN dbo.MAS_ACTIONS A
               ON A.STATUS_ID = G.STATUS_ID AND A.SGATE_ID = G.SGATE_ID
        WHERE G.OPP_LINE_ID = @OppLineId AND G.STATUS_ID = @GateId
        ORDER BY ISNULL(A.SEQUENCE, 0)";

    private static string SQL_Projects_Deliverables() => @"
        SELECT
            D.SGATE_ID,
            D.DELIVERABLE_ID,
            D.DELIVERABLE_NAME,
            D.DELIVERABLE_CREATION_TYPE,
            D.DELIVERABLE_ACEPTANCE_CRITERIA,
            D.DELIVERABLE_STATUS_ID,
            ISNULL(MD.DELIVERABLE_TYPE, '') AS DELIVERABLE_TYPE
        FROM  dbo.TRA_PROJECTS_DELIVERABLES D
        LEFT JOIN dbo.MAS_DELIVERABLES MD
               ON MD.STATUS_ID = D.STATUS_ID AND MD.SGATE_ID = D.SGATE_ID AND MD.SEQUENCE = D.DELIVERABLE_ID
        WHERE D.OPP_LINE_ID = @OppLineId AND D.STATUS_ID = @GateId AND D.SGATE_ID = @ActionId
        ORDER BY D.SGATE_ID, D.DELIVERABLE_ID";


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
        return $@"SELECT OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                         CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                         PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,PARENT_NAME,SF_LINK,
                         MIN(PLANNED_START_DATE)MIN_PLAN,MAX(PLANNED_END_DATE)MAX_PLAN,MIN(ACTUAL_START_DATE)MIN_ACTUAL,MAX(ACTUAL_END_DATE)MAX_ACTUAL,MIN(USER_START_DATE)MIN_USER,MAX(USER_FINISH_DATE)MAX_USER,
                         COUNT(*) AS #_DELIVERABLES
                 FROM dbo.TRA_PROJECTS T1
                 LEFT JOIN dbo.TRA_PROJECTS_DELIVERABLES T2 ON T2.OPP_LINE_ID=OPPORTUNITY_LINE_ID
                 WHERE CURRENT_QG_STATUS='FEAS' AND [IsGeneratedFeasibility]=1
                 GROUP BY OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                          CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                          PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,PARENT_NAME,SF_LINK";
    }
}
