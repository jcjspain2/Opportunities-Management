using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QUALITY_GATES.Classes;

// ── Models — CIR ───────────────────────────────────────────────────────────────────────

/// <summary>Result returned by CIR_ImportFromJsonFolder for each processed file.</summary>
public class CIR_IMPORT_RESULT
{
    public string       OpportunityLineId { get; set; } = string.Empty;
    public string       FileName          { get; set; } = string.Empty;
    public bool         HeaderCreated     { get; set; }  // true=INSERT, false=UPDATE
    public int          ParametersUpdated { get; set; }
    public int          ParametersSkipped { get; set; }  // in JSON but not in DB family → skipped
    public List<string> Warnings          { get; set; } = [];
    public bool         Success           { get; set; }
    public string       ErrorMessage      { get; set; } = string.Empty;
}

// ── Internal JSON DTOs (deserialization only) ──────────────────────────────────────────

file sealed class JsonCirRoot
{
    [JsonPropertyName("header")]     public JsonCirHeader?      Header     { get; set; }
    [JsonPropertyName("parameters")] public List<JsonCirParam>? Parameters { get; set; }
}

file sealed class JsonCirHeader
{
    [JsonPropertyName("ID_OPPORTUNITY_LINE")]   public string?       OpportunityLineId  { get; set; }
    [JsonPropertyName("PRODUCT_CATEGORY_KEY")]  public string?       ProductCategoryKey { get; set; }
    [JsonPropertyName("PRODUCT_FAMILY_KEY")]    public string?       ProductFamilyKey   { get; set; }
    [JsonPropertyName("filename")]              public string?       Filename           { get; set; }
    [JsonPropertyName("extraction_date")]       public string?       ExtractionDate     { get; set; }
    [JsonPropertyName("client_detected")]       public string?       ClientDetected     { get; set; }
    [JsonPropertyName("warnings")]              public List<string>? Warnings           { get; set; }
}

file sealed class JsonCirParam
{
    [JsonPropertyName("key_parameter")]  public string? KeyParameter  { get; set; }
    [JsonPropertyName("value_min")]      public string? ValueMin      { get; set; }
    [JsonPropertyName("value_typ")]      public string? ValueTyp      { get; set; }
    [JsonPropertyName("value_max")]      public string? ValueMax      { get; set; }
    [JsonPropertyName("unit")]           public string? Unit          { get; set; }
    [JsonPropertyName("type_parameter")] public string? TypeParameter { get; set; }
}

// ── Business layer ─────────────────────────────────────────────────────────────────────

