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

public class SPEC_CATEGORY
{
    public string CatKey    { get; set; } = string.Empty;
    public string CatName   { get; set; } = string.Empty;
    public bool   IsDeleted { get; set; }
}

public class SPEC_FAMILY
{
    public string CatKey    { get; set; } = string.Empty;
    public string FamKey    { get; set; } = string.Empty;
    public string FamName   { get; set; } = string.Empty;
    public bool   IsDeleted { get; set; }
}

public class SPEC_PARAMETER
{
    public string KeyParameter       { get; set; } = string.Empty;
    public string ParameterName      { get; set; } = string.Empty;
    public string ParameterDefaultUm { get; set; } = string.Empty;
    public bool   IsDeleted          { get; set; }
}

public class SPEC_FAMILY_PARAMETER
{
    public string FamKey        { get; set; } = string.Empty;
    public string KeyParameter  { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string DefaultUm     { get; set; } = string.Empty;
    public string TypeParameter { get; set; } = string.Empty; // M=Mandatory / O=Optional / I=Informative
    public bool   IsDeleted     { get; set; }
}

public class SPEC_UNIT
{
    public string UmCode        { get; set; } = string.Empty;
    public string UmDescription { get; set; } = string.Empty;
}

public class SPEC_MATNR_HEADER
{
    public string   Matnr           { get; set; } = string.Empty;
    public string   Edition         { get; set; } = string.Empty;
    public string   Description     { get; set; } = string.Empty;
    public string   ProductCategory { get; set; } = string.Empty; // → PRODUCT_CATEGOTY (typo in DB, correct here)
    public string   ProductFamily   { get; set; } = string.Empty;
    public string   SpecFilePath    { get; set; } = string.Empty;
    public DateOnly IssueDate       { get; set; } = DateOnly.MinValue;
    public string   CreatedBy       { get; set; } = string.Empty;
    public DateTime CreatedDate     { get; set; } = DateTime.MinValue;
    public bool     IsDeleted       { get; set; }
}

public class SPEC_MATNR_PARAMETER
{
    public string   Matnr         { get; set; } = string.Empty;
    public string   Edition       { get; set; } = string.Empty;
    public string   KeyParameter  { get; set; } = string.Empty;
    public string   ParameterName { get; set; } = string.Empty;
    public string   TypeParameter { get; set; } = string.Empty; // M / O / I — joined from MAS_SPEC_FAMILY_VS_PARAMETER
    public decimal? ValueMax      { get; set; }
    public decimal? ValueTyp      { get; set; }
    public decimal? ValueMin      { get; set; }
    public string?  ValueText     { get; set; }
    public string   Um            { get; set; } = string.Empty;
    public string   DefaultUm     { get; set; } = string.Empty;
    public string   ModifiedBy    { get; set; } = string.Empty;
    public DateTime ModifiedDate  { get; set; } = DateTime.MinValue;
    public bool     IsDeleted     { get; set; }
}

public class SPEC_MATNR_DETAIL
{
    public SPEC_MATNR_HEADER          Header     { get; set; } = new();
    public List<SPEC_MATNR_PARAMETER> Parameters { get; set; } = [];
}

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

/// <summary>One parameter value to set in a batch update operation.</summary>
public class SPEC_PARAM_UPDATE
{
    public string   KeyParameter { get; set; } = string.Empty;
    public decimal? ValueMax     { get; set; }
    public decimal? ValueTyp     { get; set; }
    public decimal? ValueMin     { get; set; }
    public string?  ValueText    { get; set; }
    public string?  Um           { get; set; }
}

/// <summary>Result returned by Specs_ImportFromJson.</summary>
public class SPEC_IMPORT_RESULT
{
    public string       Matnr               { get; set; } = string.Empty;
    public string       Edition             { get; set; } = string.Empty;
    public bool         HeaderCreated       { get; set; }  // true=INSERT, false=UPDATE
    public int          ParametersUpdated   { get; set; }
    public int          ParametersSkipped   { get; set; }  // in JSON but not found in DB for this MATNR
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private static SPEC_MATNR_HEADER MapSpecHeaderRow(DataRow row) => new()
    {
        Matnr           = GetString(row, "MATNR"),
        Edition         = GetString(row, "EDITION"),
        Description     = GetString(row, "DESCRIPTION"),
        ProductCategory = GetString(row, "PRODUCT_CATEGOTY"),
        ProductFamily   = GetString(row, "PRODUCT_FAMILY"),
        SpecFilePath    = GetString(row, "SPEC_FILE_PATH"),
        IssueDate       = GetDateOnly(row, "ISSUE_DATE"),
        CreatedBy       = GetString(row, "CREATED_BY"),
        CreatedDate     = GetDateTime(row, "CREATED_DATE"),
        IsDeleted       = GetBoolean(row, "IS_DELETED")
    };

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

