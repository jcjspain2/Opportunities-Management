using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QUALITY_GATES.Classes;

// ── Models — SPECS ─────────────────────────────────────────────────────────────────────

/// <summary>One parameter criterion in a parametric search: VALUE_TYP must fall in [ValueTypMin, ValueTypMax] (either bound optional).</summary>
public class SPEC_PARAM_FILTER
{
    public string   KeyParameter { get; set; } = string.Empty;
    public decimal? ValueTypMin  { get; set; }
    public decimal? ValueTypMax  { get; set; }
}

/// <summary>Search result: a matching product header plus all its active parameters.</summary>
public class SPEC_SEARCH_RESULT
{
    public SPEC_MATNR_HEADER          Header     { get; set; } = new();
    public List<SPEC_MATNR_PARAMETER> Parameters { get; set; } = [];
}

/// <summary>Result returned by Specs_ImportFromJson.</summary>
public class SPEC_IMPORT_RESULT
{
    public string       Matnr               { get; set; } = string.Empty;
    public string       Edition             { get; set; } = string.Empty;
    public bool         HeaderCreated       { get; set; }  // true=INSERT, false=UPDATE
    public int          ParametersUpdated   { get; set; }  // rows really written (value changed)
    public int          ParametersSkipped   { get; set; }  // in JSON but not an active parameter of this MATNR, or invalid
    public int          ParametersProtected { get; set; }  // DATA_SOURCE='MANUAL' — import value ignored
    public List<string> Warnings            { get; set; } = [];
}

// ── Internal JSON DTOs (deserialization only) ──────────────────────────────

file sealed class JsonSpecRoot
{
    [JsonPropertyName("MATNR")]                public string?            Matnr           { get; set; }
    [JsonPropertyName("EDITION")]              public string?            Edition         { get; set; }
    [JsonPropertyName("DESCRIPTION")]          public string?            Description     { get; set; }
    [JsonPropertyName("product_category_key")] public string?            ProductCategory { get; set; }
    [JsonPropertyName("family_key")]           public string?            FamilyKey       { get; set; }
    [JsonPropertyName("ISSUE_DATE")]           public string?            IssueDate       { get; set; }
    [JsonPropertyName("source_url")]           public string?            SourceUrl       { get; set; }
    [JsonPropertyName("warnings")]             public List<string>?      Warnings        { get; set; }
    [JsonPropertyName("parameters")]           public List<JsonSpecParam>? Parameters    { get; set; }
}

file sealed class JsonSpecParam
{
    [JsonPropertyName("key_parameter")]  public string? KeyParameter  { get; set; }
    [JsonPropertyName("value_min")]      public string? ValueMin      { get; set; }
    [JsonPropertyName("value_typ")]      public string? ValueTyp      { get; set; }
    [JsonPropertyName("value_max")]      public string? ValueMax      { get; set; }
    [JsonPropertyName("unit")]           public string? Unit          { get; set; }
}

// ── Business layer ─────────────────────────────────────────────────────────────────────

public partial class Class_Projects_Quality_Gates
{
    // MODULE_ID used in MAS_JOB_TITLES_RESTRICTIONS for all SPECS operations
    private const string SPECS_MODULE_ID = "SPECS";

    // Single key process in MAS_JOB_TITLES_RESTRICTIONS for EVERY SPECS write
    // (catalog maintenance, product create / edit / delete, parameter values). "System" bypasses.
    private const string SPECS_KEY_PROCESS = "@SPECS";

    // ── Private helpers ───────────────────────────────────────────────────────

    // Sets SESSION_CONTEXT('CurrentUser') so MATNR audit triggers record who made the change.
    // Must be called inside a transaction before any UPDATE/DELETE on MATNR tables.
    private async Task Specs_SetSessionUserAsync(string user, SqlTransaction tx, CancellationToken ct)
        => await _db.NonQueryAsync(
               "EXEC sys.sp_set_session_context @key = N'CurrentUser', @value = @user, @read_only = 0",
               new[] { new SqlParameter("@user", user) },
               transaction: tx,
               ct: ct);

    /// <summary>
    /// Verifies that userRequester's job title is authorized for the given keyProcess
    /// in the SPECS module (MAS_JOB_TITLES_RESTRICTIONS, MODULE_ID='SPECS').
    /// Uses the same user-resolution chain as QG (AD → USERS_CADENA → JOB_TITLES_CADENA).
    /// </summary>
    private async Task<OperationResult<bool>> Specs_CheckPermission(
        string keyProcess,
        string userRequester,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(userRequester, "System", StringComparison.OrdinalIgnoreCase))
            return OperationResult<bool>.Ok(true);

