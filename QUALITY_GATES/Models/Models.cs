namespace QUALITY_GATES.Models;


// To display top level information in projects like dashboards
public class PROJECTS_HEADER
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string CUST_NAME { get; set; } = string.Empty;
    public string CUST_SAP_CODE { get; set; } = string.Empty;
    public string CUST_PARENT { get; set; } = string.Empty;
    public string SALES_ORGANIZATION { get; set; } = string.Empty;
    public string BUSINESS_UNIT { get; set; } = string.Empty;
    public string PRODUCT_CATEGORY { get; set; } = string.Empty;
    public string OWNER { get; set; } = string.Empty;
    public string PROJECT_SALES_FORCE_LINK { get; set; } = string.Empty;
    public DateOnly SOP { get; set; } = DateOnly.MinValue;
    public DateOnly RELEASED_DATE { get; set; } = DateOnly.MinValue;
    public int AGEING_DAYS { get; set; } = 0;
    public int PARTS_1Y { get; set; } = 0;
    public int VALUE_1Y_EUR { get; set; } = 0;
    public int PARTS_2Y { get; set; } = 0;
    public int VALUE_2Y_EUR { get; set; } = 0;
    public int PARTS_3Y { get; set; } = 0;
    public int VALUE_3Y_EUR { get; set; } = 0;
    public int PARTS_4Y { get; set; } = 0;
    public int VALUE_4Y_EUR { get; set; } = 0;
    public int VALUE_TOTAL_ALL_EUR { get; set; } = 0;


    public string CURRENT_GATE_ID { get; set; } = string.Empty;
    public int TOTAL_DELIVERABLES { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_OWNER { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_ACCOUNTANT { get; set; } = 0;
}

public class PROJECT_DETAIL
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string CURRENT_GATE_ID { get; set; } = string.Empty;
    public List<GATES_ACTIONS> List_Actions { get; set; } = new();
}

public class PROJECT_GATES
{
    public string GATE_ID { get; set; } = string.Empty;
    public string GATE_NAME { get; set; } = string.Empty;
    public DateTime GATE_TIME_GENERATION { get; set; } = DateTime.UtcNow;
    public DateTime GATE_TIME_CLOSED { get; set; } = DateTime.UtcNow;
    public List<GATES_ACTIONS> List_Actions { get; set; } = new();
}

public class GATES_ACTIONS
{
    public string ACTION_ID { get; set; } = string.Empty;
    public int ACTION_SEQUENCE { get; set; }
    public string ACTION_TARGET { get; set; } = string.Empty;
    public string ACTION_GENERATION_TYPE { get; set; } = string.Empty;
    public string RESPONSIBLE_JOB_DESCRIPTION { get; set; } = string.Empty;
    public string ACCOUNTABLE_JOB_DESCRIPTION { get; set; } = string.Empty;
    public string SUPPORTING_JOB_DESCRIPTION { get; set; } = string.Empty;
    public string INFORMED_JOB_DESCRIPTION { get; set; } = string.Empty;
    public string RESPONSIBLE_USER { get; set; } = string.Empty;
    public string ACCOUNTABLE_USER { get; set; } = string.Empty;
    public string SUPPORTING_USER { get; set; } = string.Empty;
    public string INFORMED_USER { get; set; } = string.Empty;
    public List<GATES_DELIVERABLES> List_Deliverables { get; set; } = new();
}

public class GATES_DELIVERABLES
{
    public int DELIVERABLE_SEQUENCE { get; set; }
    public string DELIVERABLE_DESCRIPTION { get; set; } = string.Empty;
    public string DELIVERABLE_TYPE_GENERATION { get; set; } = string.Empty;
    public string DELIVERABLE_ACCEPTANCE_CRITERIA { get; set; } = string.Empty;
    public string DELIVERABLE_TYPE { get; set; } = string.Empty;
    public List<DeliverableFile> DeliverableFiles { get; set; } = new();
}

public class DeliverableFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
}
