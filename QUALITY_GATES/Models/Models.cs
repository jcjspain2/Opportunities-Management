namespace QUALITY_GATES.Models;


// To display top level information in projects like dashboards

/// <summary>
/// Represents the header information for projects, typically used in dashboards to display top-level project details.
/// </summary>
/// <remarks>This class encapsulates various properties related to project opportunities, customer details, sales
/// information,  SAP data, plant-related data, and project timelines. It provides a comprehensive overview of a
/// project's status  and metrics over multiple years.</remarks>
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
    // SAP  data
    public string MATNR { get; set; } = string.Empty;
    public string MATNR_DESCRIPTION { get; set; } = string.Empty;
    // END SAP DATA
    // PLANTS RELATED
    public string PLANT_MANUFACTURING { get; set; } = string.Empty;
    public string PLANT_SAMPLES { get; set; } = string.Empty;
    public string PLANT_DESIGN { get; set; } = string.Empty;
    // END PLANTS RELATED
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
    public int PARTS_5Y { get; set; } = 0;
    public int VALUE_5Y_EUR { get; set; } = 0;
    public int VALUE_TOTAL_ALL_EUR { get; set; } = 0;
     public string CURRENT_GATE_ID { get; set; } = string.Empty;
    public int TOTAL_DELIVERABLES { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_OWNER { get; set; } = 0;
    public int TOTAL_DELIVERABLES_PENDING_ACCOUNTANT { get; set; } = 0;
    public List<PROJECT_STATUS> List_Project_Status { get; set; } = new();
}
/// <summary>
/// Maximium details of a project, including the list of actions and deliverables for the current gate, previously we need to choose a projects
/// </summary>
public class PROJECT_DETAIL
{
    public string OPP_ID { get; set; } = string.Empty;
    public string OPP_LINE_ID { get; set; } = string.Empty;
    public string OPP_NAME { get; set; } = string.Empty;
    public string OPP_LINE_NAME { get; set; } = string.Empty;
    public string CURRENT_GATE_ID { get; set; } = string.Empty;
    public string CURRENT_GATE_DESCRIPTION { get; set; } = string.Empty;
    public string PATH_TO_SAVE_FILES { get; set; } = string.Empty;
    public string LINK_TO_PDCC { get; set; } = string.Empty;
    public List<GATES_ACTIONS> List_Actions { get; set; } = new();
}
/// <summary>
/// Model for creating a new Action within a selected project
/// </summary>
public class Manual_Creation_Action
{  
    public string OPP_LINE_ID { get; set; } = string.Empty; // SF ID
    public string STATUS_ID { get; set; } = string.Empty; // PCA
    public string ACTION_ID { get; set; } = string.Empty; // PCA_1
    public int ACTION_SEQUENCE { get; set; } = 10;
    public string ACTION_TARGET { get; set; } = string.Empty;
    public DateOnly PLANNED_START_DATE { get; set; } = DateOnly.MinValue;
    public DateOnly PLANNED_END_DATE { get; set; } = DateOnly.MinValue;

    public List<Manual_Creation_Deliverable> LIST_DELIVERABLES = new();
}
/// <summary>
/// Child model for creation of new actions
/// </summary>
public class Manual_Creation_Deliverable() // Only can be used as child class
{
    public int DEL_SEQUENCE { get; set; } = 10;
    public string DELIVERABLE_ACTION { get; set; } = string.Empty;
    public string DELIVERABLE_ACEPTANCE_CRITERIA { get; set; } = string.Empty;
    public string RESPONSIBLE_JOB_TITLE { get; set; } = string.Empty;
    public string ACCOUNTABLE_JOB_TITLE { get; set; } = string.Empty;
    public string PATH_TO_SAVE { get; set; } = string.Empty;
    public int EFFORT_RATE { get; set; } = 1;

}
public class Manual_Creation_Deliverable_From_Existing_Action() // Only can be used as child class
{
    public string OPP_LINE_ID { get; set; } = string.Empty; // SF ID
    public string STATUS_ID { get; set; } = string.Empty; // PCA
    public string ACTION_ID { get; set; } = string.Empty; // PCA_1
    public int DEL_SEQUENCE { get; set; } = 10; // Should be last one existing on project
    public string DELIVERABLE_ACTION { get; set; } = string.Empty;
    public string DELIVERABLE_ACEPTANCE_CRITERIA { get; set; } = string.Empty;
    public string RESPONSIBLE_JOB_TITLE { get; set; } = string.Empty;
    public string ACCOUNTABLE_JOB_TITLE { get; set; } = string.Empty;
    public string PATH_TO_SAVE { get; set; } = string.Empty;
    public int EFFORT_RATE { get; set; } = 1;

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
public class JOB_TITLE
{
    public string JOB_TITLE_ID { get; set; } = string.Empty;
    public string JOB_TITLE_DESCRIPTION { get; set; } = string.Empty;
}

public class DeliverableAllocationRow
{
    public string Opportunity_Line_id { get; set; } = string.Empty;
    public string StatusId { get; set; } = string.Empty;
    public string GateId { get; set; } = string.Empty;
    public string DeliverableId { get; set; } = string.Empty;
    public string OpportunityName { get; set; } = string.Empty;
    public string DeliverableName { get; set; } = string.Empty;
    public string ResponsibleUserId { get; set; } = string.Empty;
    public string AccountableUserId { get; set; } = string.Empty;
    public string ResponsibleJobTitle { get; set; } = string.Empty;
    public string AccountableJobTitle { get; set; } = string.Empty;
    public AllocationState ResponsibleState { get; set; }
    public AllocationState AccountableState { get; set; }
    public decimal EffortRate { get; set; }
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

public enum AllocationState
{
    NotStarted = 1,
    InProgress = 2,
    PendingReview = 3,
    Completed = 4    // role reached final state — row still returned because the other role is still active
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
        throw new ArgumentException($"Valor de prioridad no v�lido: {value}");
    }
}

public class MEETING_HEADER
{
    public string StatusId                { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
    public DateOnly MeetingDate           { get; set; } = DateOnly.MinValue;
    public string CurrentStatus           { get; set; } = string.Empty;
    public string MeetingTarget           { get; set; } = string.Empty;
    public string MeetingMinutes          { get; set; } = string.Empty;
    public string MeetingUserGen          { get; set; } = string.Empty;
    public DateTime MeetingDateGeneration { get; set; } = DateTime.MinValue;
}

public class MEETING_USER
{
    public string StatusId              { get; set; } = string.Empty;
    public DateOnly MeetingDate         { get; set; } = DateOnly.MinValue;
    public string MeetingUserRequest    { get; set; } = string.Empty;
    public bool MeetingUserCame         { get; set; } = false;
    public bool MeetingUserReviewed     { get; set; } = false;
}

public class MEETING_PROJECT
{
    public string StatusId              { get; set; } = string.Empty;
    public DateOnly MeetingDate         { get; set; } = DateOnly.MinValue;
    public string OpportunityLineId     { get; set; } = string.Empty;
    public string MeetingMinutes        { get; set; } = string.Empty;
}

public class MEETING_DETAIL
{
    public MEETING_HEADER Header            { get; set; } = new();
    public List<MEETING_USER> Users         { get; set; } = [];
    public List<MEETING_PROJECT> Projects   { get; set; } = [];
}

public class PROJECT_STATUS_PICKING_LIST
{
    public string StatusId          { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
}