        var userDetails = await GetUser_ID_Job_Description(userRequester, cancellationToken);
        if (!userDetails.Success)
            return OperationResult<bool>.Fail(userDetails.ErrorMessage);

        const string sql = @"
            SELECT JOB_TITLE
            FROM   dbo.MAS_JOB_TITLES_RESTRICTIONS
            WHERE  MODULE_ID = @moduleId AND KEY_PROCESS = @keyProcess";

        var q = await _db.SelectAsync(sql,
            new[]
            {
                new SqlParameter("@moduleId",   _MODULE_ID),
                new SqlParameter("@keyProcess", keyProcess)
            },
            cancellationToken);

        if (!q.Success || q.DTResults == null)
            return OperationResult<bool>.Fail(
                $"Error retrieving restrictions for '{keyProcess}': {q.Message}");

        if (q.DTResults.Rows.Count == 0)
            return OperationResult<bool>.Fail(
                $"No restrictions defined for key process '{keyProcess}' in module '{SPECS_MODULE_ID}'.");

        var authorized = q.DTResults.Rows
            .Cast<DataRow>()
            .Select(r => GetString(r, "JOB_TITLE"))
            .ToList();

        if (!authorized.Contains(userDetails.Data.User_Q_GATES_Job_Title_ID, StringComparer.OrdinalIgnoreCase))
            return OperationResult<bool>.Fail(
                $"User '{userRequester}' (role '{userDetails.Data.User_Q_GATES_Job_Title_ID}') " +
                $"is not authorized for '{keyProcess}'. Allowed: {string.Join(", ", authorized)}.");