public partial class Class_Projects_Quality_Gates
{
    /// <summary>
    /// Reads all CIR*.json files from <paramref name="folderPath"/> and upserts each one into
    /// MAS_SPEC_CIR_HEADER and MAS_SPEC_CIR_PARAMETERS.
    ///
    /// Header logic per file:
    ///   - OPPORTUNITY_LINE_ID not found → INSERT header (trigger TR_CIR_HDR_INIT_PARAMS
    ///     auto-creates parameter rows with DATA_SOURCE='MANUAL').
    ///   - Already exists → UPDATE FILENAME, EXTRACTION_DATE, CLIENT_DETECTED, MODIFIED_BY/DATE.
    ///     CAT_KEY and FAM_KEY are never changed on update (they define the parameter structure).
    ///
    /// Parameter logic per file:
    ///   - type_parameter='X' (client extras not in catalog) → skipped.
    ///   - Parameters absent from MAS_SPEC_CIR_PARAMETERS for that opportunity → skipped + warning.
    ///   - Parameters where all values are null/empty → skipped silently.
    ///   - value_typ: tried as decimal (InvariantCulture); on failure goes to VALUE_TYP_TEXT.
    ///   - UM: null/empty in JSON → existing DB value preserved (COALESCE in SQL).
    ///   - DATA_SOURCE is always set to 'DOC' for all parameters updated by this process.
    ///
    /// Individual file errors are captured inside each CIR_IMPORT_RESULT (Success=false)
    /// and never abort the overall operation.
    ///
    /// Requires CREATED_BY = userRequester (defaults to "System") — permission check is bypassed
    /// for "System" via Specs_CheckPermission early-return.
    /// </summary>
    public async Task<OperationResult<List<CIR_IMPORT_RESULT>>> CIR_ImportFromJsonFolder(
                                                                                            string folderPath,
                                                                                            string userRequester = "System",
                                                                                            CancellationToken cancellationToken = default)
    {
        // 1 — validate folder
        if (!Directory.Exists(folderPath))
            return OperationResult<List<CIR_IMPORT_RESULT>>.Fail(
                $"Folder not found: '{folderPath}'.");

        // 2 — collect CIR*.json files
        var files = Directory.GetFiles(folderPath, "CIR*.json", SearchOption.TopDirectoryOnly);
        var results = new List<CIR_IMPORT_RESULT>(files.Length);

        if (files.Length == 0)
            return OperationResult<List<CIR_IMPORT_RESULT>>.Ok(results);

        // 3 — process each file sequentially
        foreach (var filePath in files)
        {
            var fileResult = new CIR_IMPORT_RESULT
            {
                FileName = Path.GetFileName(filePath)
            };

            try
            {
                await ProcessCirFileAsync(filePath, fileResult, userRequester, cancellationToken);
            }
            catch (Exception ex)
            {
                fileResult.Success      = false;
                fileResult.ErrorMessage = $"Unexpected error: {ex.Message}";
            }

            results.Add(fileResult);
        }

        return OperationResult<List<CIR_IMPORT_RESULT>>.Ok(results);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task ProcessCirFileAsync(
        string filePath,
        CIR_IMPORT_RESULT result,
        string userRequester,
        CancellationToken ct)
    {
        // 3a — deserialize
        JsonCirRoot root;
        try
        {
            await using var stream = File.OpenRead(filePath);
            root = await JsonSerializer.DeserializeAsync<JsonCirRoot>(stream,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                       ct)
                   ?? throw new InvalidDataException("JSON root is null.");
        }
        catch (Exception ex)
        {
            result.Success      = false;
            result.ErrorMessage = $"Error reading JSON: {ex.Message}";
            return;
        }

        // 3b — validate required header fields
        if (root.Header == null)
        {
            result.Success      = false;
            result.ErrorMessage = "JSON 'header' object is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(root.Header.OpportunityLineId))
        {
            result.Success      = false;
            result.ErrorMessage = "JSON field 'ID_OPPORTUNITY_LINE' is missing or empty.";
            return;
        }
        if (string.IsNullOrWhiteSpace(root.Header.ProductCategoryKey))
        {
            result.Success      = false;
            result.ErrorMessage = "JSON field 'PRODUCT_CATEGORY_KEY' is missing or empty.";
            return;
        }
        if (string.IsNullOrWhiteSpace(root.Header.ProductFamilyKey))
        {
            result.Success      = false;
            result.ErrorMessage = "JSON field 'PRODUCT_FAMILY_KEY' is missing or empty.";
            return;
        }

        var oppLineId = root.Header.OpportunityLineId.Trim();
        result.OpportunityLineId = oppLineId;
        result.Warnings.AddRange(root.Header.Warnings ?? []);

        DateOnly? extractionDate = null;
        if (!string.IsNullOrWhiteSpace(root.Header.ExtractionDate) &&
            DateOnly.TryParse(root.Header.ExtractionDate, out var parsedDate))
            extractionDate = parsedDate;

        // 3c — check header existence
        const string sqlExists = @"
            SELECT COUNT(1)
            FROM   dbo.MAS_SPEC_CIR_HEADER
            WHERE  OPPORTUNITY_LINE_ID = @oppLineId";

        var existsQuery = await _db.SelectAsync(sqlExists,
            new[] { new SqlParameter("@oppLineId", oppLineId) },
            ct);

        if (!existsQuery.Success || existsQuery.DTResults == null)
        {
            result.Success      = false;
            result.ErrorMessage = $"Error checking header existence: {existsQuery.Message}";
            return;
        }

        bool headerExists = Convert.ToInt32(existsQuery.DTResults.Rows[0][0]) > 0;

        if (!headerExists)
        {
            // 3d — INSERT header; trigger auto-creates parameter rows
            const string sqlInsert = @"
                INSERT INTO dbo.MAS_SPEC_CIR_HEADER
                    (OPPORTUNITY_LINE_ID, CAT_KEY, FAM_KEY,
                     FILENAME, EXTRACTION_DATE, CLIENT_DETECTED,
                     CREATED_BY, CREATED_DATE)
                VALUES
                    (@oppLineId, @catKey, @famKey,
                     @filename, @extractionDate, @clientDetected,
                     @createdBy, GETDATE())";

            var insertResult = await _db.NonQueryAsync(sqlInsert,
                new[]
                {
                    new SqlParameter("@oppLineId",       oppLineId),
                    new SqlParameter("@catKey",          root.Header.ProductCategoryKey.Trim()),
                    new SqlParameter("@famKey",          root.Header.ProductFamilyKey.Trim()),
                    new SqlParameter("@filename",        string.IsNullOrWhiteSpace(root.Header.Filename)
                                                             ? (object)DBNull.Value : root.Header.Filename.Trim()),
                    new SqlParameter("@extractionDate",  extractionDate == null
                                                             ? (object)DBNull.Value
                                                             : extractionDate.Value.ToDateTime(TimeOnly.MinValue)),
                    new SqlParameter("@clientDetected",  string.IsNullOrWhiteSpace(root.Header.ClientDetected)
                                                             ? (object)DBNull.Value : root.Header.ClientDetected.Trim()),
                    new SqlParameter("@createdBy",       userRequester)
                },
                ct: ct);

            if (!insertResult.Success)
            {
                result.Success      = false;
                result.ErrorMessage = $"Error inserting header: {insertResult.Message}";
                return;
            }

            result.HeaderCreated = true;
        }
        else
        {
            // 3e — UPDATE document fields only (CAT_KEY / FAM_KEY are intentionally not changed)
            const string sqlUpdate = @"
                UPDATE dbo.MAS_SPEC_CIR_HEADER
                SET FILENAME        = @filename,
                    EXTRACTION_DATE = @extractionDate,
                    CLIENT_DETECTED = @clientDetected,
                    MODIFIED_BY     = @user,
                    MODIFIED_DATE   = GETDATE()
                WHERE OPPORTUNITY_LINE_ID = @oppLineId";

            var updateResult = await _db.NonQueryAsync(sqlUpdate,
                new[]
                {
                    new SqlParameter("@oppLineId",       oppLineId),
                    new SqlParameter("@filename",        string.IsNullOrWhiteSpace(root.Header.Filename)
                                                             ? (object)DBNull.Value : root.Header.Filename.Trim()),
                    new SqlParameter("@extractionDate",  extractionDate == null
                                                             ? (object)DBNull.Value
                                                             : extractionDate.Value.ToDateTime(TimeOnly.MinValue)),
                    new SqlParameter("@clientDetected",  string.IsNullOrWhiteSpace(root.Header.ClientDetected)
                                                             ? (object)DBNull.Value : root.Header.ClientDetected.Trim()),
                    new SqlParameter("@user",            userRequester)
                },
                ct: ct);

            if (!updateResult.Success)
            {
                result.Success      = false;
                result.ErrorMessage = $"Error updating header: {updateResult.Message}";
                return;
            }

            result.HeaderCreated = false;
        }

        // 3f — load existing KEY_PARAMETERs from DB for this opportunity
        const string sqlExistingParams = @"
            SELECT KEY_PARAMETER
            FROM   dbo.MAS_SPEC_CIR_PARAMETERS
            WHERE  OPPORTUNITY_LINE_ID = @oppLineId AND IS_DELETED = 0";

        var existingParamsQuery = await _db.SelectAsync(sqlExistingParams,
            new[] { new SqlParameter("@oppLineId", oppLineId) },
            ct);

        if (!existingParamsQuery.Success || existingParamsQuery.DTResults == null)
        {
            result.Success      = false;
            result.ErrorMessage = $"Error reading existing parameters: {existingParamsQuery.Message}";
            return;
        }

        var dbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in existingParamsQuery.DTResults.Rows)
            dbKeys.Add(GetString(row, "KEY_PARAMETER").Trim());

        // 3g — build parameter update list from JSON
        var updates = new List<(string key, decimal? max, decimal? typ, string? typText, decimal? min, string? um)>();

        foreach (var jp in root.Parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(jp.KeyParameter)) continue;

            // 'X' type = client-specific extras not in the family catalog
            if (string.Equals(jp.TypeParameter, "X", StringComparison.OrdinalIgnoreCase)) continue;

            var key = jp.KeyParameter.Trim();

            if (!dbKeys.Contains(key))
            {
                result.ParametersSkipped++;
                result.Warnings.Add($"Parameter '{key}' not found in DB for '{oppLineId}' — skipped.");
                continue;
            }

            // skip fully empty rows
            if (string.IsNullOrWhiteSpace(jp.ValueMin) &&
                string.IsNullOrWhiteSpace(jp.ValueTyp) &&
                string.IsNullOrWhiteSpace(jp.ValueMax) &&
                string.IsNullOrWhiteSpace(jp.Unit))
                continue;

            // value_typ: numeric first, text fallback
            decimal? valueTypNum  = null;
            string?  valueTypText = null;

            if (!string.IsNullOrWhiteSpace(jp.ValueTyp))
            {
                if (decimal.TryParse(jp.ValueTyp,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed))
                    valueTypNum = parsed;
                else
                    valueTypText = jp.ValueTyp.Trim();
            }

            updates.Add((
                key,
                ParseDecimalNullable(jp.ValueMax),
                valueTypNum,
                valueTypText,
                ParseDecimalNullable(jp.ValueMin),
                string.IsNullOrWhiteSpace(jp.Unit) ? null : jp.Unit.Trim()
            ));
        }

        // 3h — persist parameter updates inside a transaction (audit trigger reads SESSION_CONTEXT)
        if (updates.Count > 0)
        {
            const string sqlUpdateParam = @"
                UPDATE dbo.MAS_SPEC_CIR_PARAMETERS
                SET VALUE_MAX      = @valueMax,
                    VALUE_TYP      = @valueTyp,
                    VALUE_TYP_TEXT = @valueTypText,
                    VALUE_MIN      = @valueMin,
                    UM             = COALESCE(NULLIF(@um, ''), UM),
                    DATA_SOURCE    = 'DOC',
                    MODIFIED_BY    = @user,
                    MODIFIED_DATE  = GETDATE()
                WHERE OPPORTUNITY_LINE_ID = @oppLineId AND KEY_PARAMETER = @keyParameter";

            var batchResult = await _db.ExecuteInTransactionAsync(async (tx, innerCt) =>
            {
                await Specs_SetSessionUserAsync(userRequester, tx, innerCt);

                foreach (var (key, max, typ, typText, min, um) in updates)
                {
                    await _db.NonQueryAsync(sqlUpdateParam,
                        new[]
                        {
                            new SqlParameter("@oppLineId",    oppLineId),
                            new SqlParameter("@keyParameter", key),
                            new SqlParameter("@valueMax",     (object?)max     ?? DBNull.Value),
                            new SqlParameter("@valueTyp",     (object?)typ     ?? DBNull.Value),
                            new SqlParameter("@valueTypText", string.IsNullOrWhiteSpace(typText)
                                                                  ? (object)DBNull.Value : typText),
                            new SqlParameter("@valueMin",     (object?)min     ?? DBNull.Value),
                            new SqlParameter("@um",           string.IsNullOrWhiteSpace(um)
                                                                  ? (object)DBNull.Value : um),
                            new SqlParameter("@user",         userRequester)
                        },
                        transaction: tx,
                        ct: innerCt);
                }
            }, ct);

            if (!batchResult.Success)
            {
                result.Success      = false;
                result.ErrorMessage = $"Error updating parameters: {batchResult.Message}";
                return;
            }
        }

        // 3i — record final counts
        result.ParametersUpdated = updates.Count;
        result.Success           = true;
    }
}
