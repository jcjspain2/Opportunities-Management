namespace QUALITY_GATES.Classes;

using GrupoPremo.Intranet.Library.Models;
using Microsoft.Data.SqlClient;
using QUALITY_GATES.Data;
using QUALITY_GATES.Models;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static QUALITY_GATES.Data.SqlConnectionFactory;
using static QUALITY_GATES.Tools.Static_Local_Functions;


public partial class Class_Projects_Quality_Gates // Directr Query on BMS Database
{
    // DMS flow identifier for Quality Gates deliverables — confirm value with DMS admin
    // This function searh deliverable by deliverable.
    // TODO: We need a function to retrieve all files relates with a project Gate tell to Hemath
    private const string QUALITY_GATES_FLOW_ID = "GATES";


    public async Task start(string Opp_Line_ID)
    {
        var xxx = await Get_DMS_FilesPerproject(Opp_Line_ID);
    }
    internal async Task<OperationResult<List<DELIVERABLE_FILE_FROM_DMS>>> Get_DMS_FilesPerproject(string Opp_Line_ID)
        {
        CancellationToken cancellationToken = default;
        DELIVERABLE_FILE_FROM_DMS DMSFiles = new DELIVERABLE_FILE_FROM_DMS();
        List<DELIVERABLE_FILE_FROM_DMS> CurList = new List<DELIVERABLE_FILE_FROM_DMS>();
        var paramsFile = new[] { new SqlParameter("@oppLineId", Opp_Line_ID) };
        var queryResult = await _db.GetDatatableFromSelectAsync(SQL_FILES_PER_PROJECT_DMS(), paramsFile,DatabaseTarget.DMS, cancellationToken: cancellationToken);
        if (!queryResult.Success || queryResult.DTResults == null)
            return OperationResult<List<DELIVERABLE_FILE_FROM_DMS>>.Fail($"Error: {queryResult.Message}");
        
        try
        {
            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                DMSFiles = new DELIVERABLE_FILE_FROM_DMS
                {
                    OppLineId = GetString(row, "OPP_LIN_ID"),
                    Gate_Id = GetString(row, "SGATE_ID"),
                    StatusiD = GetString(row, "STATUS_ID"),
                    DeliverableID=GetInt(row,"DELIV_ID"),
                    Id = GetInt(row, "TRANS_ID"),
                    FileName = GetString(row, "FILE_NAME"),
                    FilePath =GetString(row, "FILE_LINK_LOCATION"),
                    FileType = GetString(row, "FILE_TYPE_ID"),
                    UploadDate = GetDateTime(row, "DATE_UPLOAD"),
                    UserUpload = GetString(row, "USER_UPLOAD")
                };
                CurList.Add(DMSFiles);
            }
            return OperationResult<List<DELIVERABLE_FILE_FROM_DMS>>.Ok(_db.DeepCopyList(CurList));

        }
          
        catch (Exception ex)
        {
            return OperationResult<List<DELIVERABLE_FILE_FROM_DMS>>.Fail($"Error: {ex.Message}");
            throw;
        }


    }

     private string SQL_FILES_PER_PROJECT_DMS()
    {
        return $@"
                ;WITH ID_IN AS (
                    SELECT T1.TRANS_ID
                    FROM   [DMS].[dbo].[TRA_FLOW_FIELD_VALUES] T1
                    WHERE  T1.FLOW_ID = 'GATES'
                      AND  TRIM(T1.FIELD_ID) + TRIM(T1.FIELD_VALUE) IN ('OPP_LIN_ID' + @oppLineId)
                )
                SELECT TRANS_ID, FLOW_ID, FILE_NAME, USER_UPLOAD,DATE_UPLOAD,FILE_TYPE_ID,FILE_LINK_LOCATION,
                       [SGATE_ID], [OPP_LIN_ID], [STATUS_ID], [DELIV_ID]
                FROM (
                    SELECT T1.TRANS_ID, T1.FLOW_ID, T1.FIELD_ID, T1.FIELD_VALUE,
                           T2.FILE_NAME, T2.USER_UPLOAD,T2.DATE_UPLOAD,T2.FILE_TYPE_ID,T2.FILE_LINK_LOCATION
                    FROM   [DMS].[dbo].[TRA_FLOW_FIELD_VALUES] T1
                    JOIN   ID_IN AS T3 ON T3.TRANS_ID = T1.TRANS_ID
                    JOIN   [DMS].[dbo].[TRA_FLOW_FIELD_FILES] T2 ON T2.TRANS_ID = T1.TRANS_ID
                ) AS Src
                PIVOT (
                    MAX(FIELD_VALUE)
                    FOR FIELD_ID IN ([SGATE_ID], [OPP_LIN_ID], [STATUS_ID], [DELIV_ID])
                ) AS Pvt;";

    }

}