        return OperationResult<bool>.Ok(true);
    }

    /// <summary>
    /// Parametric search: returns all products of a family that match ALL supplied filters.
    /// Each filter is an exact equality on a parameter value.
    /// Passing an empty or null filter list returns every active product of the family.
    /// Each result includes the full parameter list so the caller can render a comparison grid.
    /// </summary>
    public async Task<OperationResult<List<SPEC_SEARCH_RESULT>>> Specs_SearchProducts(
        string catKey,
        string famKey,
        List<SPEC_PARAM_FILTER>? filters = null,
        CancellationToken cancellationToken = default)
    {
        // One query returns header + all parameter rows for each matching product.
        // IS_DELETED appears in both tables so it is aliased to avoid DataRow column-name collision.
        // Structural variation (number of EXISTS clauses) is controlled by an integer index —
        // all user-supplied values always flow through @parameterized placeholders, never concatenated.
        var sb = new StringBuilder(@"
            SELECT h.MATNR, h.EDITION, h.DESCRIPTION, h.PRODUCT_CATEGOTY, h.PRODUCT_FAMILY,
                   h.SPEC_FILE_PATH, h.ISSUE_DATE, h.CREATED_BY, h.CREATED_DATE,
                   h.IS_DELETED     AS HDR_DELETED,
                   p.KEY_PARAMETER, p.VALUE_MAX, p.VALUE_TYP, p.VALUE_MIN, p.VALUE_TEXT,
                   p.UM, p.MODIFIED_BY, p.MODIFIED_DATE,
                   p.IS_DELETED     AS PRM_DELETED,
                   mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM,
                   fvp.TYPE_PARAMETER
            FROM   dbo.MAS_SPEC_MATNR_HEADER h
            JOIN   dbo.MAS_SPEC_MATNR_PARAMETERS p
                     ON p.MATNR = h.MATNR AND p.EDITION = h.EDITION AND p.IS_DELETED = 0
            JOIN   dbo.MAS_SPEC_PARAMETER mp
                     ON mp.KEY_PARAMETER = p.KEY_PARAMETER
            JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
                     ON fvp.FAM_KEY = @famKey AND fvp.KEY_PARAMETER = p.KEY_PARAMETER
            WHERE  h.PRODUCT_CATEGOTY = @catKey
              AND  h.PRODUCT_FAMILY   = @famKey
              AND  h.IS_DELETED = 0");

        var paramList = new List<SqlParameter>
        {
            new SqlParameter("@catKey", catKey),
            new SqlParameter("@famKey", famKey)
        };

        if (filters != null)
        {
            for (int i = 0; i < filters.Count; i++)
            {
                sb.Append($@"
              AND EXISTS (
                  SELECT 1 FROM dbo.MAS_SPEC_MATNR_PARAMETERS pf{i}
                  WHERE  pf{i}.MATNR         = h.MATNR
                    AND  pf{i}.EDITION       = h.EDITION
                    AND  pf{i}.KEY_PARAMETER = @fKey{i}
                    AND  (@fMin{i} IS NULL OR pf{i}.VALUE_TYP >= @fMin{i})
                    AND  (@fMax{i} IS NULL OR pf{i}.VALUE_TYP <= @fMax{i})
                    AND  pf{i}.IS_DELETED    = 0)");

                paramList.Add(new SqlParameter($"@fKey{i}", filters[i].KeyParameter));
                paramList.Add(new SqlParameter($"@fMin{i}", (object?)filters[i].ValueTypMin ?? DBNull.Value));
                paramList.Add(new SqlParameter($"@fMax{i}", (object?)filters[i].ValueTypMax ?? DBNull.Value));
            }
        }

        sb.Append(@"
            ORDER BY h.MATNR, h.EDITION, fvp.TYPE_PARAMETER, mp.PARAMETER_NAME");

        var q = await _db.SelectAsync(sb.ToString(), [.. paramList], cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_SEARCH_RESULT>>.Fail($"Error: {q.Message}");

        var resultList  = new List<SPEC_SEARCH_RESULT>();
        var resultIndex = new Dictionary<(string, string), SPEC_SEARCH_RESULT>();

        foreach (DataRow row in q.DTResults.Rows)
        {
            var matnr   = GetString(row, "MATNR");
            var edition = GetString(row, "EDITION");
            var key     = (matnr, edition);

            if (!resultIndex.TryGetValue(key, out var entry))
            {
                entry = new SPEC_SEARCH_RESULT
                {
                    Header = new SPEC_MATNR_HEADER
                    {
                        Matnr           = matnr,
                        Edition         = edition,
                        Description     = GetString(row, "DESCRIPTION"),
                        ProductCategory = GetString(row, "PRODUCT_CATEGOTY"),
                        ProductFamily   = GetString(row, "PRODUCT_FAMILY"),
                        SpecFilePath    = GetString(row, "SPEC_FILE_PATH"),
                        IssueDate       = GetDateOnly(row, "ISSUE_DATE"),
                        CreatedBy       = GetString(row, "CREATED_BY"),
                        CreatedDate     = GetDateTime(row, "CREATED_DATE"),
                        IsDeleted       = GetBoolean(row, "HDR_DELETED")
                    }
                };
                resultIndex[key] = entry;
                resultList.Add(entry);
            }

            entry.Parameters.Add(new SPEC_MATNR_PARAMETER
            {
                Matnr         = matnr,
                Edition       = edition,
                KeyParameter  = GetString(row, "KEY_PARAMETER"),
                ParameterName = GetString(row, "PARAMETER_NAME"),
                TypeParameter = GetString(row, "TYPE_PARAMETER"),
                ValueMax      = GetDecimalNullable(row, "VALUE_MAX"),
                ValueTyp      = GetDecimalNullable(row, "VALUE_TYP"),
                ValueMin      = GetDecimalNullable(row, "VALUE_MIN"),
                ValueText     = GetString(row, "VALUE_TEXT"),
                Um            = GetString(row, "UM"),
                DefaultUm     = GetString(row, "PARAMETER_DEFAULT_UM"),
                ModifiedBy    = GetString(row, "MODIFIED_BY"),
                ModifiedDate  = GetDateTime(row, "MODIFIED_DATE"),
                IsDeleted     = GetBoolean(row, "PRM_DELETED")
            });
        }

        return OperationResult<List<SPEC_SEARCH_RESULT>>.Ok(resultList);
    }

    // ── Write — Products ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new product header. Requires @SPECS permission.
    /// Triggers auto-populate parameters (TR_MATNR_HDR_INIT_PARAMS) and audit (TR_MATNR_HDR_AUDIT).
    /// </summary>
    public async Task<SqlActionResult> Specs_CreateProduct(
                                                            SPEC_MATNR_HEADER header,
                                                            string userRequester = "System",
                                                            CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission(SPECS_KEY_PROCESS, userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        const string sql = @"
            INSERT INTO dbo.MAS_SPEC_MATNR_HEADER
                (MATNR, EDITION, DESCRIPTION, PRODUCT_CATEGOTY, PRODUCT_FAMILY,
                 SPEC_FILE_PATH, ISSUE_DATE, CREATED_BY)
            VALUES
                (@matnr, @edition, @description, @category, @family,
                 @filePath, @issueDate, @createdBy)";

        return await _db.NonQueryAsync(sql,
            new[]
            {
                new SqlParameter("@matnr",       header.Matnr),
                new SqlParameter("@edition",     header.Edition),
                new SqlParameter("@description", header.Description),
                new SqlParameter("@category",    header.ProductCategory),
                new SqlParameter("@family",      header.ProductFamily),
                new SqlParameter("@filePath",    string.IsNullOrWhiteSpace(header.SpecFilePath)
                                                     ? (object)DBNull.Value : header.SpecFilePath),
                new SqlParameter("@issueDate",   header.IssueDate == DateOnly.MinValue
                                                     ? (object)DBNull.Value
                                                     : header.IssueDate.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@createdBy",   userRequester)
            });
    }

    // ── Import — JSON → MATNR tables ─────────────────────────────────────────

    /// <summary>
    /// Reads a SPEC JSON file from <paramref name="filePath"/> and upserts its data into
    /// MAS_SPEC_MATNR_HEADER and MAS_SPEC_MATNR_PARAMETERS.
    ///
    /// Header logic:
    ///   - If MATNR+EDITION does not exist → INSERT (trigger creates parameter rows automatically).
    ///   - If MATNR+EDITION already exists → only EMPTY header fields are filled
    ///     (description, spec file path, issue date); manual header values are never overwritten.
    ///
    /// Parameter logic (per JSON entry):
    ///   - value_typ is tried as decimal; on failure it goes to VALUE_TEXT.
    ///   - Only active parameters of the product are updated; others are skipped and counted.
    ///   - Parameters with DATA_SOURCE='MANUAL' are NEVER overwritten (counted in ParametersProtected).
    ///   - Only values that really change are written, marked DATA_SOURCE='DOC'.
    ///   - Parameters in DB but absent from the JSON are left untouched.
    ///   - UM: null/empty in JSON → existing DB value is preserved. Unknown UM → saved + warning.
    ///
    /// Requires @SPECS permission.
    /// </summary>
    public async Task<OperationResult<SPEC_IMPORT_RESULT>> Specs_ImportFromJson(
        string filePath,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        // 1 — permission
        var perm = await Specs_CheckPermission(SPECS_KEY_PROCESS, userRequester, cancellationToken);
        if (!perm.Success)
            return OperationResult<SPEC_IMPORT_RESULT>.Fail(perm.ErrorMessage);

        // 2 — read and deserialize
        if (!File.Exists(filePath))
            return OperationResult<SPEC_IMPORT_RESULT>.Fail($"File not found: '{filePath}'.");

        JsonSpecRoot root;
        try
        {
            await using var stream = File.OpenRead(filePath);
            root = await JsonSerializer.DeserializeAsync<JsonSpecRoot>(stream,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                       cancellationToken)
                   ?? throw new InvalidDataException("JSON root is null.");
        }
        catch (Exception ex)
        {
            return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error reading JSON: {ex.Message}");
        }

        // 3 — validate required fields
        if (string.IsNullOrWhiteSpace(root.Matnr))
            return OperationResult<SPEC_IMPORT_RESULT>.Fail("JSON field 'MATNR' is missing or empty.");
        if (string.IsNullOrWhiteSpace(root.Edition))
            return OperationResult<SPEC_IMPORT_RESULT>.Fail("JSON field 'EDITION' is missing or empty.");
        if (string.IsNullOrWhiteSpace(root.ProductCategory))
            return OperationResult<SPEC_IMPORT_RESULT>.Fail("JSON field 'product_category_key' is missing or empty.");
        if (string.IsNullOrWhiteSpace(root.FamilyKey))
            return OperationResult<SPEC_IMPORT_RESULT>.Fail("JSON field 'family_key' is missing or empty.");

        var matnr   = root.Matnr.Trim();
        var edition = root.Edition.Trim();

        DateOnly? issueDate = null;
        if (!string.IsNullOrWhiteSpace(root.IssueDate) &&
            DateOnly.TryParse(root.IssueDate, out var parsedDate))
            issueDate = parsedDate;

        var result = new SPEC_IMPORT_RESULT
        {
            Matnr    = matnr,
            Edition  = edition,
            Warnings = root.Warnings ?? []
        };

        // 4 — check if header already exists
        const string sqlExists = @"
            SELECT COUNT(1)
            FROM   dbo.MAS_SPEC_MATNR_HEADER
            WHERE  MATNR = @matnr AND EDITION = @edition";

        var existsQuery = await _db.SelectAsync(sqlExists,
            new[] { new SqlParameter("@matnr", matnr), new SqlParameter("@edition", edition) },
            cancellationToken);

        if (!existsQuery.Success || existsQuery.DTResults == null)
            return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error checking header existence: {existsQuery.Message}");

        bool headerExists = Convert.ToInt32(existsQuery.DTResults.Rows[0][0]) > 0;

        // 5a — INSERT header (trigger will create parameter rows)
        if (!headerExists)
        {
            var header = new SPEC_MATNR_HEADER
            {
                Matnr           = matnr,
                Edition         = edition,
                Description     = root.Description?.Trim() ?? string.Empty,
                ProductCategory = root.ProductCategory.Trim(),
                ProductFamily   = root.FamilyKey.Trim(),
                SpecFilePath    = root.SourceUrl?.Trim() ?? string.Empty,
                IssueDate       = issueDate ?? DateOnly.MinValue
            };

            var insertResult = await Specs_CreateProduct(header, userRequester, cancellationToken);
            if (!insertResult.Success)
                return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error inserting header: {insertResult.Message}");

            result.HeaderCreated = true;
        }
        else
        {
            // 5b — existing header: only fill EMPTY fields (manual values are never overwritten)
            var updateResult = await Specs_ImportFillHeaderAsync(
                matnr, edition,
                root.Description,
                root.SourceUrl,
                issueDate,
                userRequester,
                cancellationToken);

            if (!updateResult.Success)
                return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error updating header: {updateResult.Message}");

            result.HeaderCreated = false;
        }

        // 6 — current product (after INSERT the trigger has already created the parameter rows)
        var current = await Specs_LoadProductAsync(matnr, edition, cancellationToken);
        if (!current.Success)
            return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error reading existing parameters: {current.ErrorMessage}");

        var dbKeys = new HashSet<string>(current.Data.Parameters.Select(p => p.KeyParameter), StringComparer.OrdinalIgnoreCase);

        // 7 — build batch update list from JSON parameters
        var updates = new List<SPEC_PARAM_UPDATE>();

        foreach (var jp in root.Parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(jp.KeyParameter)) continue;

            var key = jp.KeyParameter.Trim();

            if (!dbKeys.Contains(key))
            {
                result.ParametersSkipped++;
                result.Warnings.Add($"Parameter '{key}' not found in DB for {matnr}/{edition} — skipped.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(jp.ValueMin) &&
                string.IsNullOrWhiteSpace(jp.ValueTyp) &&
                string.IsNullOrWhiteSpace(jp.ValueMax) &&
                string.IsNullOrWhiteSpace(jp.Unit))
                continue;

            // value_typ: try numeric first, fall back to text
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

            updates.Add(new SPEC_PARAM_UPDATE
            {
                KeyParameter = key,
                ValueMax     = ParseDecimalNullable(jp.ValueMax),
                ValueTyp     = valueTypNum,
                ValueMin     = ParseDecimalNullable(jp.ValueMin),
                ValueText    = valueTypText,
                Um           = string.IsNullOrWhiteSpace(jp.Unit) ? null : jp.Unit.Trim()
            });
        }

        // 8 — diff + protection of MANUAL values, then persist only real changes as DATA_SOURCE='DOC'
        var units = await Specs_LoadActiveUnitsAsync(cancellationToken);
        if (!units.Success)
            return OperationResult<SPEC_IMPORT_RESULT>.Fail(units.ErrorMessage);

        var plan = Specs_BuildParameterPlan(current.Data.Parameters, updates,
                                            isImport: true, keepUmWhenEmpty: true, units.Data);
        result.Warnings.AddRange(plan.Warnings);
        result.ParametersProtected = plan.Protected;
        result.ParametersSkipped  += updates.Count - plan.Changes.Count - plan.Protected - plan.Unchanged;

        if (plan.Changes.Count > 0)
        {
            int written = 0;
            var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
            {
                await Specs_SetSessionUserAsync(userRequester, t, ct);
                written = await Specs_WriteParameterChangesAsync(matnr, edition, plan.Changes,
                              SPECS_SRC_DOC, protectManual: true, userRequester, t, ct);
            }, cancellationToken);

            if (!tx.Success)
                return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error updating parameters: {tx.Message}");

            result.ParametersUpdated = written;
        }

        return OperationResult<SPEC_IMPORT_RESULT>.Ok(result);
    }

    // Parses a nullable string to decimal using invariant culture; returns null on failure.
    private static decimal? ParseDecimalNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result) ? result : null;
    }
}
