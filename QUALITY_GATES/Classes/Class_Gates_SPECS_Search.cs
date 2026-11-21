using Microsoft.Data.SqlClient;
using SqlDataLayer;
using SqlDataLayer.Models;
using static SqlDataLayer.SqlDbAccess;
using static QUALITY_GATES.Tools.Static_Local_Functions;
using System.Data;
using System.Globalization;

namespace QUALITY_GATES.Classes;

// ════════════════════════════════════════════════════════════════════════════════════════
//  SPECS — Area 3: parametric search (decisions 2026-11-21, SPECS.md §6)
//
//  The user picks category → family, then filters on that family's parameters.
//    N (numeric) filter — values in the DEFAULT UM of the parameter; product values are converted from their
//      UM_PARAMETER with MAS_SPEC_UM_CONVERSION (a unit with no conversion to the default UM = no data).
//        WITHIN (default): TYP inside [From, To]; without TYP, the product range MIN–MAX overlaps [From, To].
//        COVERS          : product MIN ≤ From and MAX ≥ To (a needed bound missing = no data).
//    L (list) filter — product value is one of the selected list values. A text not in the list = no data.
//    T (free text)    — product text contains the given text (case-insensitive).
//    A filter without criteria only asks for "has data".
//  Every product of the family is classified (never silently discarded):
//    Match    — every filter matches
//    Partial  — no filter fails, but some filters have no data (FiltersNoData lists them)
//    NoMatch  — at least one filter with data fails (hidden unless includeNoMatch)
//  Ordered by status, then filters matched (desc), then MATNR.
//  DOC and MANUAL rows both count; HasUnreviewedData marks results that use document data not reviewed yet.
// ════════════════════════════════════════════════════════════════════════════════════════

// ── Models — search ────────────────────────────────────────────────────────────────────

public enum SPEC_MATCH_STATUS  { Match = 0, Partial = 1, NoMatch = 2 }
public enum SPEC_FILTER_STATUS { Match = 0, NoData = 1, NoMatch = 2 }

/// <summary>One filter of the advanced search. Fill only the fields of the parameter's value type.</summary>
public class SPEC_SEARCH_FILTER
{
    public string       KeyParameter { get; set; } = string.Empty;

    // N — in the default UM of the parameter (SPEC_SEARCH_PARAMETER.DefaultUm); either bound optional
    public decimal?     From         { get; set; }
    public decimal?     To           { get; set; }
    public string       NumericMode  { get; set; } = "WITHIN";   // WITHIN (default) | COVERS

    // L — product value must be one of these list codes (case-insensitive); empty = has data
    public List<string> Values       { get; set; } = [];

    // T — product text must contain this text (case-insensitive); empty = has data
    public string?      Contains     { get; set; }
}

/// <summary>Result of one filter for one product.</summary>
public class SPEC_SEARCH_FILTER_RESULT
{
    public string             KeyParameter  { get; set; } = string.Empty;
    public string             ParameterName { get; set; } = string.Empty;
    public SPEC_FILTER_STATUS Status        { get; set; }
    public string             ProductValue  { get; set; } = string.Empty;  // value as compared (converted to the default UM for N)
    public string             Reason        { get; set; } = string.Empty;  // why NoData (no value, unit not convertible, text not in list…)
    public bool               IsReviewed    { get; set; }                  // the row is MANUAL (reviewed by an engineer)
}

/// <summary>One product of the family with its classification against the filters.</summary>
public class SPEC_SEARCH_ADVANCED_RESULT
{
    public SPEC_MATNR_HEADER               Header                  { get; set; } = new();
    public SPEC_MATCH_STATUS               Status                  { get; set; }
    public int                             FiltersMatched          { get; set; }
    public List<string>                    FiltersNoData           { get; set; } = [];   // KEY_PARAMETER of filters without data
    public List<string>                    FiltersFailed           { get; set; } = [];   // KEY_PARAMETER of filters not matched
    public List<SPEC_SEARCH_FILTER_RESULT> FilterResults           { get; set; } = [];
    public bool                            HasUnreviewedData       { get; set; }         // a filter was evaluated on DOC (not reviewed) data
    public int                             ParametersPendingReview { get; set; }         // parameters of the product with NeedsReview
    public List<SPEC_MATNR_PARAMETER>      Parameters              { get; set; } = [];   // every active family parameter of the product
}

