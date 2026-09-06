namespace QUALITY_GATES.Classes;

using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UtilidadesFichero;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;

public partial class Class_Projects_Quality_Gates
{

    /// <summary>
    /// Allow to get current situation regarding deliverables allocations not finished.
    /// </summary>
    private async Task<OperationResult<bool>> GetDashboardCurrentAllocations(CancellationToken cancellationToken =default)
    {
        //TODO: Esto no va a servir para usar en la gestión dd proyectos
        const string sql_KEY_PROCESS_CHECK = @""" 
                  SELECT OPP_LINE_ID,STATUS_ID,SGATE_ID,DELIVERABLE_ID,RESPONSIBLE_JOB_ID,ACCOUNTABLE_JOB_ID,RESPONSIBLE_USER_ID,ACCOUNTABLE_USER_ID,
                         T4.JOB_TITLE_DESCRIPTION,T5.JOB_TITLE_DESCRIPTION,
                         T2.IS_INITIAL_STATE,T2.IS_PENDING_REVIEW,T2.IS_IN_PROGRESS,
                         T3.IS_INITIAL_STATE,T3.IS_PENDING_REVIEW,T3.IS_IN_PROGRESS
                  FROM dbo.TRA_PROJECTS_DELIVERABLES T1
                  JOIN dbo.TRA_PROJECTS TP ON TP.OPPORTUNITY_LINE_ID=T1.OPP_LINE_ID AND IsLost_SF=0
                  JOIN dbo.MAS_GATE_STATUS T2 ON T2.MODULE_ID='Q_GATES' AND T2.KEY_PROCESS='DEL_RESP' AND T1.DELIVERABLE_STATUS_ID=T2.GATE_STATUS_ID 
                  JOIN dbo.MAS_GATE_STATUS T3 ON T3.MODULE_ID='Q_GATES' AND T3.KEY_PROCESS='DEL_ACC' AND T1.DELIVERABLE_STATUS_ID=T3.GATE_STATUS_ID 
                  JOIN dbo.MAS_JOB_TITLES T4 ON T4.JOB_TITLE_ID =T1.RESPONSIBLE_JOB_ID
                  JOIN dbo.MAS_JOB_TITLES T5 ON T5.JOB_TITLE_ID =T1.ACCOUNTABLE_JOB_ID
                  WHERE T2.IS_FINAL_STATE=0 OR T3.IS_FINAL_STATE=0
                   """;

        return  OperationResult<bool>.Fail($"Error");

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
        //TODO: Use MODULE_ID instead Q_GATES
        const string sql_KEY_PROCESS_CHECK = """
                  SELECT [JOB_TITLE]
                  FROM [dbo].[MAS_JOB_TITLES_RESTRICTIONS]
                  WHERE MODULE_ID = 'Q_GATES' AND KEY_PROCESS = @KeyProcess
                  """;

        var parameters = new[] { new SqlParameter("@KeyProcess", KeyProcess) };

        var queryResult = await _db.GetDatatableFromSelectAsync(sql_KEY_PROCESS_CHECK, parameters, cancellationToken: cancellationToken);

        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<bool>.Fail($"Database error retrieving restrictions for key process '{KeyProcess}': {queryResult.Message}");


        if (queryResult.DTResults.Rows.Count == 0)
            return OperationResult<bool>.Fail($"User '{UserRequester}' do not have proper job title to generate next gate.");


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

        var queryResult = await _db.GetDatatableFromSelectAsync(sqlUser_Job_title, parameters, cancellationToken: cancellationToken);

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
        var queryResult = await _db.GetDatatableFromSelectAsync(SQL_Users_Id_By_JobTitle,null, cancellationToken: cancellationToken);
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

        var queryResult = await _db.GetDatatableFromSelectAsync(SQL_Users_Id_By_JobTitle, parameters, cancellationToken: cancellationToken);
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
