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
    public bool         CirClosed         { get; set; }  // true if the CIR ended closed (all mandatory informed)
    public bool         DeliverableCreated { get; set; } // true if the FORM_CIR deliverable was created by this import
    public bool         Skipped           { get; set; }  // true if not imported because the FORM_CIR deliverable already exists
    public bool         Success           { get; set; }
    public string       ErrorMessage      { get; set; } = string.Empty;
}

/// <summary>Header of a CIR (MAS_SPEC_CIR_HEADER + category/family names).</summary>
public class CIR_HEADER
{
    public string    OpportunityLineId    { get; set; } = string.Empty;
    public string    CategoryKey          { get; set; } = string.Empty;  // CAT_KEY
    public string    CategoryName         { get; set; } = string.Empty;  // MAS_SPEC_CATEGORY.CAT_NAME
    public string    FamilyKey            { get; set; } = string.Empty;  // FAM_KEY
    public string    FamilyName           { get; set; } = string.Empty;  // MAS_SPEC_FAMILY.FAM_NAME
    public string    FileName             { get; set; } = string.Empty;
    public DateOnly? ExtractionDate       { get; set; }
    public string    ClientDetected       { get; set; } = string.Empty;
    public string    CreatedBy            { get; set; } = string.Empty;
    public DateTime? CreatedDate          { get; set; }
    public string    ModifiedBy           { get; set; } = string.Empty;
    public DateTime? ModifiedDate         { get; set; }
    public bool      IsDeliverableCreated { get; set; }                   // Is_Deliverable_Created
    public DateTime? ClosedDate           { get; set; }                   // NULL = open
    public string    ClosedBy             { get; set; } = string.Empty;
    public int       TimesReopened        { get; set; }
    public bool      IsClosed             => ClosedDate.HasValue;
}

/// <summary>One parameter of a CIR (MAS_SPEC_CIR_PARAMETERS + catalog info).</summary>
public class CIR_PARAMETER
{
    public string    KeyParameter  { get; set; } = string.Empty;
    public string    ParameterName { get; set; } = string.Empty;  // MAS_SPEC_PARAMETER.PARAMETER_NAME
    public string    TypeParameter { get; set; } = string.Empty;  // M / O / I (MAS_SPEC_FAMILY_VS_PARAMETER)
    public string    ValueType     { get; set; } = "N";           // N numeric · L list · T free text (MAS_SPEC_PARAMETER.VALUE_TYPE) — read only
    public string    DefaultUm     { get; set; } = string.Empty;  // PARAMETER_DEFAULT_UM
    public string    ParameterCategoryKey  { get; set; } = string.Empty;  // MAS_SPEC_PARAMETER.PARAMETER_CATEGORY
    public string    ParameterCategoryName { get; set; } = string.Empty;  // MAS_SPEC_PARAMETER_CATEGORY.CATEGORY_PAR_NAME
    public decimal?  ValueMin      { get; set; }
    public decimal?  ValueTyp      { get; set; }
    public string?   ValueTypText  { get; set; }                  // L = a ValueOptions code
    public List<SPEC_VALUE_OPTION> ValueOptions { get; set; } = []; // picking list for ValueTypText when ValueType = L (filled by CIR_GetDetail)
    public decimal?  ValueMax      { get; set; }
    public string?   Um            { get; set; }                  // unit as written in the document — READ ONLY (never saved by CIR_SaveChanges)
    public string    UmParameter   { get; set; } = string.Empty;  // UM_PARAMETER — unit of the value, edited with the picking list
    public List<SPEC_UNIT_OPTION> UnitOptions { get; set; } = []; // picking list for UmParameter (filled by CIR_GetDetail)
    public string    DataSource    { get; set; } = string.Empty;  // 'DOC' | 'MANUAL'
    public string    ModifiedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate  { get; set; }

    public bool IsMandatory => string.Equals(TypeParameter, "M", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if the document unit differs from UM_PARAMETER — to be reviewed by an engineer.</summary>
    public bool UmDiffers => !string.IsNullOrWhiteSpace(Um)
                             && !string.Equals(Um.Trim(), UmParameter?.Trim(), StringComparison.Ordinal);

    public bool IsManual => string.Equals(DataSource, "MANUAL", StringComparison.OrdinalIgnoreCase);

    /// <summary>Informed for the mandatory coverage / auto-close, by ValueType (UM does not count):
    /// N → MIN/TYP/MAX · L → text in the list · T → text.</summary>
    public bool HasValue => ValueType switch
    {
        "L" => ValueOptions.Any(o => string.Equals(o.ValueCode, ValueTypText?.Trim(), StringComparison.OrdinalIgnoreCase)),
        "T" => !string.IsNullOrWhiteSpace(ValueTypText),
        _   => ValueMin.HasValue || ValueTyp.HasValue || ValueMax.HasValue
    };

    /// <summary>List parameter whose text (from the document) is not a value of the list — the engineer must choose one.</summary>
    public bool TextNotInList => ValueType == "L" && !string.IsNullOrWhiteSpace(ValueTypText) && !HasValue;

    /// <summary>Value read from the document and not reviewed yet (DATA_SOURCE = DOC with some value or unit).</summary>
    public bool NeedsReview => !IsManual && (ValueMin.HasValue || ValueTyp.HasValue || ValueMax.HasValue
                                             || !string.IsNullOrWhiteSpace(ValueTypText) || !string.IsNullOrWhiteSpace(Um));
}

/// <summary>Full CIR for the UI: header + parameters + warnings + coverage + permission flags.</summary>
public class CIR_DETAIL
{
    public CIR_HEADER          Header     { get; set; } = new();
    public List<CIR_PARAMETER> Parameters { get; set; } = [];
    public List<CIR_WARNING>   Warnings   { get; set; } = [];

    // FORM_CIR deliverable (TRA_PROJECTS_DELIVERABLES, PCA/PCA_1, DELIVERABLE_CREATION_TYPE='FORM_CIR')
    public int?   FormDeliverableId     { get; set; }
    public string FormResponsibleUserId { get; set; } = string.Empty;

    // Coverage of mandatory parameters
    public int          MandatoryTotal   { get; set; }
    public int          MandatoryFilled  { get; set; }
    public List<string> MissingMandatory { get; set; } = [];  // KEY_PARAMETER of empty mandatory parameters
    public bool         CanBeClosed      => MissingMandatory.Count == 0;

    // Permission flags for the requesting user (computed by CIR_GetDetail)
    public bool CanEdit   { get; set; }  // is responsible AND CIR open
    public bool CanReopen { get; set; }  // is responsible AND CIR closed
}

/// <summary>One row of MAS_SPEC_CIR_WARNINGS (informative only).</summary>
public class CIR_WARNING
{
    public string   OpportunityLineId { get; set; } = string.Empty;
    public int      WarningSeq        { get; set; }
    public string   WarningSource     { get; set; } = string.Empty;  // 'DOC' | 'IMPORT'
    public string   WarningText       { get; set; } = string.Empty;
    public string   CreatedBy         { get; set; } = string.Empty;
    public DateTime CreatedDate       { get; set; } = DateTime.MinValue;
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
    ///   - Is_Deliverable_Created = 1 (FORM_CIR deliverable exists) → file skipped, DB untouched
    ///     (Skipped=true, Success=true, reason in Warnings).
    ///   - CIR closed → Success=false, DB untouched.
    ///   - OPPORTUNITY_LINE_ID not found → INSERT header (trigger TR_CIR_HDR_INIT_PARAMS
    ///     auto-creates parameter rows with DATA_SOURCE='MANUAL').
    ///   - Already exists → UPDATE FILENAME, EXTRACTION_DATE, CLIENT_DETECTED, MODIFIED_BY/DATE.
    ///     CAT_KEY and FAM_KEY are never changed on update (they define the parameter structure).
    ///
    /// Parameter logic per file:
    ///   - type_parameter='X' (client extras not in catalog) → skipped.
    ///   - Parameters absent from MAS_SPEC_CIR_PARAMETERS for that opportunity → skipped + warning.
    ///   - Parameters where all values are null/empty → skipped silently.
    ///   - value_typ: numeric parameter (VALUE_TYPE N) → tried as decimal (InvariantCulture), on failure
    ///     VALUE_TYP_TEXT + IMPORT warning; list / free-text parameter (L / T) → always VALUE_TYP_TEXT.
    ///     List texts matching a list value (case-insensitive) are stored with the list spelling, otherwise
    ///     as read + warning; texts longer than 50 characters are truncated + warning. Never blocks.
    ///   - UM (unit as written in the document): null/empty in JSON → existing DB value preserved
    ///     (COALESCE in SQL); longer than 20 characters → truncated + warning.
    ///   - UM_PARAMETER: ALWAYS the default UM of the parameter (MAS_SPEC_PARAMETER), even when the
    ///     document unit differs — an engineer reviews it in the UI. Document unit different from the
    ///     default UM (allowed variant or unknown unit) → IMPORT warning. Unit problems never block
    ///     the import.
    ///   - DATA_SOURCE is always set to 'DOC' for all parameters updated by this process.
    ///
    /// Warnings logic per file (MAS_SPEC_CIR_WARNINGS):
    ///   - All existing warnings of the opportunity are deleted and replaced, in the same
    ///     transaction as the parameter updates.
    ///   - header.warnings from JSON → WARNING_SOURCE='DOC' (first), then warnings generated
    ///     by this import → WARNING_SOURCE='IMPORT'.
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

