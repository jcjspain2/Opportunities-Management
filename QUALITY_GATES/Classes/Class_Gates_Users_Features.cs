using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;

namespace QUALITY_GATES.Classes
{
    public partial class Class_Projects_Quality_Gates // Features related with Users
    {
        /// <summary>
        /// Updates an especific deliverable with ginen USER_ID
        /// </summary>
        /// <param name="Role">Must use Estructure for role</param>
        /// <returns>Sucess or fail with detailed message</returns>
        public async Task<Return_SQL_Action> AllocateUserIDToDeliverable(string OPP_LINE_ID,
                                                                          string STATUS_ID,
                                                                          string SGATE_ID,
                                                                          string DELIVERABLE_ID,
                                                                          string USER_ID_TO_ALLOCATE,
                                                                          string USER_ID_WHO_REQUEST,
                                                                          DeliverableRolesEstructure Role,
                                                                          CancellationToken cancellationToken = default)
        {
            string Type_Alloc = Role switch
            {
                DeliverableRolesEstructure.Responsible => "RESP",
                DeliverableRolesEstructure.Accountable => "ACC",
                _ => throw new ArgumentException($"Invalid role: {Role}", nameof(Role))
            };
            return await AllocateUserIDToDeliverable(OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID, USER_ID_TO_ALLOCATE, Type_Alloc, cancellationToken);
        }
        /// <summary>
        /// Updates an especific deliverable with ginen USER_ID
        /// </summary>
        /// <param name="Type_Alloc">RESP updates responsible, ACC updates accountant person</param>
        /// <returns>Sucess or fail with detailed message</returns>
        private async Task<Return_SQL_Action> AllocateUserIDToDeliverable(string OPP_LINE_ID,
                                                                          string STATUS_ID,
                                                                          string SGATE_ID,
                                                                          string DELIVERABLE_ID,
                                                                          string USER_ID,
                                                                          string Type_Alloc = "RESP",
                                                                          CancellationToken cancellationToken = default)
        {
            if (Type_Alloc != "RESP" && Type_Alloc != "ACC")
                return new Return_SQL_Action
                {
                    Success = false,
                    Message = $"Type_Alloc '{Type_Alloc}' is not valid. Allowed values: 'RESP' or 'ACC'.",
             
                };

            string fieldToUpdate = Type_Alloc == "RESP" ? "RESPONSIBLE_USER_ID" : "ACCOUNTABLE_USER_ID";

            string sql = $"""
                UPDATE dbo.TRA_PROJECTS_DELIVERABLES
                SET {fieldToUpdate} = @USER_ID,
                    MODIFIED_BY     = @USER_ID,
                    MODIFIED_DATE   = GETDATE()
                WHERE OPP_LINE_ID    = @OPP_LINE_ID
                  AND STATUS_ID      = @STATUS_ID
                  AND SGATE_ID       = @SGATE_ID
                  AND DELIVERABLE_ID = @DELIVERABLE_ID
                """;

            var parameters = new[]
            {
                new SqlParameter("@USER_ID",        USER_ID),
                new SqlParameter("@OPP_LINE_ID",    OPP_LINE_ID),
                new SqlParameter("@STATUS_ID",      STATUS_ID),
                new SqlParameter("@SGATE_ID",       SGATE_ID),
                new SqlParameter("@DELIVERABLE_ID", DELIVERABLE_ID)
            };

            return await _db.NonQueryDataToSQLServer(sql, parameters, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Gets users that can be allocated in a job description
        /// </summary>
        /// <param name="JOB_TITLE_ID">Job Title from where function will retrieve users that fits</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Lists of users that fits requierement</returns>
        public async Task<OperationResult<List<USERS_DETAILS>>> Get_Users_Fits_Job_Description(string JOB_TITLE_ID,
                                                                                               CancellationToken cancellationToken = default)
        {
            List<USERS_DETAILS> userList = new();
            var parameters = new[] { new SqlParameter("@JOB_TITLE_ID", JOB_TITLE_ID) };

            try
            {
                var queryResult = await _db.GetDatatableFromSelectAsync(
                    SQL_USERS_FITS_JOB_DESCRIPTION(), parameters, cancellationToken: cancellationToken);

                if (!queryResult.Success || queryResult.DTResults == null)
                    return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {queryResult.Message}");

                foreach (DataRow row in queryResult.DTResults.Rows)
                {
                    userList.Add(new USERS_DETAILS
                    {
                        UserId                      = GetString(row, "samaccountname"),
                        User_Display_Name           = GetString(row, "DisplayName"),
                        User_Q_GATES_Job_Title      = GetString(row, "JOB_TITLE_DESCRIPTION"),
                        User_Q_GATES_Job_Title_ID    = GetString(row, "JOB_TITLE_ID"),
                        User_Cadena_JobTitle        = GetString(row, "jobTitle"),
                        UserSite                    = GetString(row, "Office"),
                        UserManager_Mail            = GetString(row, "ManagerEmail"),
                        UserManager_Functional_Mail = GetString(row, "FunManagerEmail")
                    });
                }

                return OperationResult<List<USERS_DETAILS>>.Ok(_db.DeepCopyList(userList));
            }
            catch (Exception ex)
            {
                return OperationResult<List<USERS_DETAILS>>.Fail($"Error: {ex.Message}");
            }
        }
        /// <summary>
        /// Gets user details
        /// </summary>
        /// <param name="USER_ID">User ID we are going to dsplay details</param>
        /// <param name="cancellationToken"></param>
        /// <returns>Lists of users that fits requierement</returns>
        public async Task<OperationResult<USERS_DETAILS>> Get_User_Details(string USER_ID,
                                                                           CancellationToken cancellationToken = default)
        {
            var parameters = new[] { new SqlParameter("@USER_ID", USER_ID) };

            try
            {
                var queryResult = await _db.GetDatatableFromSelectAsync(
                    SQL_GET_USER_DETAILS(), parameters, cancellationToken: cancellationToken);

                if (!queryResult.Success || queryResult.DTResults == null)
                    return OperationResult<USERS_DETAILS>.Fail($"Error: {queryResult.Message}");

                if (queryResult.DTResults.Rows.Count == 0)
                    return OperationResult<USERS_DETAILS>.Fail($"User '{USER_ID}' not found.");

                DataRow row = queryResult.DTResults.Rows[0];
                return OperationResult<USERS_DETAILS>.Ok(new USERS_DETAILS
                {
                    UserId                      = GetString(row, "samaccountname"),
                    User_Display_Name           = GetString(row, "DisplayName"),
                    User_Q_GATES_Job_Title      = GetString(row, "JOB_TITLE_DESCRIPTION"),
                    User_Q_GATES_Job_Title_ID   = GetString(row, "JOB_TITLE_ID"),
                    User_Cadena_JobTitle        = GetString(row, "jobTitle"),
                    UserSite                    = GetString(row, "Office"),
                    UserManager_Mail            = GetString(row, "ManagerEmail"),
                    UserManager_Functional_Mail = GetString(row, "FunManagerEmail")
                });
            }
            catch (Exception ex)
            {
                return OperationResult<USERS_DETAILS>.Fail($"Error: {ex.Message}");
            }
        }

        private string SQL_GET_USER_DETAILS()
        {
            return @"
                SELECT T1.samaccountname,T1.EmailAddress,T1.GivenName,T1.Surname,
                       T1.DisplayName,T1.Title,T1.Department,T1.Office,
                       T2.ManagerEmail,T2.FunManagerEmail,T2.State,JobRole,T2.jobTitle,employeeLevel,
                       T3.JOB_TITLE_ID,T4.JOB_TITLE_DESCRIPTION
                FROM dbo.MAS_AD_Users T1
                LEFT JOIN dbo.MAS_USERS_CADENA T2 ON T1.EmailAddress=T2.Email
                LEFT JOIN dbo.MAS_JOB_TITLES_CADENA T3 ON T3.JOB_TITLE_CADENA=T2.jobTitle
                LEFT JOIN dbo.MAS_JOB_TITLES T4 ON T3.JOB_TITLE_ID=T4.JOB_TITLE_ID
                WHERE T1.samaccountname=@USER_ID";
        }

        private string SQL_USERS_FITS_JOB_DESCRIPTION()
        {
            return @"
                SELECT T1.samaccountname,T1.EmailAddress,T1.GivenName,T1.Surname,
                       T1.DisplayName,T1.Title,T1.Department,T1.Office,
                       T2.ManagerEmail,T2.FunManagerEmail,T2.State,JobRole,T2.jobTitle,employeeLevel,
                       T3.JOB_TITLE_ID,T4.JOB_TITLE_DESCRIPTION
                FROM dbo.MAS_AD_Users T1
                LEFT JOIN dbo.MAS_USERS_CADENA T2 on T1.EmailAddress=T2.Email
                LEFT JOIN dbo.MAS_JOB_TITLES_CADENA T3 ON T3.JOB_TITLE_CADENA=T2.jobTitle
                LEFT JOIN dbo.MAS_JOB_TITLES T4 ON T3.JOB_TITLE_ID=T4.JOB_TITLE_ID
                WHERE T1.Enabled=1 and T1.EmailAddress <> '' AND T3.JOB_TITLE_ID=@JOB_TITLE_ID "; 

        }
    }
}
