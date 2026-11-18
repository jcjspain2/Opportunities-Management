using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;

namespace QUALITY_GATES.Classes;

// ════════════════════════════════════════════════════════════════════════════════════════
//  SPECS — Area 2: maintenance of existing specs (MAS_SPEC_MATNR_HEADER + _PARAMETERS)
//
//  Rules
//    - A spec has no status / close: it is maintained continuously; changes are traced by
//      the audit triggers (TR_MAS_SPEC_MATNR_HEADER_AUD / TR_MAS_SPEC_MATNR_PARAMETERS_AUD).
//    - Mandatory coverage (same as CIR): a TYPE_PARAMETER='M' parameter is informed when it
//      has VALUE_MIN, VALUE_TYP, VALUE_TEXT or VALUE_MAX (UM does not count).
//      Missing mandatory values are reported, never block a save.
//    - Only real changes are written (diff against DB) → clean audit.
//    - Every manual change marks the parameter DATA_SOURCE='MANUAL'. An external import never
//      overwrites a MANUAL parameter. Specs_ReleaseManualParameters gives it back to imports.
//    - Manual validation (blocks the save): MIN > MAX, numeric TYP + text TYP together,
//      UM not active in MAS_SPEC_UM (catalog of base units AND variants; only when the UM changes),
//      text / UM too long. Units are compared case-sensitively (mH ≠ MH).
//    - Write permission: @SPECS ("System" bypasses). Requires Docs/SQL_SPECS_MATNR_DataSource.sql.
// ════════════════════════════════════════════════════════════════════════════════════════

// ── Models — products ─────────────────────────────────────────────────────────────────

public class SPEC_MATNR_HEADER
{
    public string   Matnr           { get; set; } = string.Empty;
    public string   Edition         { get; set; } = string.Empty;
    public string   Description     { get; set; } = string.Empty;
    public string   ProductCategory { get; set; } = string.Empty; // → PRODUCT_CATEGOTY (typo in DB, correct here)
    public string   ProductFamily   { get; set; } = string.Empty;
    public string   CategoryName    { get; set; } = string.Empty; // read only (joined)
    public string   FamilyName      { get; set; } = string.Empty; // read only (joined)
    public string   SpecFilePath    { get; set; } = string.Empty;
    public DateOnly IssueDate       { get; set; } = DateOnly.MinValue;
    public string   CreatedBy       { get; set; } = string.Empty;
    public DateTime CreatedDate     { get; set; } = DateTime.MinValue;
    public bool     IsDeleted       { get; set; }

    // Mandatory coverage — filled by Specs_GetProductList / Specs_GetProductDetail
    public int  MandatoryTotal   { get; set; }
    public int  MandatoryFilled  { get; set; }
    public int  ManualParameters { get; set; }   // parameters with DATA_SOURCE = 'MANUAL'
    public bool IsComplete       => MandatoryFilled >= MandatoryTotal;
}

public class SPEC_MATNR_PARAMETER
{
    public string   Matnr                 { get; set; } = string.Empty;
    public string   Edition               { get; set; } = string.Empty;
    public string   KeyParameter          { get; set; } = string.Empty;
    public string   ParameterName         { get; set; } = string.Empty;
    public string   ParameterCategoryKey  { get; set; } = string.Empty;
    public string   ParameterCategoryName { get; set; } = string.Empty;
    public string   TypeParameter         { get; set; } = string.Empty; // M / O / I — from MAS_SPEC_FAMILY_VS_PARAMETER
    public decimal? ValueMax              { get; set; }
    public decimal? ValueTyp              { get; set; }
    public decimal? ValueMin              { get; set; }
    public string?  ValueText             { get; set; }                 // typical value as text (DB: VALUE_TEXT)
    public string   Um                    { get; set; } = string.Empty;
    public string   DefaultUm             { get; set; } = string.Empty;
    public string   DataSource            { get; set; } = "DOC";        // DOC = import · MANUAL = edited by a user
    public string   ModifiedBy            { get; set; } = string.Empty;
    public DateTime ModifiedDate          { get; set; } = DateTime.MinValue;
    public bool     IsDeleted             { get; set; }

    public bool IsMandatory => TypeParameter == "M";
    public bool IsManual    => DataSource == "MANUAL";
    public bool HasValue    => ValueMin.HasValue || ValueTyp.HasValue || ValueMax.HasValue
                               || !string.IsNullOrWhiteSpace(ValueText);
}

public class SPEC_MATNR_DETAIL
{
    public SPEC_MATNR_HEADER          Header     { get; set; } = new();
    public List<SPEC_MATNR_PARAMETER> Parameters { get; set; } = [];
    public bool                       CanEdit    { get; set; }  // user holds @SPECS and the product is active

    // Computed from Parameters — recalculated live while the UI edits values
    public int  MandatoryTotal  => Parameters.Count(p => p.IsMandatory);
    public int  MandatoryFilled => Parameters.Count(p => p.IsMandatory && p.HasValue);
    public bool IsComplete      => MandatoryFilled >= MandatoryTotal;
    public List<SPEC_MATNR_PARAMETER> MissingMandatory => Parameters.Where(p => p.IsMandatory && !p.HasValue).ToList();
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

/// <summary>Header audit row (snapshot of the header after the change).</summary>
public class SPEC_HEADER_AUDIT
{
    public int       AuditId         { get; set; }
    public string    Action          { get; set; } = string.Empty;  // I / U / D
    public string    ChangedBy       { get; set; } = string.Empty;
    public DateTime  ChangedDate     { get; set; }
    public string    Description     { get; set; } = string.Empty;
    public string    ProductCategory { get; set; } = string.Empty;
    public string    ProductFamily   { get; set; } = string.Empty;
    public string    SpecFilePath    { get; set; } = string.Empty;
    public DateOnly? IssueDate       { get; set; }
    public bool?     IsDeleted       { get; set; }
}

/// <summary>Parameter audit row (old → new).</summary>
public class SPEC_PARAMETER_AUDIT
{
    public int      AuditId       { get; set; }
    public string   Action        { get; set; } = string.Empty;  // I / U / D
    public string   ChangedBy     { get; set; } = string.Empty;
    public DateTime ChangedDate   { get; set; }
    public string   KeyParameter  { get; set; } = string.Empty;
    public string   ParameterName { get; set; } = string.Empty;
    public decimal? OldValueMax   { get; set; }
    public decimal? NewValueMax   { get; set; }
    public decimal? OldValueTyp   { get; set; }
    public decimal? NewValueTyp   { get; set; }
    public decimal? OldValueMin   { get; set; }
    public decimal? NewValueMin   { get; set; }
    public string   OldValueText  { get; set; } = string.Empty;
    public string   NewValueText  { get; set; } = string.Empty;
    public string   OldUm         { get; set; } = string.Empty;
    public string   NewUm         { get; set; } = string.Empty;
    public string   OldDataSource { get; set; } = string.Empty;
    public string   NewDataSource { get; set; } = string.Empty;
    public bool?    OldIsDeleted  { get; set; }
    public bool?    NewIsDeleted  { get; set; }
}

/// <summary>Change history of one product.</summary>
public class SPEC_PRODUCT_AUDIT
{
    public string                     Matnr            { get; set; } = string.Empty;
    public string                     Edition          { get; set; } = string.Empty;
    public List<SPEC_HEADER_AUDIT>    HeaderChanges    { get; set; } = [];
    public List<SPEC_PARAMETER_AUDIT> ParameterChanges { get; set; } = [];
}

// ── Business layer ────────────────────────────────────────────────────────────────────

public partial class Class_Projects_Quality_Gates
{
    private const string SPECS_SRC_DOC    = "DOC";
    private const string SPECS_SRC_MANUAL = "MANUAL";