    // ── Read — Catalog ────────────────────────────────────────────────────────

    public async Task<OperationResult<List<SPEC_CATEGORY>>> Specs_GetCategories(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT CAT_KEY, CAT_NAME, IS_DELETED
            FROM   dbo.MAS_SPEC_CATEGORY
            WHERE  IS_DELETED = 0
            ORDER BY CAT_NAME";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_CATEGORY>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_CATEGORY>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_CATEGORY
            {
                CatKey    = GetString(row, "CAT_KEY"),
                CatName   = GetString(row, "CAT_NAME"),
                IsDeleted = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_CATEGORY>>.Ok(list);
    }

    /// <summary>
    /// Returns families. Pass catKey to filter by category; omit (or null) to get all.
    /// </summary>
    public async Task<OperationResult<List<SPEC_FAMILY>>> Specs_GetFamilies(
        string? catKey = null,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT CAT_KEY, FAM_KEY, FAM_NAME, IS_DELETED
            FROM   dbo.MAS_SPEC_FAMILY
            WHERE  IS_DELETED = 0";

        SqlParameter[]? parameters = null;
        if (!string.IsNullOrWhiteSpace(catKey))
        {
            sql += " AND CAT_KEY = @catKey";
            parameters = new[] { new SqlParameter("@catKey", catKey) };
        }
        sql += " ORDER BY FAM_NAME";

        var q = await _db.SelectAsync(sql, parameters, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_FAMILY>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_FAMILY>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_FAMILY
            {
                CatKey    = GetString(row, "CAT_KEY"),
                FamKey    = GetString(row, "FAM_KEY"),
                FamName   = GetString(row, "FAM_NAME"),
                IsDeleted = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_FAMILY>>.Ok(list);
    }

    /// <summary>
    /// Returns the parameter list for a family, joined with names and type (M/O/I).
    /// Set includeDeleted=true to also return soft-deleted rows (needed for catalog management).
    /// </summary>
    public async Task<OperationResult<List<SPEC_FAMILY_PARAMETER>>> Specs_GetFamilyParameters(
        string famKey,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT fvp.FAM_KEY, fvp.KEY_PARAMETER, fvp.TYPE_PARAMETER, fvp.IS_DELETED,
                   p.PARAMETER_NAME, p.PARAMETER_DEFAULT_UM
            FROM   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
            JOIN   dbo.MAS_SPEC_PARAMETER p ON p.KEY_PARAMETER = fvp.KEY_PARAMETER
            WHERE  fvp.FAM_KEY = @famKey";

        if (!includeDeleted) sql += " AND fvp.IS_DELETED = 0";
        sql += " ORDER BY fvp.TYPE_PARAMETER, p.PARAMETER_NAME";

        var q = await _db.SelectAsync(sql, new[] { new SqlParameter("@famKey", famKey) }, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_FAMILY_PARAMETER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_FAMILY_PARAMETER>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_FAMILY_PARAMETER
            {
                FamKey        = GetString(row, "FAM_KEY"),
                KeyParameter  = GetString(row, "KEY_PARAMETER"),
                ParameterName = GetString(row, "PARAMETER_NAME"),
                DefaultUm     = GetString(row, "PARAMETER_DEFAULT_UM"),
                TypeParameter = GetString(row, "TYPE_PARAMETER"),
                IsDeleted     = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_FAMILY_PARAMETER>>.Ok(list);
    }

    /// <summary>
    /// Returns the master parameter catalog. Used when adding a new family×parameter link.
    /// Set includeDeleted=true for catalog management screens.
    /// </summary>
    public async Task<OperationResult<List<SPEC_PARAMETER>>> Specs_GetMasterParameters(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT KEY_PARAMETER, PARAMETER_NAME, PARAMETER_DEFAULT_UM, IS_DELETED
            FROM   dbo.MAS_SPEC_PARAMETER";

        if (!includeDeleted) sql += " WHERE IS_DELETED = 0";
        sql += " ORDER BY PARAMETER_NAME";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_PARAMETER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_PARAMETER>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_PARAMETER
            {
                KeyParameter       = GetString(row, "KEY_PARAMETER"),
                ParameterName      = GetString(row, "PARAMETER_NAME"),
                ParameterDefaultUm = GetString(row, "PARAMETER_DEFAULT_UM"),
                IsDeleted          = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_PARAMETER>>.Ok(list);
    }

    /// <summary>
    /// Returns the unit of measure catalog.
    /// Set includeDeleted=true for catalog management screens.
    /// </summary>
    public async Task<OperationResult<List<SPEC_UNIT>>> Specs_GetUnits(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT UM_CODE, UM_DESCRIPTION
            FROM   dbo.MAS_SPEC_UM
            ORDER BY UM_CODE";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_UNIT>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_UNIT>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_UNIT
            {
                UmCode        = GetString(row, "UM_CODE"),
                UmDescription = GetString(row, "UM_DESCRIPTION")
            });

        return OperationResult<List<SPEC_UNIT>>.Ok(list);
    }

    // ── Read — Products ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the product header list. Both filters are optional.
    /// </summary>
    public async Task<OperationResult<List<SPEC_MATNR_HEADER>>> Specs_GetProductList(
        string? catKey = null,
        string? famKey = null,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT MATNR, EDITION, DESCRIPTION, PRODUCT_CATEGOTY, PRODUCT_FAMILY,
                   SPEC_FILE_PATH, ISSUE_DATE, CREATED_BY, CREATED_DATE, IS_DELETED
            FROM   dbo.MAS_SPEC_MATNR_HEADER
            WHERE  IS_DELETED = 0";

        var paramList = new List<SqlParameter>();
        if (!string.IsNullOrWhiteSpace(catKey))
        {
            sql += " AND PRODUCT_CATEGOTY = @catKey";
            paramList.Add(new SqlParameter("@catKey", catKey));
        }
        if (!string.IsNullOrWhiteSpace(famKey))
        {
            sql += " AND PRODUCT_FAMILY = @famKey";
            paramList.Add(new SqlParameter("@famKey", famKey));
        }
        sql += " ORDER BY MATNR, EDITION";

        var q = await _db.SelectAsync(sql, paramList.Count > 0 ? [.. paramList] : null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_MATNR_HEADER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_MATNR_HEADER>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(MapSpecHeaderRow(row));

        return OperationResult<List<SPEC_MATNR_HEADER>>.Ok(list);
    }

    /// <summary>
    /// Returns full product detail: header + all active parameters with type and default UM joined.
    /// Parameters are ordered Mandatory → Optional → Informative, then alphabetically.
    /// </summary>
    public async Task<OperationResult<SPEC_MATNR_DETAIL>> Specs_GetProductDetail(
        string matnr,
        string edition,
        CancellationToken cancellationToken = default)
    {
        const string sqlHeader = @"
            SELECT MATNR, EDITION, DESCRIPTION, PRODUCT_CATEGOTY, PRODUCT_FAMILY,
                   SPEC_FILE_PATH, ISSUE_DATE, CREATED_BY, CREATED_DATE, IS_DELETED
            FROM   dbo.MAS_SPEC_MATNR_HEADER
            WHERE  MATNR = @matnr AND EDITION = @edition";

        var keyParams = new[]
        {
            new SqlParameter("@matnr",   matnr),
            new SqlParameter("@edition", edition)
        };

        var hq = await _db.SelectAsync(sqlHeader, keyParams, cancellationToken);
        if (!hq.Success || hq.DTResults == null)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Error: {hq.Message}");
        if (hq.DTResults.Rows.Count == 0)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Product '{matnr}/{edition}' not found.");

        var header = MapSpecHeaderRow(hq.DTResults.Rows[0]);

        const string sqlParams = @"
            SELECT p.MATNR, p.EDITION, p.KEY_PARAMETER,
                   p.VALUE_MAX, p.VALUE_TYP, p.VALUE_MIN, p.VALUE_TEXT,
                   p.UM, p.MODIFIED_BY, p.MODIFIED_DATE, p.IS_DELETED,
                   mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM,
                   fvp.TYPE_PARAMETER
            FROM   dbo.MAS_SPEC_MATNR_PARAMETERS p
            JOIN   dbo.MAS_SPEC_PARAMETER mp
                     ON mp.KEY_PARAMETER = p.KEY_PARAMETER
            JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
                     ON fvp.FAM_KEY = @famKey AND fvp.KEY_PARAMETER = p.KEY_PARAMETER
            WHERE  p.MATNR = @matnr AND p.EDITION = @edition AND p.IS_DELETED = 0
            ORDER BY fvp.TYPE_PARAMETER, mp.PARAMETER_NAME";

        var pq = await _db.SelectAsync(sqlParams,
            new[]
            {
                new SqlParameter("@matnr",   matnr),
                new SqlParameter("@edition", edition),
                new SqlParameter("@famKey",  header.ProductFamily)
            },
            cancellationToken);

        if (!pq.Success || pq.DTResults == null)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Error loading parameters: {pq.Message}");

        var paramList = new List<SPEC_MATNR_PARAMETER>();
        foreach (DataRow row in pq.DTResults.Rows)
            paramList.Add(new SPEC_MATNR_PARAMETER
            {
                Matnr         = GetString(row, "MATNR"),
                Edition       = GetString(row, "EDITION"),
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
                IsDeleted     = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<SPEC_MATNR_DETAIL>.Ok(new SPEC_MATNR_DETAIL
        {
            Header     = header,
            Parameters = paramList
        });
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

    /// <summary>
    /// Returns true if userId holds the SPECS_EDIT permission.
    /// Call from UI to decide whether to show edit controls before attempting a save.
    /// </summary>
    public async Task<bool> Specs_CanUserEdit(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var result = await Specs_CheckPermission("SPECS_EDIT", userId, cancellationToken);
        return result.Success;
    }

    public async Task<bool> Specs_CanCreate(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var result = await Specs_CheckPermission("SPECS_CRT", userId, cancellationToken);
        return result.Success;
    }

    // ── Write — Products ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new product header. Requires SPECS_CREATE permission.
    /// Triggers auto-populate parameters (TR_MATNR_HDR_INIT_PARAMS) and audit (TR_MATNR_HDR_AUDIT).
    /// </summary>
    public async Task<SqlActionResult> Specs_CreateProduct(
                                                            SPEC_MATNR_HEADER header,
                                                            string userRequester = "System",
                                                            CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CRT", userRequester, cancellationToken);
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

    /// <summary>
    /// Updates the editable fields of a product header (description, file path, issue date).
    /// Requires SPECS_EDIT permission. TR_MATNR_HDR_AUDIT records the change.
    /// </summary>
    public async Task<SqlActionResult> Specs_UpdateProductHeader(
        string matnr,
        string edition,
        string description,
        string? specFilePath,
        DateOnly? issueDate,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_EDIT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);

            const string sql = @"
                UPDATE dbo.MAS_SPEC_MATNR_HEADER
                SET DESCRIPTION    = @description,
                    SPEC_FILE_PATH = @filePath,
                    ISSUE_DATE     = @issueDate
                WHERE MATNR = @matnr AND EDITION = @edition";

            await _db.NonQueryAsync(sql,
                new[]
                {
                    new SqlParameter("@matnr",       matnr),
                    new SqlParameter("@edition",     edition),
                    new SqlParameter("@description", description),
                    new SqlParameter("@filePath",    string.IsNullOrWhiteSpace(specFilePath)
                                                         ? (object)DBNull.Value : specFilePath),
                    new SqlParameter("@issueDate",   issueDate == null || issueDate == DateOnly.MinValue
                                                         ? (object)DBNull.Value
                                                         : issueDate.Value.ToDateTime(TimeOnly.MinValue))
                },
                transaction: tx,
                ct: ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Updates the values and UM of a single parameter line. Requires SPECS_EDIT permission.
    /// Pass null for any value to clear it. Pass null/empty for um to keep the family default.
    /// TR_MATNR_PRM_AUDIT records old → new automatically.
    /// </summary>
    public async Task<SqlActionResult> Specs_UpdateParameterValue(
        string matnr,
        string edition,
        string keyParameter,
        decimal? valueMax,
        decimal? valueTyp,
        decimal? valueMin,
        string? valueText,
        string? um,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_EDIT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);

            const string sql = @"
                UPDATE dbo.MAS_SPEC_MATNR_PARAMETERS
                SET VALUE_MAX     = @valueMax,
                    VALUE_TYP     = @valueTyp,
                    VALUE_MIN     = @valueMin,
                    VALUE_TEXT    = @valueText,
                    UM            = COALESCE(NULLIF(@um, ''), UM),
                    MODIFIED_BY   = @user,
                    MODIFIED_DATE = GETDATE()
                WHERE MATNR = @matnr AND EDITION = @edition AND KEY_PARAMETER = @keyParameter";

            await _db.NonQueryAsync(sql,
                new[]
                {
                    new SqlParameter("@matnr",        matnr),
                    new SqlParameter("@edition",      edition),
                    new SqlParameter("@keyParameter", keyParameter),
                    new SqlParameter("@valueMax",     (object?)valueMax ?? DBNull.Value),
                    new SqlParameter("@valueTyp",     (object?)valueTyp ?? DBNull.Value),
                    new SqlParameter("@valueMin",     (object?)valueMin ?? DBNull.Value),
                    new SqlParameter("@valueText",    string.IsNullOrWhiteSpace(valueText) ? (object)DBNull.Value : valueText),
                    new SqlParameter("@um",           string.IsNullOrWhiteSpace(um) ? (object)DBNull.Value : um),
                    new SqlParameter("@user",         userRequester)
                },
                transaction: tx,
                ct: ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Updates multiple parameter values in a single transaction. Requires SPECS_EDIT permission.
    /// Only the parameters present in the updates list are modified; the rest remain unchanged.
    /// TR_MATNR_PRM_AUDIT records old → new for each line automatically.
    /// </summary>
    public async Task<SqlActionResult> Specs_UpdateParametersBatch(
                                                                    string matnr,
                                                                    string edition,
                                                                    List<SPEC_PARAM_UPDATE> updates,
                                                                    string userRequester = "System",
                                                                    CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_EDIT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        if (updates == null || updates.Count == 0)
            return new SqlActionResult { Success = true, Message = "OK", RecordsAffected = 0 };

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);

            const string sql = @"
                UPDATE dbo.MAS_SPEC_MATNR_PARAMETERS
                SET VALUE_MAX     = @valueMax,
                    VALUE_TYP     = @valueTyp,
                    VALUE_MIN     = @valueMin,
                    VALUE_TEXT    = @valueText,
                    UM            = COALESCE(NULLIF(@um, ''), UM),
                    MODIFIED_BY   = @user,
                    MODIFIED_DATE = GETDATE()
                WHERE MATNR = @matnr AND EDITION = @edition AND KEY_PARAMETER = @keyParameter";

            foreach (var upd in updates)
            {
                await _db.NonQueryAsync(sql,
                    new[]
                    {
                        new SqlParameter("@matnr",        matnr),
                        new SqlParameter("@edition",      edition),
                        new SqlParameter("@keyParameter", upd.KeyParameter),
                        new SqlParameter("@valueMax",     (object?)upd.ValueMax ?? DBNull.Value),
                        new SqlParameter("@valueTyp",     (object?)upd.ValueTyp ?? DBNull.Value),
                        new SqlParameter("@valueMin",     (object?)upd.ValueMin ?? DBNull.Value),
                        new SqlParameter("@valueText",    string.IsNullOrWhiteSpace(upd.ValueText) ? (object)DBNull.Value : upd.ValueText),
                        new SqlParameter("@um",           string.IsNullOrWhiteSpace(upd.Um) ? (object)DBNull.Value : upd.Um),
                        new SqlParameter("@user",         userRequester)
                    },
                    transaction: tx,
                    ct: ct);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a product (IS_DELETED = 1). Requires SPECS_DELETE permission.
    /// TR_MATNR_HDR_AUDIT logs the operation.
    /// </summary>
    public async Task<SqlActionResult> Specs_DeleteProduct(
        string matnr,
        string edition,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_DEL", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);

            const string sql = @"
                UPDATE dbo.MAS_SPEC_MATNR_HEADER
                SET IS_DELETED = 1
                WHERE MATNR = @matnr AND EDITION = @edition";

            await _db.NonQueryAsync(sql,
                new[]
                {
                    new SqlParameter("@matnr",   matnr),
                    new SqlParameter("@edition", edition)
                },
                transaction: tx,
                ct: ct);
        }, cancellationToken);
    }

    // ── Write — Catalog: Categories ───────────────────────────────────────────

    public async Task<SqlActionResult> Specs_AddCategory(
        string catKey,
        string catName,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_CATEGORY (CAT_KEY, CAT_NAME, IS_DELETED) VALUES (@key, @name, 0)",
            new[]
            {
                new SqlParameter("@key",  catKey),
                new SqlParameter("@name", catName)
            });
    }

    public async Task<SqlActionResult> Specs_UpdateCategory(
        string catKey,
        string catName,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_CATEGORY SET CAT_NAME = @name WHERE CAT_KEY = @key",
            new[]
            {
                new SqlParameter("@key",  catKey),
                new SqlParameter("@name", catName)
            });
    }

    public async Task<SqlActionResult> Specs_DisableCategory(
        string catKey,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_CATEGORY SET IS_DELETED = 1 WHERE CAT_KEY = @key",
            new[] { new SqlParameter("@key", catKey) });
    }

    // ── Write — Catalog: Families ─────────────────────────────────────────────

    public async Task<SqlActionResult> Specs_AddFamily(
        string catKey,
        string famKey,
        string famName,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        //var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        //if (!perm.Success)
        //    return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_FAMILY (CAT_KEY, FAM_KEY, FAM_NAME, IS_DELETED) VALUES (@catKey, @famKey, @name, 0)",
            new[]
            {
                new SqlParameter("@catKey", catKey),
                new SqlParameter("@famKey", famKey),
                new SqlParameter("@name",   famName)
            });
    }

    public async Task<SqlActionResult> Specs_UpdateFamily(
        string catKey,
        string famKey,
        string famName,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY SET FAM_NAME = @name WHERE CAT_KEY = @catKey AND FAM_KEY = @famKey",
            new[]
            {
                new SqlParameter("@catKey", catKey),
                new SqlParameter("@famKey", famKey),
                new SqlParameter("@name",   famName)
            });
    }

    public async Task<SqlActionResult> Specs_DisableFamily(
        string catKey,
        string famKey,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY SET IS_DELETED = 1 WHERE CAT_KEY = @catKey AND FAM_KEY = @famKey",
            new[]
            {
                new SqlParameter("@catKey", catKey),
                new SqlParameter("@famKey", famKey)
            });
    }

    // ── Write — Catalog: Master Parameters ───────────────────────────────────

    public async Task<SqlActionResult> Specs_AddMasterParameter(
        string keyParameter,
        string parameterName,
        string defaultUm,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_PARAMETER (KEY_PARAMETER, PARAMETER_NAME, PARAMETER_DEFAULT_UM, IS_DELETED) VALUES (@key, @name, @um, 0)",
            new[]
            {
                new SqlParameter("@key",  keyParameter),
                new SqlParameter("@name", parameterName),
                new SqlParameter("@um",   defaultUm)
            });
    }

    public async Task<SqlActionResult> Specs_UpdateMasterParameter(
        string keyParameter,
        string parameterName,
        string defaultUm,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_PARAMETER SET PARAMETER_NAME = @name, PARAMETER_DEFAULT_UM = @um WHERE KEY_PARAMETER = @key",
            new[]
            {
                new SqlParameter("@key",  keyParameter),
                new SqlParameter("@name", parameterName),
                new SqlParameter("@um",   defaultUm)
            });
    }

    /// <summary>
    /// Soft-deletes a master parameter. Requires SPECS_CATALOG permission.
    /// Transaction: soft-deletes in MAS_SPEC_PARAMETER then in all MAS_SPEC_FAMILY_VS_PARAMETER rows.
    /// TR_FAMILY_PARAM_IS_DELETED_SYNC fires per row and propagates IS_DELETED to MATNRs automatically.
    /// </summary>
    public async Task<SqlActionResult> Specs_DisableMasterParameter(
        string keyParameter,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await _db.NonQueryAsync(
                "UPDATE dbo.MAS_SPEC_PARAMETER SET IS_DELETED = 1 WHERE KEY_PARAMETER = @key",
                new[] { new SqlParameter("@key", keyParameter) },
                transaction: tx,
                ct: ct);

            // Each row update fires TR_FAMILY_PARAM_IS_DELETED_SYNC → propagates to MATNR_PARAMETERS
            await _db.NonQueryAsync(
                "UPDATE dbo.MAS_SPEC_FAMILY_VS_PARAMETER SET IS_DELETED = 1 WHERE KEY_PARAMETER = @key",
                new[] { new SqlParameter("@key", keyParameter) },
                transaction: tx,
                ct: ct);
        }, cancellationToken);
    }

    // ── Write — Catalog: Family × Parameter ──────────────────────────────────

    /// <summary>
    /// Links a parameter to a family with the given type (M/O/I). Requires SPECS_CATALOG permission.
    /// TR_FAMILY_PARAM_ADDED fires and inserts a null value row in every active MATNR of that family.
    /// </summary>
    public async Task<SqlActionResult> Specs_AddFamilyParameter(
        string famKey,
        string keyParameter,
        string typeParameter,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_FAMILY_VS_PARAMETER (FAM_KEY, KEY_PARAMETER, TYPE_PARAMETER, IS_DELETED) VALUES (@fam, @key, @type, 0)",
            new[]
            {
                new SqlParameter("@fam",  famKey),
                new SqlParameter("@key",  keyParameter),
                new SqlParameter("@type", typeParameter)
            });
    }

