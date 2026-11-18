using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;

namespace QUALITY_GATES.Classes;

// ════════════════════════════════════════════════════════════════════════════════════════
//  SPECS — Area 1: master data maintenance
//    MAS_SPEC_UM · MAS_SPEC_PARAMETER_CATEGORY · MAS_SPEC_PARAMETER
//    MAS_SPEC_CATEGORY · MAS_SPEC_FAMILY · MAS_SPEC_FAMILY_VS_PARAMETER · MAS_SPEC_UM_CONVERSION
//
//  Rules
//    - Delete = soft delete (IS_DELETED = 1). Enable* reactivates.
//    - Keys: required, trimmed, upper-case, max 10 chars (nchar(10)).
//      Unit codes are the exception: exact spelling kept (mH ≠ MH, µ ≠ μ), compared case-sensitively.
//    - Add of an existing key fails; if the key exists disabled → use Enable*.
//    - Add / Enable require the referenced parent to exist and be active.
//    - Disable is blocked while active children use the row
//      (exception: Specs_DisableMasterParameter cascades to its family links).
//    - Every read trims nchar padding (CATEGORY_PAR_NAME also carries a trailing \r\n).
//    - Write permission: key process @SPECS (MAS_JOB_TITLES_RESTRICTIONS). "System" bypasses.
// ════════════════════════════════════════════════════════════════════════════════════════

// ── Models — catalog ──────────────────────────────────────────────────────────────────

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

public class SPEC_PARAMETER_CATEGORY
{
    public string CategoryKey  { get; set; } = string.Empty;  // KEY_CATEGORY_PAR
    public string CategoryName { get; set; } = string.Empty;  // CATEGORY_PAR_NAME
    public bool   IsDeleted    { get; set; }
}

public class SPEC_PARAMETER
{
    public string KeyParameter          { get; set; } = string.Empty;
    public string ParameterName         { get; set; } = string.Empty;
    public string ParameterDefaultUm    { get; set; } = string.Empty;
    public string ParameterCategoryKey  { get; set; } = string.Empty;  // PARAMETER_CATEGORY
    public string ParameterCategoryName { get; set; } = string.Empty;  // read only (joined)
    public bool   IsDeleted             { get; set; }
}

public class SPEC_FAMILY_PARAMETER
{
    public string FamKey                { get; set; } = string.Empty;
    public string KeyParameter          { get; set; } = string.Empty;
    public string ParameterName         { get; set; } = string.Empty;
    public string DefaultUm             { get; set; } = string.Empty;
    public string ParameterCategoryKey  { get; set; } = string.Empty;
    public string ParameterCategoryName { get; set; } = string.Empty;
    public string TypeParameter         { get; set; } = string.Empty; // M=Mandatory / O=Optional / I=Informative
    public bool   IsDeleted             { get; set; }
}

public class SPEC_UNIT
{
    public string UmCode        { get; set; } = string.Empty;
    public string UmDescription { get; set; } = string.Empty;
    public bool   IsDeleted     { get; set; }   // requires Docs/SQL_SPECS_UM_IsDeleted.sql

    // Conversion info (read only) — a unit is a VARIANT when it is the UM_FROM of an active conversion
    public bool     IsBase   { get; set; } = true;
    public string   BaseUnit { get; set; } = string.Empty;  // = UmCode when IsBase
    public decimal  Factor   { get; set; } = 1m;            // value_in_BaseUnit = value × Factor
}

/// <summary>MAS_SPEC_UM_CONVERSION row — a unit variant and how to convert it to its base unit.</summary>
public class SPEC_UNIT_CONVERSION
{
    public string   UmFrom          { get; set; } = string.Empty;  // unit as found in the data (case-sensitive)
    public string   UmTo            { get; set; } = string.Empty;  // base unit (MAS_SPEC_UM.UM_CODE)
    public string   UmToDescription { get; set; } = string.Empty;  // read only (joined)
    public decimal  Factor          { get; set; }                  // value_in_UmTo = value × Factor
    public bool     IsDeleted       { get; set; }
    public string   CreatedBy       { get; set; } = string.Empty;
    public DateTime CreatedDate     { get; set; }
}

// ── Business layer ────────────────────────────────────────────────────────────────────

public partial class Class_Projects_Quality_Gates
{
    private const int    SPECS_KEY_MAX         = 10;

    // Column lengths (characters) — from the live schema
    private const int SPECS_LEN_CAT_NAME     = 75;
    private const int SPECS_LEN_FAM_NAME     = 75;
    private const int SPECS_LEN_PARAM_NAME   = 75;
    private const int SPECS_LEN_PARCAT_NAME  = 50;
    private const int SPECS_LEN_UM_DESC      = 75;
    private const int SPECS_LEN_UM_CODE      = 20;   // MAS_SPEC_UM.UM_CODE / conversion UM_FROM, UM_TO nvarchar(20)
    private const int SPECS_LEN_DEFAULT_UM   = 10;   // MAS_SPEC_PARAMETER.PARAMETER_DEFAULT_UM nchar(10)

    private static readonly string[] SPECS_PARAMETER_TYPES = ["M", "O", "I"];

    // ══ Read ══════════════════════════════════════════════════════════════════════════