/// <summary>Help to build the filters: one active parameter of the family with what can be filtered.</summary>
public class SPEC_SEARCH_PARAMETER
{
    public string   KeyParameter          { get; set; } = string.Empty;
    public string   ParameterName         { get; set; } = string.Empty;
    public string   ParameterCategoryKey  { get; set; } = string.Empty;
    public string   ParameterCategoryName { get; set; } = string.Empty;
    public string   ValueType             { get; set; } = "N";            // N / L / T
    public string   TypeParameter         { get; set; } = string.Empty;   // M / O / I
    public bool     IsMandatory           => TypeParameter == "M";
    public int      ProductsTotal         { get; set; }                   // active products of the family
    public int      ProductsWithData      { get; set; }

    // N — filter values are expressed in DefaultUm; Min/Max = real range of the family converted to DefaultUm
    public string   DefaultUm             { get; set; } = string.Empty;
    public string   DefaultUmDescription  { get; set; } = string.Empty;
    public decimal? MinValue              { get; set; }
    public decimal? MaxValue              { get; set; }
    public int      ProductsNotConvertible { get; set; }                  // numeric data in a unit with no conversion to DefaultUm

    // L — list values with the number of products using each one (disabled values listed only if used)
    public List<SPEC_SEARCH_VALUE_COUNT> Values { get; set; } = [];
    public int      ProductsTextNotInList { get; set; }                   // text from the document not in the list (= no data)
}

public class SPEC_SEARCH_VALUE_COUNT
{
    public string ValueCode        { get; set; } = string.Empty;
    public string ValueDescription { get; set; } = string.Empty;
    public int    SortOrder        { get; set; }
    public bool   IsActive         { get; set; } = true;
    public int    Products         { get; set; }
}

// ── Business layer ─────────────────────────────────────────────────────────────────────

public partial class Class_Projects_Quality_Gates
{
    private const string SPECS_MODE_WITHIN = "WITHIN";
    private const string SPECS_MODE_COVERS = "COVERS";

    /// <summary>
    /// Help for the search screen: every active parameter of the family with its value type, default UM,
    /// real numeric range (converted to the default UM), list values with product counts and products with data.
    /// DOC and MANUAL rows both count.
    /// </summary>
    public async Task<OperationResult<List<SPEC_SEARCH_PARAMETER>>> Specs_GetSearchParameters(
        string catKey,
        string famKey,
        CancellationToken cancellationToken = default)
    {
        var data = await Specs_LoadSearchDataAsync(catKey, famKey, cancellationToken);
        if (data.Error != null) return OperationResult<List<SPEC_SEARCH_PARAMETER>>.Fail(data.Error);
        var d = data.Data!;

        var result = new List<SPEC_SEARCH_PARAMETER>();
        foreach (var fp in d.FamilyParameters)
        {
            var info = new SPEC_SEARCH_PARAMETER
            {
                KeyParameter          = fp.KeyParameter,
                ParameterName         = fp.ParameterName,
                ParameterCategoryKey  = fp.ParameterCategoryKey,
                ParameterCategoryName = fp.ParameterCategoryName,
                ValueType             = fp.ValueType,
                TypeParameter         = fp.TypeParameter,
                DefaultUm             = fp.DefaultUm,
                DefaultUmDescription  = fp.DefaultUmDescription,
                ProductsTotal         = d.Products.Count
            };
            var listValues = d.ListValues.TryGetValue(fp.KeyParameter, out var lv) ? lv : [];
            var counts     = listValues.ToDictionary(v => v.ValueCode, _ => 0, StringComparer.OrdinalIgnoreCase);

            foreach (var product in d.Products)
            {
                var p = product.Parameters.FirstOrDefault(x => string.Equals(x.KeyParameter, fp.KeyParameter, StringComparison.OrdinalIgnoreCase));
                if (p == null) continue;

                switch (fp.ValueType)
                {
                    case SPECS_VT_LIST:
                        if (Specs_MatchListValue(p.ValueText, listValues, activeOnly: false) is { } m)
                        { info.ProductsWithData++; counts[m.ValueCode]++; }
                        else if (!string.IsNullOrWhiteSpace(p.ValueText)) info.ProductsTextNotInList++;
                        break;

                    case SPECS_VT_TEXT:
                        if (!string.IsNullOrWhiteSpace(p.ValueText)) info.ProductsWithData++;
                        break;

                    default:
                        if (!(p.ValueMin.HasValue || p.ValueTyp.HasValue || p.ValueMax.HasValue)) break;
                        var conv = Specs_ConvertRow(p, fp.DefaultUm, d.Conversions);
                        if (!conv.Convertible) { info.ProductsNotConvertible++; break; }
                        info.ProductsWithData++;
                        foreach (var v in new[] { conv.Min, conv.Typ, conv.Max }.Where(v => v.HasValue).Select(v => v!.Value))
                        {
                            info.MinValue = info.MinValue.HasValue ? Math.Min(info.MinValue.Value, v) : v;
                            info.MaxValue = info.MaxValue.HasValue ? Math.Max(info.MaxValue.Value, v) : v;
                        }
                        break;
                }
            }

            if (fp.ValueType == SPECS_VT_LIST)
                info.Values = listValues
                    .Where(v => v.IsActive || counts[v.ValueCode] > 0)
                    .Select(v => new SPEC_SEARCH_VALUE_COUNT
                    {
                        ValueCode = v.ValueCode, ValueDescription = v.ValueDescription, SortOrder = v.SortOrder,
                        IsActive = v.IsActive, Products = counts[v.ValueCode]
                    }).ToList();

            result.Add(info);
        }

        return OperationResult<List<SPEC_SEARCH_PARAMETER>>.Ok(result);
    }