    public async Task<SqlActionResult> Specs_UpdateFamilyParameterType(
        string famKey,
        string keyParameter,
        string typeParameter,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY_VS_PARAMETER SET TYPE_PARAMETER = @type WHERE FAM_KEY = @fam AND KEY_PARAMETER = @key",
            new[]
            {
                new SqlParameter("@fam",  famKey),
                new SqlParameter("@key",  keyParameter),
                new SqlParameter("@type", typeParameter)
            });
    }

    /// <summary>
    /// Soft-deletes a family×parameter link. Requires SPECS_CATALOG permission.
    /// TR_FAMILY_PARAM_IS_DELETED_SYNC fires and propagates IS_DELETED to all MATNR rows of that family.
    /// </summary>
    public async Task<SqlActionResult> Specs_DisableFamilyParameter(
        string famKey,
        string keyParameter,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY_VS_PARAMETER SET IS_DELETED = 1 WHERE FAM_KEY = @fam AND KEY_PARAMETER = @key",
            new[]
            {
                new SqlParameter("@fam", famKey),
                new SqlParameter("@key", keyParameter)
            });
    }

    // ── Write — Catalog: Units of Measure ────────────────────────────────────

    public async Task<SqlActionResult> Specs_AddUnit(
        string umCode,
        string umDescription,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_UM (UM_CODE, UM_DESCRIPTION) VALUES (@code, @desc)",
            new[]
            {
                new SqlParameter("@code", umCode),
                new SqlParameter("@desc", umDescription)
            });
    }

    public async Task<SqlActionResult> Specs_UpdateUnit(
        string umCode,
        string umDescription,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_UM SET UM_DESCRIPTION = @desc WHERE UM_CODE = @code",
            new[]
            {
                new SqlParameter("@code", umCode),
                new SqlParameter("@desc", umDescription)
            });
    }

    public async Task<SqlActionResult> Specs_DeleteUnit(
        string umCode,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        var perm = await Specs_CheckPermission("SPECS_CAT", userRequester, cancellationToken);
        if (!perm.Success)
            return new SqlActionResult { Success = false, Message = perm.ErrorMessage };

        return await _db.NonQueryAsync(
            "DELETE FROM dbo.MAS_SPEC_UM WHERE UM_CODE = @code",
            new[] { new SqlParameter("@code", umCode) });
    }

    // ── Import — JSON → MATNR tables ─────────────────────────────────────────

    /// <summary>
    /// Reads a SPEC JSON file from <paramref name="filePath"/> and upserts its data into
    /// MAS_SPEC_MATNR_HEADER and MAS_SPEC_MATNR_PARAMETERS.
    ///
    /// Header logic:
    ///   - If MATNR+EDITION does not exist → INSERT (trigger creates parameter rows automatically).
    ///   - If MATNR+EDITION already exists → UPDATE description and issue date only.
    ///
    /// Parameter logic (per JSON entry):
    ///   - value_typ is tried as decimal; on failure it goes to VALUE_TYP_TEXT.
    ///   - Only parameters already present in MAS_SPEC_MATNR_PARAMETERS are updated.
    ///     Parameters in the JSON but absent from the DB (not in the family) are skipped and counted.
    ///   - Parameters in DB but absent from the JSON are left untouched.
    ///   - UM: null/empty in JSON → existing DB value is preserved (COALESCE in SQL).
    ///
    /// Requires SPECS_CRT permission.
    /// </summary>
    public async Task<OperationResult<SPEC_IMPORT_RESULT>> Specs_ImportFromJson(
        string filePath,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        // 1 — permission
        var perm = await Specs_CheckPermission("SPECS_CRT", userRequester, cancellationToken);
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
            // 5b — UPDATE header editable fields only
            var updateResult = await Specs_UpdateProductHeader(
                matnr, edition,
                root.Description?.Trim() ?? string.Empty,
                root.SourceUrl?.Trim(),
                issueDate,
                userRequester,
                cancellationToken);

            if (!updateResult.Success)
                return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error updating header: {updateResult.Message}");

            result.HeaderCreated = false;
        }

        // 6 — resolve which KEY_PARAMETERs actually exist in DB for this MATNR+EDITION
        //     (after INSERT the trigger has already created them; after UPDATE they were there)
        const string sqlExistingParams = @"
            SELECT KEY_PARAMETER
            FROM   dbo.MAS_SPEC_MATNR_PARAMETERS
            WHERE  MATNR = @matnr AND EDITION = @edition AND IS_DELETED = 0";

        var existingParamsQuery = await _db.SelectAsync(sqlExistingParams,
            new[] { new SqlParameter("@matnr", matnr), new SqlParameter("@edition", edition) },
            cancellationToken);

        if (!existingParamsQuery.Success || existingParamsQuery.DTResults == null)
            return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error reading existing parameters: {existingParamsQuery.Message}");

        var dbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in existingParamsQuery.DTResults.Rows)
            dbKeys.Add(GetString(row, "KEY_PARAMETER").Trim());

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

        // 8 — persist parameter values
        if (updates.Count > 0)
        {
            var batchResult = await Specs_UpdateParametersBatch(
                matnr, edition, updates, userRequester, cancellationToken);

            if (!batchResult.Success)
                return OperationResult<SPEC_IMPORT_RESULT>.Fail($"Error updating parameters: {batchResult.Message}");
        }

        result.ParametersUpdated = updates.Count;
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