    /// <summary>
    /// Returns the warnings stored for a CIR (MAS_SPEC_CIR_WARNINGS), ordered by WARNING_SEQ.
    /// Empty list if the CIR has no warnings.
    /// </summary>
    public async Task<OperationResult<List<CIR_WARNING>>> CIR_GetWarnings(
                                                                            string OPPORTUNITY_LINE_ID,
                                                                            CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT OPPORTUNITY_LINE_ID, WARNING_SEQ, WARNING_SOURCE, WARNING_TEXT, CREATED_BY, CREATED_DATE
            FROM   dbo.MAS_SPEC_CIR_WARNINGS
            WHERE  OPPORTUNITY_LINE_ID = @oppLineId
            ORDER BY WARNING_SEQ";

        try
        {
            var queryResult = await _db.SelectAsync(sql,
                new[] { new SqlParameter("@oppLineId", OPPORTUNITY_LINE_ID.Trim()) },
                cancellationToken);

            if (!queryResult.Success || queryResult.DTResults == null)
                return OperationResult<List<CIR_WARNING>>.Fail($"Error: {queryResult.Message}");

            var list = new List<CIR_WARNING>();
            foreach (DataRow row in queryResult.DTResults.Rows)
            {
                list.Add(new CIR_WARNING
                {
                    OpportunityLineId = GetString(row, "OPPORTUNITY_LINE_ID").Trim(),
                    WarningSeq        = GetInt(row, "WARNING_SEQ"),
                    WarningSource     = GetString(row, "WARNING_SOURCE").Trim(),
                    WarningText       = GetString(row, "WARNING_TEXT"),
                    CreatedBy         = GetString(row, "CREATED_BY").Trim(),
                    CreatedDate       = row["CREATED_DATE"] is DateTime d ? d : DateTime.MinValue
                });
            }

            return OperationResult<List<CIR_WARNING>>.Ok(list);
        }
        catch (Exception ex)
        {
            return OperationResult<List<CIR_WARNING>>.Fail($"Error: {ex.Message}");
        }
    }

    // ── Phase 2 — review, auto-close, reopen, FORM_CIR deliverable ────────────

    private const string CIR_FORM_STATUS_ID     = "PCA";
    private const string CIR_FORM_SGATE_ID      = "PCA_1";
    private const string CIR_FORM_CREATION_TYPE = "FORM_CIR";
    private const string CIR_FORM_NAME          = "CIR Technical Review (AI) standard form generation";
    private const string CIR_FORM_CRITERIA      = "All mandatory parameters should be populated";
    private const string CIR_FORM_RESP_JOB      = "F_APP_ENG";
    private const string CIR_FORM_ACC_JOB       = "BUD";

    /// <summary>
    /// Returns the full CIR of an opportunity line: header, all active parameters (with name,
    /// type M/O/I and default UM), warnings, mandatory coverage and the permission flags
    /// CanEdit / CanReopen for <paramref name="userRequester"/>.
    /// Read-only — no permission check. If <paramref name="userRequester"/> is null/empty
    /// both flags are false.
    /// </summary>
    public async Task<OperationResult<CIR_DETAIL>> CIR_GetDetail(
                                                                    string OPPORTUNITY_LINE_ID,
                                                                    string? userRequester = null,
                                                                    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OPPORTUNITY_LINE_ID))
            return OperationResult<CIR_DETAIL>.Fail("OPPORTUNITY_LINE_ID is required.");

        var oppLineId = OPPORTUNITY_LINE_ID.Trim();