    /// <summary>
    /// Advanced parametric search over the active products of a family (rules in the header of this file).
    /// Filters must reference active parameters of the family (one filter per parameter); From ≤ To.
    /// includeNoMatch = false hides the NoMatch products. No filters = every product as Match.
    /// </summary>
    public async Task<OperationResult<List<SPEC_SEARCH_ADVANCED_RESULT>>> Specs_SearchProductsAdvanced(
        string catKey,
        string famKey,
        List<SPEC_SEARCH_FILTER>? filters = null,
        bool includeNoMatch = false,
        CancellationToken cancellationToken = default)
    {
        var data = await Specs_LoadSearchDataAsync(catKey, famKey, cancellationToken);
        if (data.Error != null) return OperationResult<List<SPEC_SEARCH_ADVANCED_RESULT>>.Fail(data.Error);
        var d = data.Data!;

        // validate filters
        var famByKey = d.FamilyParameters.ToDictionary(p => p.KeyParameter, StringComparer.OrdinalIgnoreCase);
        var errors   = new List<string>();
        var seen     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan     = new List<(SPEC_SEARCH_FILTER Filter, Specs_SearchParamDef Def, string Mode)>();
        foreach (var f in filters ?? [])
        {
            var key = (f?.KeyParameter ?? string.Empty).Trim();
            if (key.Length == 0)                     { errors.Add("A filter without KeyParameter was received."); continue; }
            if (!seen.Add(key))                      { errors.Add($"Parameter '{key}' is filtered twice."); continue; }
            if (!famByKey.TryGetValue(key, out var def)) { errors.Add($"Parameter '{key}' is not an active parameter of family '{d.FamKey}'."); continue; }

            var mode = string.IsNullOrWhiteSpace(f!.NumericMode) ? SPECS_MODE_WITHIN : f.NumericMode.Trim().ToUpperInvariant();
            if (def.ValueType == SPECS_VT_NUMERIC)
            {
                if (mode is not (SPECS_MODE_WITHIN or SPECS_MODE_COVERS)) errors.Add($"Parameter '{key}': numeric mode '{f.NumericMode}' is not valid (WITHIN or COVERS).");
                if (f.From.HasValue && f.To.HasValue && f.From > f.To)   errors.Add($"Parameter '{key}': From ({f.From}) is greater than To ({f.To}).");
            }
            if (def.ValueType == SPECS_VT_LIST)
            {
                var list = d.ListValues.TryGetValue(key, out var lv) ? lv : [];
                var unknown = (f.Values ?? []).Where(v => !string.IsNullOrWhiteSpace(v) && Specs_MatchListValue(v, list, activeOnly: false) == null).ToList();
                if (unknown.Count > 0) errors.Add($"Parameter '{key}': values not in the list: {string.Join(", ", unknown)}.");
            }
            plan.Add((f, def, mode));
        }
        if (errors.Count > 0) return OperationResult<List<SPEC_SEARCH_ADVANCED_RESULT>>.Fail(string.Join(Environment.NewLine, errors));

        // evaluate every product
        var results = new List<SPEC_SEARCH_ADVANCED_RESULT>();
        foreach (var product in d.Products)
        {
            var res = new SPEC_SEARCH_ADVANCED_RESULT
            {
                Header                  = product.Header,
                Parameters              = product.Parameters,
                ParametersPendingReview = product.Parameters.Count(p => p.NeedsReview)
            };

            foreach (var (filter, def, mode) in plan)
            {
                var p  = product.Parameters.FirstOrDefault(x => string.Equals(x.KeyParameter, def.KeyParameter, StringComparison.OrdinalIgnoreCase));
                var fr = Specs_EvaluateFilter(filter, def, mode, p, d);
                res.FilterResults.Add(fr);
                switch (fr.Status)
                {
                    case SPEC_FILTER_STATUS.Match:   res.FiltersMatched++;                  break;
                    case SPEC_FILTER_STATUS.NoData:  res.FiltersNoData.Add(def.KeyParameter); break;
                    case SPEC_FILTER_STATUS.NoMatch: res.FiltersFailed.Add(def.KeyParameter); break;
                }
                if (fr.Status != SPEC_FILTER_STATUS.NoData && !fr.IsReviewed) res.HasUnreviewedData = true;
            }

            res.Status = res.FiltersFailed.Count > 0 ? SPEC_MATCH_STATUS.NoMatch
                       : res.FiltersNoData.Count > 0 ? SPEC_MATCH_STATUS.Partial
                       : SPEC_MATCH_STATUS.Match;
            if (res.Status == SPEC_MATCH_STATUS.NoMatch && !includeNoMatch) continue;
            results.Add(res);
        }

        results = results.OrderBy(r => r.Status)
                         .ThenByDescending(r => r.FiltersMatched)
                         .ThenBy(r => r.Header.Matnr, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.Header.Edition, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        return OperationResult<List<SPEC_SEARCH_ADVANCED_RESULT>>.Ok(results);
    }

    /// <summary>
    /// Parametric search kept for the current Intranet screen (signature unchanged). Runs the Area 3 engine:
    /// each SPEC_PARAM_FILTER is a WITHIN filter [ValueTypMin, ValueTypMax] in the default UM of the parameter
    /// (values converted from UM_PARAMETER; without TYP the MIN–MAX range must overlap). Returns only the products
    /// that match every filter (status Match), each with its full parameter list.
    /// </summary>
    public async Task<OperationResult<List<SPEC_SEARCH_RESULT>>> Specs_SearchProducts(
        string catKey,
        string famKey,
        List<SPEC_PARAM_FILTER>? filters = null,
        CancellationToken cancellationToken = default)
    {
        var advanced = await Specs_SearchProductsAdvanced(catKey, famKey,
            (filters ?? []).Select(f => new SPEC_SEARCH_FILTER
            {
                KeyParameter = f.KeyParameter, From = f.ValueTypMin, To = f.ValueTypMax, NumericMode = SPECS_MODE_WITHIN
            }).ToList(),
            includeNoMatch: false, cancellationToken);
        if (!advanced.Success) return OperationResult<List<SPEC_SEARCH_RESULT>>.Fail(advanced.ErrorMessage);

        return OperationResult<List<SPEC_SEARCH_RESULT>>.Ok(advanced.Data!
            .Where(r => r.Status == SPEC_MATCH_STATUS.Match)
            .Select(r => new SPEC_SEARCH_RESULT { Header = r.Header, Parameters = r.Parameters })
            .ToList());
    }

    // ══ Private helpers — search ═════════════════════════════════════════════════════

    private sealed class Specs_SearchParamDef
    {
        public string KeyParameter          { get; init; } = string.Empty;
        public string ParameterName         { get; init; } = string.Empty;
        public string ParameterCategoryKey  { get; init; } = string.Empty;
        public string ParameterCategoryName { get; init; } = string.Empty;
        public string ValueType             { get; init; } = SPECS_VT_NUMERIC;
        public string TypeParameter         { get; init; } = string.Empty;
        public string DefaultUm             { get; init; } = string.Empty;
        public string DefaultUmDescription  { get; init; } = string.Empty;
    }

    private sealed class Specs_SearchProduct
    {
        public SPEC_MATNR_HEADER          Header     { get; init; } = new();
        public List<SPEC_MATNR_PARAMETER> Parameters { get; } = [];
    }

    private sealed class Specs_SearchData
    {
        public string                                        FamKey           { get; init; } = string.Empty;
        public List<Specs_SearchParamDef>                    FamilyParameters { get; } = [];
        public List<Specs_SearchProduct>                     Products         { get; } = [];
        public Dictionary<string, List<SPEC_VALUE_OPTION>>   ListValues       { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, (string To, decimal Factor)> Conversions    { get; } = new(StringComparer.Ordinal);   // units are case-sensitive
    }

    /// <summary>Everything the search needs for one family, in 4 queries: family parameters, products + parameters,
    /// list values (incl. disabled), active unit conversions.</summary>
    private async Task<(Specs_SearchData? Data, string? Error)> Specs_LoadSearchDataAsync(string catKey, string famKey, CancellationToken ct)
    {
        if (Specs_ValidateKey(catKey, "Category", out var cat) is { } e1) return (null, e1);
        if (Specs_ValidateKey(famKey, "Family", out var fam) is { } e2) return (null, e2);
        var famState = await Specs_RequireExistsAsync(SQL_FAM_STATE, () => P2("@c", cat, "@f", fam), $"Family '{fam}' of category '{cat}'", ct);
        if (famState.Error is { } e3) return (null, e3);

        var data = new Specs_SearchData { FamKey = fam };

        // 1 — active parameters of the family
        var fq = await _db.SelectAsync(@"
            SELECT fvp.KEY_PARAMETER, fvp.TYPE_PARAMETER, mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM, mp.VALUE_TYPE,
                   mp.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME, u.UM_DESCRIPTION
            FROM   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
            JOIN   dbo.MAS_SPEC_PARAMETER mp ON mp.KEY_PARAMETER = fvp.KEY_PARAMETER AND mp.IS_DELETED = 0
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc ON pc.KEY_CATEGORY_PAR = mp.PARAMETER_CATEGORY
            LEFT JOIN dbo.MAS_SPEC_UM u ON u.UM_CODE = mp.PARAMETER_DEFAULT_UM
            WHERE  fvp.FAM_KEY = @f AND fvp.IS_DELETED = 0
            ORDER BY CASE fvp.TYPE_PARAMETER WHEN 'M' THEN 0 WHEN 'O' THEN 1 WHEN 'I' THEN 2 ELSE 3 END, mp.PARAMETER_NAME",
            P("@f", fam), ct);
        if (!fq.Success || fq.DTResults == null) return (null, $"Error reading family parameters: {fq.Message}");
        foreach (DataRow r in fq.DTResults.Rows)
            data.FamilyParameters.Add(new Specs_SearchParamDef
            {
                KeyParameter          = GetString(r, "KEY_PARAMETER").Trim(),
                ParameterName         = GetString(r, "PARAMETER_NAME").Trim(),
                ParameterCategoryKey  = GetString(r, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(r, "CATEGORY_PAR_NAME").Trim(),
                ValueType             = Specs_NormValueType(GetString(r, "VALUE_TYPE")),
                TypeParameter         = GetString(r, "TYPE_PARAMETER").Trim(),
                DefaultUm             = GetString(r, "PARAMETER_DEFAULT_UM").Trim(),
                DefaultUmDescription  = GetString(r, "UM_DESCRIPTION").Trim()
            });

        // 2 — active products of the family with their active family parameters
        var pq = await _db.SelectAsync(@"
            SELECT h.MATNR, h.EDITION, h.DESCRIPTION, h.PRODUCT_CATEGOTY, h.PRODUCT_FAMILY,
                   h.SPEC_FILE_PATH, h.ISSUE_DATE, h.CREATED_BY, h.CREATED_DATE, h.IS_DELETED,
                   c.CAT_NAME, f.FAM_NAME,
                   p.KEY_PARAMETER, p.VALUE_MAX, p.VALUE_TYP, p.VALUE_MIN, p.VALUE_TEXT, p.UM, p.UM_PARAMETER,
                   p.DATA_SOURCE, p.MODIFIED_BY, p.MODIFIED_DATE,
                   mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM, mp.VALUE_TYPE, mp.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME,
                   fvp.TYPE_PARAMETER
            FROM   dbo.MAS_SPEC_MATNR_HEADER h
            LEFT JOIN dbo.MAS_SPEC_CATEGORY c ON c.CAT_KEY = h.PRODUCT_CATEGOTY
            LEFT JOIN dbo.MAS_SPEC_FAMILY   f ON f.CAT_KEY = h.PRODUCT_CATEGOTY AND f.FAM_KEY = h.PRODUCT_FAMILY
            JOIN   dbo.MAS_SPEC_MATNR_PARAMETERS p ON p.MATNR = h.MATNR AND p.EDITION = h.EDITION AND p.IS_DELETED = 0
            JOIN   dbo.MAS_SPEC_PARAMETER mp ON mp.KEY_PARAMETER = p.KEY_PARAMETER AND mp.IS_DELETED = 0
            JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp
                     ON fvp.FAM_KEY = h.PRODUCT_FAMILY AND fvp.KEY_PARAMETER = p.KEY_PARAMETER AND fvp.IS_DELETED = 0
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc ON pc.KEY_CATEGORY_PAR = mp.PARAMETER_CATEGORY
            WHERE  h.PRODUCT_CATEGOTY = @c AND h.PRODUCT_FAMILY = @f AND h.IS_DELETED = 0
            ORDER BY h.MATNR, h.EDITION,
                     CASE fvp.TYPE_PARAMETER WHEN 'M' THEN 0 WHEN 'O' THEN 1 WHEN 'I' THEN 2 ELSE 3 END, mp.PARAMETER_NAME",
            P2("@c", cat, "@f", fam), ct);
        if (!pq.Success || pq.DTResults == null) return (null, $"Error reading products: {pq.Message}");

        Specs_SearchProduct? current = null;
        foreach (DataRow r in pq.DTResults.Rows)
        {
            var matnr = GetString(r, "MATNR").Trim();
            var ed    = GetString(r, "EDITION").Trim();
            if (current == null || current.Header.Matnr != matnr || current.Header.Edition != ed)
            {
                current = new Specs_SearchProduct { Header = MapSpecHeaderRow(r) };
                data.Products.Add(current);
            }
            current.Parameters.Add(new SPEC_MATNR_PARAMETER
            {
                Matnr                 = matnr,
                Edition               = ed,
                KeyParameter          = GetString(r, "KEY_PARAMETER").Trim(),
                ParameterName         = GetString(r, "PARAMETER_NAME").Trim(),
                ParameterCategoryKey  = GetString(r, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(r, "CATEGORY_PAR_NAME").Trim(),
                TypeParameter         = GetString(r, "TYPE_PARAMETER").Trim(),
                ValueType             = Specs_NormValueType(GetString(r, "VALUE_TYPE")),
                ValueMax              = GetDecimalNullable(r, "VALUE_MAX"),
                ValueTyp              = GetDecimalNullable(r, "VALUE_TYP"),
                ValueMin              = GetDecimalNullable(r, "VALUE_MIN"),
                ValueText             = Specs_NullIfBlank(GetString(r, "VALUE_TEXT")),
                Um                    = GetString(r, "UM").Trim(),
                UmParameter           = GetString(r, "UM_PARAMETER").Trim(),
                DefaultUm             = GetString(r, "PARAMETER_DEFAULT_UM").Trim(),
                DataSource            = GetString(r, "DATA_SOURCE").Trim(),
                ModifiedBy            = GetString(r, "MODIFIED_BY").Trim(),
                ModifiedDate          = GetDateTime(r, "MODIFIED_DATE")
            });
        }

        // 3 — list values of the family's list parameters (disabled ones too: stored data may use them)
        var lq = await _db.SelectAsync(@"
            SELECT v.KEY_PARAMETER, v.VALUE_CODE, v.VALUE_DESCRIPTION, v.SORT_ORDER, v.IS_DELETED
            FROM   dbo.MAS_SPEC_PARAMETER_VALUE v
            JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fvp ON fvp.KEY_PARAMETER = v.KEY_PARAMETER AND fvp.FAM_KEY = @f AND fvp.IS_DELETED = 0
            ORDER BY v.KEY_PARAMETER, v.SORT_ORDER, v.VALUE_CODE",
            P("@f", fam), ct);
        if (!lq.Success || lq.DTResults == null) return (null, $"Error reading list values: {lq.Message}");
        foreach (DataRow r in lq.DTResults.Rows)
        {
            var key = GetString(r, "KEY_PARAMETER").Trim();
            if (!data.ListValues.TryGetValue(key, out var list)) data.ListValues[key] = list = [];
            list.Add(new SPEC_VALUE_OPTION
            {
                ValueCode        = GetString(r, "VALUE_CODE").Trim(),
                ValueDescription = GetString(r, "VALUE_DESCRIPTION").Trim(),
                SortOrder        = GetInt(r, "SORT_ORDER"),
                IsActive         = !GetBoolean(r, "IS_DELETED")
            });
        }
        foreach (var p in data.Products.SelectMany(x => x.Parameters))
            p.ValueOptions = data.ListValues.TryGetValue(p.KeyParameter, out var opts) && p.ValueType == SPECS_VT_LIST ? opts : [];

        // 4 — active conversions: variant → base unit (value_in_base = value × FACTOR)
        var cq = await _db.SelectAsync("SELECT UM_FROM, UM_TO, FACTOR FROM dbo.MAS_SPEC_UM_CONVERSION WHERE IS_DELETED = 0", null, ct);
        if (!cq.Success || cq.DTResults == null) return (null, $"Error reading unit conversions: {cq.Message}");
        foreach (DataRow r in cq.DTResults.Rows)
        {
            var factor = GetDecimalNullable(r, "FACTOR");
            if (factor is > 0) data.Conversions[GetString(r, "UM_FROM").Trim()] = (GetString(r, "UM_TO").Trim(), factor.Value);
        }

        return (data, null);
    }

    /// <summary>Factor to convert a value from <paramref name="fromUm"/> to <paramref name="toUm"/>, or null when the units
    /// do not share a base unit. Units are case-sensitive (mH ≠ MH).</summary>
    private static decimal? Specs_ConversionFactor(string fromUm, string toUm, Dictionary<string, (string To, decimal Factor)> conversions)
    {
        if (string.Equals(fromUm, toUm, StringComparison.Ordinal)) return 1m;
        var (baseFrom, fFrom) = conversions.TryGetValue(fromUm, out var a) ? a : (fromUm, 1m);
        var (baseTo,   fTo)   = conversions.TryGetValue(toUm,   out var b) ? b : (toUm,   1m);
        return string.Equals(baseFrom, baseTo, StringComparison.Ordinal) ? fFrom / fTo : null;
    }

    private readonly record struct Specs_ConvertedRow(bool Convertible, decimal? Min, decimal? Typ, decimal? Max);

    /// <summary>MIN / TYP / MAX of a numeric row converted from its UM_PARAMETER to the default UM.</summary>
    private static Specs_ConvertedRow Specs_ConvertRow(SPEC_MATNR_PARAMETER p, string defaultUm, Dictionary<string, (string To, decimal Factor)> conversions)
    {
        var unit   = string.IsNullOrWhiteSpace(p.UmParameter) ? defaultUm : p.UmParameter;
        var factor = Specs_ConversionFactor(unit, defaultUm, conversions);
        if (factor == null) return new(false, null, null, null);
        return new(true, Specs_NormDecimal(p.ValueMin * factor), Specs_NormDecimal(p.ValueTyp * factor), Specs_NormDecimal(p.ValueMax * factor));
    }

    /// <summary>Rounds a converted value to 6 decimals and drops trailing zeros (4000.0000000000 → 4000).</summary>
    private static decimal? Specs_NormDecimal(decimal? v)
        => v.HasValue
            ? decimal.Parse(Math.Round(v.Value, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : null;

    private static string Specs_Fmt(decimal? v) => v.HasValue ? v.Value.ToString("0.######", CultureInfo.InvariantCulture) : "–";

    private static SPEC_SEARCH_FILTER_RESULT Specs_EvaluateFilter(
        SPEC_SEARCH_FILTER filter, Specs_SearchParamDef def, string mode, SPEC_MATNR_PARAMETER? p, Specs_SearchData d)
    {
        var fr = new SPEC_SEARCH_FILTER_RESULT
        {
            KeyParameter  = def.KeyParameter,
            ParameterName = def.ParameterName,
            IsReviewed    = p?.IsManual == true
        };
        SPEC_SEARCH_FILTER_RESULT NoData(string reason) { fr.Status = SPEC_FILTER_STATUS.NoData; fr.Reason = reason; return fr; }
        SPEC_SEARCH_FILTER_RESULT Result(bool ok)       { fr.Status = ok ? SPEC_FILTER_STATUS.Match : SPEC_FILTER_STATUS.NoMatch; return fr; }

        if (p == null) return NoData("Parameter row missing for this product.");

        switch (def.ValueType)
        {
            case SPECS_VT_LIST:
            {
                if (string.IsNullOrWhiteSpace(p.ValueText)) return NoData("No value.");
                var list  = d.ListValues.TryGetValue(def.KeyParameter, out var lv) ? lv : [];
                var match = Specs_MatchListValue(p.ValueText, list, activeOnly: false);
                if (match == null) { fr.ProductValue = p.ValueText.Trim(); return NoData($"'{p.ValueText.Trim()}' is not in the value list."); }
                fr.ProductValue = match.ValueCode;
                var wanted = (filter.Values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                return Result(wanted.Count == 0 || wanted.Any(v => string.Equals(v.Trim(), match.ValueCode, StringComparison.OrdinalIgnoreCase)));
            }

            case SPECS_VT_TEXT:
            {
                if (string.IsNullOrWhiteSpace(p.ValueText)) return NoData("No value.");
                fr.ProductValue = p.ValueText.Trim();
                var needle = filter.Contains?.Trim();
                return Result(string.IsNullOrEmpty(needle) || fr.ProductValue.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            default:
            {
                if (!(p.ValueMin.HasValue || p.ValueTyp.HasValue || p.ValueMax.HasValue))
                    return NoData(string.IsNullOrWhiteSpace(p.ValueText) ? "No value." : $"Text '{p.ValueText.Trim()}' instead of a number.");
                var c = Specs_ConvertRow(p, def.DefaultUm, d.Conversions);
                if (!c.Convertible) return NoData($"Unit '{p.UmParameter}' cannot be converted to '{def.DefaultUm}'.");
                fr.ProductValue = c.Typ.HasValue && !c.Min.HasValue && !c.Max.HasValue
                    ? $"{Specs_Fmt(c.Typ)} {def.DefaultUm}"
                    : $"{Specs_Fmt(c.Min)} / {Specs_Fmt(c.Typ)} / {Specs_Fmt(c.Max)} {def.DefaultUm}";

                if (!filter.From.HasValue && !filter.To.HasValue) return Result(true);   // only "has data"

                if (mode == SPECS_MODE_COVERS)
                {
                    if (filter.From.HasValue && !c.Min.HasValue) return NoData("No MIN value to check the range.");
                    if (filter.To.HasValue   && !c.Max.HasValue) return NoData("No MAX value to check the range.");
                    return Result((!filter.From.HasValue || c.Min <= filter.From) && (!filter.To.HasValue || c.Max >= filter.To));
                }

                // WITHIN: TYP inside [From, To]; without TYP, MIN–MAX overlaps [From, To]
                if (c.Typ.HasValue)
                    return Result((!filter.From.HasValue || c.Typ >= filter.From) && (!filter.To.HasValue || c.Typ <= filter.To));
                return Result((!filter.From.HasValue || !c.Max.HasValue || c.Max >= filter.From)
                           && (!filter.To.HasValue   || !c.Min.HasValue || c.Min <= filter.To));
            }
        }
    }
}
