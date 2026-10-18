using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;
using QUALITY_GATES.Models;

namespace QUALITY_GATES.Classes;

public partial class Class_Projects_Quality_Gates
{
    // ── SQL strings ──────────────────────────────────────────────────────────

    private static string SQL_Insert_Meeting_Header() => @"
        INSERT INTO dbo.SAP_MEETING_HEADER
               (STATUS_ID, MEETING_DATE, CURRENT_STATUS, MEETING_TARGET,
                MEETING_MINUTES, MEETING_USER_GEN, MEETING_DATE_GENERATION)
        VALUES (@StatusId, @MeetingDate, 'NOTSTARTED', @MeetingTarget,
                @MeetingMinutes, @MeetingUserGen, SYSDATETIME())";

    private static string SQL_Insert_Meeting_User() => @"
        INSERT INTO dbo.SAP_MEETING_USERS
               (STATUS_ID, MEETING_DATE, MEETING_USER_REQUEST,
                MEETING_USER_CAME, MEETING_USER_REVIEWED)
        VALUES (@StatusId, @MeetingDate, @UserRequest, @UserCame, @UserReviewed)";

    private static string SQL_Insert_Meeting_Project() => @"
        INSERT INTO dbo.SAP_MEETING_PROJECTS
               (STATUS_ID, MEETING_DATE, OPPORTUNITY_LINE_ID, MEETING_MINUTES)
        VALUES (@StatusId, @MeetingDate, @OppLineId, @Minutes)";

    private static string SQL_Select_Meetings_By_Gate(string CurrentModule, byte IsFinish = 0) => @$"
        SELECT  T1.STATUS_ID,STATUS_DESCRIPTION, MEETING_DATE, CURRENT_STATUS, MEETING_TARGET,
                MEETING_MINUTES, MEETING_USER_GEN, MEETING_DATE_GENERATION
        FROM   dbo.SAP_MEETING_HEADER T1
        JOIN   dbo.MAS_GATE_STATUS T2 ON T2.GATE_STATUS_ID=T1.CURRENT_STATUS AND MODULE_ID='{CurrentModule}' AND KEY_PROCESS='MEETING' AND IS_FINAL_STATE = {IsFinish}
        JOIN   dbo.MAS_STATUS T3 ON T3.STATUS_ID=T1.STATUS_ID AND STATUS_MODULE='{CurrentModule}'
        ORDER  BY MEETING_DATE DESC";

    private static string SQL_Select_Meeting_Header_Single() => @"
        SELECT STATUS_ID, MEETING_DATE, CURRENT_STATUS, MEETING_TARGET,
               MEETING_MINUTES, MEETING_USER_GEN, MEETING_DATE_GENERATION
        FROM   dbo.SAP_MEETING_HEADER
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Select_Meeting_Users() => @"
        SELECT STATUS_ID, MEETING_DATE, MEETING_USER_REQUEST,
               MEETING_USER_CAME, MEETING_USER_REVIEWED
        FROM   dbo.SAP_MEETING_USERS
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Select_Meeting_Projects() => @"
        SELECT STATUS_ID, MEETING_DATE, OPPORTUNITY_LINE_ID, MEETING_MINUTES
        FROM   dbo.SAP_MEETING_PROJECTS
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Status() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    CURRENT_STATUS = @NewStatus
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Minutes() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    MEETING_MINUTES = @Minutes
        WHERE  STATUS_ID = @StatusId
          AND  MEETING_DATE = @MeetingDate";

    private static string SQL_Delete_Meeting_Project() => @"
        DELETE FROM dbo.SAP_MEETING_PROJECTS
        WHERE STATUS_ID = @StatusId
          AND MEETING_DATE = @MeetingDate
          AND OPPORTUNITY_LINE_ID = @OppLineId";

    private static string SQL_Delete_Meeting_User() => @"
        DELETE FROM dbo.SAP_MEETING_USERS
        WHERE STATUS_ID = @StatusId
          AND MEETING_DATE = @MeetingDate
          AND MEETING_USER_REQUEST = @UserRequest";

    private static string SQL_Get_Status_Picking_List(string currentModule) => $@"
                    SELECT STATUS_ID, STATUS_DESCRIPTION
                    FROM dbo.MAS_STATUS
                    WHERE STATUS_MODULE='{currentModule}' AND IsDeleted=0
                    ORDER BY STATUS_SEQUENCE";