        try
        {
            var header = await CIR_LoadHeaderAsync(oppLineId, cancellationToken);
            if (header.Error != null)
                return OperationResult<CIR_DETAIL>.Fail(header.Error);
            if (header.Header == null)
                return OperationResult<CIR_DETAIL>.Fail($"CIR '{oppLineId}' not found.");

            var parameters = await CIR_LoadParametersAsync(oppLineId, cancellationToken);
            if (parameters.Error != null)
                return OperationResult<CIR_DETAIL>.Fail(parameters.Error);

            // UM_PARAMETER picking list per parameter
            var unitOptions = await CIR_LoadUnitOptionsAsync(oppLineId, cancellationToken);
            if (unitOptions.Error != null)
                return OperationResult<CIR_DETAIL>.Fail(unitOptions.Error);
            foreach (var p in parameters.Parameters)
                p.UnitOptions = unitOptions.Options.TryGetValue(p.KeyParameter, out var opts) ? opts : [];

            // VALUE_TYP_TEXT picking list of the list parameters (needed before the coverage: HasValue uses it)
            var valueOptions = await CIR_LoadValueOptionsAsync(oppLineId, cancellationToken);
            if (valueOptions.Error != null)
                return OperationResult<CIR_DETAIL>.Fail(valueOptions.Error);
            foreach (var p in parameters.Parameters)
                p.ValueOptions = valueOptions.Options.TryGetValue(p.KeyParameter, out var vopts) ? vopts : [];

            var warnings = await CIR_GetWarnings(oppLineId, cancellationToken);
            if (!warnings.Success)
                return OperationResult<CIR_DETAIL>.Fail(warnings.ErrorMessage);

            var form = await CIR_GetFormDeliverableAsync(oppLineId, cancellationToken);
            if (form.Error != null)
                return OperationResult<CIR_DETAIL>.Fail(form.Error);

            var mandatory = parameters.Parameters.Where(p => p.IsMandatory).ToList();
            bool isResponsible = !string.IsNullOrWhiteSpace(userRequester)
                                 && CIR_IsAllowed(userRequester, form.ResponsibleUserId);

            var detail = new CIR_DETAIL
            {
                Header                = header.Header,
                Parameters            = parameters.Parameters,
                Warnings              = warnings.Data ?? [],
                FormDeliverableId     = form.DeliverableId,
                FormResponsibleUserId = form.ResponsibleUserId,
                MandatoryTotal        = mandatory.Count,
                MandatoryFilled       = mandatory.Count(p => p.HasValue),
                MissingMandatory      = mandatory.Where(p => !p.HasValue).Select(p => p.KeyParameter).ToList(),
                CanEdit               = isResponsible && !header.Header.IsClosed,
                CanReopen             = isResponsible &&  header.Header.IsClosed
            };

            return OperationResult<CIR_DETAIL>.Ok(detail);
        }
        catch (Exception ex)
        {
            return OperationResult<CIR_DETAIL>.Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the parameter values edited in the UI.
    /// Validations (any failure → Success=false, DB untouched):
    ///   1. CIR and OPPORTUNITY_LINE_ID informed.  2. CIR exists.
    ///   3. userRequester is the RESPONSIBLE_USER_ID of the FORM_CIR deliverable (or "System").
    ///   4. CIR is open.  5. Every KeyParameter exists for the CIR (no new parameters are created).
    ///   6. No parameter has both ValueTyp and ValueTypText.  7. Text lengths fit the columns.
    ///   8. A changed UmParameter must be an active allowed unit of the parameter (picking list,
    ///      case-sensitive); an unchanged one is accepted even if it was disabled later.
    ///   9. Review rules by value type on every changed row (it becomes MANUAL): N no text · L active list value
    ///      (written with the list spelling), no numbers · T no numbers. Same rules as TR_CIR_PRM_VALIDATE.
    /// Only the parameters whose values or UmParameter differ from the DB are updated (DATA_SOURCE='MANUAL');
    /// the UM_PARAMETER change is audited by TR_CIR_PRM_AUDIT.
    /// Um (unit as written in the document) is READ ONLY and never saved. Empty UmParameter keeps the DB value.
    /// A save with no changes does nothing (and does not close).
    /// In the same transaction, if every mandatory parameter is informed the CIR is closed automatically.
    /// RecordsAffected = number of parameters updated. Message says if the CIR was closed.
    /// </summary>
    public async Task<SqlActionResult> CIR_SaveChanges(
                                                        CIR_DETAIL cirFromUi,
                                                        string userRequester = "System",
                                                        CancellationToken cancellationToken = default)
    {
        // 1 — input
        if (cirFromUi?.Header == null || string.IsNullOrWhiteSpace(cirFromUi.Header.OpportunityLineId))
            return CIR_Fail("CIR and Header.OpportunityLineId are required.");

        var oppLineId = cirFromUi.Header.OpportunityLineId.Trim();

        try
        {
            // 2 — exists   3 — permission   4 — open
            var check = await CIR_CheckWriteAccessAsync(oppLineId, userRequester, cancellationToken);
            if (check.Error != null) return CIR_Fail(check.Error);
            if (check.Header!.IsClosed)
                return CIR_Fail($"CIR '{oppLineId}' is closed — reopen it before editing.");

            // 5/6/7 — validate incoming parameters against DB and build the change list
            var current = await CIR_LoadParametersAsync(oppLineId, cancellationToken);
            if (current.Error != null) return CIR_Fail(current.Error);

            var unitOptions = await CIR_LoadUnitOptionsAsync(oppLineId, cancellationToken);
            if (unitOptions.Error != null) return CIR_Fail(unitOptions.Error);

            var valueOptions = await CIR_LoadValueOptionsAsync(oppLineId, cancellationToken);
            if (valueOptions.Error != null) return CIR_Fail(valueOptions.Error);

            var dbByKey = current.Parameters.ToDictionary(p => p.KeyParameter, StringComparer.OrdinalIgnoreCase);
            var errors  = new List<string>();
            var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changes = new List<CIR_PARAMETER>();

            foreach (var p in cirFromUi.Parameters ?? [])
            {
                var key = (p.KeyParameter ?? string.Empty).Trim();
                if (key.Length == 0)            { errors.Add("A parameter without KeyParameter was received."); continue; }
                if (!seen.Add(key))             { errors.Add($"Parameter '{key}' is duplicated."); continue; }
                if (!dbByKey.TryGetValue(key, out var db)) { errors.Add($"Parameter '{key}' does not exist in CIR '{oppLineId}'."); continue; }

                var typText     = CIR_NormText(p.ValueTypText);
                var listValues  = valueOptions.Options.TryGetValue(key, out var vopts) ? vopts : null;
                if (db.ValueType == SPECS_VT_LIST && Specs_MatchListValue(typText, listValues, activeOnly: false) is { } listMatch)
                    typText = listMatch.ValueCode;   // "smd" → "SMD": same value, list spelling
                // Um (document unit) is read only and ignored. UmParameter empty → keep the DB value.
                var umParameter = CIR_NormText(p.UmParameter) ?? db.UmParameter;
                bool umChanged  = !string.Equals(umParameter, db.UmParameter, StringComparison.Ordinal);

                if (p.ValueTyp.HasValue && typText != null)
                    errors.Add($"Parameter '{key}': ValueTyp and ValueTypText cannot both be informed.");
                if (typText?.Length > 50)
                    errors.Add($"Parameter '{key}': ValueTypText exceeds 50 characters.");

                // a changed UM_PARAMETER must be one of the active options of the picking list (case-sensitive)
                if (Specs_ValidateUmParameter(key, umParameter, db.UmParameter,
                        unitOptions.Options.TryGetValue(key, out var opts) ? opts : null) is { } umError)
                    errors.Add(umError);

                bool valuesChanged = p.ValueMin != db.ValueMin
                                  || p.ValueTyp != db.ValueTyp
                                  || p.ValueMax != db.ValueMax
                                  || !string.Equals(typText, CIR_NormText(db.ValueTypText), StringComparison.Ordinal);
                bool changed = valuesChanged || umChanged;

                // the row becomes MANUAL (reviewed): review rules by value type (same as TR_CIR_PRM_VALIDATE).
                // A value disabled later in the list is accepted only if the row is already MANUAL and its values do not change.
                if (changed)
                {
                    if (Specs_ValidateReviewedValue($"Parameter '{key}'", db.ValueType, p.ValueMin, p.ValueTyp, p.ValueMax, typText,
                            listValues, allowInactive: db.IsManual && !valuesChanged, out var reviewedText) is { } reviewError)
                    {
                        errors.Add(reviewError);
                        continue;
                    }
                    typText = reviewedText;
                }

                if (changed)
                    changes.Add(new CIR_PARAMETER
                    {
                        KeyParameter = key,
                        ValueMin     = p.ValueMin,
                        ValueTyp     = p.ValueTyp,
                        ValueTypText = typText,
                        ValueMax     = p.ValueMax,
                        UmParameter  = umParameter
                    });
            }

            if (errors.Count > 0)
                return CIR_Fail(string.Join(" | ", errors));

            if (changes.Count == 0)
                return new SqlActionResult { Success = true, Message = "No changes to save.", RecordsAffected = 0 };

            // Persist + auto-close in one transaction
            const string sqlTouchHeader = @"
                UPDATE dbo.MAS_SPEC_CIR_HEADER
                SET    MODIFIED_BY = @user, MODIFIED_DATE = SYSDATETIME()
                WHERE  OPPORTUNITY_LINE_ID = @oppLineId AND CLOSED_DATE IS NULL";

            const string sqlUpdateParam = @"
                UPDATE dbo.MAS_SPEC_CIR_PARAMETERS
                SET VALUE_MAX      = @valueMax,
                    VALUE_TYP      = @valueTyp,
                    VALUE_TYP_TEXT = @valueTypText,
                    VALUE_MIN      = @valueMin,
                    UM_PARAMETER   = @umParameter,
                    DATA_SOURCE    = 'MANUAL',
                    MODIFIED_BY    = @user,
                    MODIFIED_DATE  = GETDATE()
                WHERE OPPORTUNITY_LINE_ID = @oppLineId AND KEY_PARAMETER = @keyParameter AND IS_DELETED = 0";

            bool closed = false;
            var txResult = await _db.ExecuteInTransactionAsync(async (tx, ct) =>
            {
                await Specs_SetSessionUserAsync(userRequester, tx, ct);

                // Locks the header row and re-checks it is still open (race with close/reopen)
                var touch = await _db.NonQueryAsync(sqlTouchHeader,
                    new[] { new SqlParameter("@oppLineId", oppLineId), new SqlParameter("@user", userRequester) },
                    transaction: tx, ct: ct);
                if (!touch.Success) throw new InvalidOperationException($"Error updating header: {touch.Message}");
                if (touch.RecordsAffected == 0) throw new InvalidOperationException($"CIR '{oppLineId}' was closed by another process.");

                foreach (var c in changes)
                {
                    var upd = await _db.NonQueryAsync(sqlUpdateParam,
                        new[]
                        {
                            new SqlParameter("@oppLineId",    oppLineId),
                            new SqlParameter("@keyParameter", c.KeyParameter),
                            new SqlParameter("@valueMax",     (object?)c.ValueMax     ?? DBNull.Value),
                            new SqlParameter("@valueTyp",     (object?)c.ValueTyp     ?? DBNull.Value),
                            new SqlParameter("@valueTypText", (object?)c.ValueTypText ?? DBNull.Value),
                            new SqlParameter("@valueMin",     (object?)c.ValueMin     ?? DBNull.Value),
                            new SqlParameter("@umParameter",  c.UmParameter),
                            new SqlParameter("@user",         userRequester)
                        },
                        transaction: tx, ct: ct);

                    if (!upd.Success) throw new InvalidOperationException($"Error updating parameter '{c.KeyParameter}': {upd.Message}");
                    if (upd.RecordsAffected != 1) throw new InvalidOperationException($"Parameter '{c.KeyParameter}' not found or deleted.");
                }

                closed = await CIR_TryAutoCloseAsync(oppLineId, userRequester, tx, ct);
            }, cancellationToken);

            if (!txResult.Success)
                return CIR_Fail($"Error saving CIR: {txResult.Message}");

            return new SqlActionResult
            {
                Success         = true,
                RecordsAffected = changes.Count,
                Message         = closed
                                    ? $"{changes.Count} parameter(s) saved. All mandatory parameters informed — CIR closed."
                                    : $"{changes.Count} parameter(s) saved."
            };
        }
        catch (Exception ex)
        {
            return CIR_Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Reopens a closed CIR: CLOSED_DATE/CLOSED_BY = NULL, TIMES_REOPENED + 1.
    /// Only the RESPONSIBLE_USER_ID of the FORM_CIR deliverable (or "System") can reopen.
    /// Fails if the CIR is already open.
    /// </summary>
    public async Task<SqlActionResult> CIR_Reopen(
                                                    string OPPORTUNITY_LINE_ID,
                                                    string userRequester = "System",
                                                    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OPPORTUNITY_LINE_ID))
            return CIR_Fail("OPPORTUNITY_LINE_ID is required.");

        var oppLineId = OPPORTUNITY_LINE_ID.Trim();

        try
        {
            var check = await CIR_CheckWriteAccessAsync(oppLineId, userRequester, cancellationToken);
            if (check.Error != null) return CIR_Fail(check.Error);
            if (!check.Header!.IsClosed)
                return CIR_Fail($"CIR '{oppLineId}' is already open.");

            const string sqlReopen = @"
                UPDATE dbo.MAS_SPEC_CIR_HEADER
                SET    CLOSED_DATE    = NULL,
                       CLOSED_BY      = NULL,
                       TIMES_REOPENED = TIMES_REOPENED + 1,
                       MODIFIED_BY    = @user,
                       MODIFIED_DATE  = SYSDATETIME()
                WHERE  OPPORTUNITY_LINE_ID = @oppLineId AND CLOSED_DATE IS NOT NULL";

            var result = await _db.NonQueryAsync(sqlReopen,
                new[] { new SqlParameter("@oppLineId", oppLineId), new SqlParameter("@user", userRequester) },
                ct: cancellationToken);

            if (!result.Success)
                return CIR_Fail($"Error reopening CIR: {result.Message}");
            if (result.RecordsAffected == 0)
                return CIR_Fail($"CIR '{oppLineId}' is already open.");

            result.Message = $"CIR '{oppLineId}' reopened.";
            return result;
        }
        catch (Exception ex)
        {
            return CIR_Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// "Validate" action: the responsible of the FORM_CIR deliverable confirms values read from the document
    /// without changing them (DATA_SOURCE 'DOC' → 'MANUAL', audited by TR_CIR_PRM_AUDIT). Only rows pending review
    /// (NeedsReview: DOC with a value or a document unit) are validated; keyParameters null/empty = all of them.
    /// The CIR must be open. Each row must comply with the review rules of its value type (N: no text ·
    /// L: active list value, no numbers · T: no numbers); rows that do not comply are NOT validated and are
    /// listed in Message (fix them with CIR_SaveChanges). Values do not change, so the coverage does not change.
    /// RecordsAffected = parameters validated. Success = false only on a technical error or a wrong request.
    /// </summary>
    public async Task<SqlActionResult> CIR_ValidateParameters(
                                                                string OPPORTUNITY_LINE_ID,
                                                                List<string>? keyParameters = null,
                                                                string userRequester = "System",
                                                                CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OPPORTUNITY_LINE_ID))
            return CIR_Fail("OPPORTUNITY_LINE_ID is required.");

        var oppLineId = OPPORTUNITY_LINE_ID.Trim();

        try
        {
            var check = await CIR_CheckWriteAccessAsync(oppLineId, userRequester, cancellationToken);
            if (check.Error != null) return CIR_Fail(check.Error);
            if (check.Header!.IsClosed)
                return CIR_Fail($"CIR '{oppLineId}' is closed — reopen it before validating.");

            var current = await CIR_LoadParametersAsync(oppLineId, cancellationToken);
            if (current.Error != null) return CIR_Fail(current.Error);

            var valueOptions = await CIR_LoadValueOptionsAsync(oppLineId, cancellationToken);
            if (valueOptions.Error != null) return CIR_Fail(valueOptions.Error);
            foreach (var p in current.Parameters)
                p.ValueOptions = valueOptions.Options.TryGetValue(p.KeyParameter, out var vopts) ? vopts : [];

            var keys = (keyParameters ?? []).Where(k => !string.IsNullOrWhiteSpace(k))
                                            .Select(k => k.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = keys.Where(k => !current.Parameters.Any(p => string.Equals(p.KeyParameter, k, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0)
                return CIR_Fail($"Parameters not in CIR '{oppLineId}': {string.Join(", ", unknown)}.");

            var candidates = current.Parameters
                .Where(p => p.NeedsReview && (keys.Count == 0 || keys.Contains(p.KeyParameter)))
                .ToList();

            var valid    = new List<string>();
            var rejected = new List<string>();
            foreach (var p in candidates)
            {
                var error = Specs_ValidateReviewedValue($"Parameter '{p.KeyParameter}'", p.ValueType,
                                p.ValueMin, p.ValueTyp, p.ValueMax, p.ValueTypText, p.ValueOptions, allowInactive: false, out _);
                if (error == null) valid.Add(p.KeyParameter); else rejected.Add(error);
            }

            int written = 0;
            if (valid.Count > 0)
            {
                var sql = @"
                    UPDATE dbo.MAS_SPEC_CIR_PARAMETERS
                    SET    DATA_SOURCE = 'MANUAL', MODIFIED_BY = @user, MODIFIED_DATE = GETDATE()
                    WHERE  OPPORTUNITY_LINE_ID = @oppLineId AND DATA_SOURCE <> 'MANUAL' AND IS_DELETED = 0
                      AND  KEY_PARAMETER IN (" + string.Join(", ", valid.Select((_, i) => $"@k{i}")) + ")";

                var tx = await _db.ExecuteInTransactionAsync(async (t, ct) =>
                {
                    await Specs_SetSessionUserAsync(userRequester, t, ct);
                    var prms = new List<SqlParameter> { new("@oppLineId", oppLineId), new("@user", userRequester) };
                    prms.AddRange(valid.Select((k, i) => new SqlParameter($"@k{i}", k)));

                    var r = await _db.NonQueryAsync(sql, prms.ToArray(), transaction: t, ct: ct);
                    if (!r.Success) throw new InvalidOperationException(r.Message);
                    written = r.RecordsAffected;
                }, cancellationToken);
                if (!tx.Success) return CIR_Fail($"Error validating CIR: {tx.Message}");
            }

            var message = candidates.Count == 0
                ? "No parameters pending review."
                : $"{written} parameter(s) validated."
                  + (rejected.Count > 0 ? $" {rejected.Count} not validated: {string.Join(" | ", rejected)}" : string.Empty);
            return new SqlActionResult { Success = true, RecordsAffected = written, Message = message };
        }
        catch (Exception ex)
        {
            return CIR_Fail($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates the FORM_CIR deliverable in TRA_PROJECTS_DELIVERABLES (PCA / PCA_1) for a CIR:
    ///   DELIVERABLE_ID = last DELIVERABLE_ID of PCA_1 + 10, DELIVERABLE_CREATION_TYPE='FORM_CIR',
    ///   name "CIR Technical Review", RESP job F_APP_ENG, ACC job BUD, no users assigned,
    ///   PATH_TO_SAVE copied from the last deliverable of PCA_1.
    /// Then sets MAS_SPEC_CIR_HEADER.Is_Deliverable_Created = 1 (same transaction).
    /// Called automatically by CIR_ImportFromJsonFolder; public so it can be retried manually.
    /// If a FORM_CIR deliverable already exists, only the flag is synchronised.
    /// </summary>
    public async Task<SqlActionResult> CIR_CreateFormDeliverable(
                                                                    string OPPORTUNITY_LINE_ID,
                                                                    string userRequester = "System",
                                                                    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(OPPORTUNITY_LINE_ID))
            return CIR_Fail("OPPORTUNITY_LINE_ID is required.");

        var oppLineId = OPPORTUNITY_LINE_ID.Trim();

        try
        {
            // 1 — CIR exists and deliverable not yet created
            var header = await CIR_LoadHeaderAsync(oppLineId, cancellationToken);
            if (header.Error != null) return CIR_Fail(header.Error);
            if (header.Header == null) return CIR_Fail($"CIR '{oppLineId}' not found.");
            if (header.Header.IsDeliverableCreated)
                return CIR_Fail($"FORM_CIR deliverable already created for '{oppLineId}'.");

            // 2 — action PCA / PCA_1 exists in TRA_PROJECTS_GATES (implies the project exists)
            var actionCheck = await _db.SelectAsync(SQL_Check_Action_Exists(),
                new[]
                {
                    new SqlParameter("@OppLineId", oppLineId),
                    new SqlParameter("@StatusId",  CIR_FORM_STATUS_ID),
                    new SqlParameter("@ActionId",  CIR_FORM_SGATE_ID)
                },
                cancellationToken);

            if (!actionCheck.Success || actionCheck.DTResults == null)
                return CIR_Fail($"Error checking action: {actionCheck.Message}");
            if (Convert.ToInt32(actionCheck.DTResults.Rows[0][0]) == 0)
                return CIR_Fail($"Action '{CIR_FORM_STATUS_ID}/{CIR_FORM_SGATE_ID}' not found for project '{oppLineId}'.");

            const string sqlSetFlag = @"
                UPDATE dbo.MAS_SPEC_CIR_HEADER
                SET    Is_Deliverable_Created = 1, MODIFIED_BY = @user, MODIFIED_DATE = SYSDATETIME()
                WHERE  OPPORTUNITY_LINE_ID = @oppLineId";

            var flagParams = () => new[] { new SqlParameter("@oppLineId", oppLineId), new SqlParameter("@user", userRequester) };

            // 3 — FORM_CIR already exists (flag out of sync) → only sync the flag
            var form = await CIR_GetFormDeliverableAsync(oppLineId, cancellationToken);
            if (form.Error != null) return CIR_Fail(form.Error);
            if (form.DeliverableId.HasValue)
            {
                var sync = await _db.NonQueryAsync(sqlSetFlag, flagParams(), ct: cancellationToken);
                if (!sync.Success) return CIR_Fail($"Error updating Is_Deliverable_Created: {sync.Message}");
                sync.Message = $"FORM_CIR deliverable {form.DeliverableId} already existed — flag synchronised.";
                return sync;
            }

            // 4 — MAX + INSERT in a single statement (locked) + flag, in one transaction
            const string sqlInsert = @"
                INSERT INTO dbo.TRA_PROJECTS_DELIVERABLES
                       (OPP_LINE_ID, STATUS_ID, SGATE_ID, DELIVERABLE_ID,
                        DELIVERABLE_STATUS_ID, ACCOUNTABLE_STATUS_ID, DELIVERABLE_CREATION_TYPE,
                        DELIVERABLE_NAME, DELIVERABLE_ACEPTANCE_CRITERIA,
                        PLANNED_START_DATE, PLANNED_END_DATE, ACTUAL_START_DATE, ACTUAL_END_DATE,
                        USER_START_DATE, USER_FINISH_DATE,
                        RESPONSIBLE_JOB_ID, ACCOUNTABLE_JOB_ID,
                        RESPONSIBLE_USER_ID, ACCOUNTABLE_USER_ID,
                        CREATED_BY, CREATED_DATE, MODIFIED_BY, MODIFIED_DATE,
                        PATH_TO_SAVE, NEXT_GATE_TRIGGERS, EFFORT_RATE)
                SELECT TOP 1
                        @oppLineId, @statusId, @sgateId, d.DELIVERABLE_ID + 10,
                        'NOTSTARTED', 'NOTSTARTED', @creationType,
                        @name, @criteria,
                        GETDATE(), GETDATE(), GETDATE(), GETDATE(),
                        GETDATE(), GETDATE(),
                        @respJob, @accJob,
                        '', '',
                        @user, GETDATE(), @user, GETDATE(),
                        d.PATH_TO_SAVE, 0, 1
                FROM   dbo.TRA_PROJECTS_DELIVERABLES d WITH (UPDLOCK, HOLDLOCK)
                WHERE  d.OPP_LINE_ID = @oppLineId AND d.STATUS_ID = @statusId AND d.SGATE_ID = @sgateId
                  AND  NOT EXISTS (SELECT 1 FROM dbo.TRA_PROJECTS_DELIVERABLES x
                                   WHERE x.OPP_LINE_ID = @oppLineId AND x.STATUS_ID = @statusId
                                     AND x.SGATE_ID = @sgateId AND x.DELIVERABLE_CREATION_TYPE = @creationType)
                ORDER BY d.DELIVERABLE_ID DESC";

            var txResult = await _db.ExecuteInTransactionAsync(async (tx, ct) =>
            {
                var ins = await _db.NonQueryAsync(sqlInsert,
                    new[]
                    {
                        new SqlParameter("@oppLineId",    oppLineId),
                        new SqlParameter("@statusId",     CIR_FORM_STATUS_ID),
                        new SqlParameter("@sgateId",      CIR_FORM_SGATE_ID),
                        new SqlParameter("@creationType", CIR_FORM_CREATION_TYPE),
                        new SqlParameter("@name",         CIR_FORM_NAME),
                        new SqlParameter("@criteria",     CIR_FORM_CRITERIA),
                        new SqlParameter("@respJob",      CIR_FORM_RESP_JOB),
                        new SqlParameter("@accJob",       CIR_FORM_ACC_JOB),
                        new SqlParameter("@user",         userRequester)
                    },
                    transaction: tx, ct: ct);

                if (!ins.Success) throw new InvalidOperationException($"Error inserting deliverable: {ins.Message}");
                if (ins.RecordsAffected != 1)
                    throw new InvalidOperationException(
                        $"No deliverables found in '{CIR_FORM_STATUS_ID}/{CIR_FORM_SGATE_ID}' for '{oppLineId}' (or FORM_CIR already exists).");

                var flag = await _db.NonQueryAsync(sqlSetFlag, flagParams(), transaction: tx, ct: ct);
                if (!flag.Success) throw new InvalidOperationException($"Error updating Is_Deliverable_Created: {flag.Message}");
            }, cancellationToken);

            if (!txResult.Success)
                return CIR_Fail($"Error creating FORM_CIR deliverable: {txResult.Message}");

            return new SqlActionResult { Success = true, RecordsAffected = 1, Message = $"FORM_CIR deliverable created for '{oppLineId}'." };
        }
        catch (Exception ex)
        {
            return CIR_Fail($"Error: {ex.Message}");
        }
    }

    // ── Phase 2 — private helpers ─────────────────────────────────────────────

    private const int CIR_LEN_VALUE_TYP_TEXT = 50;   // MAS_SPEC_CIR_PARAMETERS.VALUE_TYP_TEXT nvarchar(50)

    private static SqlActionResult CIR_Fail(string message) => new() { Success = false, Message = message };

    /// <summary>Trim; empty → null.</summary>
    private static string? CIR_NormText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    /// <summary>"System" always allowed; otherwise the user must be the (non-empty) responsible.
    /// Compares the login part (before '@') case-insensitively, so email or samaccountname both work.</summary>
    private static bool CIR_IsAllowed(string userRequester, string responsibleUserId)
    {
        if (string.Equals(userRequester?.Trim(), "System", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(userRequester) || string.IsNullOrWhiteSpace(responsibleUserId))
            return false;

        static string Login(string s) => s.Trim().Split('@')[0];
        return string.Equals(Login(userRequester), Login(responsibleUserId), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Loads the header and checks the user may write (responsible of FORM_CIR or "System").</summary>
    private async Task<(CIR_HEADER? Header, string? Error)> CIR_CheckWriteAccessAsync(
                                                                                        string oppLineId,
                                                                                        string userRequester,
                                                                                        CancellationToken ct)
    {
        var header = await CIR_LoadHeaderAsync(oppLineId, ct);
        if (header.Error != null)  return (null, header.Error);
        if (header.Header == null) return (null, $"CIR '{oppLineId}' not found.");

        var form = await CIR_GetFormDeliverableAsync(oppLineId, ct);
        if (form.Error != null) return (null, form.Error);

        if (!CIR_IsAllowed(userRequester, form.ResponsibleUserId))
        {
            if (!form.DeliverableId.HasValue)
                return (null, $"CIR '{oppLineId}' is read-only: FORM_CIR deliverable not created yet.");
            if (string.IsNullOrWhiteSpace(form.ResponsibleUserId))
                return (null, $"CIR '{oppLineId}' is read-only: no responsible assigned to the FORM_CIR deliverable.");
            return (null, $"User '{userRequester}' is not the responsible of the FORM_CIR deliverable of '{oppLineId}'.");
        }

        return (header.Header, null);
    }

    /// <summary>Header + category/family names. Header=null when not found.</summary>
    private async Task<(CIR_HEADER? Header, string? Error)> CIR_LoadHeaderAsync(string oppLineId, CancellationToken ct)
    {
        const string sql = @"
            SELECT h.OPPORTUNITY_LINE_ID, h.CAT_KEY, c.CAT_NAME, h.FAM_KEY, f.FAM_NAME,
                   h.FILENAME, h.EXTRACTION_DATE, h.CLIENT_DETECTED,
                   h.CREATED_BY, h.CREATED_DATE, h.MODIFIED_BY, h.MODIFIED_DATE,
                   ISNULL(h.Is_Deliverable_Created, 0) AS IS_DELIVERABLE_CREATED,
                   h.CLOSED_DATE, h.CLOSED_BY, h.TIMES_REOPENED
            FROM   dbo.MAS_SPEC_CIR_HEADER h
            LEFT JOIN dbo.MAS_SPEC_CATEGORY c ON c.CAT_KEY = h.CAT_KEY
            LEFT JOIN dbo.MAS_SPEC_FAMILY   f ON f.CAT_KEY = h.CAT_KEY AND f.FAM_KEY = h.FAM_KEY
            WHERE  h.OPPORTUNITY_LINE_ID = @oppLineId";

        var q = await _db.SelectAsync(sql, new[] { new SqlParameter("@oppLineId", oppLineId) }, ct);
        if (!q.Success || q.DTResults == null) return (null, $"Error reading CIR header: {q.Message}");
        if (q.DTResults.Rows.Count == 0)       return (null, null);

        var row = q.DTResults.Rows[0];
        return (new CIR_HEADER
        {
            OpportunityLineId    = GetString(row, "OPPORTUNITY_LINE_ID").Trim(),
            CategoryKey          = GetString(row, "CAT_KEY").Trim(),
            CategoryName         = GetString(row, "CAT_NAME").Trim(),
            FamilyKey            = GetString(row, "FAM_KEY").Trim(),
            FamilyName           = GetString(row, "FAM_NAME").Trim(),
            FileName             = GetString(row, "FILENAME").Trim(),
            ExtractionDate       = row["EXTRACTION_DATE"] is DateTime ed ? DateOnly.FromDateTime(ed) : null,
            ClientDetected       = GetString(row, "CLIENT_DETECTED").Trim(),
            CreatedBy            = GetString(row, "CREATED_BY").Trim(),
            CreatedDate          = row["CREATED_DATE"]  is DateTime cd ? cd : null,
            ModifiedBy           = GetString(row, "MODIFIED_BY").Trim(),
            ModifiedDate         = row["MODIFIED_DATE"] is DateTime md ? md : null,
            IsDeliverableCreated = GetBoolean(row, "IS_DELIVERABLE_CREATED"),
            ClosedDate           = row["CLOSED_DATE"]   is DateTime cl ? cl : null,
            ClosedBy             = GetString(row, "CLOSED_BY").Trim(),
            TimesReopened        = GetInt(row, "TIMES_REOPENED")
        }, null);
    }

    /// <summary>Active parameters of a CIR, mandatory first, then optional, then informative.</summary>
    private async Task<(List<CIR_PARAMETER> Parameters, string? Error)> CIR_LoadParametersAsync(string oppLineId, CancellationToken ct)
    {
        const string sql = @"
            SELECT p.KEY_PARAMETER, mp.PARAMETER_NAME, mp.PARAMETER_DEFAULT_UM, mp.VALUE_TYPE,
                   mp.PARAMETER_CATEGORY, pc.CATEGORY_PAR_NAME,
                   CASE WHEN fp.IS_DELETED = 0 THEN fp.TYPE_PARAMETER ELSE '' END AS TYPE_PARAMETER,
                   p.VALUE_MIN, p.VALUE_TYP, p.VALUE_TYP_TEXT, p.VALUE_MAX, p.UM, p.UM_PARAMETER,
                   p.DATA_SOURCE, p.MODIFIED_BY, p.MODIFIED_DATE
            FROM   dbo.MAS_SPEC_CIR_PARAMETERS p
            JOIN   dbo.MAS_SPEC_CIR_HEADER h ON h.OPPORTUNITY_LINE_ID = p.OPPORTUNITY_LINE_ID
            LEFT JOIN dbo.MAS_SPEC_FAMILY_VS_PARAMETER fp ON fp.FAM_KEY = h.FAM_KEY AND fp.KEY_PARAMETER = p.KEY_PARAMETER
            LEFT JOIN dbo.MAS_SPEC_PARAMETER mp           ON mp.KEY_PARAMETER = p.KEY_PARAMETER
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_CATEGORY pc  ON pc.KEY_CATEGORY_PAR = mp.PARAMETER_CATEGORY
            WHERE  p.OPPORTUNITY_LINE_ID = @oppLineId AND p.IS_DELETED = 0
            ORDER BY CASE WHEN fp.IS_DELETED = 0 THEN
                         CASE fp.TYPE_PARAMETER WHEN 'M' THEN 1 WHEN 'O' THEN 2 WHEN 'I' THEN 3 ELSE 4 END
                     ELSE 5 END,
                     mp.PARAMETER_NAME, p.KEY_PARAMETER";

        var q = await _db.SelectAsync(sql, new[] { new SqlParameter("@oppLineId", oppLineId) }, ct);
        if (!q.Success || q.DTResults == null) return ([], $"Error reading CIR parameters: {q.Message}");

        var list = new List<CIR_PARAMETER>(q.DTResults.Rows.Count);
        foreach (DataRow row in q.DTResults.Rows)
        {
            list.Add(new CIR_PARAMETER
            {
                KeyParameter  = GetString(row, "KEY_PARAMETER").Trim(),
                ParameterName = GetString(row, "PARAMETER_NAME").Trim(),
                TypeParameter = GetString(row, "TYPE_PARAMETER").Trim(),
                ValueType     = Specs_NormValueType(GetString(row, "VALUE_TYPE")),
                DefaultUm     = GetString(row, "PARAMETER_DEFAULT_UM").Trim(),
                ParameterCategoryKey  = GetString(row, "PARAMETER_CATEGORY").Trim(),
                ParameterCategoryName = GetString(row, "CATEGORY_PAR_NAME").Trim(),   // DB value has trailing \r\n + padding
                ValueMin      = GetDecimalNullable(row, "VALUE_MIN"),
                ValueTyp      = GetDecimalNullable(row, "VALUE_TYP"),
                ValueTypText  = CIR_NormText(row["VALUE_TYP_TEXT"] as string),
                ValueMax      = GetDecimalNullable(row, "VALUE_MAX"),
                Um            = CIR_NormText(row["UM"] as string),
                UmParameter   = GetString(row, "UM_PARAMETER").Trim(),
                DataSource    = GetString(row, "DATA_SOURCE").Trim(),
                ModifiedBy    = GetString(row, "MODIFIED_BY").Trim(),
                ModifiedDate  = row["MODIFIED_DATE"] is DateTime md ? md : null
            });
        }
        return (list, null);
    }

    /// <summary>
    /// UM_PARAMETER picking list of every parameter of a CIR, keyed by KEY_PARAMETER:
    /// active allowed units (MAS_SPEC_PARAMETER_UM active + MAS_SPEC_UM active), default UM first, then by code.
    /// The current UM_PARAMETER is always included; if it is no longer allowed it comes with IsActive = false.
    /// </summary>
    private Task<(Dictionary<string, List<SPEC_UNIT_OPTION>> Options, string? Error)> CIR_LoadUnitOptionsAsync(
        string oppLineId, CancellationToken ct)
        => Specs_LoadUnitOptionsAsync("dbo.MAS_SPEC_CIR_PARAMETERS", "c.OPPORTUNITY_LINE_ID = @oppLineId",
                                      () => [new SqlParameter("@oppLineId", oppLineId)], ct);

    /// <summary>VALUE_TYP_TEXT picking list of every list parameter (VALUE_TYPE = 'L') of a CIR, keyed by KEY_PARAMETER.</summary>
    private Task<(Dictionary<string, List<SPEC_VALUE_OPTION>> Options, string? Error)> CIR_LoadValueOptionsAsync(
        string oppLineId, CancellationToken ct)
        => Specs_LoadValueOptionsAsync("dbo.MAS_SPEC_CIR_PARAMETERS", "c.OPPORTUNITY_LINE_ID = @oppLineId", "VALUE_TYP_TEXT",
                                       () => [new SqlParameter("@oppLineId", oppLineId)], ct);

    /// <summary>FORM_CIR deliverable of the opportunity (PCA/PCA_1, DELIVERABLE_CREATION_TYPE='FORM_CIR').
    /// DeliverableId=null when it does not exist.</summary>
    private async Task<(int? DeliverableId, string ResponsibleUserId, string? Error)> CIR_GetFormDeliverableAsync(
                                                                                                                string oppLineId,
                                                                                                                CancellationToken ct)
    {
        const string sql = @"
            SELECT TOP 1 DELIVERABLE_ID, RESPONSIBLE_USER_ID
            FROM   dbo.TRA_PROJECTS_DELIVERABLES
            WHERE  OPP_LINE_ID = @oppLineId AND STATUS_ID = @statusId AND SGATE_ID = @sgateId
              AND  DELIVERABLE_CREATION_TYPE = @creationType
            ORDER BY DELIVERABLE_ID DESC";

        var q = await _db.SelectAsync(sql,
            new[]
            {
                new SqlParameter("@oppLineId",    oppLineId),
                new SqlParameter("@statusId",     CIR_FORM_STATUS_ID),
                new SqlParameter("@sgateId",      CIR_FORM_SGATE_ID),
                new SqlParameter("@creationType", CIR_FORM_CREATION_TYPE)
            }, ct);

        if (!q.Success || q.DTResults == null) return (null, string.Empty, $"Error reading FORM_CIR deliverable: {q.Message}");
        if (q.DTResults.Rows.Count == 0)       return (null, string.Empty, null);

        var row = q.DTResults.Rows[0];
        return (GetInt(row, "DELIVERABLE_ID"), GetString(row, "RESPONSIBLE_USER_ID").Trim(), null);
    }

    /// <summary>
    /// Inside <paramref name="tx"/>: closes the CIR if it is open and every mandatory (M) parameter is
    /// informed according to its value type (N: MIN/TYP/MAX · L: text in the list · T: text).
    /// Returns true if it was closed by this call.
    /// </summary>
    private async Task<bool> CIR_TryAutoCloseAsync(string oppLineId, string user, SqlTransaction tx, CancellationToken ct)
    {
        string sql = $@"
            UPDATE h
            SET    CLOSED_DATE = SYSDATETIME(), CLOSED_BY = @user
            FROM   dbo.MAS_SPEC_CIR_HEADER h
            WHERE  h.OPPORTUNITY_LINE_ID = @oppLineId
              AND  h.CLOSED_DATE IS NULL
              AND  NOT EXISTS (
                       SELECT 1
                       FROM   dbo.MAS_SPEC_CIR_PARAMETERS p
                       JOIN   dbo.MAS_SPEC_FAMILY_VS_PARAMETER fp
                              ON fp.FAM_KEY = h.FAM_KEY AND fp.KEY_PARAMETER = p.KEY_PARAMETER
                       LEFT JOIN dbo.MAS_SPEC_PARAMETER mp ON mp.KEY_PARAMETER = p.KEY_PARAMETER
                       WHERE  p.OPPORTUNITY_LINE_ID = h.OPPORTUNITY_LINE_ID
                         AND  p.IS_DELETED = 0 AND fp.IS_DELETED = 0 AND fp.TYPE_PARAMETER = 'M'
                         AND  NOT {Specs_SqlIsInformed("p", "mp", "VALUE_TYP_TEXT")})";

        var r = await _db.NonQueryAsync(sql,
            new[] { new SqlParameter("@oppLineId", oppLineId), new SqlParameter("@user", user) },
            transaction: tx, ct: ct);

        if (!r.Success) throw new InvalidOperationException($"Error evaluating CIR auto-close: {r.Message}");
        return r.RecordsAffected == 1;
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
        // Warnings are kept per source so they can be persisted with WARNING_SOURCE
        var docWarnings    = (root.Header.Warnings ?? [])
                                 .Where(w => !string.IsNullOrWhiteSpace(w))
                                 .Select(w => w.Trim())
                                 .ToList();
        var importWarnings = new List<string>();
        result.Warnings.AddRange(docWarnings);

        DateOnly? extractionDate = null;
        if (!string.IsNullOrWhiteSpace(root.Header.ExtractionDate) &&
            DateOnly.TryParse(root.Header.ExtractionDate, out var parsedDate))
            extractionDate = parsedDate;

        // 3c — check header existence, closed state and FORM_CIR flag
        const string sqlExists = @"
            SELECT CLOSED_DATE, ISNULL(Is_Deliverable_Created, 0) AS IS_DELIVERABLE_CREATED
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

        bool headerExists       = existsQuery.DTResults.Rows.Count > 0;
        bool deliverableCreated = headerExists && GetBoolean(existsQuery.DTResults.Rows[0], "IS_DELIVERABLE_CREATED");

        // FORM_CIR deliverable already created → the CIR is in user review: file is not imported
        if (deliverableCreated)
        {
            result.Skipped = true;
            result.Success = true;
            result.Warnings.Add($"FORM_CIR deliverable already created for '{oppLineId}' — file not imported.");
            return;
        }

        // A closed CIR is never overwritten by a re-import
        if (headerExists && existsQuery.DTResults.Rows[0]["CLOSED_DATE"] is DateTime)
        {
            result.Success      = false;
            result.ErrorMessage = $"CIR '{oppLineId}' is closed — reopen it before re-importing.";
            return;
        }

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

        // 3f — load existing KEY_PARAMETERs of this opportunity with their default UM
        //      (MAS_SPEC_PARAMETER) and active allowed units (MAS_SPEC_PARAMETER_UM)
        const string sqlExistingParams = @"
            SELECT c.KEY_PARAMETER, p.PARAMETER_DEFAULT_UM, p.VALUE_TYPE, a.UM_CODE
            FROM   dbo.MAS_SPEC_CIR_PARAMETERS c
            JOIN   dbo.MAS_SPEC_PARAMETER p ON p.KEY_PARAMETER = c.KEY_PARAMETER
            LEFT JOIN dbo.MAS_SPEC_PARAMETER_UM a
                   ON a.KEY_PARAMETER = c.KEY_PARAMETER AND a.IS_DELETED = 0
            WHERE  c.OPPORTUNITY_LINE_ID = @oppLineId AND c.IS_DELETED = 0";

        var existingParamsQuery = await _db.SelectAsync(sqlExistingParams,
            new[] { new SqlParameter("@oppLineId", oppLineId) },
            ct);

        if (!existingParamsQuery.Success || existingParamsQuery.DTResults == null)
        {
            result.Success      = false;
            result.ErrorMessage = $"Error reading existing parameters: {existingParamsQuery.Message}";
            return;
        }

        var dbKeys       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultUms   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var valueTypes   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowedUnits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in existingParamsQuery.DTResults.Rows)
        {
            var key = GetString(row, "KEY_PARAMETER").Trim();
            if (dbKeys.Add(key))
            {
                defaultUms[key]   = GetString(row, "PARAMETER_DEFAULT_UM").Trim();
                valueTypes[key]   = Specs_NormValueType(GetString(row, "VALUE_TYPE"));
                allowedUnits[key] = new HashSet<string>(StringComparer.Ordinal);   // units are case-sensitive
            }
            var allowed = GetString(row, "UM_CODE").Trim();
            if (allowed.Length > 0) allowedUnits[key].Add(allowed);
        }

        // active values of the list parameters (normalisation of the document text)
        var listValues = await Specs_LoadActiveListValuesAsync(ct);
        if (listValues.Error != null)
        {
            result.Success      = false;
            result.ErrorMessage = listValues.Error;
            return;
        }

        // 3g — build parameter update list from JSON
        var updates = new List<(string key, decimal? max, decimal? typ, string? typText, decimal? min, string? um, string umParameter)>();

        foreach (var jp in root.Parameters ?? [])
        {
            if (string.IsNullOrWhiteSpace(jp.KeyParameter)) continue;

            // 'X' type = client-specific extras not in the family catalog
            if (string.Equals(jp.TypeParameter, "X", StringComparison.OrdinalIgnoreCase)) continue;

            var key = jp.KeyParameter.Trim();

            if (!dbKeys.Contains(key))
            {
                result.ParametersSkipped++;
                importWarnings.Add($"Parameter '{key}' not found in DB for '{oppLineId}' — skipped.");
                continue;
            }

            // skip fully empty rows
            if (string.IsNullOrWhiteSpace(jp.ValueMin) &&
                string.IsNullOrWhiteSpace(jp.ValueTyp) &&
                string.IsNullOrWhiteSpace(jp.ValueMax) &&
                string.IsNullOrWhiteSpace(jp.Unit))
                continue;

            // value_typ: numeric parameter → numeric first, text fallback;
            //            list / free-text parameter → always text (e.g. a ratio "14" stays text)
            var valueType = valueTypes[key];
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

            // L → list spelling or warning · N with text → warning · longer than VALUE_TYP_TEXT → truncated (never blocks)
            valueTypText = Specs_ResolveImportText(key, valueType, valueTypText,
                               listValues.Values.TryGetValue(key, out var keyValues) ? keyValues : null,
                               CIR_LEN_VALUE_TYP_TEXT, importWarnings);
            var valueMin = ParseDecimalNullable(jp.ValueMin);
            var valueMax = ParseDecimalNullable(jp.ValueMax);
            if (valueType != SPECS_VT_NUMERIC && (valueMin.HasValue || valueMax.HasValue || valueTypNum.HasValue))
                importWarnings.Add($"Parameter '{key}': text parameter received numeric values — the engineer must review them.");

            // UM = unit as written in the document; UM_PARAMETER = always the default UM
            var (um, umParameter) = Specs_ResolveImportUnit(key, jp.Unit, defaultUms[key], allowedUnits[key], importWarnings);

            updates.Add((
                key,
                valueMax,
                valueTypNum,
                valueTypText,
                valueMin,
                um,
                umParameter
            ));
        }

        result.Warnings.AddRange(importWarnings);

        // 3h — persist parameter updates + replace warnings inside one transaction
        //      (audit trigger reads SESSION_CONTEXT). Always runs: warnings must be
        //      replaced even when no parameter has values.
        {
            const string sqlUpdateParam = @"
                UPDATE dbo.MAS_SPEC_CIR_PARAMETERS
                SET VALUE_MAX      = @valueMax,
                    VALUE_TYP      = @valueTyp,
                    VALUE_TYP_TEXT = @valueTypText,
                    VALUE_MIN      = @valueMin,
                    UM             = COALESCE(NULLIF(@um, ''), UM),
                    UM_PARAMETER   = @umParameter,
                    DATA_SOURCE    = 'DOC',
                    MODIFIED_BY    = @user,
                    MODIFIED_DATE  = GETDATE()
                WHERE OPPORTUNITY_LINE_ID = @oppLineId AND KEY_PARAMETER = @keyParameter";

            const string sqlDeleteWarnings = @"
                DELETE FROM dbo.MAS_SPEC_CIR_WARNINGS
                WHERE  OPPORTUNITY_LINE_ID = @oppLineId";

            const string sqlInsertWarning = @"
                INSERT INTO dbo.MAS_SPEC_CIR_WARNINGS
                    (OPPORTUNITY_LINE_ID, WARNING_SEQ, WARNING_SOURCE, WARNING_TEXT, CREATED_BY, CREATED_DATE)
                VALUES
                    (@oppLineId, @seq, @source, @text, @user, SYSDATETIME())";

            var batchResult = await _db.ExecuteInTransactionAsync(async (tx, innerCt) =>
            {
                await Specs_SetSessionUserAsync(userRequester, tx, innerCt);

                foreach (var (key, max, typ, typText, min, um, umParameter) in updates)
                {
                    var updateParam = await _db.NonQueryAsync(sqlUpdateParam,
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
                            new SqlParameter("@umParameter",  umParameter),
                            new SqlParameter("@user",         userRequester)
                        },
                        transaction: tx,
                        ct: innerCt);

                    if (!updateParam.Success)
                        throw new InvalidOperationException($"Error updating parameter '{key}': {updateParam.Message}");
                }

                // Replace warnings: DELETE all, then INSERT DOC first and IMPORT after
                var deleteResult = await _db.NonQueryAsync(sqlDeleteWarnings,
                    new[] { new SqlParameter("@oppLineId", oppLineId) },
                    transaction: tx,
                    ct: innerCt);

                if (!deleteResult.Success)
                    throw new InvalidOperationException($"Error deleting warnings: {deleteResult.Message}");

                int seq = 0;
                foreach (var (source, text) in docWarnings.Select(w => ("DOC", w))
                                                .Concat(importWarnings.Select(w => ("IMPORT", w))))
                {
                    seq++;
                    var insertWarning = await _db.NonQueryAsync(sqlInsertWarning,
                        new[]
                        {
                            new SqlParameter("@oppLineId", oppLineId),
                            new SqlParameter("@seq",       seq),
                            new SqlParameter("@source",    source),
                            new SqlParameter("@text",      text),
                            new SqlParameter("@user",      userRequester)
                        },
                        transaction: tx,
                        ct: innerCt);

                    if (!insertWarning.Success)
                        throw new InvalidOperationException($"Error inserting warning #{seq}: {insertWarning.Message}");
                }

                // Auto-close: if the document already brings every mandatory parameter
                result.CirClosed = await CIR_TryAutoCloseAsync(oppLineId, userRequester, tx, innerCt);
            }, ct);

            if (!batchResult.Success)
            {
                result.CirClosed    = false;
                result.Success      = false;
                result.ErrorMessage = $"Error updating parameters/warnings: {batchResult.Message}";
                return;
            }
        }

        // 3i — record final counts
        result.ParametersUpdated = updates.Count;
        result.Success           = true;

        // 3j — create the FORM_CIR deliverable if not created yet. A failure here does NOT
        //      undo the import: it is reported in Warnings (not persisted) and can be retried.
        if (!deliverableCreated)
        {
            var formResult = await CIR_CreateFormDeliverable(oppLineId, userRequester, ct);
            if (formResult.Success)
                result.DeliverableCreated = true;
            else
                result.Warnings.Add($"FORM_CIR deliverable not created: {formResult.Message}");
        }
    }
}
