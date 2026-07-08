namespace QUALITY_GATES.Classes;

using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Data;
using System.Threading;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;

public partial class Class_Projects_Quality_Gates // Reader provide functions to display in UI current staus of projects and quality gates
{
   // public async Task<OperationResult<List<PROJECTS_HEADER>>> Get_Projects_Max_Details()
    //{
    //}
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
                PROJECTS_HEADER project = new PROJECTS_HEADER
                {
                    OPP_ID = GetString(row, "OPPORTUNITY_ID"),
                    OPP_LINE_ID = GetString(row, "OPPORTUNITY_LINE_ID"),
                    OPP_NAME = GetString(row, "OPPORTUNITY_NAME"),
                    OPP_LINE_NAME = GetString(row, "OPPORTUNITY_LNE_NAME"),
                    SALES_ORGANIZATION = GetString(row,"SALES_ORGANIZATION"),
                    BUSINESS_UNIT = GetString(row, "BUSINESS_UNIT"),
                    PRODUCT_CATEGORY = GetString(row, "PRODUCT_CATEGORY"),
                    CUST_NAME = GetString(row, "CUST_NAME"),
                    CUST_SAP_CODE = GetString(row, "SAP_CUSTOMER"),
                    CUST_PARENT= "PARENT",
                    CURRENT_GATE_ID = GetString(row, "CURRENT_QG_STATUS"),
                    TOTAL_DELIVERABLES = GetInt(row, "#_DELIVERABLES"),
                    TOTAL_DELIVERABLES_PENDING_OWNER = 0,
                    TOTAL_DELIVERABLES_PENDING_ACCOUNTANT = 0,
                    PARTS_1Y = GetInt(row, "PIECES_1Y"),
                    VALUE_1Y_EUR = GetInt(row, "PRICE_1Y"),
                    PARTS_2Y= GetInt(row, "PIECES_2Y"),
                    VALUE_2Y_EUR = GetInt(row, "PRICE_2Y"),
                    PARTS_3Y= GetInt(row, "PIECES_3Y"),
                    VALUE_3Y_EUR = GetInt(row, "PRICE_3Y"),
                    PARTS_4Y= GetInt(row, "PIECES_4Y"),
                    VALUE_4Y_EUR = GetInt(row, "PRICE_4Y"),
                    VALUE_TOTAL_ALL_EUR= GetInt(row, "PIECES_1Y")+ GetInt(row, "PIECES_2Y")+ GetInt(row, "PIECES_3Y")+ GetInt(row, "PIECES_4Y")
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

    private string SQL_TRA_PROJECTS()
    {
        return $@"SELECT OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                         CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                         PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF,
                         MIN(PLANNED_START_DATE)MIN_PLAN,MAX(PLANNED_END_DATE)MAX_PLAN,MIN(ACTUAL_START_DATE)MIN_ACTUAL,MAX(ACTUAL_END_DATE)MAX_ACTUAL,MIN(USER_START_DATE)MIN_USER,MAX(USER_FINISH_DATE)MAX_USER,
                         COUNT(*) AS #_DELIVERABLES
                 FROM dbo.TRA_PROJECTS T1
                 LEFT JOIN dbo.TRA_PROJECTS_DELIVERABLES T2 ON T2.OPP_LINE_ID=OPPORTUNITY_LINE_ID
                 WHERE CURRENT_QG_STATUS='FEAS' AND [IsGeneratedFeasibility]=1
                 GROUP BY OPPORTUNITY_ID,OPPORTUNITY_LINE_ID,OPPORTUNITY_NAME,OPPORTUNITY_LNE_NAME,MATNR,DESCRIPTION,SAP_CUSTOMER,
                          CUST_NAME,SALES_ORGANIZATION,PRODUCT_CATEGORY,BUSINESS_UNIT,CURRENT_QG_STATUS,RELEASED_DATE,
                          PIECES_1Y,PIECES_2Y,PIECES_3Y,PIECES_4Y,PRICE_1Y,PRICE_2Y,PRICE_3Y,PRICE_4Y,PRICE_CUR,RATE_VS_EUR,SOP,OWNER_SF";
    }
}
