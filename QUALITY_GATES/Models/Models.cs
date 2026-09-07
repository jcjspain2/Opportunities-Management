namespace QUALITY_GATES.Models;


// To display top level information in projects like dashboards
public class PROJECTS_HEADER
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public Priority OPP_LINE_PRIORITY { get; set; } = Priority.Normal;
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
    public List<PROJECT_STATUS> List_Project_Status { get; set; } = new();
}

public class PROJECT_DETAIL
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string CURRENT_GATE_ID { get; set; } = string.Empty;
    public string PATH_TO_SAVE_FILES { get; set; } = string.Empty;
    public string LINK_TO_PDCC { get; set; } = string.Empty;
    public List<GATES_ACTIONS> List_Actions { get; set; } = new();
}

public class PROJECT_STATUS
{
    public string GATE_STATUS_ID { get; set; } = string.Empty;
    public bool GATE_GENERATED { get; set; } = false;
    public bool GATE_CLOSED { get; set; } = false;
    public string GATE_STATUS_DESCRIPTION { get; set; } = string.Empty; // Descripcion del estado FEAS PCA
    public DateTime GATE_TIME_GENERATION { get; set; } = DateTime.UtcNow;
    public string GENERATION_USER_ID { get; set; }=string.Empty;
    public String CURRENT_STATUS_ID { get; set; }= string.Empty;
    public String CURRENT_STATUS_DESCRIPTION_ID { get; set; }=String.Empty;
    public bool IS_CURRENT_STATUS_FINAL { get; set; } = false;
    public bool IS_CURRENT_STATUS_INITIAL { get; set; } = false;
    public bool IS_CURRENT_STATUS_IN_PROGRESS { get; set; } = false;
    public DateTime GATE_TIME_CLOSED { get; set; } = DateTime.UtcNow;
    public int TIMES_REOPENED { get; set; } = 0;
    public string CLOSING_USER_ID { get; set; } = string.Empty;
    public int TOTAL_DELIVERABLES { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_OWNER { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_ACCOUNTANT { get; set; } = 0;
    
}
public class MY_TASKS
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string STATUS_ID { get; set; } = string.Empty; 
    public string ACTION_ID { get; set; } = string.Empty;
    public int DELIVERABLE_ID { get; set; } = 0;
    public Priority OPP_LINE_PRIORITY { get; set; } = Priority.Normal;
    public StatusState CurrentStatus { get; set; } = StatusState.NotStarted;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string STATUS_NAME { get; set; } = string.Empty; 
    public string ACTION_NAME { get; set; } = string.Empty;
    public string DELIVERABLE_DESCRIPTION { get; set; } = string.Empty;
    public string SALES_ORGANIZATION { get; set; } = string.Empty;

    public string TASK_COMMENT { get; set; } = string.Empty;
}
public class TASKS_PENDING_TO_ALLOCATE
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string STATUS_ID { get; set; } = string.Empty;
    public string ACTION_ID { get; set; } = string.Empty;
    public int DELIVERABLE_ID { get; set; } = 0;
    public Priority OPP_LINE_PRIORITY { get; set; } = Priority.Normal;
    public StatusState CurrentStatus { get; set; } = StatusState.NotStarted;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string STATUS_NAME { get; set; } = string.Empty;
    public string ACTION_NAME { get; set; } = string.Empty;
    public string DELIVERABLE_DESCRIPTION { get; set; } = string.Empty;
    public string SALES_ORGANIZATION { get; set; } = string.Empty;
    public string BUSINESS_UNIT { get; set; } = string.Empty;
    public string PRODUCT_CATEGORY { get; set; } = string.Empty;
    public string OWNER { get; set; } = string.Empty;
    public string JOB_TITLE_ID { get; set; } = string.Empty;
    public string JOB_TITLE_DESCRIPTION { get; set; } = string.Empty;

}
public class GATES_ACTIONS
{
    public string STATUS_ID { get; set; } = string.Empty; // FEAS
    public string ACTION_ID { get; set; } = string.Empty; // FEAS_1
    public int ACTION_SEQUENCE { get; set; }              // For sorting actions in the gate
    public string ACTION_TARGET { get; set; } = string.Empty; // Text containing the target of the action
    public string ACTION_GENERATION_TYPE { get; set; } = string.Empty; // DEFAULT, MANUAL

    public DateOnly PLANNED_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly PLANNED_END_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly ACTUAL_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly ACTUAL_END_DATE { get; set; } = DateOnly.MinValue;
    public List<GATES_DELIVERABLES> List_Deliverables { get; set; } = new();

}
public class GATES_DELIVERABLES

{
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string STATUS_ID { get; set; } = string.Empty; // FEAS
    public string ACTION_ID { get; set; } = string.Empty; // FEAS_1
    public int DELIVERABLE_SEQUENCE { get; set; }
    public string DELIVERABLE_DESCRIPTION { get; set; } = string.Empty;
    public string DELIVERABLE_TYPE_GENERATION { get; set; } = string.Empty; // DEFAULT, MANUAL
    public string DELIVERABLE_ACCEPTANCE_CRITERIA { get; set; } = string.Empty;
    public string DELIVERABLE_TYPE { get; set; } = string.Empty; // FILE or TEXT
    public string PATH_TO_SAVE_FILES { get; set; } = string.Empty;
    // CURRENT STATUS OF DELIVERABLE
    public string DELIVERABLE_STATUS_ID { get; set; } = string.Empty;
    public string ACCOUNTED_STATUS_ID { get; set; } = string.Empty;

