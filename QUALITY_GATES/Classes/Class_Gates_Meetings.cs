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

    private static string SQL_Select_All_Meetings(string currentModule, byte isFinish = 0) => $@"
        SELECT  T1.STATUS_ID, T3.STATUS_DESCRIPTION, T1.MEETING_DATE, T1.CURRENT_STATUS,
                T1.MEETING_TARGET, T1.MEETING_MINUTES, T1.MEETING_USER_GEN,
                T1.MEETING_DATE_GENERATION, T1.CLOSING_DATE, T1.CLOSING_USER
        FROM    dbo.SAP_MEETING_HEADER T1
        JOIN    dbo.MAS_GATE_STATUS T2 ON  T2.GATE_STATUS_ID = T1.CURRENT_STATUS
                                       AND T2.MODULE_ID      = '{currentModule}'
                                       AND T2.KEY_PROCESS    = 'MEETING'
                                       AND T2.IS_FINAL_STATE = {isFinish}
        JOIN    dbo.MAS_STATUS T3 ON T3.STATUS_ID = T1.STATUS_ID AND T3.STATUS_MODULE = '{currentModule}'
        ORDER   BY T1.MEETING_DATE DESC";

    private static string SQL_Select_Meetings_Filtered(string currentModule, byte isFinish = 0) => $@"
        SELECT  T1.STATUS_ID, T3.STATUS_DESCRIPTION, T1.MEETING_DATE, T1.CURRENT_STATUS,
                T1.MEETING_TARGET, T1.MEETING_MINUTES, T1.MEETING_USER_GEN,
                T1.MEETING_DATE_GENERATION, T1.CLOSING_DATE, T1.CLOSING_USER
        FROM    dbo.SAP_MEETING_HEADER T1
        JOIN    dbo.MAS_GATE_STATUS T2 ON  T2.GATE_STATUS_ID = T1.CURRENT_STATUS
                                       AND T2.MODULE_ID      = '{currentModule}'
                                       AND T2.KEY_PROCESS    = 'MEETING'
                                       AND T2.IS_FINAL_STATE = {isFinish}
        JOIN    dbo.MAS_STATUS T3 ON T3.STATUS_ID = T1.STATUS_ID AND T3.STATUS_MODULE = '{currentModule}'
        WHERE   T1.STATUS_ID = @StatusId
        ORDER   BY T1.MEETING_DATE DESC";

    private static string SQL_Select_Meeting_Header_Single(string currentModule) => $@"
        SELECT  T1.STATUS_ID, T3.STATUS_DESCRIPTION, T1.MEETING_DATE, T1.CURRENT_STATUS,
                T1.MEETING_TARGET, T1.MEETING_MINUTES, T1.MEETING_USER_GEN,
                T1.MEETING_DATE_GENERATION, T1.CLOSING_DATE, T1.CLOSING_USER
        FROM    dbo.SAP_MEETING_HEADER T1
        JOIN    dbo.MAS_STATUS T3 ON T3.STATUS_ID = T1.STATUS_ID AND T3.STATUS_MODULE = '{currentModule}'
        WHERE   T1.STATUS_ID = @StatusId AND T1.MEETING_DATE = @MeetingDate";

    private static string SQL_Select_Meeting_Users() => @"
        SELECT STATUS_ID, MEETING_DATE, MEETING_USER_REQUEST,
               MEETING_USER_CAME, MEETING_USER_REVIEWED
        FROM   dbo.SAP_MEETING_USERS
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Select_Meeting_Projects() => @"
        SELECT STATUS_ID, MEETING_DATE, OPPORTUNITY_LINE_ID, MEETING_MINUTES
        FROM   dbo.SAP_MEETING_PROJECTS
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Status_Open() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    CURRENT_STATUS = @NewStatus
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Status_Close() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    CURRENT_STATUS = @NewStatus,
               CLOSING_DATE   = SYSDATETIME(),
               CLOSING_USER   = @ClosingUser
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Minutes() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    MEETING_MINUTES = @Minutes
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Update_Meeting_Header_Users_Date() => @"
        UPDATE dbo.SAP_MEETING_USERS
        SET    MEETING_DATE = @NewDate
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @OldDate";

    private static string SQL_Update_Meeting_Header_Projects_Date() => @"
        UPDATE dbo.SAP_MEETING_PROJECTS
        SET    MEETING_DATE = @NewDate
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @OldDate";

    private static string SQL_Update_Meeting_Header_Data() => @"
        UPDATE dbo.SAP_MEETING_HEADER
        SET    MEETING_DATE   = @NewDate,
               MEETING_TARGET = @NewTarget
        WHERE  STATUS_ID = @StatusId AND MEETING_DATE = @OldDate";

    private static string SQL_Update_Meeting_User_Attendance() => @"
        UPDATE dbo.SAP_MEETING_USERS
        SET    MEETING_USER_CAME     = @UserCame,
               MEETING_USER_REVIEWED = @UserReviewed
        WHERE  STATUS_ID             = @StatusId
          AND  MEETING_DATE          = @MeetingDate
          AND  MEETING_USER_REQUEST  = @UserRequest";

    private static string SQL_Update_Meeting_Project_Minutes() => @"
        UPDATE dbo.SAP_MEETING_PROJECTS
        SET    MEETING_MINUTES     = @Minutes
        WHERE  STATUS_ID           = @StatusId
          AND  MEETING_DATE        = @MeetingDate
          AND  OPPORTUNITY_LINE_ID = @OppLineId";

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
        FROM   dbo.MAS_STATUS
        WHERE  STATUS_MODULE = '{currentModule}' AND IsDeleted = 0
        ORDER  BY STATUS_SEQUENCE";

    private static string SQL_Delete_Meeting_Users_All() => @"
        DELETE FROM dbo.SAP_MEETING_USERS
        WHERE STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Delete_Meeting_Projects_All() => @"
        DELETE FROM dbo.SAP_MEETING_PROJECTS
        WHERE STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private static string SQL_Delete_Meeting_Header() => @"
        DELETE FROM dbo.SAP_MEETING_HEADER
        WHERE STATUS_ID = @StatusId AND MEETING_DATE = @MeetingDate";

    private string SQL_Select_Projects_In_Gate(string currentModule) => $@"
        SELECT OPPORTUNITY_LINE_ID, OPPORTUNITY_NAME, OPPORTUNITY_LNE_NAME, CUST_NAME, CURRENT_QG_STATUS
        FROM   dbo.TRA_PROJECTS
        WHERE  {(currentModule == "Q_GATES_PR" ? "Is_Q_GATES_PR = 1 AND [IsGeneratedInitial_PR] = 1" : "Is_Q_GATES = 1 AND [IsGeneratedInitial] = 1")}
          AND  CURRENT_QG_STATUS = @StatusId
        ORDER  BY OPPORTUNITY_LNE_NAME";

    private static string SQL_Insert_Meeting_Comment() => @"
        INSERT INTO dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS
               (OPP_LINE_ID, STATUS_ID, SGATE_ID, DEL_ID,
                COMMENT_ID, COMMENT_TEXT, COMMENT_BY, COMMENT_DATE, KEY_PROCESS)
        VALUES (@OppLineId, @StatusId, @MeetingDateKey, 0,
                @CommentId, @CommentText, @CommentBy, SYSDATETIME(), 'MEETING')";

    private static string SQL_Select_Meeting_Comments() => @"
        SELECT OPP_LINE_ID, STATUS_ID, SGATE_ID, DEL_ID,
               COMMENT_ID, COMMENT_TEXT, COMMENT_BY, COMMENT_DATE
        FROM   dbo.TRA_PROJECTS_DELIVERABLE_COMMENTS
        WHERE  STATUS_ID    = @StatusId
          AND  SGATE_ID     = @MeetingDateKey
          AND  OPP_LINE_ID  = @OppLineId
          AND  KEY_PROCESS  = 'MEETING'
        ORDER  BY COMMENT_DATE";

    // ── Public methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of STATUS_IDs available in the current module (e.g. PCA, FEAS).
    /// </summary>
    public async Task<OperationResult<List<PROJECT_STATUS_PICKING_LIST>>> GetPickingListStatusID(CancellationToken cancellationToken = default)
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
    /// Checks whether the requesting user is authorized to create meetings for the current module.
    /// Returns Ok(true) if authorized, Fail with reason otherwise.
    /// </summary>
    public async Task<OperationResult<bool>> Can_User_Create_Meeting(
                                                                    string UserRequester,
                                                                    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserRequester) || UserRequester == "System")
            return OperationResult<bool>.Fail("A valid user ID is required.");

        return await Get_KeyProcess_Job_title_Restriction("@MEETING", UserRequester, cancellationToken);
    }

    /// <summary>
    /// Creates a meeting with its header, users and projects in a single transaction.
    /// Only users authorized for the @MEETING key process may create meetings.
    /// </summary>
    public async Task<SqlActionResult> Create_Meeting(
                                                        string STATUS_ID,
                                                        DateOnly MEETING_DATE,
                                                        string MEETING_TARGET,
                                                        string UserRequester = "System",
                                                        List<MEETING_USER>? Users = null,
                                                        List<MEETING_PROJECT>? Projects = null,
                                                        CancellationToken cancellationToken = default)
    {
        if (UserRequester != "System")
        {
            var authResult = await Get_KeyProcess_Job_title_Restriction("@MEETING", UserRequester, cancellationToken);
            if (!authResult.Success)
                return new SqlActionResult { Success = false, Message = authResult.ErrorMessage };
        }

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
            var headerResult = await _db.NonQueryAsync(SQL_Insert_Meeting_Header(), headerParams, tx, cancellationToken);
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
    /// Returns all meetings for the current module.
    /// </summary>
    /// <param name="IsMeetingFinish">0 = active meetings, 1 = closed/cancelled meetings.</param>
    public async Task<OperationResult<List<MEETING_HEADER>>> Get_Meetings(
                                                                            byte IsMeetingFinish = 0,
                                                                            CancellationToken cancellationToken = default)
    {
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Select_All_Meetings(_MODULE_ID, IsMeetingFinish), null, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<MEETING_HEADER>>.Fail($"Error: {queryResult.Message}");

            List<MEETING_HEADER> list = [];
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
    /// Returns meetings filtered by a specific gate (STATUS_ID) for the current module.
    /// </summary>
    /// <param name="STATUS_ID">Gate to filter by, e.g. PCA, FEAS.</param>
    /// <param name="IsMeetingFinish">0 = active meetings, 1 = closed/cancelled meetings.</param>
    public async Task<OperationResult<List<MEETING_HEADER>>> Get_Meetings_By_Gate(
                                                                                    string STATUS_ID,
                                                                                    byte IsMeetingFinish = 0,
                                                                                    CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new[] { new SqlParameter("@StatusId", STATUS_ID) };
            var queryResult = await _db.SelectAsync(SQL_Select_Meetings_Filtered(_MODULE_ID, IsMeetingFinish), parameters, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<MEETING_HEADER>>.Fail($"Error: {queryResult.Message}");

            List<MEETING_HEADER> list = [];
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
    /// Returns the full detail of a meeting: header, attendees and projects.
    /// </summary>
    public async Task<OperationResult<MEETING_DETAIL>> Get_Meeting_Detail(
        string STATUS_ID,
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
            var headerResult = await _db.SelectAsync(SQL_Select_Meeting_Header_Single(_MODULE_ID), p, cancellationToken);
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
    /// Changes the status of a meeting. Only users authorized for @MEETING may change status.
    /// When the new status is a final state (COMPLETED, CANCELLED), CLOSING_DATE and CLOSING_USER are recorded.
    /// </summary>
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

        // Check whether the new status is a final state to decide if closing fields must be stamped
        const string sqlCheckFinal = @"
            SELECT IS_FINAL_STATE
            FROM   dbo.MAS_GATE_STATUS
            WHERE  MODULE_ID      = @ModuleId
              AND  KEY_PROCESS    = 'MEETING'
              AND  GATE_STATUS_ID = @NewStatus";

        var finalCheck = await _db.SelectAsync(
            sqlCheckFinal,
            new[]
            {
                new SqlParameter("@ModuleId",  _MODULE_ID),
                new SqlParameter("@NewStatus", NewStatus)
            },
            cancellationToken);

        bool isFinalState = finalCheck.Success
                         && finalCheck.DTResults?.Rows.Count > 0
                         && GetBoolean(finalCheck.DTResults.Rows[0], "IS_FINAL_STATE");

        if (isFinalState)
        {
            var closeParams = new[]
            {
                new SqlParameter("@NewStatus",   NewStatus),
                new SqlParameter("@ClosingUser", UserRequester),
                new SqlParameter("@StatusId",    STATUS_ID),
                new SqlParameter("@MeetingDate", MEETING_DATE)
            };
            return await _db.NonQueryAsync(SQL_Update_Meeting_Status_Close(), closeParams, ct: cancellationToken);
        }
        else
        {
            var openParams = new[]
            {
                new SqlParameter("@NewStatus",   NewStatus),
                new SqlParameter("@StatusId",    STATUS_ID),
                new SqlParameter("@MeetingDate", MEETING_DATE)
            };
            return await _db.NonQueryAsync(SQL_Update_Meeting_Status_Open(), openParams, ct: cancellationToken);
        }
    }

    /// <summary>
    /// Updates the global minutes (acta) of the meeting header.
    /// </summary>
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
    /// Updates the meeting date and/or target. Only allowed when the meeting is in NOTSTARTED status.
    /// Only the organizer (MEETING_USER_GEN) or System may modify the header.
    /// Changing the date cascades to all child rows (SAP_MEETING_USERS, SAP_MEETING_PROJECTS) in one transaction.
    /// </summary>
    public async Task<SqlActionResult> Update_Meeting_Header(
        string STATUS_ID,
        DateOnly CURRENT_MEETING_DATE,
        DateOnly NEW_MEETING_DATE,
        string NEW_MEETING_TARGET,
        string UserRequester = "System",
        CancellationToken cancellationToken = default)
    {
        // Verify the meeting exists and check organizer + status
        var headerCheck = await _db.SelectAsync(
            SQL_Select_Meeting_Header_Single(_MODULE_ID),
            new[]
            {
                new SqlParameter("@StatusId",    STATUS_ID),
                new SqlParameter("@MeetingDate", CURRENT_MEETING_DATE)
            },
            cancellationToken);

        if (!headerCheck.Success || headerCheck.DTResults == null)
            return new SqlActionResult { Success = false, Message = $"Error: {headerCheck.Message}" };
        if (headerCheck.DTResults.Rows.Count == 0)
            return new SqlActionResult { Success = false, Message = $"Meeting '{STATUS_ID}/{CURRENT_MEETING_DATE:yyyy-MM-dd}' not found." };

        var headerRow = headerCheck.DTResults.Rows[0];
        string currentStatus = GetString(headerRow, "CURRENT_STATUS");
        string organizer     = GetString(headerRow, "MEETING_USER_GEN");

        if (currentStatus != "NOTSTARTED")
            return new SqlActionResult { Success = false, Message = "Only meetings in NOTSTARTED status can be edited." };

        if (UserRequester != "System" && !string.Equals(organizer, UserRequester, StringComparison.OrdinalIgnoreCase))
            return new SqlActionResult { Success = false, Message = "Only the meeting organizer can modify the meeting header." };

        var dateParams = new[]
        {
            new SqlParameter("@NewDate",  NEW_MEETING_DATE),
            new SqlParameter("@OldDate",  CURRENT_MEETING_DATE),
            new SqlParameter("@StatusId", STATUS_ID)
        };

        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();
        try
        {
            // Update child tables first to satisfy FK constraints before changing the PK
            var usersResult = await _db.NonQueryAsync(SQL_Update_Meeting_Header_Users_Date(), dateParams, tx, cancellationToken);
            if (!usersResult.Success)
            {
                tx.Rollback();
                return new SqlActionResult { Success = false, Message = $"Error updating attendees date: {usersResult.Message}" };
            }

            var projResult = await _db.NonQueryAsync(SQL_Update_Meeting_Header_Projects_Date(), dateParams, tx, cancellationToken);
            if (!projResult.Success)
            {
                tx.Rollback();
                return new SqlActionResult { Success = false, Message = $"Error updating projects date: {projResult.Message}" };
            }

            var headerResult = await _db.NonQueryAsync(
                SQL_Update_Meeting_Header_Data(),
                new[]
                {
                    new SqlParameter("@NewDate",    NEW_MEETING_DATE),
                    new SqlParameter("@NewTarget",  NEW_MEETING_TARGET),
                    new SqlParameter("@OldDate",    CURRENT_MEETING_DATE),
                    new SqlParameter("@StatusId",   STATUS_ID)
                },
                tx,
                cancellationToken);

            if (!headerResult.Success)
            {
                tx.Rollback();
                return new SqlActionResult { Success = false, Message = $"Error updating meeting header: {headerResult.Message}" };
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
    /// Adds a project to an existing meeting.
    /// </summary>
    public async Task<SqlActionResult> Add_Project_To_Meeting(
        string STATUS_ID,
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

    /// <summary>
    /// Removes a project from an existing meeting.
    /// </summary>
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

    /// <summary>
    /// Returns the list of projects assigned to a meeting.
    /// </summary>
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
    /// Updates the per-project minutes within a meeting (distinct from the global meeting minutes).
    /// </summary>
    public async Task<SqlActionResult> Update_Meeting_Project_Minutes(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string OPP_LINE_ID,
        string Minutes,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@Minutes",     Minutes),
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE),
            new SqlParameter("@OppLineId",   OPP_LINE_ID)
        };
        return await _db.NonQueryAsync(SQL_Update_Meeting_Project_Minutes(), parameters, ct: cancellationToken);
    }

    /// <summary>
    /// Adds an attendee to an existing meeting.
    /// </summary>
    public async Task<SqlActionResult> Add_User_To_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string MeetingUserRequest,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@StatusId",     STATUS_ID),
            new SqlParameter("@MeetingDate",  MEETING_DATE),
            new SqlParameter("@UserRequest",  MeetingUserRequest),
            new SqlParameter("@UserCame",     false),
            new SqlParameter("@UserReviewed", false)
        };
        return await _db.NonQueryAsync(SQL_Insert_Meeting_User(), parameters, ct: cancellationToken);
    }

    /// <summary>
    /// Removes an attendee from an existing meeting.
    /// </summary>
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

    /// <summary>
    /// Returns the list of attendees for a meeting.
    /// </summary>
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

    /// <summary>
    /// Updates the attendance flags for a specific attendee.
    /// UserCame can be set automatically when the user is active during the meeting (INPROGRESS status).
    /// </summary>
    public async Task<SqlActionResult> Update_Meeting_User_Attendance(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string MeetingUserRequest,
        bool UserCame,
        bool UserReviewed,
        CancellationToken cancellationToken = default)
    {
        var parameters = new[]
        {
            new SqlParameter("@UserCame",     UserCame),
            new SqlParameter("@UserReviewed", UserReviewed),
            new SqlParameter("@StatusId",     STATUS_ID),
            new SqlParameter("@MeetingDate",  MEETING_DATE),
            new SqlParameter("@UserRequest",  MeetingUserRequest)
        };
        return await _db.NonQueryAsync(SQL_Update_Meeting_User_Attendance(), parameters, ct: cancellationToken);
    }

    /// <summary>
    /// Adds a comment linked to a specific project within a meeting.
    /// Returns the new comment's GUID on success.
    /// </summary>
    public async Task<OperationResult<Guid>> Add_Meeting_Comment(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string OPP_LINE_ID,
        string CommentText,
        string CommentBy,
        CancellationToken cancellationToken = default)
    {
        var newCommentId    = Guid.NewGuid();
        string meetingDateKey = MEETING_DATE.ToString("yyyyMMdd");

        var parameters = new[]
        {
            new SqlParameter("@OppLineId",      OPP_LINE_ID),
            new SqlParameter("@StatusId",       STATUS_ID),
            new SqlParameter("@MeetingDateKey", meetingDateKey),
            new SqlParameter("@CommentId",      newCommentId),
            new SqlParameter("@CommentText",    CommentText),
            new SqlParameter("@CommentBy",      CommentBy)
        };

        var result = await _db.NonQueryAsync(SQL_Insert_Meeting_Comment(), parameters, ct: cancellationToken);

        return result.Success
            ? OperationResult<Guid>.Ok(newCommentId)
            : OperationResult<Guid>.Fail($"Error inserting meeting comment: {result.Message}");
    }

    /// <summary>
    /// Returns all comments for a specific project within a meeting, ordered by date.
    /// </summary>
    public async Task<OperationResult<List<DELIVERABLES_COMMENTS>>> Get_Meeting_Comments(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string OPP_LINE_ID,
        CancellationToken cancellationToken = default)
    {
        string meetingDateKey = MEETING_DATE.ToString("yyyyMMdd");
        var parameters = new[]
        {
            new SqlParameter("@StatusId",       STATUS_ID),
            new SqlParameter("@MeetingDateKey", meetingDateKey),
            new SqlParameter("@OppLineId",      OPP_LINE_ID)
        };
        try
        {
            var queryResult = await _db.SelectAsync(SQL_Select_Meeting_Comments(), parameters, cancellationToken);
            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<DELIVERABLES_COMMENTS>>.Fail($"Error: {queryResult.Message}");

            List<DELIVERABLES_COMMENTS> list = [];
            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(new DELIVERABLES_COMMENTS
                {
                    OppLineId    = GetString(row, "OPP_LINE_ID"),
                    StatusiD     = GetString(row, "STATUS_ID"),
                    Gate_Id      = GetString(row, "SGATE_ID"),
                    DeliverableID = GetInt(row,   "DEL_ID"),
                    Id           = Guid.Parse(GetString(row, "COMMENT_ID")),
                    COMMENT_TEXT = GetString(row, "COMMENT_TEXT"),
                    USER         = GetString(row, "COMMENT_BY"),
                    COMMENT_DATE = GetDateTime(row, "COMMENT_DATE")
                });

            return OperationResult<List<DELIVERABLES_COMMENTS>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<DELIVERABLES_COMMENTS>>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a meeting and all its children (attendees and projects) in one transaction.
    /// Only allowed when the meeting is in NOTSTARTED status and only by the organizer.
    /// </summary>
    public async Task<SqlActionResult> Delete_Meeting(
        string STATUS_ID,
        DateOnly MEETING_DATE,
        string UserRequester = "System",
        CancellationToken cancellationToken = default)
    {
        // Fetch header to verify status and organizer
        var headerCheck = await _db.SelectAsync(
            SQL_Select_Meeting_Header_Single(_MODULE_ID),
            new[]
            {
                new SqlParameter("@StatusId",    STATUS_ID),
                new SqlParameter("@MeetingDate", MEETING_DATE)
            },
            cancellationToken);

        if (!headerCheck.Success || headerCheck.DTResults == null)
            return new SqlActionResult { Success = false, Message = $"Error: {headerCheck.Message}" };
        if (headerCheck.DTResults.Rows.Count == 0)
            return new SqlActionResult { Success = false, Message = $"Meeting '{STATUS_ID}/{MEETING_DATE:yyyy-MM-dd}' not found." };

        var headerRow    = headerCheck.DTResults.Rows[0];
        string status    = GetString(headerRow, "CURRENT_STATUS");
        string organizer = GetString(headerRow, "MEETING_USER_GEN");

        if (status != "NOTSTARTED")
            return new SqlActionResult { Success = false, Message = "Only meetings in NOTSTARTED status can be deleted." };

        if (UserRequester != "System" && !string.Equals(organizer, UserRequester, StringComparison.OrdinalIgnoreCase))
            return new SqlActionResult { Success = false, Message = "Only the meeting organizer can delete the meeting." };

        var p = new[]
        {
            new SqlParameter("@StatusId",    STATUS_ID),
            new SqlParameter("@MeetingDate", MEETING_DATE)
        };

        await using var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = conn.BeginTransaction();
        try
        {
            var r1 = await _db.NonQueryAsync(SQL_Delete_Meeting_Projects_All(), p, tx, cancellationToken);
            if (!r1.Success) { tx.Rollback(); return new SqlActionResult { Success = false, Message = r1.Message }; }

            var r2 = await _db.NonQueryAsync(SQL_Delete_Meeting_Users_All(), p, tx, cancellationToken);
            if (!r2.Success) { tx.Rollback(); return new SqlActionResult { Success = false, Message = r2.Message }; }

            var r3 = await _db.NonQueryAsync(SQL_Delete_Meeting_Header(), p, tx, cancellationToken);
            if (!r3.Success) { tx.Rollback(); return new SqlActionResult { Success = false, Message = r3.Message }; }

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
    /// Returns the list of projects currently in the specified gate (STATUS_ID) for this module.
    /// Used in the meeting create dialog to populate the project picker.
    /// </summary>
    public async Task<OperationResult<List<PROJECT_FOR_MEETING>>> Get_Projects_In_Gate(
        string STATUS_ID,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queryResult = await _db.SelectAsync(
                SQL_Select_Projects_In_Gate(_MODULE_ID),
                new[] { new SqlParameter("@StatusId", STATUS_ID) },
                cancellationToken);

            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<PROJECT_FOR_MEETING>>.Fail($"Error: {queryResult.Message}");

            List<PROJECT_FOR_MEETING> list = [];
            foreach (DataRow row in queryResult.DTResults.Rows)
                list.Add(new PROJECT_FOR_MEETING
                {
                    OppLineId      = GetString(row, "OPPORTUNITY_LINE_ID"),
                    OppName        = GetString(row, "OPPORTUNITY_NAME"),
                    OppLineName    = GetString(row, "OPPORTUNITY_LNE_NAME"),
                    CustomerName   = GetString(row, "CUST_NAME"),
                    CurrentGateId  = GetString(row, "CURRENT_QG_STATUS")
                });

            return OperationResult<List<PROJECT_FOR_MEETING>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<PROJECT_FOR_MEETING>>.Fail($"Error: {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static MEETING_HEADER MapMeetingHeader(DataRow row) => new()
    {
        StatusId              = GetString(row,  "STATUS_ID"),
        StatusDescription     = GetString(row,  "STATUS_DESCRIPTION"),
        MeetingDate           = GetDateOnly(row, "MEETING_DATE"),
        CurrentStatus         = GetString(row,  "CURRENT_STATUS"),
        MeetingTarget         = GetString(row,  "MEETING_TARGET"),
        MeetingMinutes        = GetString(row,  "MEETING_MINUTES"),
        MeetingUserGen        = GetString(row,  "MEETING_USER_GEN"),
        MeetingDateGeneration = GetDateTime(row, "MEETING_DATE_GENERATION"),
        ClosingDate           = GetDateTime(row, "CLOSING_DATE"),
        ClosingUser           = GetString(row,  "CLOSING_USER")
    };
}
