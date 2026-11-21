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

/// <summary>
/// One criterion of the legacy parametric search (Specs_SearchProducts): a WITHIN filter of the Area 3 engine —
/// TYP in [ValueTypMin, ValueTypMax] (without TYP, MIN–MAX overlaps), in the default UM of the parameter, either bound optional.
/// New callers use SPEC_SEARCH_FILTER + Specs_SearchProductsAdvanced.
/// </summary>
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

    // ── Write — Products ──────────────────────────────────────────────────────

    private const int SPECS_LEN_MATNR   = 18;   // MAS_SPEC_MATNR_HEADER.MATNR nchar(18)
    private const int SPECS_LEN_EDITION = 10;   // MAS_SPEC_MATNR_HEADER.EDITION nchar(10)

    /// <summary>
    /// Creates a new product (manual creation from scratch, also used by the JSON import). Requires @SPECS.
    /// Validates: MATNR / EDITION / description required and within length, product not existing (active or
    /// deleted), category active, family active and belonging to the category.
    /// One transaction with the session user, so the audit rows carry the engineer:
    ///   TR_MATNR_HDR_INIT_PARAMS creates one parameter row per active family parameter with UM = NULL and
    ///   UM_PARAMETER = default UM of the parameter; the header audit trigger records the creation.
    /// Then open the editor with Specs_GetProductDetail (parameters + UnitOptions picking list).
    /// </summary>
    public async Task<SqlActionResult> Specs_CreateProduct(
                                                            SPEC_MATNR_HEADER header,
                                                            string userRequester = "System",
                                                            CancellationToken cancellationToken = default)
    {
        if (header is null) return Specs_Fail("Product header is required.");

        var matnr       = (header.Matnr ?? string.Empty).Trim();
        var edition     = (header.Edition ?? string.Empty).Trim();
        var description = (header.Description ?? string.Empty).Trim();
        var filePath    = Specs_NullIfBlank(header.SpecFilePath);

        if (matnr.Length == 0)                         return Specs_Fail("MATNR is required.");
        if (matnr.Length > SPECS_LEN_MATNR)            return Specs_Fail($"MATNR '{matnr}' exceeds {SPECS_LEN_MATNR} characters.");
        if (edition.Length == 0)                       return Specs_Fail("Edition is required.");
        if (edition.Length > SPECS_LEN_EDITION)        return Specs_Fail($"Edition '{edition}' exceeds {SPECS_LEN_EDITION} characters.");
        if (description.Length == 0)                   return Specs_Fail("Description is required.");
        if (description.Length > SPECS_LEN_DESCRIPTION) return Specs_Fail($"Description exceeds {SPECS_LEN_DESCRIPTION} characters.");
        if ((filePath?.Length ?? 0) > SPECS_LEN_FILE_PATH) return Specs_Fail($"Spec file path exceeds {SPECS_LEN_FILE_PATH} characters.");
        if (Specs_ValidateKey(header.ProductCategory, "Product category", out var cat) is { } eCat) return Specs_Fail(eCat);
        if (Specs_ValidateKey(header.ProductFamily, "Product family", out var fam) is { } eFam)     return Specs_Fail(eFam);

        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var exists = await _db.SelectAsync(
            "SELECT IS_DELETED FROM dbo.MAS_SPEC_MATNR_HEADER WHERE MATNR = @m AND EDITION = @e",
            P2("@m", matnr, "@e", edition), cancellationToken);
        if (!exists.Success || exists.DTResults == null) return Specs_Fail($"Error: {exists.Message}");
        if (exists.DTResults.Rows.Count > 0)
            return Specs_Fail(GetBoolean(exists.DTResults.Rows[0], "IS_DELETED")
                ? $"Product '{matnr}/{edition}' already exists but is deleted. Enable it instead."
                : $"Product '{matnr}/{edition}' already exists.");

        if (await Specs_RequireActiveAsync(SQL_CAT_STATE, () => P("@k", cat), $"Category '{cat}'", cancellationToken) is { } eC)
            return Specs_Fail(eC);
        if (await Specs_RequireActiveAsync(SQL_FAM_STATE, () => P2("@c", cat, "@f", fam), $"Family '{fam}' of category '{cat}'", cancellationToken) is { } eF)
            return Specs_Fail(eF);

        int written = 0;
        var result = await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);   // CHANGED_BY of the header / parameter audit

            var r = await _db.NonQueryAsync(@"
                INSERT INTO dbo.MAS_SPEC_MATNR_HEADER
                    (MATNR, EDITION, DESCRIPTION, PRODUCT_CATEGOTY, PRODUCT_FAMILY,
                     SPEC_FILE_PATH, ISSUE_DATE, CREATED_BY)
                VALUES
                    (@matnr, @edition, @description, @category, @family,
                     @filePath, @issueDate, @createdBy)",
                new[]
                {
                    new SqlParameter("@matnr",       matnr),
                    new SqlParameter("@edition",     edition),
                    new SqlParameter("@description", description),
                    new SqlParameter("@category",    cat),
                    new SqlParameter("@family",      fam),
                    new SqlParameter("@filePath",    (object?)filePath ?? DBNull.Value),
                    new SqlParameter("@issueDate",   header.IssueDate == DateOnly.MinValue
                                                         ? (object)DBNull.Value
                                                         : header.IssueDate.ToDateTime(TimeOnly.MinValue)),
                    new SqlParameter("@createdBy",   userRequester)
                },
                transaction: tx, ct: ct);
            if (!r.Success) throw new InvalidOperationException(r.Message);
            written = r.RecordsAffected;
        }, cancellationToken);

        if (result.Success) result.RecordsAffected = written;
        return result;
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
    ///   - value_typ: numeric parameter (VALUE_TYPE N) → tried as decimal, on failure VALUE_TEXT + warning;
    ///     list / free-text parameter (L / T) → always VALUE_TEXT. List texts matching a list value
    ///     (case-insensitive) are stored with the list spelling; otherwise stored as read + warning.
    ///     Values are never restricted on import (DATA_SOURCE='DOC'); the engineer reviews them.
    ///   - Only active parameters of the product are updated; others are skipped and counted.
    ///   - Parameters with DATA_SOURCE='MANUAL' are NEVER overwritten (counted in ParametersProtected).
    ///   - Only values that really change are written, marked DATA_SOURCE='DOC'.
    ///   - Parameters in DB but absent from the JSON are left untouched.
    ///   - UM (unit as written in the document): null/empty in JSON → existing DB value preserved;
    ///     longer than 20 characters → truncated + warning.
    ///   - UM_PARAMETER: ALWAYS the default UM of the parameter, even when the document unit differs
    ///     (an engineer reviews it in the UI). Document unit ≠ default UM → warning. Unit problems never
    ///     skip a parameter. MANUAL parameters are untouched (values and units).
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

        var valueTypes = current.Data.Parameters.ToDictionary(p => p.KeyParameter, p => p.ValueType, StringComparer.OrdinalIgnoreCase);

        // 7 — build batch update list from JSON parameters
        var updates = new List<SPEC_PARAM_UPDATE>();

        foreach (var jp in root.Parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(jp.KeyParameter)) continue;

            var key = jp.KeyParameter.Trim();

            if (!valueTypes.TryGetValue(key, out var valueType))
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

            // value_typ: numeric parameter → try numeric first, fall back to text;
            //            list / free-text parameter → always text (e.g. a ratio "14" stays text)
            decimal? valueTypNum  = null;
            string?  valueTypText = null;

            if (!string.IsNullOrWhiteSpace(jp.ValueTyp))
            {
                if (valueType != SPECS_VT_NUMERIC)
                    valueTypText = jp.ValueTyp.Trim();
                else if (decimal.TryParse(jp.ValueTyp,
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
        var plan = Specs_BuildParameterPlan(current.Data.Parameters, updates, isImport: true);
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