    // ── Public methods ───────────────────────────────────────────────────────
    /// <summary>
    /// Gets all posible Status_Id for current Module
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<OperationResult<List<PROJECT_STATUS_PICKING_LIST>>> GetPickingListStatusID( CancellationToken cancellationToken = default)
    {
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Get_Status_Picking_List(_MODULE_ID), null, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<PROJECT_STATUS_PICKING_LIST>>.Fail($"Error: {queryResult.Message}");

            List<PROJECT_STATUS_PICKING_LIST> list = [];
            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(new PROJECT_STATUS_PICKING_LIST
                {
                    StatusId          = GetString(row, "STATUS_ID"),
                    StatusDescription = GetString(row, "STATUS_DESCRIPTION")
                });

            return OperationResult<List<PROJECT_STATUS_PICKING_LIST>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<PROJECT_STATUS_PICKING_LIST>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Allow to create a meeting with its header, users and projects. All operations are done in a single transaction.
    /// </summary>
    /// <param name="STATUS_ID">Status is about meeting like PCA. FEAS</param>
    /// <param name="MEETING_DATE">Date when meeting is going to happend</param>
    /// <param name="MEETING_TARGET">Text title of the meeting</param>
    /// <param name="UserRequester">User that request meeting.</param>
    /// <param name="Users">List of users invited to meeting</param>
    /// <param name="Projects">List of projects that must be included in meeting</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SqlActionResult> Create_Meeting(  string STATUS_ID,
                                                        DateOnly MEETING_DATE,
                                                        string MEETING_TARGET,
                                                        string UserRequester = "System",
                                                        List<MEETING_USER>? Users = null,
                                                        List<MEETING_PROJECT>? Projects = null,
                                                        CancellationToken cancellationToken = default)
                                                      {
        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();
        try
        {
            var headerParams = new[]
            {
                new SqlParameter("@StatusId",       STATUS_ID),
                new SqlParameter("@MeetingDate",    MEETING_DATE),
                new SqlParameter("@MeetingTarget",  MEETING_TARGET),
                new SqlParameter("@MeetingMinutes", string.Empty),
                new SqlParameter("@MeetingUserGen", UserRequester)
            };
            var headerResult = await _db.NonQueryAsync(
                SQL_Insert_Meeting_Header(), headerParams, tx, cancellationToken);
            if (!headerResult.Success)
            {
                tx.Rollback();
                return new SqlActionResult { Success = false, Message = headerResult.Message };
            }

            foreach (var user in Users ?? [])
            {
                var userParams = new[]
                {
                    new SqlParameter("@StatusId",     STATUS_ID),
                    new SqlParameter("@MeetingDate",  MEETING_DATE),
                    new SqlParameter("@UserRequest",  user.MeetingUserRequest),
                    new SqlParameter("@UserCame",     user.MeetingUserCame),
                    new SqlParameter("@UserReviewed", user.MeetingUserReviewed)
                };
                var ur = await _db.NonQueryAsync(SQL_Insert_Meeting_User(), userParams, tx, cancellationToken);
                if (!ur.Success)
                {
                    tx.Rollback();
                    return new SqlActionResult { Success = false, Message = ur.Message };
                }
            }

            foreach (var project in Projects ?? [])
            {
                var projParams = new[]
                {
                    new SqlParameter("@StatusId",    STATUS_ID),
                    new SqlParameter("@MeetingDate", MEETING_DATE),
                    new SqlParameter("@OppLineId",   project.OpportunityLineId),
                    new SqlParameter("@Minutes",     project.MeetingMinutes)
                };
                var pr = await _db.NonQueryAsync(SQL_Insert_Meeting_Project(), projParams, tx, cancellationToken);
                if (!pr.Success)
                {
                    tx.Rollback();
                    return new SqlActionResult { Success = false, Message = pr.Message };
                }
            }

            tx.Commit();
            return new SqlActionResult { Success = true, Message = "OK", RecordsAffected = 1 };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
/// <summary>
/// Gets list of meetings
/// </summary>
/// <param name="IsMeetingFinish">0 Alive meetings , 1 Closed meetings</param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
    public async Task<OperationResult<List<MEETING_HEADER>>> Get_Meetings(byte IsMeetingFinish=0 ,CancellationToken cancellationToken = default)
    {
        List<MEETING_HEADER> list = [];
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Select_Meetings_By_Gate(_MODULE_ID, IsMeetingFinish), null, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<MEETING_HEADER>>.Fail($"Error: {queryResult.Message}");

            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(MapMeetingHeader(row));

            return OperationResult<List<MEETING_HEADER>>.Ok(DeepCopyList(list));
        }
        catch (Exception ex)
        {
            return OperationResult<List<MEETING_HEADER>>.Fail($"Error: {ex.Message}");
        }
    }
    /// <summary>
    /// Gets max detial of selected meeting
    /// </summary>
    /// <param name="STATUS_ID">Status_id of the meeting</param>
    /// <param name="MEETING_DATE">Date of the meeting</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<OperationResult<MEETING_DETAIL>> Get_Meeting_Detail(string STATUS_ID,
                                                                          DateOnly MEETING_DATE,
                                                                          CancellationToken cancellationToken = default)
    {
        var p = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };
        try
        {
            var headerResult = await _db.SelectAsync(SQL_Select_Meeting_Header_Single(), p, cancellationToken);
            if (!headerResult.Success || headerResult.DTResults == null)
                return OperationResult<MEETING_DETAIL>.Fail($"Error: {headerResult.Message}");
            if (headerResult.DTResults.Rows.Count == 0)
                return OperationResult<MEETING_DETAIL>.Fail($"Meeting '{STATUS_ID}/{MEETING_DATE:yyyy-MM-dd}' not found.");

            var detail = new MEETING_DETAIL { Header = MapMeetingHeader(headerResult.DTResults.Rows[0]) };

            var usersResult = await _db.SelectAsync(SQL_Select_Meeting_Users(), p, cancellationToken);
            if (usersResult.Success && usersResult.DTResults != null)
                foreach (DataRow row in usersResult.DTResults.Rows)
                    detail.Users.Add(new MEETING_USER
                    {
                        StatusId            = GetString(row, "STATUS_ID"),
                        MeetingDate         = GetDateOnly(row, "MEETING_DATE"),
                        MeetingUserRequest  = GetString(row, "MEETING_USER_REQUEST"),
                        MeetingUserCame     = GetBoolean(row, "MEETING_USER_CAME"),
                        MeetingUserReviewed = GetBoolean(row, "MEETING_USER_REVIEWED")
                    });

            var projResult = await _db.SelectAsync(SQL_Select_Meeting_Projects(), p, cancellationToken);
            if (projResult.Success && projResult.DTResults != null)
                foreach (DataRow row in projResult.DTResults.Rows)
                    detail.Projects.Add(new MEETING_PROJECT
                    {
                        StatusId          = GetString(row, "STATUS_ID"),
                        MeetingDate       = GetDateOnly(row, "MEETING_DATE"),
                        OpportunityLineId = GetString(row, "OPPORTUNITY_LINE_ID"),
                        MeetingMinutes    = GetString(row, "MEETING_MINUTES")
                    });

            return OperationResult<MEETING_DETAIL>.Ok(detail);
        }
        catch (Exception ex)
        {
            return OperationResult<MEETING_DETAIL>.Fail($"Error: {ex.Message}");
        }
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="STATUS_ID"></param>
    /// <param name="MEETING_DATE"></param>
    /// <param name="NewStatus"></param>
    /// <param name="UserRequester"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SqlActionResult> Update_Meeting_Status(
                                                            string STATUS_ID,
                                                            DateOnly MEETING_DATE,
                                                            string NewStatus,
                                                            string UserRequester = "System",
                                                            CancellationToken cancellationToken = default)
                                                        {
        if (UserRequester != "System")
        {
            var resultJob = await Get_KeyProcess_Job_title_Restriction("@MEETING", UserRequester, cancellationToken);
            if (!resultJob.Success)
                return new SqlActionResult { Success = false, Message = resultJob.ErrorMessage };
        }

        var (isValid, validationError) = await ValidateDeliverableStatusExistsAsync(NewStatus, "MEETING", cancellationToken);
        if (!isValid)
            return new SqlActionResult { Success = false, Message = validationError! };

        var parameters = new[]
        {
            new SqlParameter("@NewStatus",   NewStatus),
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };
        return await _db.NonQueryAsync(SQL_Update_Meeting_Status(), parameters, ct: cancellationToken);
    }

    public async Task<SqlActionResult> Update_Meeting_Minutes(
                                                                string STATUS_ID,
                                                                DateOnly MEETING_DATE,
                                                                string Minutes,
                                                                CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@Minutes",     Minutes),
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };
        return await _db.NonQueryAsync(SQL_Update_Meeting_Minutes(), parameters, ct: cancellationToken);
    }

    /// <summary>
    /// Allows to add Project Status into ameeting. This is useful when a project is added to a meeting after the meeting has been created.
    /// </summary>
    /// <param name="STATUS_ID"></param>
    /// <param name="MEETING_DATE"></param>
    /// <param name="OPP_LINE_ID"></param>
    /// <param name="Minutes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>

    public async Task<SqlActionResult> Add_Project_To_Meeting(string STATUS_ID,
                                                             DateOnly MEETING_DATE,
                                                             string OPP_LINE_ID,
                                                             string Minutes = "",
                                                             CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE),
            new SqlParameter("@OppLineId",   OPP_LINE_ID),
            new SqlParameter("@Minutes",     Minutes)
        };
        return await _db.NonQueryAsync(SQL_Insert_Meeting_Project(), parameters, ct: cancellationToken);
    }

    public async Task<SqlActionResult> Remove_Project_From_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string OPP_LINE_ID,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE),
            new SqlParameter("@OppLineId",   OPP_LINE_ID)
        };
        return await _db.NonQueryAsync(SQL_Delete_Meeting_Project(), parameters, ct: cancellationToken);
    }

    public async Task<OperationResult<List<MEETING_PROJECT>>> Get_Projects_From_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Select_Meeting_Projects(), parameters, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<MEETING_PROJECT>>.Fail($"Error: {queryResult.Message}");

            List<MEETING_PROJECT> list = [];
            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(new MEETING_PROJECT
                {
                    StatusId          = GetString(row, "STATUS_ID"),
                    MeetingDate       = GetDateOnly(row, "MEETING_DATE"),
                    OpportunityLineId = GetString(row, "OPPORTUNITY_LINE_ID"),
                    MeetingMinutes    = GetString(row, "MEETING_MINUTES")
                });

            return OperationResult<List<MEETING_PROJECT>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<MEETING_PROJECT>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Allows to add user to a meeting. This is useful when a user is added to a meeting after the meeting has been created.
    /// </summary>
    /// <param name="STATUS_ID"></param>
    /// <param name="MEETING_DATE"></param>
    /// <param name="MeetingUserRequest"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SqlActionResult> Add_User_To_Meeting( string STATUS_ID,
                                                            DateOnly MEETING_DATE,
                                                            string MeetingUserRequest,
                                                            CancellationToken cancellationToken = default)
     {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",     STATUS_ID),
            new SqlParameter("@MeetingDate",  MEETING_DATE),
            new SqlParameter("@UserRequest",  MeetingUserRequest),
            new SqlParameter("@UserCame",     0),
            new SqlParameter("@UserReviewed", 0)
        };
        return await _db.NonQueryAsync(SQL_Insert_Meeting_User(), parameters, ct: cancellationToken);
    }

    public async Task<SqlActionResult> Remove_User_From_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string MeetingUserRequest,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE),
            new SqlParameter("@UserRequest", MeetingUserRequest)
        };
        return await _db.NonQueryAsync(SQL_Delete_Meeting_User(), parameters, ct: cancellationToken);
    }

    public async Task<OperationResult<List<MEETING_USER>>> Get_Users_From_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Select_Meeting_Users(), parameters, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<MEETING_USER>>.Fail($"Error: {queryResult.Message}");

            List<MEETING_USER> list = [];
            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(new MEETING_USER
                {
                    StatusId            = GetString(row, "STATUS_ID"),
                    MeetingDate         = GetDateOnly(row, "MEETING_DATE"),
                    MeetingUserRequest  = GetString(row, "MEETING_USER_REQUEST"),
                    MeetingUserCame     = GetBoolean(row, "MEETING_USER_CAME"),
                    MeetingUserReviewed = GetBoolean(row, "MEETING_USER_REVIEWED")
                });

            return OperationResult<List<MEETING_USER>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<MEETING_USER>>.Fail($"Error: {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static MEETING_HEADER MapMeetingHeader(DataRow row) => new()
    {
        StatusId              = GetString(row, "STATUS_ID"),
        StatusDescription     = GetString(row, "STATUS_DESCRIPTION"),
        MeetingDate           = GetDateOnly(row, "MEETING_DATE"),
        CurrentStatus         = GetString(row, "CURRENT_STATUS"),
        MeetingTarget         = GetString(row, "MEETING_TARGET"),
        MeetingMinutes        = GetString(row, "MEETING_MINUTES"),
        MeetingUserGen        = GetString(row, "MEETING_USER_GEN"),
        MeetingDateGeneration = GetDateTime(row, "MEETING_DATE_GENERATION")
    };
}
