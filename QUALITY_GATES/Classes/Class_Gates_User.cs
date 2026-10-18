

using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Models;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using UtilidadesFichero;
using static QUALITY_GATES.Tools.Static_Local_Functions;

namespace QUALITY_GATES.Classes;
public partial class Class_Projects_Quality_Gates
{

    /// <summary>
    /// Returns one <see cref="DeliverableAllocationRow"/> per active deliverable in the given module.
    /// A deliverable is active when at least one role (Responsible or Accountable) has not reached a final state.
    /// Each row carries both role states as <see cref="AllocationState"/> — the UI applies LINQ grouping and
    /// aggregation to produce any workload view it needs.
    /// </summary>
    public async Task<OperationResult<List<DeliverableAllocationRow>>> GetDashboardCurrentAllocations(
        CancellationToken cancellationToken = default)
    {
        string Sql;
        switch (_MODULE_ID_ENUM)
        {
            case Module_ID.Q_GATES:
                Sql = @" SELECT T1.OPP_LINE_ID,T1.STATUS_ID,T1.SGATE_ID,T1.DELIVERABLE_ID,
                              TP.OPPORTUNITY_NAME,T1.DELIVERABLE_NAME,RESPONSIBLE_USER_ID,
                              ACCOUNTABLE_USER_ID,
                              T4.JOB_TITLE_DESCRIPTION JOB_TITLE_RESP,
                              T5.JOB_TITLE_DESCRIPTION JOB_TITLE_ACC,
                              T2.IS_INITIAL_STATE RESP_INITIAL_STATE,
                              T2.IS_PENDING_REVIEW RESP_PENDING_REVIEW,
                              T2.IS_IN_PROGRESS RESP_IN_PROGRESS,
                              T3.IS_INITIAL_STATE ACC_INITIAL_STATE,
                              T3.IS_PENDING_REVIEW ACC_PENDING_STATE,
                              T3.IS_IN_PROGRESS ACC_IN_PROGRESS
                       FROM dbo.TRA_PROJECTS_DELIVERABLES T1
                       JOIN dbo.TRA_PROJECTS TP ON TP.OPPORTUNITY_LINE_ID=T1.OPP_LINE_ID AND TP.IsLost_SF=0 AND TP.IsAbandoned=0 AND TP.Is_Q_GATES=1
                       JOIN dbo.TRA_PROJECTS_STATUS AS TS ON TS.OPP_LINE_ID=T1.OPP_LINE_ID AND TS.STATUS_ID=T1.STATUS_ID AND TS.MODULE_ID='Q_GATES'
                       JOIN dbo.MAS_GATE_STATUS T2 ON T2.MODULE_ID='Q_GATES' AND T2.KEY_PROCESS='DEL_RESP' AND T1.DELIVERABLE_STATUS_ID=T2.GATE_STATUS_ID
                       JOIN dbo.MAS_GATE_STATUS T3 ON T3.MODULE_ID='Q_GATES' AND T3.KEY_PROCESS='DEL_ACC' AND T1.ACCOUNTABLE_STATUS_ID=T3.GATE_STATUS_ID
                       JOIN dbo.MAS_JOB_TITLES T4 ON T4.JOB_TITLE_ID =T1.RESPONSIBLE_JOB_ID
                       JOIN dbo.MAS_JOB_TITLES T5 ON T5.JOB_TITLE_ID =T1.ACCOUNTABLE_JOB_ID
                       WHERE (T2.IS_FINAL_STATE=0 OR T3.IS_FINAL_STATE=0)";
                break;
            case Module_ID.Q_GATES_PR:
            default:
                Sql = @"
               SELECT T1.OPP_LINE_ID,T1.STATUS_ID,T1.SGATE_ID,T1.DELIVERABLE_ID,
                      TP.OPPORTUNITY_NAME,T1.DELIVERABLE_NAME,RESPONSIBLE_USER_ID,
                      ACCOUNTABLE_USER_ID,
                              T4.JOB_TITLE_DESCRIPTION JOB_TITLE_RESP,
                              T5.JOB_TITLE_DESCRIPTION JOB_TITLE_ACC,
                              T2.IS_INITIAL_STATE RESP_INITIAL_STATE,
                              T2.IS_PENDING_REVIEW RESP_PENDING_REVIEW,
                              T2.IS_IN_PROGRESS RESP_IN_PROGRESS,
                              T3.IS_INITIAL_STATE ACC_INITIAL_STATE,
                              T3.IS_PENDING_REVIEW ACC_PENDING_STATE,
                              T3.IS_IN_PROGRESS ACC_IN_PROGRESS
               FROM dbo.TRA_PROJECTS_DELIVERABLES T1
               JOIN dbo.TRA_PROJECTS TP ON TP.OPPORTUNITY_LINE_ID=T1.OPP_LINE_ID AND TP.IsLost_SF=0 AND TP.IsAbandoned=0 AND TP.Is_Q_GATES_PR=1
               JOIN dbo.TRA_PROJECTS_STATUS AS TS ON TS.OPP_LINE_ID=T1.OPP_LINE_ID AND TS.STATUS_ID=T1.STATUS_ID AND TS.MODULE_ID='Q_GATES_PR'
               JOIN dbo.MAS_GATE_STATUS T2 ON T2.MODULE_ID='Q_GATES_PR' AND T2.KEY_PROCESS='DEL_RESP' AND T1.DELIVERABLE_STATUS_ID=T2.GATE_STATUS_ID
               JOIN dbo.MAS_GATE_STATUS T3 ON T3.MODULE_ID='Q_GATES_PR' AND T3.KEY_PROCESS='DEL_ACC' AND T1.ACCOUNTABLE_STATUS_ID=T3.GATE_STATUS_ID
               JOIN dbo.MAS_JOB_TITLES T4 ON T4.JOB_TITLE_ID =T1.RESPONSIBLE_JOB_ID
               JOIN dbo.MAS_JOB_TITLES T5 ON T5.JOB_TITLE_ID =T1.ACCOUNTABLE_JOB_ID
               WHERE (T2.IS_FINAL_STATE=0 OR T3.IS_FINAL_STATE=0)";
                break;
        }

        var queryResult = await _db.SelectAsync(Sql, null, cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<List<DeliverableAllocationRow>>.Fail($"Error: {queryResult.Message}");

        var result = new List<DeliverableAllocationRow>(queryResult.DTResults.Rows.Count);

        foreach (DataRow row in queryResult.DTResults.Rows)
        {
            result.Add(new DeliverableAllocationRow
            {
                Opportunity_Line_id= GetString(row, "OPP_LINE_ID"),
                StatusId = GetString(row, "STATUS_ID"),
                GateId = GetString(row, "SGATE_ID"),
                DeliverableId = GetString(row, "DELIVERABLE_ID"),
                OpportunityName = GetString(row, "OPPORTUNITY_NAME"),
                DeliverableName = GetString(row, "DELIVERABLE_NAME"),
                ResponsibleUserId = GetString(row, "RESPONSIBLE_USER_ID"),
                AccountableUserId = GetString(row, "ACCOUNTABLE_USER_ID"),
                ResponsibleJobTitle = GetString(row, "JOB_TITLE_RESP"),
                AccountableJobTitle = GetString(row, "JOB_TITLE_ACC"),
                ResponsibleState    = ResolveAllocationState(row, "RESP_INITIAL_STATE", "RESP_IN_PROGRESS", "RESP_PENDING_REVIEW"),
                AccountableState    = ResolveAllocationState(row, "ACC_INITIAL_STATE", "ACC_IN_PROGRESS", "ACC_PENDING_STATE"),
                EffortRate          = 0,
            });
        }

        return OperationResult<List<DeliverableAllocationRow>>.Ok(result);
    }
    private static AllocationState ResolveAllocationState(DataRow row, string notStartedCol, string inProgressCol, string pendingCol)
    {
        if (Convert.ToBoolean(row[inProgressCol]))  return AllocationState.InProgress;
        if (Convert.ToBoolean(row[pendingCol]))     return AllocationState.PendingReview;
        if (Convert.ToBoolean(row[notStartedCol]))  return AllocationState.NotStarted;
        return AllocationState.Completed;
    }

    /// <summary>
    /// Checks whether the requesting user's job title is authorized for a given key process.
    /// </summary>
    /// <param name="KeyProcess">Key process to check restrictions against (e.g. 'GATE_GEN').</param>
    /// <param name="UserRequester">User ID (samaccountname) of the user making the request.</param>
    /// <returns>
    /// The <see cref="USERS_DETAILS"/> of the user if authorized.
    /// Throws <see cref="InvalidOperationException"/> if the user is not found, has no standard job title,
    /// no restrictions are defined for the key process, or the user's job title is not in the authorized list.
    /// </returns>
    private async Task<OperationResult<bool>> Get_KeyProcess_Job_title_Restriction(string KeyProcess,
                                                                            string UserRequester = "System",
                                                                            CancellationToken cancellationToken = default)
    {
        // Step 1: resolve user's job title — returns Fail if user not found or has no standard job title
        var userDetails = await GetUser_ID_Job_Description(UserRequester, cancellationToken);
        if (!userDetails.Success)
            return OperationResult<bool>.Fail(userDetails.ErrorMessage);

        // Step 2: retrieve authorized job titles for this key process
        string sql_KEY_PROCESS_CHECK = """
                  SELECT [JOB_TITLE]
                  FROM [dbo].[MAS_JOB_TITLES_RESTRICTIONS]
                  WHERE MODULE_ID = @MODULE_ID AND KEY_PROCESS = @KeyProcess
                  """;

        var parameters = new[]
        {
            new SqlParameter("@MODULE_ID", _MODULE_ID),
            new SqlParameter("@KeyProcess", KeyProcess)
        };

        var queryResult = await _db.SelectAsync(sql_KEY_PROCESS_CHECK, parameters, cancellationToken);

        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<bool>.Fail($"Database error retrieving restrictions for key process '{KeyProcess}': {queryResult.Message}");

        if (queryResult.DTResults.Rows.Count == 0)
            return OperationResult<bool>.Fail($"No restrictions defined for key process '{KeyProcess}' in module '{_MODULE_ID}'.");


        // Step 3: check if the user's job title is in the authorized list
        var authorizedRoles = queryResult.DTResults.Rows
            .Cast<DataRow>()
            .Select(r => GetString(r, "JOB_TITLE"))
            .ToList();

        if (!authorizedRoles.Contains(userDetails.Data.User_Q_GATES_Job_Title_ID, StringComparer.OrdinalIgnoreCase))
            return OperationResult<bool>.Fail(
                $"User '{UserRequester}' with job title '{userDetails.Data.User_Q_GATES_Job_Title_ID}' " +
                $"is not authorized for key process '{KeyProcess}'. " +
                $"Allowed roles: {string.Join(", ", authorizedRoles)}.");

        return OperationResult<bool>.Ok(true);
    }

    /// <summary>
    /// Recovers Job Title of an especific USER_ID if can not recover produces an error.
    /// </summary>
    /// <param name="UserRequester">User logged into app that makes request.</param>
    /// <returns>Return object USER_DETAILS, to display Job description and more details, if not recovered show error</returns>

    private async Task<OperationResult<USERS_DETAILS>> GetUser_ID_Job_Description(string UserRequester = "System",
                                                                     CancellationToken cancellationToken = default)
    {
        const string sqlUser_Job_title = """
                  SELECT T1.samaccountname,T1.EmailAddress,T1.GivenName,T1.Surname,
                         T1.DisplayName,T1.Title,T1.Department,T1.Office,
                         T2.ManagerEmail,T2.FunManagerEmail,T2.State,JobRole,T2.jobTitle,employeeLevel,
                         ISNULL(T3.JOB_TITLE_ID,'') JOB_TITLE_ID,ISNULL(T4.JOB_TITLE_DESCRIPTION,'') JOB_TITLE_DESCRIPTION
                  FROM dbo.MAS_AD_Users T1
                  LEFT JOIN dbo.MAS_USERS_CADENA T2 on T1.EmailAddress=T2.Email
                  LEFT JOIN dbo.MAS_JOB_TITLES_CADENA T3 ON T3.JOB_TITLE_CADENA=T2.jobTitle
                  LEFT JOIN dbo.MAS_JOB_TITLES T4 ON T3.JOB_TITLE_ID=T4.JOB_TITLE_ID
                  WHERE T1.Enabled=1 and T1.EmailAddress <> '' AND T1.samaccountname=@User_ID
                  """;

        var parameters = new[] { new SqlParameter("@User_ID", UserRequester) };

        var queryResult = await _db.SelectAsync(sqlUser_Job_title, parameters, cancellationToken);

        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<USERS_DETAILS>.Fail($"Database error retrieving user '{UserRequester}': {queryResult.Message}");

        if (queryResult.DTResults.Rows.Count == 0)
            return OperationResult<USERS_DETAILS>.Fail($"User '{UserRequester}' was not found in the system.");

        DataRow row = queryResult.DTResults.Rows[0];
        string jobTitleDescription = GetString(row, "JOB_TITLE_DESCRIPTION");

        if (string.IsNullOrEmpty(jobTitleDescription))
            return OperationResult<USERS_DETAILS>.Fail($"User '{UserRequester}' does not have a standard job description assigned. " +
                $"The job title '{GetString(row, "jobTitle")}' is not mapped to any Quality Gates standard role.");

        return OperationResult<USERS_DETAILS>.Ok(new USERS_DETAILS
        {
            UserId = GetString(row, "samaccountname"),
            User_Display_Name = GetString(row, "DisplayName"),
            User_Q_GATES_Job_Title = jobTitleDescription,
            User_Q_GATES_Job_Title_ID = GetString(row, "JOB_TITLE_ID"),
            User_Cadena_JobTitle = GetString(row, "jobTitle"),
            UserSite = GetString(row, "Office"),
            UserManager_Mail = GetString(row, "ManagerEmail"),
            UserManager_Functional_Mail = GetString(row, "FunManagerEmail")
        });

    }
    /// <summary>
    /// Gets collaboratives allready set
    /// </summary>
    /// <param name="OPP_LINE_ID"></param>
    /// <param name="STATUS_ID"></param>
    /// <param name="GATE_ID"></param>
    /// <param name="DELIVERABLE_ID"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>List of user collaboratives ready to use in UI</returns>
    public async Task<OperationResult<List<USERS_COLLABORATIVE>>> Get_Collaboratives_Deliverable(
        string OPP_LINE_ID, string STATUS_ID, string GATE_ID, int DELIVERABLE_ID,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID, User_ID, User_Mail, RESP_COMMENT
            FROM dbo.TRA_PROJECTS_DELIVERABLE_COOPERATION
            WHERE OPP_LINE_ID = @OPP_LINE_ID
              AND STATUS_ID   = @STATUS_ID
              AND SGATE_ID    = @SGATE_ID
              AND DELIVERABLE_ID = @DELIVERABLE_ID
            """;

        var parameters = new[]
        {
            new SqlParameter("@OPP_LINE_ID",    OPP_LINE_ID),
            new SqlParameter("@STATUS_ID",      STATUS_ID),
            new SqlParameter("@SGATE_ID",       GATE_ID),
            new SqlParameter("@DELIVERABLE_ID", DELIVERABLE_ID)
        };

        var queryResult = await _db.SelectAsync(sql, parameters, cancellationToken);

        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<List<USERS_COLLABORATIVE>>.Fail($"Error: {queryResult.Message}");

        var resultList = new List<USERS_COLLABORATIVE>();
        foreach (DataRow row in queryResult.DTResults.Rows)
        {
            resultList.Add(new USERS_COLLABORATIVE
            {
                UserId = GetString(row, "User_ID"),
                Responsible_Task_Comment = GetString(row, "RESP_COMMENT")
            });
        }

        return OperationResult<List<USERS_COLLABORATIVE>>.Ok(resultList);
    }
    /// <summary>
    /// REturns all users ID .
    /// </summary>
    /// <param name="UserRequest">User logged into app that makes requests, restriction can be checked.</param>
    /// <returns>Return object USER_DETAILS, to display and choose</returns>
    public async Task<OperationResult<List<USERS_DETAILS>>> Get_Users_ID_All(string UserRequest)
{
CancellationToken cancellationToken = default;
const string SQL_Users_Id_By_JobTitle = """
             SELECT T1.samaccountname,T1.EmailAddress,T1.GivenName,T1.Surname,
                         T1.DisplayName,T1.Title,T1.Department,T1.Office,
                         T2.ManagerEmail,T2.FunManagerEmail,T2.State,JobRole,T2.jobTitle,employeeLevel,
                         T3.JOB_TITLE_ID,T4.JOB_TITLE_DESCRIPTION
             FROM dbo.MAS_AD_Users T1
             LEFT JOIN dbo.MAS_USERS_CADENA T2 on T1.EmailAddress=T2.Email
             LEFT JOIN dbo.MAS_JOB_TITLES_CADENA T3 ON T3.JOB_TITLE_CADENA=T2.jobTitle
             LEFT JOIN dbo.MAS_JOB_TITLES T4 ON T3.JOB_TITLE_ID=T4.JOB_TITLE_ID
             WHERE T1.Enabled=1 and T1.EmailAddress <> ''
          """;
var queryResult = await _db.SelectAsync(SQL_Users_Id_By_JobTitle,null, cancellationToken);
if (!queryResult.Success || queryResult.DTResults == null)
    return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {queryResult.Message}");

if (queryResult.DTResults.Rows.Count == 0)
    return OperationResult<List<USERS_DETAILS>>.Fail($"No users found.");

try
{
    var resultList = new List<USERS_DETAILS>();
    foreach (DataRow row in queryResult.DTResults.Rows)
    {
        resultList.Add(new USERS_DETAILS
        {
            UserId = GetString(row, "samaccountname"),
            User_Display_Name = GetString(row, "DisplayName"),
            User_Q_GATES_Job_Title = GetString(row, "JOB_TITLE_DESCRIPTION"),
            User_Q_GATES_Job_Title_ID = GetString(row, "JOB_TITLE_ID"),
            User_Cadena_JobTitle = GetString(row, "jobTitle"),
            UserSite = GetString(row, "Office"),
            UserManager_Mail = GetString(row, "ManagerEmail"),
            UserManager_Functional_Mail = GetString(row, "FunManagerEmail"),
        });
    }
    return OperationResult<List<USERS_DETAILS>>.Ok(resultList);
}
catch (Exception ex)
{
    return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {ex.Message}");
}

}


/// <summary>
/// REturns all users ID that belong to a given job title.
/// </summary>
/// <param name="UserRequest">User logged into app that makes requests, restriction can be checked.</param>
/// <param name="JobTitle">Job Title Target where User Id should be allocated</param>
/// <returns>Return object USER_DETAILS, to display and choose</returns>
public async Task<OperationResult<List<USERS_DETAILS>>> Get_Users_ID_By_JobTitle(string UserRequest, string JobTitle)
{
CancellationToken cancellationToken = default;
const string SQL_Users_Id_By_JobTitle = """
             SELECT T1.samaccountname,T1.EmailAddress,T1.GivenName,T1.Surname,
                         T1.DisplayName,T1.Title,T1.Department,T1.Office,
                         T2.ManagerEmail,T2.FunManagerEmail,T2.State,JobRole,T2.jobTitle,employeeLevel,
                         T3.JOB_TITLE_ID,T4.JOB_TITLE_DESCRIPTION
             FROM dbo.MAS_AD_Users T1
             LEFT JOIN dbo.MAS_USERS_CADENA T2 on T1.EmailAddress=T2.Email
             LEFT JOIN dbo.MAS_JOB_TITLES_CADENA T3 ON T3.JOB_TITLE_CADENA=T2.jobTitle
             LEFT JOIN dbo.MAS_JOB_TITLES T4 ON T3.JOB_TITLE_ID=T4.JOB_TITLE_ID
             WHERE T1.Enabled=1 and T1.EmailAddress <> '' AND T3.JOB_TITLE_ID=@JOB_TITLE_ID
          """;
var parameters = new[] { new SqlParameter("@JOB_TITLE_ID", JobTitle) };

var queryResult = await _db.SelectAsync(SQL_Users_Id_By_JobTitle, parameters, cancellationToken);
if (!queryResult.Success || queryResult.DTResults == null)
    return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {queryResult.Message}");

if (queryResult.DTResults.Rows.Count == 0)
    return OperationResult<List<USERS_DETAILS>>.Fail($"No users found for job title '{JobTitle}'.");

try
{
    var resultList = new List<USERS_DETAILS>();
    foreach (DataRow row in queryResult.DTResults.Rows)
    {
        resultList.Add(new USERS_DETAILS
        {
            UserId = GetString(row, "samaccountname"),
            User_Display_Name = GetString(row, "DisplayName"),
            User_Q_GATES_Job_Title = GetString(row, "JOB_TITLE_DESCRIPTION"),
            User_Q_GATES_Job_Title_ID = GetString(row, "JOB_TITLE_ID"),
            User_Cadena_JobTitle = GetString(row, "jobTitle"),
            UserSite = GetString(row, "Office"),
            UserManager_Mail = GetString(row, "ManagerEmail"),
            UserManager_Functional_Mail = GetString(row, "FunManagerEmail"),
        });
    }
    return OperationResult<List<USERS_DETAILS>>.Ok(resultList);
}
catch (Exception ex)
{
    return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {ex.Message}");
}
}



}