    public string DELIVERABLE_STATUS_DESCRIPTION { get; set; } = string.Empty;
    public string ACCOUNTED_STATUS_DESCRIPTION { get; set; } = string.Empty;
    public bool IS_DELIVERABLE_RESPONSIBLE_FINISH { get; set; } = false;
    public bool IS_DELIVERABLE_ACCOUNTED_FINISH { get; set; } = false;
    // JOBS ID RESPONSIBLES & ACCOUNBTANTS
    public string RESPONSIBLE_JOB_ID { get; set; } = string.Empty;
    public string RESPONSIBLE_JOB_NAME { get; set; } = string.Empty;
    public string ACCOUNTABLE_JOB_ID { get; set; } = string.Empty;
    public string ACCOUNTABLE_JOB_NAME { get; set; } = string.Empty;
    // USERS ID RESPONSIBLES & ACCOUNTANTS

    public string RESPONSIBLE_USER_ID { get; set; } = string.Empty;
    public string RESPONSIBLE_USER_NAME { get; set; } = string.Empty;
    public string ACCOUNTABLE_USER_ID { get; set; } = string.Empty;
    public string ACCOUNTABLE_USER_NAME { get; set; } = string.Empty;

    // DATES MANAGEMENT
    public DateOnly PLANNED_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly PLANNED_END_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly ACTUAL_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly ACTUAL_END_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly USER_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly USER_END_DATE { get; set; } = DateOnly.MinValue;

    // Links to patterns & explanation pages for users Open
    public string LINK_TO_TEMPLATE { get; set; } = string.Empty;
    public string LINK_TO_INSTRUCTION_TO_FOLLOW { get; set; } = string.Empty;
    public string DELIVERABLE_USER_TEXT { get; set; } = string.Empty;
    public string LINK_TO_PDCC { get; set; } = string.Empty;

    public List<DELIVERABLE_FILE> DeliverableFiles { get; set; } = new();
    public List<DELIVERABLES_COMMENTS> DeliverableComments { get; set; } = new();
    public List<DELIVERABLES_COMMENTS> AccountantComments { get; set; } = new();

}
public class DELIVERABLES_COMMENTS
{
    public string OppLineId { get; set; } = string.Empty;
    public string Gate_Id { get; set; } = string.Empty;
    public string StatusiD { get; set; } = string.Empty;
    public int DeliverableID { get; set; } = 0;
    public Guid Id { get; set; }
    public string COMMENT_TEXT { get; set; } = string.Empty;
    public string USER { get; set; } = string.Empty;
    public DateTime COMMENT_DATE { get; set; } = DateTime.MinValue;
}

public class DELIVERABLE_FILE
{
    public string OppLineId { get; set; } = string.Empty;
    public string Gate_Id { get; set; } = string.Empty;
    public string StatusiD { get; set; } = string.Empty;
    public int DeliverableID { get; set; } = 0;

    public int Id_Trans { get; set; } = 0;
    public int Id_File { get; set; } = 0;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string UserUpload { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    // Metadata From system
    public string METADATA_OS_User_Creation { get; set; } = string.Empty;
    public string METADATA_OS_User_Modification { get; set; } = string.Empty;
    public DateTime METADADATA_OS_Creation_Date { get; set; } = DateTime.MinValue;
    public DateTime METADADATA_OS_Modification_Date { get; set; } = DateTime.MinValue;

}
internal class DELIVERABLE_FILE_FROM_DMS
{
    public string OppLineId { get; set; }=string.Empty;
    public string Gate_Id { get; set; } = string.Empty;
    public string StatusiD { get; set; } = string.Empty;
    public int DeliverableID { get; set; } = 0;

    public int Id_Trans { get; set; } = 0;
    public int Id_File { get; set; } = 0;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string UserUpload { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; } = DateTime.MinValue;
    public string METADATA_User_Creation { get; set; } = string.Empty;
    public string METADATA_User_Modification { get; set; } = string.Empty;
    public DateTime METADADATA_Creation_Date { get; set; } = DateTime.MinValue;
    public DateTime METADADATA_Modification_Date { get; set; } = DateTime.MinValue;
}
public class USERS_DETAILS
{
    public string UserId { get; set; } = string.Empty;
    public string User_Display_Name { get; set; } = string.Empty;
    public string User_Q_GATES_Job_Title { get; set; } = string.Empty;
    public string User_Q_GATES_Job_Title_ID { get; set; } = string.Empty;
    public string User_Cadena_JobTitle { get; set; } = string.Empty;
    public string UserSite { get; set; } = string.Empty;
    public string UserManager_Mail { get; set; } = string.Empty;
    public string UserManager_Functional_Mail { get; set; } = string.Empty;

}
public class USERS_COLLABORATIVE
{
    public string UserId { get; set; } = string.Empty;
    public string Responsible_Task_Comment { get; set; } = string.Empty;
    
}
public enum Priority
{
    High = 1,
    Medium = 2,
    Normal = 3,
    Low = 4
}
public enum StatusState
{
    NotStarted = 1,
    InProgress = 2,
    Pendingreview = 3,
    Completed = 4,
    Rejected = 5
}

public enum DeliverableRolesEstructure
{
    Responsible = 1,
    Accountable = 2,
    Collaborator = 3
}
public enum Module_ID
{
    Q_GATES = 1,
    Q_GATES_PR = 2
}
public enum Enviroment
{
    PRODUCTION = 1,
    TEST = 2
}
public static class PriorityExtensions
{
    public static Priority FromValue(int value)
    {
        if (Enum.IsDefined(typeof(Priority), value))
        {
            return (Priority)value;
        }
        throw new ArgumentException($"Valor de prioridad no válido: {value}");
    }
}