    /// <summary>Parameter categories (MAS_SPEC_PARAMETER_CATEGORY), ordered by name.</summary>
    public async Task<OperationResult<List<SPEC_PARAMETER_CATEGORY>>> Specs_GetParameterCategories(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT KEY_CATEGORY_PAR, CATEGORY_PAR_NAME, IS_DELETED
            FROM   dbo.MAS_SPEC_PARAMETER_CATEGORY";
        if (!includeDeleted) sql += " WHERE IS_DELETED = 0";
        sql += " ORDER BY CATEGORY_PAR_NAME";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_PARAMETER_CATEGORY>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_PARAMETER_CATEGORY>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_PARAMETER_CATEGORY
            {
                CategoryKey  = GetString(row, "KEY_CATEGORY_PAR").Trim(),
                CategoryName = GetString(row, "CATEGORY_PAR_NAME").Trim(),
                IsDeleted    = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_PARAMETER_CATEGORY>>.Ok(list);
    }

    /// <summary>Product categories (MAS_SPEC_CATEGORY), ordered by name.</summary>
    public async Task<OperationResult<List<SPEC_CATEGORY>>> Specs_GetCategories(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT CAT_KEY, CAT_NAME, IS_DELETED
            FROM   dbo.MAS_SPEC_CATEGORY";
        if (!includeDeleted) sql += " WHERE IS_DELETED = 0";
        sql += " ORDER BY CAT_NAME";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_CATEGORY>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_CATEGORY>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_CATEGORY
            {
                CatKey    = GetString(row, "CAT_KEY").Trim(),
                CatName   = GetString(row, "CAT_NAME").Trim(),
                IsDeleted = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_CATEGORY>>.Ok(list);
    }

    /// <summary>Families (MAS_SPEC_FAMILY). Optional filter by category.</summary>
    public async Task<OperationResult<List<SPEC_FAMILY>>> Specs_GetFamilies(
        string? catKey = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT CAT_KEY, FAM_KEY, FAM_NAME, IS_DELETED
            FROM   dbo.MAS_SPEC_FAMILY
            WHERE  1 = 1";
        var parameters = new List<SqlParameter>();

        if (!includeDeleted) sql += " AND IS_DELETED = 0";
        if (!string.IsNullOrWhiteSpace(catKey))
        {
            sql += " AND CAT_KEY = @catKey";
            parameters.Add(new SqlParameter("@catKey", catKey.Trim()));
        }
        sql += " ORDER BY FAM_NAME";

        var q = await _db.SelectAsync(sql, parameters.ToArray(), cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_FAMILY>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_FAMILY>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_FAMILY
            {
                CatKey    = GetString(row, "CAT_KEY").Trim(),
                FamKey    = GetString(row, "FAM_KEY").Trim(),
                FamName   = GetString(row, "FAM_NAME").Trim(),
                IsDeleted = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_FAMILY>>.Ok(list);
    }

    /// <summary>
    /// Master parameter catalog (MAS_SPEC_PARAMETER) with its parameter category.
    /// Optional filter by parameter category.
    /// </summary>
    public async Task<OperationResult<List<SPEC_PARAMETER>>> Specs_GetMasterParameters(
        bool includeDeleted = false,
        string? parameterCategoryKey = null,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT p.KEY_PARAMETER, p.PARAMETER_NAME, p.PARAMETER_DEFAULT_UM, p.IS_DELETED,
                   p.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME
            FROM   dbo.MAS_SPEC_PARAMETER p
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc ON pc.KEY_CATEGORY_PAR = p.PARAMETER_CATEGORY
            WHERE  1 = 1";
        var parameters = new List<SqlParameter>();

        if (!includeDeleted) sql += " AND p.IS_DELETED = 0";
        if (!string.IsNullOrWhiteSpace(parameterCategoryKey))
        {
            sql += " AND p.PARAMETER_CATEGORY = @parCat";
            parameters.Add(new SqlParameter("@parCat", parameterCategoryKey.Trim()));
        }
        sql += " ORDER BY p.PARAMETER_NAME";

        var q = await _db.SelectAsync(sql, parameters.ToArray(), cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_PARAMETER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_PARAMETER>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_PARAMETER
            {
                KeyParameter          = GetString(row, "KEY_PARAMETER").Trim(),
                ParameterName         = GetString(row, "PARAMETER_NAME").Trim(),
                ParameterDefaultUm    = GetString(row, "PARAMETER_DEFAULT_UM").Trim(),
                ParameterCategoryKey  = GetString(row, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(row, "CATEGORY_PAR_NAME").Trim(),
                IsDeleted             = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_PARAMETER>>.Ok(list);
    }

    /// <summary>
    /// Parameters linked to a family (MAS_SPEC_FAMILY_VS_PARAMETER) with name, default UM,
    /// parameter category and type (M/O/I).
    /// </summary>
    public async Task<OperationResult<List<SPEC_FAMILY_PARAMETER>>> Specs_GetFamilyParameters(
        string famKey,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT fvp.FAM_KEY, fvp.KEY_PARAMETER, fvp.TYPE_PARAMETER, fvp.IS_DELETED,
                   p.PARAMETER_NAME, p.PARAMETER_DEFAULT_UM, p.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME
            FROM   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
            JOIN   dbo.MAS_SPEC_PARAMETER p ON p.KEY_PARAMETER = fvp.KEY_PARAMETER
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc ON pc.KEY_CATEGORY_PAR = p.PARAMETER_CATEGORY
            WHERE  fvp.FAM_KEY = @famKey";
        if (!includeDeleted) sql += " AND fvp.IS_DELETED = 0";
        sql += " ORDER BY fvp.TYPE_PARAMETER, p.PARAMETER_NAME";

        var q = await _db.SelectAsync(sql,
            new[] { new SqlParameter("@famKey", (famKey ?? string.Empty).Trim()) }, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_FAMILY_PARAMETER>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_FAMILY_PARAMETER>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_FAMILY_PARAMETER
            {
                FamKey                = GetString(row, "FAM_KEY").Trim(),
                KeyParameter          = GetString(row, "KEY_PARAMETER").Trim(),
                ParameterName         = GetString(row, "PARAMETER_NAME").Trim(),
                DefaultUm             = GetString(row, "PARAMETER_DEFAULT_UM").Trim(),
                ParameterCategoryKey  = GetString(row, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(row, "CATEGORY_PAR_NAME").Trim(),
                TypeParameter         = GetString(row, "TYPE_PARAMETER").Trim(),
                IsDeleted             = GetBoolean(row, "IS_DELETED")
            });

        return OperationResult<List<SPEC_FAMILY_PARAMETER>>.Ok(list);
    }

    /// <summary>
    /// Units of measure (MAS_SPEC_UM = catalog of every valid unit, base and variants), ordered by code.
    /// Each unit says whether it is a base unit or a variant (BaseUnit + Factor from the active conversion).
    /// onlyBase = true returns only base units (e.g. to choose the UM_TO of a conversion).
    /// </summary>
    public async Task<OperationResult<List<SPEC_UNIT>>> Specs_GetUnits(
        bool includeDeleted = false,
        bool onlyBase = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT u.UM_CODE, u.UM_DESCRIPTION, u.IS_DELETED, c.UM_TO, c.FACTOR
            FROM   dbo.MAS_SPEC_UM u
            LEFT JOIN dbo.MAS_SPEC_UM_CONVERSION c ON c.UM_FROM = u.UM_CODE AND c.IS_DELETED = 0
            WHERE  1 = 1";
        if (!includeDeleted) sql += " AND u.IS_DELETED = 0";
        if (onlyBase)        sql += " AND c.UM_FROM IS NULL";
        sql += " ORDER BY u.UM_CODE";

        var q = await _db.SelectAsync(sql, null, cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_UNIT>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_UNIT>();
        foreach (DataRow row in q.DTResults.Rows)
        {
            var code   = GetString(row, "UM_CODE").Trim();
            var baseUm = GetString(row, "UM_TO").Trim();
            list.Add(new SPEC_UNIT
            {
                UmCode        = code,
                UmDescription = GetString(row, "UM_DESCRIPTION").Trim(),
                IsDeleted     = GetBoolean(row, "IS_DELETED"),
                IsBase        = baseUm.Length == 0,
                BaseUnit      = baseUm.Length == 0 ? code : baseUm,
                Factor        = baseUm.Length == 0 ? 1m : GetDecimalNullable(row, "FACTOR") ?? 1m
            });
        }

        return OperationResult<List<SPEC_UNIT>>.Ok(list);
    }

    // ══ Write — Units of measure (MAS_SPEC_UM) ═══════════════════════════════════════

    public async Task<SqlActionResult> Specs_AddUnit(
        string umCode, string umDescription,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umCode, "Unit code", out var code, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(umDescription, "Unit description", SPECS_LEN_UM_DESC, out var desc) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireNewKeyAsync(SQL_UM_STATE, () => P("@k", code), $"Unit '{code}'", cancellationToken) is { } e4) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_UM (UM_CODE, UM_DESCRIPTION, IS_DELETED) VALUES (@k, @desc, 0)",
            [new SqlParameter("@k", code), new SqlParameter("@desc", desc)], ct: cancellationToken);
    }

    public async Task<SqlActionResult> Specs_UpdateUnit(
        string umCode, string umDescription,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umCode, "Unit code", out var code, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(umDescription, "Unit description", SPECS_LEN_UM_DESC, out var desc) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireExistsAsync(SQL_UM_STATE, () => P("@k", code), $"Unit '{code}'", cancellationToken) is { Error: { } e4 }) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_UM SET UM_DESCRIPTION = @desc WHERE UM_CODE = @k",
            [new SqlParameter("@k", code), new SqlParameter("@desc", desc)], ct: cancellationToken);
    }

    /// <summary>Soft delete. Blocked while an active master parameter uses it as default UM.</summary>
    public async Task<SqlActionResult> Specs_DisableUnit(
        string umCode,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umCode, "Unit code", out var code, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_UM_STATE, () => P("@k", code), $"Unit '{code}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (state.IsDeleted) return Specs_NoChange($"Unit '{code}' is already disabled.");

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_PARAMETER WHERE PARAMETER_DEFAULT_UM COLLATE Latin1_General_100_CS_AS = @k AND IS_DELETED = 0",
                () => P("@k", code),
                n => $"Unit '{code}' is the default UM of {n} active parameter(s). Change them first.",
                cancellationToken) is { } e4) return Specs_Fail(e4);

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_TO COLLATE Latin1_General_100_CS_AS = @k AND IS_DELETED = 0",
                () => P("@k", code),
                n => $"Unit '{code}' is the base unit of {n} active conversion(s). Disable them first.",
                cancellationToken) is { } e5) return Specs_Fail(e5);

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_FROM = @k AND IS_DELETED = 0",
                () => P("@k", code),
                _ => $"Unit '{code}' is a variant with an active conversion. Disable the conversion first.",
                cancellationToken) is { } e6) return Specs_Fail(e6);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_UM", "UM_CODE = @k", true, P("@k", code), cancellationToken);
    }

    /// <summary>Kept for compatibility with existing callers — now a soft delete (same as Specs_DisableUnit).</summary>
    public Task<SqlActionResult> Specs_DeleteUnit(
        string umCode,
        string userRequester = "System", CancellationToken cancellationToken = default)
        => Specs_DisableUnit(umCode, userRequester, cancellationToken);

    public async Task<SqlActionResult> Specs_EnableUnit(
        string umCode,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umCode, "Unit code", out var code, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_UM_STATE, () => P("@k", code), $"Unit '{code}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (!state.IsDeleted) return Specs_NoChange($"Unit '{code}' is already active.");

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_UM", "UM_CODE = @k", false, P("@k", code), cancellationToken);
    }

    // ══ Unit conversions (MAS_SPEC_UM_CONVERSION) ════════════════════════════════════
    //   Both units must exist (active) in MAS_SPEC_UM — FK on UM_FROM and UM_TO.
    //   UM_FROM : the variant (case- and symbol-sensitive, never upper-cased)
    //   UM_TO   : the base unit
    //   FACTOR  : value_in_UM_TO = value × FACTOR  (> 0)
    //   No chains: UM_TO cannot itself be the variant of an active conversion,
    //              and UM_FROM cannot be the base of other active conversions.

    /// <summary>Unit conversions, ordered by base unit and factor. Optional filter by base unit.</summary>
    public async Task<OperationResult<List<SPEC_UNIT_CONVERSION>>> Specs_GetUnitConversions(
        string? umTo = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        string sql = @"
            SELECT c.UM_FROM, c.UM_TO, u.UM_DESCRIPTION, c.FACTOR, c.IS_DELETED, c.CREATED_BY, c.CREATED_DATE
            FROM   dbo.MAS_SPEC_UM_CONVERSION c
            LEFT JOIN dbo.MAS_SPEC_UM u ON u.UM_CODE = c.UM_TO
            WHERE  1 = 1";
        var parameters = new List<SqlParameter>();
        if (!includeDeleted) sql += " AND c.IS_DELETED = 0";
        if (!string.IsNullOrWhiteSpace(umTo))
        {
            sql += " AND c.UM_TO COLLATE Latin1_General_100_CS_AS = @umTo";
            parameters.Add(new SqlParameter("@umTo", umTo.Trim()));
        }
        sql += " ORDER BY c.UM_TO, c.FACTOR, c.UM_FROM";

        var q = await _db.SelectAsync(sql, parameters.ToArray(), cancellationToken);
        if (!q.Success || q.DTResults == null)
            return OperationResult<List<SPEC_UNIT_CONVERSION>>.Fail($"Error: {q.Message}");

        var list = new List<SPEC_UNIT_CONVERSION>();
        foreach (DataRow row in q.DTResults.Rows)
            list.Add(new SPEC_UNIT_CONVERSION
            {
                UmFrom          = GetString(row, "UM_FROM").Trim(),
                UmTo            = GetString(row, "UM_TO").Trim(),
                UmToDescription = GetString(row, "UM_DESCRIPTION").Trim(),
                Factor          = GetDecimalNullable(row, "FACTOR") ?? 0m,
                IsDeleted       = GetBoolean(row, "IS_DELETED"),
                CreatedBy       = GetString(row, "CREATED_BY").Trim(),
                CreatedDate     = GetDateTime(row, "CREATED_DATE")
            });

        return OperationResult<List<SPEC_UNIT_CONVERSION>>.Ok(list);
    }

    /// <summary>
    /// Adds a conversion UM_FROM → UM_TO × FACTOR. Both units must exist and be active in MAS_SPEC_UM
    /// (add the variant with Specs_AddUnit first). No chains (see rules above).
    /// If the conversion exists disabled → enable it instead.
    /// </summary>
    public async Task<SqlActionResult> Specs_AddUnitConversion(
        string umFrom, string umTo, decimal factor,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umFrom, "Unit variant (UM_FROM)", out var from, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateUnitCode(umTo, "Base unit (UM_TO)", out var to, SPECS_LEN_UM_CODE) is { } e2) return Specs_Fail(e2);
        if (factor <= 0) return Specs_Fail("Factor must be greater than 0.");
        if (string.Equals(from, to, StringComparison.Ordinal)) return Specs_Fail("UM_FROM and UM_TO cannot be the same unit.");
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        if (await Specs_RequireNewKeyAsync(SQL_UMCONV_STATE, () => P("@k", from), $"Conversion '{from}'", cancellationToken) is { } e4) return Specs_Fail(e4);
        if (await Specs_CheckConversionPairAsync(from, to, checkFrom: true, cancellationToken) is { } e5) return Specs_Fail(e5);

        return await _db.NonQueryAsync(@"
            INSERT INTO dbo.MAS_SPEC_UM_CONVERSION (UM_FROM, UM_TO, FACTOR, IS_DELETED, CREATED_BY, CREATED_DATE)
            VALUES (@from, @to, @factor, 0, @user, SYSDATETIME())",
            [new SqlParameter("@from", from), new SqlParameter("@to", to),
             new SqlParameter("@factor", SqlDbType.Decimal) { Precision = 28, Scale = 12, Value = factor },
             new SqlParameter("@user", userRequester)], ct: cancellationToken);
    }

    /// <summary>Changes the base unit and/or factor of an existing conversion. A changed UM_TO must be an active base unit.</summary>
    public async Task<SqlActionResult> Specs_UpdateUnitConversion(
        string umFrom, string umTo, decimal factor,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umFrom, "Unit variant (UM_FROM)", out var from, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateUnitCode(umTo, "Base unit (UM_TO)", out var to, SPECS_LEN_UM_CODE) is { } e2) return Specs_Fail(e2);
        if (factor <= 0) return Specs_Fail("Factor must be greater than 0.");
        if (string.Equals(from, to, StringComparison.Ordinal)) return Specs_Fail("UM_FROM and UM_TO cannot be the same unit.");
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        var q = await _db.SelectAsync("SELECT UM_TO, FACTOR FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_FROM = @k", P("@k", from), cancellationToken);
        if (!q.Success || q.DTResults == null) return Specs_Fail($"Error reading conversion '{from}': {q.Message}");
        if (q.DTResults.Rows.Count == 0) return Specs_Fail($"Conversion '{from}' does not exist.");

        var currentTo     = GetString(q.DTResults.Rows[0], "UM_TO").Trim();
        var currentFactor = GetDecimalNullable(q.DTResults.Rows[0], "FACTOR") ?? 0m;
        bool toChanged    = !string.Equals(to, currentTo, StringComparison.Ordinal);

        if (!toChanged && currentFactor == factor) return Specs_NoChange($"Conversion '{from}' has no changes.");

        if (toChanged &&
            await Specs_CheckConversionPairAsync(from, to, checkFrom: false, cancellationToken) is { } e4)
            return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_UM_CONVERSION SET UM_TO = @to, FACTOR = @factor WHERE UM_FROM = @from",
            [new SqlParameter("@from", from), new SqlParameter("@to", to),
             new SqlParameter("@factor", SqlDbType.Decimal) { Precision = 28, Scale = 12, Value = factor }], ct: cancellationToken);
    }

    /// <summary>Soft-deletes a conversion: values in that unit are no longer converted (compared only with the same unit).</summary>
    public async Task<SqlActionResult> Specs_DisableUnitConversion(
        string umFrom,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umFrom, "Unit variant (UM_FROM)", out var from, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_UMCONV_STATE, () => P("@k", from), $"Conversion '{from}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (state.IsDeleted) return Specs_NoChange($"Conversion '{from}' is already disabled.");

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_UM_CONVERSION", "UM_FROM = @k", true, P("@k", from), cancellationToken);
    }

    /// <summary>Reactivates a conversion. Both units must be active and the no-chain rule must hold.</summary>
    public async Task<SqlActionResult> Specs_EnableUnitConversion(
        string umFrom,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateUnitCode(umFrom, "Unit variant (UM_FROM)", out var from, SPECS_LEN_UM_CODE) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var q = await _db.SelectAsync("SELECT UM_TO, IS_DELETED FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_FROM = @k", P("@k", from), cancellationToken);
        if (!q.Success || q.DTResults == null) return Specs_Fail($"Error reading conversion '{from}': {q.Message}");
        if (q.DTResults.Rows.Count == 0) return Specs_Fail($"Conversion '{from}' does not exist.");
        if (!GetBoolean(q.DTResults.Rows[0], "IS_DELETED")) return Specs_NoChange($"Conversion '{from}' is already active.");

        var to = GetString(q.DTResults.Rows[0], "UM_TO").Trim();
        if (await Specs_CheckConversionPairAsync(from, to, checkFrom: true, cancellationToken) is { } e3)
            return Specs_Fail(e3);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_UM_CONVERSION", "UM_FROM = @k", false, P("@k", from), cancellationToken);
    }

    /// <summary>
    /// Validates a conversion pair: UM_TO active in the master and not a variant itself;
    /// when checkFrom, UM_FROM active in the master and not the base of other active conversions.
    /// </summary>
    private async Task<string?> Specs_CheckConversionPairAsync(string from, string to, bool checkFrom, CancellationToken ct)
    {
        if (await Specs_RequireActiveAsync(SQL_UM_STATE, () => P("@k", to), $"Base unit '{to}'", ct) is { } e1) return e1;
        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_FROM = @k AND IS_DELETED = 0",
                () => P("@k", to),
                _ => $"'{to}' is itself a variant (it has an active conversion) and cannot be a base unit.",
                ct) is { } e2) return e2;

        if (!checkFrom) return null;

        if (await Specs_RequireActiveAsync(SQL_UM_STATE, () => P("@k", from), $"Unit '{from}'", ct) is { } e3)
            return e3 + " Add it to the unit master first (Specs_AddUnit).";
        return await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_TO = @k AND IS_DELETED = 0",
                () => P("@k", from),
                n => $"'{from}' is the base unit of {n} active conversion(s) and cannot be a variant.",
                ct);
    }

    // ══ Write — Parameter categories (MAS_SPEC_PARAMETER_CATEGORY) ═══════════════════

    public async Task<SqlActionResult> Specs_AddParameterCategory(
        string categoryKey, string categoryName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(categoryKey, "Parameter category key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(categoryName, "Parameter category name", SPECS_LEN_PARCAT_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireNewKeyAsync(SQL_PARCAT_STATE, () => P("@k", key), $"Parameter category '{key}'", cancellationToken) is { } e4) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_PARAMETER_CATEGORY (KEY_CATEGORY_PAR, CATEGORY_PAR_NAME, IS_DELETED) VALUES (@k, @name, 0)",
            [new SqlParameter("@k", key), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    public async Task<SqlActionResult> Specs_UpdateParameterCategory(
        string categoryKey, string categoryName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(categoryKey, "Parameter category key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(categoryName, "Parameter category name", SPECS_LEN_PARCAT_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireExistsAsync(SQL_PARCAT_STATE, () => P("@k", key), $"Parameter category '{key}'", cancellationToken) is { Error: { } e4 }) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_PARAMETER_CATEGORY SET CATEGORY_PAR_NAME = @name WHERE KEY_CATEGORY_PAR = @k",
            [new SqlParameter("@k", key), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    /// <summary>Soft delete. Blocked while active master parameters belong to the category.</summary>
    public async Task<SqlActionResult> Specs_DisableParameterCategory(
        string categoryKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(categoryKey, "Parameter category key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_PARCAT_STATE, () => P("@k", key), $"Parameter category '{key}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (state.IsDeleted) return Specs_NoChange($"Parameter category '{key}' is already disabled.");

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_PARAMETER WHERE PARAMETER_CATEGORY = @k AND IS_DELETED = 0",
                () => P("@k", key),
                n => $"Parameter category '{key}' has {n} active parameter(s). Move or disable them first.",
                cancellationToken) is { } e4) return Specs_Fail(e4);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_PARAMETER_CATEGORY", "KEY_CATEGORY_PAR = @k", true, P("@k", key), cancellationToken);
    }

    public async Task<SqlActionResult> Specs_EnableParameterCategory(
        string categoryKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(categoryKey, "Parameter category key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_PARCAT_STATE, () => P("@k", key), $"Parameter category '{key}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (!state.IsDeleted) return Specs_NoChange($"Parameter category '{key}' is already active.");

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_PARAMETER_CATEGORY", "KEY_CATEGORY_PAR = @k", false, P("@k", key), cancellationToken);
    }

    // ══ Write — Master parameters (MAS_SPEC_PARAMETER) ═══════════════════════════════

    /// <summary>
    /// Creates a master parameter. Uses KeyParameter, ParameterName, ParameterDefaultUm and
    /// ParameterCategoryKey from <paramref name="parameter"/>. UM and category must exist and be active.
    /// </summary>
    public async Task<SqlActionResult> Specs_AddMasterParameter(
        SPEC_PARAMETER parameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (parameter is null) return Specs_Fail("Parameter is required.");
        if (Specs_ValidateKey(parameter.KeyParameter, "Parameter key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(parameter.ParameterName, "Parameter name", SPECS_LEN_PARAM_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateUnitCode(parameter.ParameterDefaultUm, "Default UM", out var um, SPECS_LEN_DEFAULT_UM) is { } e3) return Specs_Fail(e3);
        if (Specs_ValidateKey(parameter.ParameterCategoryKey, "Parameter category", out var cat) is { } e4) return Specs_Fail(e4);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e5) return Specs_Fail(e5);

        if (await Specs_RequireNewKeyAsync(SQL_PARAM_STATE, () => P("@k", key), $"Parameter '{key}'", cancellationToken) is { } e6) return Specs_Fail(e6);
        if (await Specs_RequireActiveAsync(SQL_UM_STATE, () => P("@k", um), $"Unit '{um}'", cancellationToken) is { } e7) return Specs_Fail(e7);
        if (await Specs_RequireActiveAsync(SQL_PARCAT_STATE, () => P("@k", cat), $"Parameter category '{cat}'", cancellationToken) is { } e8) return Specs_Fail(e8);

        return await _db.NonQueryAsync(@"
            INSERT INTO dbo.MAS_SPEC_PARAMETER (KEY_PARAMETER, PARAMETER_NAME, PARAMETER_DEFAULT_UM, PARAMETER_CATEGORY, IS_DELETED)
            VALUES (@k, @name, @um, @cat, 0)",
            [new SqlParameter("@k", key), new SqlParameter("@name", name),
             new SqlParameter("@um", um), new SqlParameter("@cat", cat)], ct: cancellationToken);
    }

    /// <summary>
    /// Updates name, default UM and category of a master parameter (identified by KeyParameter).
    /// A changed UM / category must exist and be active; an unchanged one is accepted as is.
    /// </summary>
    public async Task<SqlActionResult> Specs_UpdateMasterParameter(
        SPEC_PARAMETER parameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (parameter is null) return Specs_Fail("Parameter is required.");
        if (Specs_ValidateKey(parameter.KeyParameter, "Parameter key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(parameter.ParameterName, "Parameter name", SPECS_LEN_PARAM_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateUnitCode(parameter.ParameterDefaultUm, "Default UM", out var um, SPECS_LEN_DEFAULT_UM) is { } e3) return Specs_Fail(e3);
        if (Specs_ValidateKey(parameter.ParameterCategoryKey, "Parameter category", out var cat) is { } e4) return Specs_Fail(e4);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e5) return Specs_Fail(e5);

        var q = await _db.SelectAsync(
            "SELECT PARAMETER_DEFAULT_UM, PARAMETER_CATEGORY FROM dbo.MAS_SPEC_PARAMETER WHERE KEY_PARAMETER = @k",
            P("@k", key), cancellationToken);
        if (!q.Success || q.DTResults == null) return Specs_Fail($"Error reading parameter '{key}': {q.Message}");
        if (q.DTResults.Rows.Count == 0) return Specs_Fail($"Parameter '{key}' does not exist.");

        var currentUm  = GetString(q.DTResults.Rows[0], "PARAMETER_DEFAULT_UM").Trim();
        var currentCat = GetString(q.DTResults.Rows[0], "PARAMETER_CATEGORY").Trim();

        if (!string.Equals(um, currentUm, StringComparison.OrdinalIgnoreCase) &&
            await Specs_RequireActiveAsync(SQL_UM_STATE, () => P("@k", um), $"Unit '{um}'", cancellationToken) is { } e6)
            return Specs_Fail(e6);

        if (!string.Equals(cat, currentCat, StringComparison.OrdinalIgnoreCase) &&
            await Specs_RequireActiveAsync(SQL_PARCAT_STATE, () => P("@k", cat), $"Parameter category '{cat}'", cancellationToken) is { } e7)
            return Specs_Fail(e7);

        return await _db.NonQueryAsync(@"
            UPDATE dbo.MAS_SPEC_PARAMETER
            SET    PARAMETER_NAME = @name, PARAMETER_DEFAULT_UM = @um, PARAMETER_CATEGORY = @cat
            WHERE  KEY_PARAMETER = @k",
            [new SqlParameter("@k", key), new SqlParameter("@name", name),
             new SqlParameter("@um", um), new SqlParameter("@cat", cat)], ct: cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a master parameter and, in the same transaction, all its family links.
    /// TR_FAMILY_PARAM_IS_DELETED_SYNC then propagates IS_DELETED to the MATNR parameter rows.
    /// </summary>
    public async Task<SqlActionResult> Specs_DisableMasterParameter(
        string keyParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_PARAM_STATE, () => P("@k", key), $"Parameter '{key}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (state.IsDeleted) return Specs_NoChange($"Parameter '{key}' is already disabled.");

        return await _db.ExecuteInTransactionAsync(async (tx, ct) =>
        {
            await Specs_SetSessionUserAsync(userRequester, tx, ct);

            var r1 = await _db.NonQueryAsync(
                "UPDATE dbo.MAS_SPEC_PARAMETER SET IS_DELETED = 1 WHERE KEY_PARAMETER = @k",
                P("@k", key), transaction: tx, ct: ct);
            if (!r1.Success) throw new InvalidOperationException(r1.Message);

            var r2 = await _db.NonQueryAsync(
                "UPDATE dbo.MAS_SPEC_FAMILY_VS_PARAMETER SET IS_DELETED = 1 WHERE KEY_PARAMETER = @k AND IS_DELETED = 0",
                P("@k", key), transaction: tx, ct: ct);
            if (!r2.Success) throw new InvalidOperationException(r2.Message);
        }, cancellationToken);
    }

    /// <summary>
    /// Reactivates a master parameter. Its category and default UM must be active.
    /// Family links are NOT reactivated — use Specs_EnableFamilyParameter per family.
    /// </summary>
    public async Task<SqlActionResult> Specs_EnableMasterParameter(
        string keyParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var q = await _db.SelectAsync(
            "SELECT PARAMETER_DEFAULT_UM, PARAMETER_CATEGORY, IS_DELETED FROM dbo.MAS_SPEC_PARAMETER WHERE KEY_PARAMETER = @k",
            P("@k", key), cancellationToken);
        if (!q.Success || q.DTResults == null) return Specs_Fail($"Error reading parameter '{key}': {q.Message}");
        if (q.DTResults.Rows.Count == 0) return Specs_Fail($"Parameter '{key}' does not exist.");

        var row = q.DTResults.Rows[0];
        if (!GetBoolean(row, "IS_DELETED")) return Specs_NoChange($"Parameter '{key}' is already active.");

        var um  = GetString(row, "PARAMETER_DEFAULT_UM").Trim();
        var cat = GetString(row, "PARAMETER_CATEGORY").Trim();

        if (await Specs_RequireActiveAsync(SQL_UM_STATE, () => P("@k", um), $"Unit '{um}'", cancellationToken) is { } e3)
            return Specs_Fail(e3);
        if (cat.Length > 0 &&
            await Specs_RequireActiveAsync(SQL_PARCAT_STATE, () => P("@k", cat), $"Parameter category '{cat}'", cancellationToken) is { } e4)
            return Specs_Fail(e4);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_PARAMETER", "KEY_PARAMETER = @k", false, P("@k", key), cancellationToken);
    }

    // ══ Write — Product categories (MAS_SPEC_CATEGORY) ═══════════════════════════════

    public async Task<SqlActionResult> Specs_AddCategory(
        string catKey, string catName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(catName, "Category name", SPECS_LEN_CAT_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireNewKeyAsync(SQL_CAT_STATE, () => P("@k", key), $"Category '{key}'", cancellationToken) is { } e4) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_CATEGORY (CAT_KEY, CAT_NAME, IS_DELETED) VALUES (@k, @name, 0)",
            [new SqlParameter("@k", key), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    public async Task<SqlActionResult> Specs_UpdateCategory(
        string catKey, string catName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var key) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateName(catName, "Category name", SPECS_LEN_CAT_NAME, out var name) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);
        if (await Specs_RequireExistsAsync(SQL_CAT_STATE, () => P("@k", key), $"Category '{key}'", cancellationToken) is { Error: { } e4 }) return Specs_Fail(e4);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_CATEGORY SET CAT_NAME = @name WHERE CAT_KEY = @k",
            [new SqlParameter("@k", key), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    /// <summary>Soft delete. Blocked while the category has active families or active products.</summary>
    public async Task<SqlActionResult> Specs_DisableCategory(
        string catKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_CAT_STATE, () => P("@k", key), $"Category '{key}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (state.IsDeleted) return Specs_NoChange($"Category '{key}' is already disabled.");

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_FAMILY WHERE CAT_KEY = @k AND IS_DELETED = 0",
                () => P("@k", key),
                n => $"Category '{key}' has {n} active family(ies). Disable them first.",
                cancellationToken) is { } e4) return Specs_Fail(e4);

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_MATNR_HEADER WHERE PRODUCT_CATEGOTY = @k AND IS_DELETED = 0",
                () => P("@k", key),
                n => $"Category '{key}' has {n} active product(s).",
                cancellationToken) is { } e5) return Specs_Fail(e5);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_CATEGORY", "CAT_KEY = @k", true, P("@k", key), cancellationToken);
    }

    public async Task<SqlActionResult> Specs_EnableCategory(
        string catKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var key) is { } e1) return Specs_Fail(e1);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e2) return Specs_Fail(e2);

        var state = await Specs_RequireExistsAsync(SQL_CAT_STATE, () => P("@k", key), $"Category '{key}'", cancellationToken);
        if (state.Error is { } e3) return Specs_Fail(e3);
        if (!state.IsDeleted) return Specs_NoChange($"Category '{key}' is already active.");

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_CATEGORY", "CAT_KEY = @k", false, P("@k", key), cancellationToken);
    }

    // ══ Write — Families (MAS_SPEC_FAMILY) ═══════════════════════════════════════════

    /// <summary>
    /// Creates a family under an active category. FAM_KEY must be unique across all categories
    /// (MAS_SPEC_FAMILY_VS_PARAMETER references the family by FAM_KEY only).
    /// </summary>
    public async Task<SqlActionResult> Specs_AddFamily(
        string catKey, string famKey, string famName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var cat) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateName(famName, "Family name", SPECS_LEN_FAM_NAME, out var name) is { } e3) return Specs_Fail(e3);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e4) return Specs_Fail(e4);

        if (await Specs_RequireActiveAsync(SQL_CAT_STATE, () => P("@k", cat), $"Category '{cat}'", cancellationToken) is { } e5) return Specs_Fail(e5);

        // FAM_KEY unique across categories
        var existing = await _db.SelectAsync(
            "SELECT CAT_KEY, IS_DELETED FROM dbo.MAS_SPEC_FAMILY WHERE FAM_KEY = @f", P("@f", fam), cancellationToken);
        if (!existing.Success || existing.DTResults == null) return Specs_Fail($"Error checking family '{fam}': {existing.Message}");
        if (existing.DTResults.Rows.Count > 0)
        {
            var r = existing.DTResults.Rows[0];
            var ownerCat = GetString(r, "CAT_KEY").Trim();
            return Specs_Fail(GetBoolean(r, "IS_DELETED")
                ? $"Family '{fam}' already exists (disabled) in category '{ownerCat}'. Enable it instead."
                : $"Family '{fam}' already exists in category '{ownerCat}'.");
        }

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_FAMILY (CAT_KEY, FAM_KEY, FAM_NAME, IS_DELETED) VALUES (@c, @f, @name, 0)",
            [new SqlParameter("@c", cat), new SqlParameter("@f", fam), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    public async Task<SqlActionResult> Specs_UpdateFamily(
        string catKey, string famKey, string famName,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var cat) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateName(famName, "Family name", SPECS_LEN_FAM_NAME, out var name) is { } e3) return Specs_Fail(e3);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e4) return Specs_Fail(e4);
        if (await Specs_RequireExistsAsync(SQL_FAM_STATE, () => P2("@c", cat, "@f", fam), $"Family '{cat}/{fam}'", cancellationToken) is { Error: { } e5 }) return Specs_Fail(e5);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY SET FAM_NAME = @name WHERE CAT_KEY = @c AND FAM_KEY = @f",
            [new SqlParameter("@c", cat), new SqlParameter("@f", fam), new SqlParameter("@name", name)], ct: cancellationToken);
    }

    /// <summary>Soft delete. Blocked while the family has active products.</summary>
    public async Task<SqlActionResult> Specs_DisableFamily(
        string catKey, string famKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var cat) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        var state = await Specs_RequireExistsAsync(SQL_FAM_STATE, () => P2("@c", cat, "@f", fam), $"Family '{cat}/{fam}'", cancellationToken);
        if (state.Error is { } e4) return Specs_Fail(e4);
        if (state.IsDeleted) return Specs_NoChange($"Family '{cat}/{fam}' is already disabled.");

        if (await Specs_RequireNoChildrenAsync(
                "SELECT COUNT(*) FROM dbo.MAS_SPEC_MATNR_HEADER WHERE PRODUCT_CATEGOTY = @c AND PRODUCT_FAMILY = @f AND IS_DELETED = 0",
                () => P2("@c", cat, "@f", fam),
                n => $"Family '{cat}/{fam}' has {n} active product(s).",
                cancellationToken) is { } e5) return Specs_Fail(e5);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_FAMILY", "CAT_KEY = @c AND FAM_KEY = @f", true, P2("@c", cat, "@f", fam), cancellationToken);
    }

    /// <summary>Reactivates a family. Its category must be active.</summary>
    public async Task<SqlActionResult> Specs_EnableFamily(
        string catKey, string famKey,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(catKey, "Category key", out var cat) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        var state = await Specs_RequireExistsAsync(SQL_FAM_STATE, () => P2("@c", cat, "@f", fam), $"Family '{cat}/{fam}'", cancellationToken);
        if (state.Error is { } e4) return Specs_Fail(e4);
        if (!state.IsDeleted) return Specs_NoChange($"Family '{cat}/{fam}' is already active.");

        if (await Specs_RequireActiveAsync(SQL_CAT_STATE, () => P("@k", cat), $"Category '{cat}'", cancellationToken) is { } e5) return Specs_Fail(e5);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_FAMILY", "CAT_KEY = @c AND FAM_KEY = @f", false, P2("@c", cat, "@f", fam), cancellationToken);
    }

    // ══ Write — Family × parameter (MAS_SPEC_FAMILY_VS_PARAMETER) ════════════════════

    /// <summary>
    /// Links an active parameter to an active family with type M/O/I.
    /// TR_FAMILY_PARAM_ADDED inserts a null value row in every active MATNR of that family.
    /// </summary>
    public async Task<SqlActionResult> Specs_AddFamilyParameter(
        string famKey, string keyParameter, string typeParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateType(typeParameter, out var type) is { } e3) return Specs_Fail(e3);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e4) return Specs_Fail(e4);

        if (await Specs_RequireActiveAsync(SQL_FAM_BY_KEY_STATE, () => P("@f", fam), $"Family '{fam}'", cancellationToken) is { } e5) return Specs_Fail(e5);
        if (await Specs_RequireActiveAsync(SQL_PARAM_STATE, () => P("@k", key), $"Parameter '{key}'", cancellationToken) is { } e6) return Specs_Fail(e6);
        if (await Specs_RequireNewKeyAsync(SQL_FVP_STATE, () => P2("@f", fam, "@k", key), $"Parameter '{key}' in family '{fam}'", cancellationToken) is { } e7) return Specs_Fail(e7);

        return await _db.NonQueryAsync(
            "INSERT INTO dbo.MAS_SPEC_FAMILY_VS_PARAMETER (FAM_KEY, KEY_PARAMETER, TYPE_PARAMETER, IS_DELETED) VALUES (@f, @k, @type, 0)",
            [new SqlParameter("@f", fam), new SqlParameter("@k", key), new SqlParameter("@type", type)], ct: cancellationToken);
    }

    public async Task<SqlActionResult> Specs_UpdateFamilyParameterType(
        string famKey, string keyParameter, string typeParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e2) return Specs_Fail(e2);
        if (Specs_ValidateType(typeParameter, out var type) is { } e3) return Specs_Fail(e3);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e4) return Specs_Fail(e4);
        if (await Specs_RequireExistsAsync(SQL_FVP_STATE, () => P2("@f", fam, "@k", key), $"Parameter '{key}' in family '{fam}'", cancellationToken) is { Error: { } e5 }) return Specs_Fail(e5);

        return await _db.NonQueryAsync(
            "UPDATE dbo.MAS_SPEC_FAMILY_VS_PARAMETER SET TYPE_PARAMETER = @type WHERE FAM_KEY = @f AND KEY_PARAMETER = @k",
            [new SqlParameter("@f", fam), new SqlParameter("@k", key), new SqlParameter("@type", type)], ct: cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a family×parameter link.
    /// TR_FAMILY_PARAM_IS_DELETED_SYNC propagates IS_DELETED to the MATNR rows of that family.
    /// </summary>
    public async Task<SqlActionResult> Specs_DisableFamilyParameter(
        string famKey, string keyParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        var state = await Specs_RequireExistsAsync(SQL_FVP_STATE, () => P2("@f", fam, "@k", key), $"Parameter '{key}' in family '{fam}'", cancellationToken);
        if (state.Error is { } e4) return Specs_Fail(e4);
        if (state.IsDeleted) return Specs_NoChange($"Parameter '{key}' in family '{fam}' is already disabled.");

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_FAMILY_VS_PARAMETER", "FAM_KEY = @f AND KEY_PARAMETER = @k", true, P2("@f", fam, "@k", key), cancellationToken);
    }

    /// <summary>
    /// Reactivates a family×parameter link. Family and parameter must be active.
    /// TR_FAMILY_PARAM_IS_DELETED_SYNC propagates the change to the MATNR rows of that family.
    /// </summary>
    public async Task<SqlActionResult> Specs_EnableFamilyParameter(
        string famKey, string keyParameter,
        string userRequester = "System", CancellationToken cancellationToken = default)
    {
        if (Specs_ValidateKey(famKey, "Family key", out var fam) is { } e1) return Specs_Fail(e1);
        if (Specs_ValidateKey(keyParameter, "Parameter key", out var key) is { } e2) return Specs_Fail(e2);
        if (await Specs_CatalogPermissionAsync(userRequester, cancellationToken) is { } e3) return Specs_Fail(e3);

        var state = await Specs_RequireExistsAsync(SQL_FVP_STATE, () => P2("@f", fam, "@k", key), $"Parameter '{key}' in family '{fam}'", cancellationToken);
        if (state.Error is { } e4) return Specs_Fail(e4);
        if (!state.IsDeleted) return Specs_NoChange($"Parameter '{key}' in family '{fam}' is already active.");

        if (await Specs_RequireActiveAsync(SQL_FAM_BY_KEY_STATE, () => P("@f", fam), $"Family '{fam}'", cancellationToken) is { } e5) return Specs_Fail(e5);
        if (await Specs_RequireActiveAsync(SQL_PARAM_STATE, () => P("@k", key), $"Parameter '{key}'", cancellationToken) is { } e6) return Specs_Fail(e6);

        return await Specs_SetDeletedAsync("dbo.MAS_SPEC_FAMILY_VS_PARAMETER", "FAM_KEY = @f AND KEY_PARAMETER = @k", false, P2("@f", fam, "@k", key), cancellationToken);
    }

    // ══ Private helpers — catalog ════════════════════════════════════════════════════

    // State lookups: return a single IS_DELETED column (0 rows = does not exist)
    // Units are case- and symbol-sensitive (mH ≠ MH, µ ≠ μ) — compare with a CS collation
    private const string SQL_UM_STATE         = "SELECT IS_DELETED FROM dbo.MAS_SPEC_UM WHERE UM_CODE COLLATE Latin1_General_100_CS_AS = @k";
    private const string SQL_UMCONV_STATE     = "SELECT IS_DELETED FROM dbo.MAS_SPEC_UM_CONVERSION WHERE UM_FROM = @k";
    private const string SQL_PARCAT_STATE     = "SELECT IS_DELETED FROM dbo.MAS_SPEC_PARAMETER_CATEGORY WHERE KEY_CATEGORY_PAR = @k";
    private const string SQL_PARAM_STATE      = "SELECT IS_DELETED FROM dbo.MAS_SPEC_PARAMETER WHERE KEY_PARAMETER = @k";
    private const string SQL_CAT_STATE        = "SELECT IS_DELETED FROM dbo.MAS_SPEC_CATEGORY WHERE CAT_KEY = @k";
    private const string SQL_FAM_STATE        = "SELECT IS_DELETED FROM dbo.MAS_SPEC_FAMILY WHERE CAT_KEY = @c AND FAM_KEY = @f";
    private const string SQL_FAM_BY_KEY_STATE = "SELECT TOP 1 IS_DELETED FROM dbo.MAS_SPEC_FAMILY WHERE FAM_KEY = @f ORDER BY IS_DELETED";
    private const string SQL_FVP_STATE        = "SELECT IS_DELETED FROM dbo.MAS_SPEC_FAMILY_VS_PARAMETER WHERE FAM_KEY = @f AND KEY_PARAMETER = @k";

    // Fresh SqlParameter arrays on every call — a SqlParameter cannot be shared between commands
    private static SqlParameter[] P(string n, object v) => [new SqlParameter(n, v)];
    private static SqlParameter[] P2(string n1, object v1, string n2, object v2) => [new SqlParameter(n1, v1), new SqlParameter(n2, v2)];

    private static SqlActionResult Specs_Fail(string message) => new() { Success = false, Message = message };
    private static SqlActionResult Specs_NoChange(string message) => new() { Success = true, Message = message, RecordsAffected = 0 };

    private static string? Specs_ValidateKey(string? value, string label, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0) return $"{label} is required.";
        if (normalized.Length > SPECS_KEY_MAX) return $"{label} '{normalized}' exceeds {SPECS_KEY_MAX} characters.";
        return null;
    }

    /// <summary>Unit codes keep their exact spelling (mH ≠ MH, µ ≠ μ) — trimmed, never upper-cased.</summary>
    private static string? Specs_ValidateUnitCode(string? value, string label, out string normalized, int maxLength = SPECS_KEY_MAX)
    {
        normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) return $"{label} is required.";
        if (normalized.Length > maxLength) return $"{label} '{normalized}' exceeds {maxLength} characters.";
        return null;
    }

    private static string? Specs_ValidateName(string? value, string label, int maxLength, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) return $"{label} is required.";
        if (normalized.Length > maxLength) return $"{label} exceeds {maxLength} characters.";
        return null;
    }

    private static string? Specs_ValidateType(string? value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return SPECS_PARAMETER_TYPES.Contains(normalized)
            ? null
            : $"Parameter type '{value}' is not valid. Allowed: {string.Join(", ", SPECS_PARAMETER_TYPES)}.";
    }

    private async Task<string?> Specs_CatalogPermissionAsync(string userRequester, CancellationToken ct)
    {
        var perm = await Specs_CheckPermission(SPECS_KEY_PROCESS, userRequester, ct);
        return perm.Success ? null : perm.ErrorMessage;
    }

    private readonly record struct Specs_RowState(bool Exists, bool IsDeleted, string? Error);

    private async Task<Specs_RowState> Specs_GetRowStateAsync(string sql, Func<SqlParameter[]> parameters, CancellationToken ct)
    {
        var q = await _db.SelectAsync(sql, parameters(), ct);
        if (!q.Success || q.DTResults == null) return new(false, false, $"Error: {q.Message}");
        if (q.DTResults.Rows.Count == 0)       return new(false, false, null);
        return new(true, GetBoolean(q.DTResults.Rows[0], "IS_DELETED"), null);
    }

    /// <summary>Error if the row exists (active or disabled).</summary>
    private async Task<string?> Specs_RequireNewKeyAsync(string sql, Func<SqlParameter[]> parameters, string label, CancellationToken ct)
    {
        var s = await Specs_GetRowStateAsync(sql, parameters, ct);
        if (s.Error != null) return s.Error;
        if (!s.Exists)       return null;
        return s.IsDeleted ? $"{label} already exists but is disabled. Enable it instead." : $"{label} already exists.";
    }

    /// <summary>Error if the row does not exist. Returns its state otherwise.</summary>
    private async Task<Specs_RowState> Specs_RequireExistsAsync(string sql, Func<SqlParameter[]> parameters, string label, CancellationToken ct)
    {
        var s = await Specs_GetRowStateAsync(sql, parameters, ct);
        if (s.Error != null || s.Exists) return s;
        return s with { Error = $"{label} does not exist." };
    }

    /// <summary>Error if the row does not exist or is disabled.</summary>
    private async Task<string?> Specs_RequireActiveAsync(string sql, Func<SqlParameter[]> parameters, string label, CancellationToken ct)
    {
        var s = await Specs_RequireExistsAsync(sql, parameters, label, ct);
        if (s.Error != null) return s.Error;
        return s.IsDeleted ? $"{label} is disabled." : null;
    }

    /// <summary>Error if the COUNT(*) query returns more than zero.</summary>
    private async Task<string?> Specs_RequireNoChildrenAsync(string countSql, Func<SqlParameter[]> parameters, Func<int, string> message, CancellationToken ct)
    {
        var q = await _db.SelectAsync(countSql, parameters(), ct);
        if (!q.Success || q.DTResults == null || q.DTResults.Rows.Count == 0) return $"Error: {q.Message}";
        var n = Convert.ToInt32(q.DTResults.Rows[0][0]);
        return n > 0 ? message(n) : null;
    }

    // table / where are internal constants (never user input) — values always go through parameters
    private async Task<SqlActionResult> Specs_SetDeletedAsync(string table, string where, bool deleted, SqlParameter[] parameters, CancellationToken ct)
    {
        var all = parameters.Append(new SqlParameter("@del", deleted)).ToArray();
        return await _db.NonQueryAsync($"UPDATE {table} SET IS_DELETED = @del WHERE {where}", all, ct: ct);
    }
}