    private const int SPECS_LEN_DESCRIPTION = 75;   // MAS_SPEC_MATNR_HEADER.DESCRIPTION nchar(75)
    private const int SPECS_LEN_FILE_PATH   = 200;  // SPEC_FILE_PATH nchar(200)
    private const int SPECS_LEN_VALUE_TEXT  = 200;  // MAS_SPEC_MATNR_PARAMETERS.VALUE_TEXT nvarchar(200)
    private const int SPECS_LEN_UM          = 20;   // UM nchar(20)

    // ══ Read ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Product list with mandatory coverage per product. Filters are optional.
    /// onlyIncomplete = true returns only products with at least one empty mandatory parameter.
    /// </summary>
    public async Task<OperationResult<List<SPEC_MATNR_HEADER>>> Specs_GetProductList(
        string? catKey = null,
        string? famKey = null,
        bool onlyIncomplete = false,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT h.MATNR, h.EDITION, h.DESCRIPTION, h.PRODUCT_CATEGOTY, h.PRODUCT_FAMILY,
                   h.SPEC_FILE_PATH, h.ISSUE_DATE, h.CREATED_BY, h.CREATED_DATE, h.IS_DELETED,
                   c.CAT_NAME, f.FAM_NAME,
                   ISNULL(cov.M_TOTAL, 0)      AS M_TOTAL,
                   ISNULL(cov.M_FILLED, 0)     AS M_FILLED,
                   ISNULL(cov.MANUAL_COUNT, 0) AS MANUAL_COUNT
            FROM   dbo.MAS_SPEC_MATNR_HEADER h
            LEFT JOIN dbo.MAS_SPEC_CATEGORY c ON c.CAT_KEY = h.PRODUCT_CATEGOTY
            LEFT JOIN dbo.MAS_SPEC_FAMILY   f ON f.CAT_KEY = h.PRODUCT_CATEGOTY AND f.FAM_KEY = h.PRODUCT_FAMILY
            OUTER APPLY (
                SELECT SUM(CASE WHEN fvp.TYPE_PARAMETER = 'M' THEN 1 ELSE 0 END) AS M_TOTAL,
                       SUM(CASE WHEN fvp.TYPE_PARAMETER = 'M'
                                 AND (p.VALUE_MIN IS NOT NULL OR p.VALUE_TYP IS NOT NULL OR p.VALUE_MAX IS NOT NULL
                                      OR NULLIF(LTRIM(RTRIM(p.VALUE_TEXT)), N'') IS NOT NULL)
                                THEN 1 ELSE 0 END) AS M_FILLED,
                       SUM(CASE WHEN p.DATA_SOURCE = N'MANUAL' THEN 1 ELSE 0 END) AS MANUAL_COUNT
                FROM   dbo.MAS_SPEC_MATNR_PARAMETERS p
                JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
                         ON fvp.FAM_KEY = h.PRODUCT_FAMILY AND fvp.KEY_PARAMETER = p.KEY_PARAMETER AND fvp.IS_DELETED = 0
                JOIN   dbo.MAS_SPEC_PARAMETER mp
                         ON mp.KEY_PARAMETER = p.KEY_PARAMETER AND mp.IS_DELETED = 0
                WHERE  p.MATNR = h.MATNR AND p.EDITION = h.EDITION AND p.IS_DELETED = 0
            ) cov
            WHERE  1 = 1";

        var parameters = new List<SqlParameter>();
        if (!includeDeleted) sql += " AND h.IS_DELETED = 0";
        if (!string.IsNullOrWhiteSpace(catKey))
        {
            sql += " AND h.PRODUCT_CATEGOTY = @catKey";
            parameters.Add(new SqlParameter("@catKey", catKey.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(famKey))
        {
            sql += " AND h.PRODUCT_FAMILY = @famKey";
            parameters.Add(new SqlParameter("@famKey", famKey.Trim()));
        }
        if (onlyIncomplete) sql += " AND ISNULL(cov.M_FILLED, 0) < ISNULL(cov.M_TOTAL, 0)";
        sql += " ORDER BY h.MATNR, h.EDITION";

        var q = await _db.SelectAsync(sql, parameters.ToArray(), cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_MATNR_HEADER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_MATNR_HEADER>();
        foreach (DataRow row in q.DTResults.Rows)
        {
            var h = MapSpecHeaderRow(row);
            h.MandatoryTotal   = GetInt(row, "M_TOTAL");
            h.MandatoryFilled  = GetInt(row, "M_FILLED");
            h.ManualParameters = GetInt(row, "MANUAL_COUNT");
            list.Add(h);
        }

        return OperationResult<List<SPEC_MATNR_HEADER>>.Ok(list);
    }

    /// <summary>
    /// Full product detail: header + active family parameters (M → O → I, then name) with
    /// category, data source and mandatory coverage. Pass userRequester to get CanEdit.
    /// </summary>
    public async Task<OperationResult<SPEC_MATNR_DETAIL>> Specs_GetProductDetail(
        string matnr,
        string edition,
        string? userRequester = null,
        CancellationToken cancellationToken = default)
    {
        var detail = await Specs_LoadProductAsync(matnr, edition, cancellationToken);
        if (!detail.Success) return detail;

        if (!string.IsNullOrWhiteSpace(userRequester) && !detail.Data.Header.IsDeleted)
            detail.Data.CanEdit = (await Specs_CheckPermission(SPECS_KEY_PROCESS, userRequester, cancellationToken)).Success;

        return detail;
    }

    /// <summary>Returns true if userId holds the @SPECS permission.</summary>
    public async Task<bool> Specs_CanUserEdit(string userId, CancellationToken cancellationToken = default)
        => (await Specs_CheckPermission(SPECS_KEY_PROCESS, userId, cancellationToken)).Success;

    /// <summary>Returns true if userId holds the @SPECS permission (same check as Specs_CanUserEdit).</summary>
    public async Task<bool> Specs_CanCreate(string userId, CancellationToken cancellationToken = default)
        => (await Specs_CheckPermission(SPECS_KEY_PROCESS, userId, cancellationToken)).Success;

    /// <summary>
    /// Change history of a product (newest first). Pass keyParameter to get only that
    /// parameter's history (header changes are then omitted).
    /// </summary>
    public async Task<OperationResult<SPEC_PRODUCT_AUDIT>> Specs_GetProductAudit(
        string matnr,
        string edition,
        string? keyParameter = null,
        CancellationToken cancellationToken = default)
    {
        var m = (matnr ?? string.Empty).Trim();
        var e = (edition ?? string.Empty).Trim();
        if (m.Length == 0 || e.Length == 0)
            return OperationResult<SPEC_PRODUCT_AUDIT>.Fail("MATNR and EDITION are required.");

        var result = new SPEC_PRODUCT_AUDIT { Matnr = m, Edition = e };

        if (string.IsNullOrWhiteSpace(keyParameter))
        {
            var hq = await _db.SelectAsync(@"
                SELECT AUDIT_ID, ACTION, CHANGED_BY, CHANGED_DATE, DESCRIPTION, PRODUCT_CATEGOTY,
                       PRODUCT_FAMILY, SPEC_FILE_PATH, ISSUE_DATE, IS_DELETED
                FROM   dbo.MAS_SPEC_MATNR_HEADER_AUDIT
                WHERE  MATNR = @m AND EDITION = @e
                ORDER BY CHANGED_DATE DESC, AUDIT_ID DESC",
                P2("@m", m, "@e", e), cancellationToken);
            if (!hq.Success || hq.DTResults == null)
                return OperationResult<SPEC_PRODUCT_AUDIT>.Fail($"Error reading header audit: {hq.Message}");

            foreach (DataRow r in hq.DTResults.Rows)
                result.HeaderChanges.Add(new SPEC_HEADER_AUDIT
                {
                    AuditId         = GetInt(r, "AUDIT_ID"),
                    Action          = GetString(r, "ACTION").Trim(),
                    ChangedBy       = GetString(r, "CHANGED_BY").Trim(),
                    ChangedDate     = GetDateTime(r, "CHANGED_DATE"),
                    Description     = GetString(r, "DESCRIPTION").Trim(),
                    ProductCategory = GetString(r, "PRODUCT_CATEGOTY").Trim(),
                    ProductFamily   = GetString(r, "PRODUCT_FAMILY").Trim(),
                    SpecFilePath    = GetString(r, "SPEC_FILE_PATH").Trim(),
                    IssueDate       = r["ISSUE_DATE"] == DBNull.Value ? null : DateOnly.FromDateTime(Convert.ToDateTime(r["ISSUE_DATE"])),
                    IsDeleted       = Specs_BoolOrNull(r, "IS_DELETED")
                });
        }

        string sqlParams = @"
            SELECT a.AUDIT_ID, a.ACTION, a.CHANGED_BY, a.CHANGED_DATE, a.KEY_PARAMETER, mp.PARAMETER_NAME,
                   a.OLD_VALUE_MAX, a.NEW_VALUE_MAX, a.OLD_VALUE_TYP, a.NEW_VALUE_TYP,
                   a.OLD_VALUE_MIN, a.NEW_VALUE_MIN, a.OLD_VALUE_TEXT, a.NEW_VALUE_TEXT,
                   a.OLD_UM, a.NEW_UM, a.OLD_DATA_SOURCE, a.NEW_DATA_SOURCE,
                   a.OLD_IS_DELETED, a.NEW_IS_DELETED
            FROM   dbo.MAS_SPEC_MATNR_PARAMETERS_AUDIT a
            LEFT JOIN dbo.MAS_SPEC_PARAMETER mp ON mp.KEY_PARAMETER = a.KEY_PARAMETER
            WHERE  a.MATNR = @m AND a.EDITION = @e";
        var pParams = new List<SqlParameter> { new("@m", m), new("@e", e) };
        if (!string.IsNullOrWhiteSpace(keyParameter))
        {
            sqlParams += " AND a.KEY_PARAMETER = @k";
            pParams.Add(new SqlParameter("@k", keyParameter.Trim()));
        }
        sqlParams += " ORDER BY a.CHANGED_DATE DESC, a.AUDIT_ID DESC";

        var pq = await _db.SelectAsync(sqlParams, pParams.ToArray(), cancellationToken);
        if (!pq.Success || pq.DTResults == null)
            return OperationResult<SPEC_PRODUCT_AUDIT>.Fail($"Error reading parameter audit: {pq.Message}");

        foreach (DataRow r in pq.DTResults.Rows)
            result.ParameterChanges.Add(new SPEC_PARAMETER_AUDIT
            {
                AuditId       = GetInt(r, "AUDIT_ID"),
                Action        = GetString(r, "ACTION").Trim(),
                ChangedBy     = GetString(r, "CHANGED_BY").Trim(),
                ChangedDate   = GetDateTime(r, "CHANGED_DATE"),
                KeyParameter  = GetString(r, "KEY_PARAMETER").Trim(),
                ParameterName = GetString(r, "PARAMETER_NAME").Trim(),
                OldValueMax   = GetDecimalNullable(r, "OLD_VALUE_MAX"),
                NewValueMax   = GetDecimalNullable(r, "NEW_VALUE_MAX"),
                OldValueTyp   = GetDecimalNullable(r, "OLD_VALUE_TYP"),
                NewValueTyp   = GetDecimalNullable(r, "NEW_VALUE_TYP"),
                OldValueMin   = GetDecimalNullable(r, "OLD_VALUE_MIN"),
                NewValueMin   = GetDecimalNullable(r, "NEW_VALUE_MIN"),
                OldValueText  = GetString(r, "OLD_VALUE_TEXT").Trim(),
                NewValueText  = GetString(r, "NEW_VALUE_TEXT").Trim(),
                OldUm         = GetString(r, "OLD_UM").Trim(),
                NewUm         = GetString(r, "NEW_UM").Trim(),
                OldDataSource = GetString(r, "OLD_DATA_SOURCE").Trim(),
                NewDataSource = GetString(r, "NEW_DATA_SOURCE").Trim(),
                OldIsDeleted  = Specs_BoolOrNull(r, "OLD_IS_DELETED"),
                NewIsDeleted  = Specs_BoolOrNull(r, "NEW_IS_DELETED")
            });

        return OperationResult<SPEC_PRODUCT_AUDIT>.Ok(result);
    }

    // ══ Write ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Main save of the spec editor. Compares <paramref name="product"/> with the DB and, in one
    /// transaction, writes only what changed: header (description, file path, issue date) and
    /// parameters (marked DATA_SOURCE='MANUAL'; UM saved as sent, "" → NULL).
    /// Validation errors block the save; missing mandatory values are only reported in Message.
    /// RecordsAffected = header (0/1) + parameters written.
    /// </summary>
    public async Task<SqlActionResult> Specs_SaveProductChanges(
        SPEC_MATNR_DETAIL product,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        if (product?.Header is null) return Specs_Fail("Product is required.");

        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var db = await Specs_LoadProductAsync(product.Header.Matnr, product.Header.Edition, cancellationToken);
        if (!db.Success) return Specs_Fail(db.ErrorMessage);
        var current = db.Data;
        if (current.Header.IsDeleted)
            return Specs_Fail($"Product '{current.Header.Matnr}/{current.Header.Edition}' is deleted. Enable it first.");

        // Header diff
        var headerPlan = Specs_BuildHeaderPlan(current.Header,
            product.Header.Description, product.Header.SpecFilePath,
            product.Header.IssueDate == DateOnly.MinValue ? null : product.Header.IssueDate);
        if (headerPlan.Error is { } eHdr) return Specs_Fail(eHdr);

        // Parameter diff + validation
        var units = await Specs_LoadActiveUnitsAsync(cancellationToken);
        if (!units.Success) return Specs_Fail(units.ErrorMessage);

        var plan = Specs_BuildParameterPlan(
            current.Parameters,
            (product.Parameters ?? []).Select(p => new SPEC_PARAM_UPDATE
            {
                KeyParameter = p.KeyParameter, ValueMax = p.ValueMax, ValueTyp = p.ValueTyp,
                ValueMin = p.ValueMin, ValueText = p.ValueText, Um = p.Um
            }),
            isImport: false, keepUmWhenEmpty: false, units.Data);
        if (plan.Errors.Count > 0) return Specs_Fail(string.Join(Environment.NewLine, plan.Errors));

        if (!headerPlan.Changed && plan.Changes.Count == 0)
            return Specs_NoChange("No changes to save.");

        int written = 0;
        var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            if (headerPlan.Changed)
                written += await Specs_WriteHeaderAsync(current.Header.Matnr, current.Header.Edition, headerPlan, t, ct);
            written += await Specs_WriteParameterChangesAsync(current.Header.Matnr, current.Header.Edition,
                           plan.Changes, SPECS_SRC_MANUAL, protectManual: false, userRequester, t, ct);
        }, cancellationToken);
        if (!tx.Success) return tx;

        var missing = (product.Parameters ?? []).Count(p => p.IsMandatory && !p.HasValue);
        return new SqlActionResult
        {
            Success         = true,
            RecordsAffected = written,
            Message         = $"Saved {written} change(s)."
                              + (missing > 0 ? $" {missing} mandatory parameter(s) still without value." : string.Empty)
        };
    }

    /// <summary>
    /// Updates the header fields of a product (description, file path, issue date) — only if they change.
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
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var db = await Specs_LoadProductAsync(matnr, edition, cancellationToken);
        if (!db.Success) return Specs_Fail(db.ErrorMessage);
        if (db.Data.Header.IsDeleted)
            return Specs_Fail($"Product '{db.Data.Header.Matnr}/{db.Data.Header.Edition}' is deleted. Enable it first.");

        var plan = Specs_BuildHeaderPlan(db.Data.Header, description, specFilePath,
                                         issueDate == DateOnly.MinValue ? null : issueDate);
        if (plan.Error is { } e) return Specs_Fail(e);
        if (!plan.Changed) return Specs_NoChange("No changes to save.");

        int written = 0;
        var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            written = await Specs_WriteHeaderAsync(db.Data.Header.Matnr, db.Data.Header.Edition, plan, t, ct);
        }, cancellationToken);
        if (tx.Success) tx.RecordsAffected = written;
        return tx;
    }

    /// <summary>
    /// Updates one parameter (manual edit → DATA_SOURCE='MANUAL'). Pass null/empty um to keep the current UM.
    /// </summary>
    public Task<SqlActionResult> Specs_UpdateParameterValue(
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
        => Specs_UpdateParametersBatch(matnr, edition,
               [new SPEC_PARAM_UPDATE { KeyParameter = keyParameter, ValueMax = valueMax, ValueTyp = valueTyp,
                                        ValueMin = valueMin, ValueText = valueText, Um = um }],
               userRequester, cancellationToken);

    /// <summary>
    /// Updates several parameters in one transaction (manual edit → DATA_SOURCE='MANUAL').
    /// Only parameters whose values really change are written. Null/empty Um keeps the current UM.
    /// RecordsAffected = parameters written.
    /// </summary>
    public async Task<SqlActionResult> Specs_UpdateParametersBatch(
        string matnr,
        string edition,
        List<SPEC_PARAM_UPDATE> updates,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);
        if (updates == null || updates.Count == 0) return Specs_NoChange("No changes to save.");

        var db = await Specs_LoadProductAsync(matnr, edition, cancellationToken);
        if (!db.Success) return Specs_Fail(db.ErrorMessage);
        if (db.Data.Header.IsDeleted)
            return Specs_Fail($"Product '{db.Data.Header.Matnr}/{db.Data.Header.Edition}' is deleted. Enable it first.");

        var units = await Specs_LoadActiveUnitsAsync(cancellationToken);
        if (!units.Success) return Specs_Fail(units.ErrorMessage);

        var plan = Specs_BuildParameterPlan(db.Data.Parameters, updates, isImport: false, keepUmWhenEmpty: true, units.Data);
        if (plan.Errors.Count > 0) return Specs_Fail(string.Join(Environment.NewLine, plan.Errors));
        if (plan.Changes.Count == 0) return Specs_NoChange("No changes to save.");

        int written = 0;
        var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            written = await Specs_WriteParameterChangesAsync(db.Data.Header.Matnr, db.Data.Header.Edition,
                          plan.Changes, SPECS_SRC_MANUAL, protectManual: false, userRequester, t, ct);
        }, cancellationToken);
        if (tx.Success) tx.RecordsAffected = written;
        return tx;
    }

    /// <summary>
    /// Removes the manual protection (DATA_SOURCE 'MANUAL' → 'DOC') so the next external import
    /// can overwrite those parameters again. keyParameters null/empty = every manual parameter of the product.
    /// Values are not changed. RecordsAffected = parameters released.
    /// </summary>
    public async Task<SqlActionResult> Specs_ReleaseManualParameters(
        string matnr,
        string edition,
        List<string>? keyParameters = null,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var state = await Specs_GetProductStateAsync(matnr, edition, cancellationToken);
        if (state.Error is { } e) return Specs_Fail(e);

        var sql = @"
            UPDATE dbo.MAS_SPEC_MATNR_PARAMETERS
            SET    DATA_SOURCE = @doc, MODIFIED_BY = @user, MODIFIED_DATE = GETDATE()
            WHERE  MATNR = @m AND EDITION = @e AND DATA_SOURCE = @manual";
        var keys = (keyParameters ?? []).Where(k => !string.IsNullOrWhiteSpace(k))
                                        .Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count > 0)
            sql += " AND KEY_PARAMETER IN (" + string.Join(", ", keys.Select((_, i) => $"@k{i}")) + ")";

        int released = 0;
        var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            var prms = new List<SqlParameter>
            {
                new("@m", state.Matnr), new("@e", state.Edition), new("@user", userRequester),
                new("@doc", SPECS_SRC_DOC), new("@manual", SPECS_SRC_MANUAL)
            };
            prms.AddRange(keys.Select((k, i) => new SqlParameter($"@k{i}", k)));

            var r = await _db.NonQueryAsync(sql, prms.ToArray(), transaction: t, ct: ct);
            if (!r.Success) throw new InvalidOperationException(r.Message);
            released = r.RecordsAffected;
        }, cancellationToken);

        if (!tx.Success) return tx;
        return released == 0
            ? Specs_NoChange("No manual parameters to release.")
            : new SqlActionResult { Success = true, RecordsAffected = released, Message = $"{released} parameter(s) released to imports." };
    }

    /// <summary>Soft-deletes a product (IS_DELETED = 1).</summary>
    public async Task<SqlActionResult> Specs_DeleteProduct(
        string matnr,
        string edition,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var state = await Specs_GetProductStateAsync(matnr, edition, cancellationToken);
        if (state.Error is { } e) return Specs_Fail(e);
        if (state.IsDeleted) return Specs_NoChange($"Product '{state.Matnr}/{state.Edition}' is already deleted.");

        return await Specs_SetProductDeletedAsync(state.Matnr, state.Edition, true, userRequester, cancellationToken);
    }

    /// <summary>Reactivates a deleted product. Its category and family must be active.</summary>
    public async Task<SqlActionResult> Specs_EnableProduct(
        string matnr,
        string edition,
        string userRequester = "System",
        CancellationToken cancellationToken = default)
    {
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } ePerm) return Specs_Fail(ePerm);

        var state = await Specs_GetProductStateAsync(matnr, edition, cancellationToken);
        if (state.Error is { } e) return Specs_Fail(e);
        if (!state.IsDeleted) return Specs_NoChange($"Product '{state.Matnr}/{state.Edition}' is already active.");

        if (await Specs_RequireActiveAsync(SQL_CAT_STATE, () => P("@k", state.Category), $"Category '{state.Category}'", cancellationToken) is { } e1)
            return Specs_Fail(e1);
        if (await Specs_RequireActiveAsync(SQL_FAM_STATE, () => P2("@c", state.Category, "@f", state.Family), $"Family '{state.Category}/{state.Family}'", cancellationToken) is { } e2)
            return Specs_Fail(e2);

        return await Specs_SetProductDeletedAsync(state.Matnr, state.Edition, false, userRequester, cancellationToken);
    }

    // ══ Private helpers — products ═══════════════════════════════════════════════════

    private static SPEC_MATNR_HEADER MapSpecHeaderRow(DataRow row) => new()
    {
        Matnr           = GetString(row, "MATNR").Trim(),
        Edition         = GetString(row, "EDITION").Trim(),
        Description     = GetString(row, "DESCRIPTION").Trim(),
        ProductCategory = GetString(row, "PRODUCT_CATEGOTY").Trim(),
        ProductFamily   = GetString(row, "PRODUCT_FAMILY").Trim(),
        CategoryName    = row.Table.Columns.Contains("CAT_NAME") ? GetString(row, "CAT_NAME").Trim() : string.Empty,
        FamilyName      = row.Table.Columns.Contains("FAM_NAME") ? GetString(row, "FAM_NAME").Trim() : string.Empty,
        SpecFilePath    = GetString(row, "SPEC_FILE_PATH").Trim(),
        IssueDate       = GetDateOnly(row, "ISSUE_DATE"),
        CreatedBy       = GetString(row, "CREATED_BY").Trim(),
        CreatedDate     = GetDateTime(row, "CREATED_DATE"),
        IsDeleted       = GetBoolean(row, "IS_DELETED")
    };

    private static bool? Specs_BoolOrNull(DataRow row, string column)
        => row[column] == DBNull.Value ? null : Convert.ToBoolean(row[column]);

    private static string? Specs_NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // decimal(18,4) in DB — round the same way so the diff does not see phantom changes
    private static decimal? Specs_Round4(decimal? value)
        => value.HasValue ? Math.Round(value.Value, 4, MidpointRounding.AwayFromZero) : null;

    /// <summary>Header + active family parameters (no permission check).</summary>
    private async Task<OperationResult<SPEC_MATNR_DETAIL>> Specs_LoadProductAsync(
        string matnr, string edition, CancellationToken ct)
    {
        var m = (matnr ?? string.Empty).Trim();
        var e = (edition ?? string.Empty).Trim();
        if (m.Length == 0 || e.Length == 0)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail("MATNR and EDITION are required.");

        var hq = await _db.SelectAsync(@"
            SELECT h.MATNR, h.EDITION, h.DESCRIPTION, h.PRODUCT_CATEGOTY, h.PRODUCT_FAMILY,
                   h.SPEC_FILE_PATH, h.ISSUE_DATE, h.CREATED_BY, h.CREATED_DATE, h.IS_DELETED,
                   c.CAT_NAME, f.FAM_NAME
            FROM   dbo.MAS_SPEC_MATNR_HEADER h
            LEFT JOIN dbo.MAS_SPEC_CATEGORY c ON c.CAT_KEY = h.PRODUCT_CATEGOTY
            LEFT JOIN dbo.MAS_SPEC_FAMILY   f ON f.CAT_KEY = h.PRODUCT_CATEGOTY AND f.FAM_KEY = h.PRODUCT_FAMILY
            WHERE  h.MATNR = @m AND h.EDITION = @e",
            P2("@m", m, "@e", e), ct);
        if (!hq.Success || hq.DTResults == null)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Error: {hq.Message}");
        if (hq.DTResults.Rows.Count == 0)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Product '{m}/{e}' not found.");

        var header = MapSpecHeaderRow(hq.DTResults.Rows[0]);

        var pq = await _db.SelectAsync(@"
            SELECT p.MATNR, p.EDITION, p.KEY_PARAMETER,
                   p.VALUE_MAX, p.VALUE_TYP, p.VALUE_MIN, p.VALUE_TEXT, p.UM, p.DATA_SOURCE,
                   p.MODIFIED_BY, p.MODIFIED_DATE, p.IS_DELETED,
                   mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM, mp.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME,
                   fvp.TYPE_PARAMETER
            FROM   dbo.MAS_SPEC_MATNR_PARAMETERS p
            JOIN   dbo.MAS_SPEC_PARAMETER mp
                     ON mp.KEY_PARAMETER = p.KEY_PARAMETER AND mp.IS_DELETED = 0
            JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
                     ON fvp.FAM_KEY = @fam AND fvp.KEY_PARAMETER = p.KEY_PARAMETER AND fvp.IS_DELETED = 0
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc ON pc.KEY_CATEGORY_PAR = mp.PARAMETER_CATEGORY
            WHERE  p.MATNR = @m AND p.EDITION = @e AND p.IS_DELETED = 0
            ORDER BY CASE fvp.TYPE_PARAMETER WHEN 'M' THEN 0 WHEN 'O' THEN 1 WHEN 'I' THEN 2 ELSE 3 END,
                     mp.PARAMETER_NAME",
            [new SqlParameter("@m", m), new SqlParameter("@e", e), new SqlParameter("@fam", header.ProductFamily)], ct);
        if (!pq.Success || pq.DTResults == null)
            return OperationResult<SPEC_MATNR_DETAIL>.Fail($"Error loading parameters: {pq.Message}");

        var detail = new SPEC_MATNR_DETAIL { Header = header };
        foreach (DataRow row in pq.DTResults.Rows)
            detail.Parameters.Add(new SPEC_MATNR_PARAMETER
            {
                Matnr                 = GetString(row, "MATNR").Trim(),
                Edition               = GetString(row, "EDITION").Trim(),
                KeyParameter          = GetString(row, "KEY_PARAMETER").Trim(),
                ParameterName         = GetString(row, "PARAMETER_NAME").Trim(),
                ParameterCategoryKey  = GetString(row, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(row, "CATEGORY_PAR_NAME").Trim(),
                TypeParameter         = GetString(row, "TYPE_PARAMETER").Trim(),
                ValueMax              = GetDecimalNullable(row, "VALUE_MAX"),
                ValueTyp              = GetDecimalNullable(row, "VALUE_TYP"),
                ValueMin              = GetDecimalNullable(row, "VALUE_MIN"),
                ValueText             = Specs_NullIfBlank(GetString(row, "VALUE_TEXT")),
                Um                    = GetString(row, "UM").Trim(),
                DefaultUm             = GetString(row, "PARAMETER_DEFAULT_UM").Trim(),
                DataSource            = GetString(row, "DATA_SOURCE").Trim(),
                ModifiedBy            = GetString(row, "MODIFIED_BY").Trim(),
                ModifiedDate          = GetDateTime(row, "MODIFIED_DATE"),
                IsDeleted             = GetBoolean(row, "IS_DELETED")
            });

        header.MandatoryTotal   = detail.MandatoryTotal;
        header.MandatoryFilled  = detail.MandatoryFilled;
        header.ManualParameters = detail.Parameters.Count(p => p.IsManual);
        return OperationResult<SPEC_MATNR_DETAIL>.Ok(detail);
    }

    private readonly record struct Specs_ProductState(
        string Matnr, string Edition, string Category, string Family, bool IsDeleted, string? Error);

    private async Task<Specs_ProductState> Specs_GetProductStateAsync(string matnr, string edition, CancellationToken ct)
    {
        var m = (matnr ?? string.Empty).Trim();
        var e = (edition ?? string.Empty).Trim();
        if (m.Length == 0 || e.Length == 0) return new(m, e, "", "", false, "MATNR and EDITION are required.");

        var q = await _db.SelectAsync(
            "SELECT IS_DELETED, PRODUCT_CATEGOTY, PRODUCT_FAMILY FROM dbo.MAS_SPEC_MATNR_HEADER WHERE MATNR = @m AND EDITION = @e",
            P2("@m", m, "@e", e), ct);
        if (!q.Success || q.DTResults == null) return new(m, e, "", "", false, $"Error: {q.Message}");
        if (q.DTResults.Rows.Count == 0)       return new(m, e, "", "", false, $"Product '{m}/{e}' not found.");

        var r = q.DTResults.Rows[0];
        return new(m, e, GetString(r, "PRODUCT_CATEGOTY").Trim(), GetString(r, "PRODUCT_FAMILY").Trim(),
                   GetBoolean(r, "IS_DELETED"), null);
    }

    private async Task<SqlActionResult> Specs_SetProductDeletedAsync(
        string matnr, string edition, bool deleted, string userRequester, CancellationToken cancellationToken)
    {
        return await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            var r = await _db.NonQueryAsync(
                "UPDATE dbo.MAS_SPEC_MATNR_HEADER SET IS_DELETED = @del WHERE MATNR = @m AND EDITION = @e",
                [new SqlParameter("@del", deleted), new SqlParameter("@m", matnr), new SqlParameter("@e", edition)],
                transaction: t, ct: ct);
            if (!r.Success) throw new InvalidOperationException(r.Message);
        }, cancellationToken);
    }

    private async Task<OperationResult<HashSet<string>>> Specs_LoadActiveUnitsAsync(CancellationToken ct)
    {
        var q = await _db.SelectAsync("SELECT UM_CODE FROM dbo.MAS_SPEC_UM WHERE IS_DELETED = 0", null, ct);
        if (!q.Success || q.DTResults == null)
            return OperationResult<HashSet<string>>.Fail($"Error reading units of measure: {q.Message}");

        var set = new HashSet<string>(StringComparer.Ordinal);   // units are case-sensitive (mH ≠ MH)
        foreach (DataRow r in q.DTResults.Rows) set.Add(GetString(r, "UM_CODE").Trim());
        return OperationResult<HashSet<string>>.Ok(set);
    }

    // ── Header plan ──────────────────────────────────────────────────────────────────

    private sealed class Specs_HeaderPlan
    {
        public bool      Changed      { get; set; }
        public string?   Error        { get; set; }
        public string    Description  { get; set; } = string.Empty;
        public string?   SpecFilePath { get; set; }
        public DateOnly? IssueDate    { get; set; }
    }

    private static Specs_HeaderPlan Specs_BuildHeaderPlan(
        SPEC_MATNR_HEADER current, string? description, string? specFilePath, DateOnly? issueDate)
    {
        var plan = new Specs_HeaderPlan
        {
            Description  = (description ?? string.Empty).Trim(),
            SpecFilePath = Specs_NullIfBlank(specFilePath),
            IssueDate    = issueDate
        };

        if (plan.Description.Length == 0)                    { plan.Error = "Description is required."; return plan; }
        if (plan.Description.Length > SPECS_LEN_DESCRIPTION) { plan.Error = $"Description exceeds {SPECS_LEN_DESCRIPTION} characters."; return plan; }
        if ((plan.SpecFilePath?.Length ?? 0) > SPECS_LEN_FILE_PATH) { plan.Error = $"Spec file path exceeds {SPECS_LEN_FILE_PATH} characters."; return plan; }

        var currentDate = current.IssueDate == DateOnly.MinValue ? (DateOnly?)null : current.IssueDate;
        plan.Changed = !string.Equals(plan.Description, current.Description, StringComparison.Ordinal)
                    || !string.Equals(plan.SpecFilePath, Specs_NullIfBlank(current.SpecFilePath), StringComparison.Ordinal)
                    || plan.IssueDate != currentDate;
        return plan;
    }

    private async Task<int> Specs_WriteHeaderAsync(string matnr, string edition, Specs_HeaderPlan plan, SqlTransaction tx, CancellationToken ct)
    {
        var r = await _db.NonQueryAsync(@"
            UPDATE dbo.MAS_SPEC_MATNR_HEADER
            SET    DESCRIPTION = @d, SPEC_FILE_PATH = @path, ISSUE_DATE = @date
            WHERE  MATNR = @m AND EDITION = @e",
            [
                new SqlParameter("@m", matnr), new SqlParameter("@e", edition),
                new SqlParameter("@d", plan.Description),
                new SqlParameter("@path", (object?)plan.SpecFilePath ?? DBNull.Value),
                new SqlParameter("@date", plan.IssueDate.HasValue ? plan.IssueDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value)
            ],
            transaction: tx, ct: ct);
        if (!r.Success) throw new InvalidOperationException(r.Message);
        return r.RecordsAffected;
    }

    // ── Parameter plan ───────────────────────────────────────────────────────────────

    private sealed class Specs_ParamPlan
    {
        public List<SPEC_PARAM_UPDATE> Changes   { get; } = [];  // final values to write
        public List<string>            Errors    { get; } = [];  // block the save (manual only)
        public List<string>            Warnings  { get; } = [];
        public int                     Protected { get; set; }   // import only: MANUAL parameters kept
        public int                     Unchanged { get; set; }
    }

    /// <summary>
    /// Compares the requested values with the current DB values and returns what must be written.
    /// isImport = true: no blocking validation (problems become warnings) and MANUAL parameters are protected.
    /// keepUmWhenEmpty = true: a null/empty Um keeps the current UM; false: it clears it.
    /// </summary>
    private static Specs_ParamPlan Specs_BuildParameterPlan(
        List<SPEC_MATNR_PARAMETER> current,
        IEnumerable<SPEC_PARAM_UPDATE> updates,
        bool isImport,
        bool keepUmWhenEmpty,
        HashSet<string> activeUnits)
    {
        var plan  = new Specs_ParamPlan();
        var byKey = current.ToDictionary(p => p.KeyParameter, StringComparer.OrdinalIgnoreCase);
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var upd in updates)
        {
            var key = (upd.KeyParameter ?? string.Empty).Trim();
            if (key.Length == 0 || !seen.Add(key)) continue;

            if (!byKey.TryGetValue(key, out var cur))
            {
                var msg = $"Parameter '{key}' is not an active parameter of this product.";
                if (isImport) plan.Warnings.Add(msg + " Skipped."); else plan.Errors.Add(msg);
                continue;
            }

            var max  = Specs_Round4(upd.ValueMax);
            var typ  = Specs_Round4(upd.ValueTyp);
            var min  = Specs_Round4(upd.ValueMin);
            var text = Specs_NullIfBlank(upd.ValueText);
            var um   = Specs_NullIfBlank(upd.Um) ?? (keepUmWhenEmpty ? Specs_NullIfBlank(cur.Um) : null);
            bool umChanged = !string.Equals(um, Specs_NullIfBlank(cur.Um), StringComparison.Ordinal);
            var label = $"'{cur.ParameterName}' ({key})";

            // Validation
            var issues = new List<string>();
            if (typ.HasValue && text != null)            issues.Add($"{label}: numeric typical value and text typical value cannot both be informed.");
            if (min.HasValue && max.HasValue && min > max) issues.Add($"{label}: MIN ({min}) is greater than MAX ({max}).");
            if ((text?.Length ?? 0) > SPECS_LEN_VALUE_TEXT) issues.Add($"{label}: text value exceeds {SPECS_LEN_VALUE_TEXT} characters.");
            if ((um?.Length ?? 0) > SPECS_LEN_UM)          issues.Add($"{label}: UM exceeds {SPECS_LEN_UM} characters.");

            if (isImport)
            {
                if (issues.Count > 0) { plan.Warnings.AddRange(issues.Select(i => i + " Skipped.")); continue; }
                if (um != null && umChanged && !activeUnits.Contains(um))
                    plan.Warnings.Add($"{label}: UM '{um}' is not an active unit of measure (saved as received).");
            }
            else
            {
                if (um != null && umChanged && !activeUnits.Contains(um))
                    issues.Add($"{label}: UM '{um}' does not exist or is disabled.");
                if (issues.Count > 0) { plan.Errors.AddRange(issues); continue; }
            }

            // Diff
            bool changed = cur.ValueMax != max || cur.ValueTyp != typ || cur.ValueMin != min
                        || !string.Equals(Specs_NullIfBlank(cur.ValueText), text, StringComparison.Ordinal)
                        || umChanged;
            if (!changed) { plan.Unchanged++; continue; }

            if (isImport && cur.IsManual)
            {
                plan.Protected++;
                plan.Warnings.Add($"{label}: manually edited value kept (import value ignored).");
                continue;
            }

            plan.Changes.Add(new SPEC_PARAM_UPDATE
            {
                KeyParameter = cur.KeyParameter, ValueMax = max, ValueTyp = typ, ValueMin = min, ValueText = text, Um = um
            });
        }

        return plan;
    }

    /// <summary>Writes the planned parameter changes. Must run inside a transaction. Returns rows written.</summary>
    private async Task<int> Specs_WriteParameterChangesAsync(
        string matnr, string edition, List<SPEC_PARAM_UPDATE> changes, string dataSource,
        bool protectManual, string userRequester, SqlTransaction tx, CancellationToken ct)
    {
        string sql = @"
            UPDATE dbo.MAS_SPEC_MATNR_PARAMETERS
            SET    VALUE_MAX = @max, VALUE_TYP = @typ, VALUE_MIN = @min, VALUE_TEXT = @text, UM = @um,
                   DATA_SOURCE = @src, MODIFIED_BY = @user, MODIFIED_DATE = GETDATE()
            WHERE  MATNR = @m AND EDITION = @e AND KEY_PARAMETER = @k AND IS_DELETED = 0";
        if (protectManual) sql += " AND DATA_SOURCE <> N'MANUAL'";   // never overwrite a manual value, even on a race

        int written = 0;
        foreach (var c in changes)
        {
            var r = await _db.NonQueryAsync(sql,
                [
                    new SqlParameter("@m", matnr), new SqlParameter("@e", edition), new SqlParameter("@k", c.KeyParameter),
                    new SqlParameter("@max",  (object?)c.ValueMax  ?? DBNull.Value),
                    new SqlParameter("@typ",  (object?)c.ValueTyp  ?? DBNull.Value),
                    new SqlParameter("@min",  (object?)c.ValueMin  ?? DBNull.Value),
                    new SqlParameter("@text", (object?)c.ValueText ?? DBNull.Value),
                    new SqlParameter("@um",   (object?)c.Um        ?? DBNull.Value),
                    new SqlParameter("@src",  dataSource),
                    new SqlParameter("@user", userRequester)
                ],
                transaction: tx, ct: ct);
            if (!r.Success) throw new InvalidOperationException($"Error updating parameter '{c.KeyParameter}': {r.Message}");
            written += r.RecordsAffected;
        }
        return written;
    }

    /// <summary>
    /// Import helper: on an existing product only fills header fields that are empty
    /// (description, spec file path, issue date) — manual header values are never overwritten.
    /// </summary>
    private async Task<SqlActionResult> Specs_ImportFillHeaderAsync(
        string matnr, string edition, string? description, string? specFilePath, DateOnly? issueDate,
        string userRequester, CancellationToken cancellationToken)
    {
        return await _db.ExecuteInTransactionAsync(async (t, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, t, ct);
            var r = await _db.NonQueryAsync(@"
                UPDATE dbo.MAS_SPEC_MATNR_HEADER
                SET    DESCRIPTION    = CASE WHEN LTRIM(RTRIM(DESCRIPTION)) = N'' AND @d IS NOT NULL THEN @d ELSE DESCRIPTION END,
                       SPEC_FILE_PATH = COALESCE(NULLIF(LTRIM(RTRIM(SPEC_FILE_PATH)), N''), @path),
                       ISSUE_DATE     = COALESCE(ISSUE_DATE, @date)
                WHERE  MATNR = @m AND EDITION = @e",
                [
                    new SqlParameter("@m", matnr), new SqlParameter("@e", edition),
                    new SqlParameter("@d",    (object?)Specs_NullIfBlank(description)  ?? DBNull.Value),
                    new SqlParameter("@path", (object?)Specs_NullIfBlank(specFilePath) ?? DBNull.Value),
                    new SqlParameter("@date", issueDate.HasValue ? issueDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value)
                ],
                transaction: t, ct: ct);
            if (!r.Success) throw new InvalidOperationException(r.Message);
        }, cancellationToken);
    }
}
