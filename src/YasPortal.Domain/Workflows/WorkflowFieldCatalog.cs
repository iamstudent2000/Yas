using System.Globalization;

namespace YasPortal.Domain.Workflows;

/// <summary>
/// A single fixed field on a workflow type's form. <paramref name="Part"/> says which part of the
/// form the field belongs to: part 1 is always filled by the requester when submitting; later parts
/// are filled by the approver at a workflow step (see <see cref="WorkflowPartDefinition"/>).
/// </summary>
public sealed record WorkflowFieldDefinition(string Key, string Label, WorkflowFieldType FieldType, bool Required, IReadOnlyList<string>? Options = null, int Part = 1);

/// <summary>
/// One part of a multi-part form. Part 1 belongs to the requester. Every later part is filled by the
/// approver at the workflow step an admin assigned it to (<see cref="WorkflowStepDefinition.FillsPart"/>)
/// and must be completed before that step can approve. The parts and their fields are fixed in code
/// (spec §11.1); which step fills which part is the admin's choice in the workflow editor.
/// </summary>
public sealed record WorkflowPartDefinition(int Part, string Title);

/// <summary>
/// One validation failure, identifying the specific field and the reason (spec §26.1 — generic
/// "something is invalid" messages are not allowed). <see cref="Message"/> is user-facing text.
/// </summary>
public sealed record WorkflowFieldError(string Key, string Label, string Message);

/// <summary>
/// The fixed field schema for every <see cref="WorkflowTypeCode"/>. This is deliberately
/// plain code, not a database table: workflow types and their fields do not change at
/// runtime. Only the approval steps (<see cref="WorkflowStepDefinition"/>) are admin-configurable.
/// </summary>
public static class WorkflowFieldCatalog
{
    private static readonly IReadOnlyDictionary<WorkflowTypeCode, (string Name, IReadOnlyList<WorkflowFieldDefinition> Fields)> Catalog =
        new Dictionary<WorkflowTypeCode, (string, IReadOnlyList<WorkflowFieldDefinition>)>
        {
            [WorkflowTypeCode.Leave] = ("مرخصی", new[]
            {
                new WorkflowFieldDefinition("startDate", "تاریخ شروع", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("endDate", "تاریخ پایان", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("leaveType", "نوع مرخصی", WorkflowFieldType.Select, true, new[] { "استحقاقی", "استعلاجی", "بدون حقوق" }),
                new WorkflowFieldDefinition("reason", "توضیحات", WorkflowFieldType.TextArea, false),
            }),
            [WorkflowTypeCode.Purchase] = ("درخواست خرید", new[]
            {
                new WorkflowFieldDefinition("itemName", "کالا/خدمت", WorkflowFieldType.Text, true),
                new WorkflowFieldDefinition("quantity", "تعداد", WorkflowFieldType.Number, true),
                new WorkflowFieldDefinition("estimatedCost", "برآورد هزینه (ریال)", WorkflowFieldType.Number, true),
                new WorkflowFieldDefinition("justification", "توجیه درخواست", WorkflowFieldType.TextArea, true),
            }),
            [WorkflowTypeCode.Access] = ("درخواست دسترسی", new[]
            {
                new WorkflowFieldDefinition("systemName", "سامانه/منبع", WorkflowFieldType.Text, true),
                new WorkflowFieldDefinition("accessLevel", "سطح دسترسی", WorkflowFieldType.Select, true, new[] { "مشاهده", "ویرایش", "مدیریت کامل" }),
                new WorkflowFieldDefinition("reason", "دلیل درخواست", WorkflowFieldType.TextArea, true),
            }),
            [WorkflowTypeCode.Loan] = ("درخواست وام", new[]
            {
                new WorkflowFieldDefinition("amount", "مبلغ درخواستی (ریال)", WorkflowFieldType.Number, true),
                new WorkflowFieldDefinition("installments", "تعداد اقساط", WorkflowFieldType.Number, true),
                new WorkflowFieldDefinition("reason", "دلیل درخواست", WorkflowFieldType.TextArea, false),
            }),
            [WorkflowTypeCode.Helpdesk] = ("درخواست پشتیبانی", new[]
            {
                new WorkflowFieldDefinition("subject", "موضوع", WorkflowFieldType.Text, true),
                new WorkflowFieldDefinition("priority", "اولویت", WorkflowFieldType.Select, true, new[] { "کم", "متوسط", "زیاد", "بحرانی" }),
                new WorkflowFieldDefinition("description", "شرح مشکل", WorkflowFieldType.TextArea, true),
            }),
            [WorkflowTypeCode.VehicleRequest] = ("درخواست خودرو", new[]
            {
                // Part 1 — requester.
                new WorkflowFieldDefinition("destination", "مقصد", WorkflowFieldType.Text, true),
                new WorkflowFieldDefinition("tripDate", "تاریخ رفت", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("returnDate", "تاریخ برگشت", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("passengers", "تعداد سرنشین", WorkflowFieldType.Number, true),
                new WorkflowFieldDefinition("purpose", "هدف از سفر", WorkflowFieldType.TextArea, true),
                // Part 2 — vehicle assignment, filled at the workflow step an admin assigns it to.
                new WorkflowFieldDefinition("vehicleType", "نوع خودرو", WorkflowFieldType.Select, true, new[] { "سواری", "ون", "وانت", "مینی‌بوس" }, Part: 2),
                new WorkflowFieldDefinition("plateNumber", "شماره پلاک", WorkflowFieldType.Text, true, Part: 2),
                new WorkflowFieldDefinition("driverName", "نام راننده", WorkflowFieldType.Text, true, Part: 2),
                // Part 3 — security gate pass, filled at the workflow step an admin assigns it to.
                new WorkflowFieldDefinition("gatePassNumber", "شماره مجوز خروج", WorkflowFieldType.Text, true, Part: 3),
                new WorkflowFieldDefinition("securityNote", "ملاحظات حراست", WorkflowFieldType.TextArea, false, Part: 3),
            }),
            [WorkflowTypeCode.WorkReport] = ("گزارش کار", new[]
            {
                new WorkflowFieldDefinition("reportDate", "تاریخ گزارش", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("summary", "خلاصه فعالیت‌ها", WorkflowFieldType.TextArea, true),
                new WorkflowFieldDefinition("hoursSpent", "ساعت صرف‌شده", WorkflowFieldType.Number, false),
            }),
        };

    /// <summary>
    /// The parts of each multi-part type. A type missing here has one part: everything is filled by
    /// the requester. Parts after the first are filled at the workflow steps an admin assigns them to.
    /// </summary>
    private static readonly IReadOnlyDictionary<WorkflowTypeCode, IReadOnlyList<WorkflowPartDefinition>> MultiPartCatalog =
        new Dictionary<WorkflowTypeCode, IReadOnlyList<WorkflowPartDefinition>>
        {
            [WorkflowTypeCode.VehicleRequest] = new[]
            {
                new WorkflowPartDefinition(1, "اطلاعات سفر"),
                new WorkflowPartDefinition(2, "تخصیص خودرو"),
                new WorkflowPartDefinition(3, "ثبت حراست"),
            },
        };

    private static readonly IReadOnlyList<WorkflowPartDefinition> SinglePart = new[] { new WorkflowPartDefinition(1, "اطلاعات درخواست") };

    public static string GetDisplayName(WorkflowTypeCode type) => Catalog[type].Name;

    /// <summary>Every field of every part of the type.</summary>
    public static IReadOnlyList<WorkflowFieldDefinition> GetFields(WorkflowTypeCode type) => Catalog[type].Fields;

    /// <summary>The fields of one part of the type.</summary>
    public static IReadOnlyList<WorkflowFieldDefinition> GetFields(WorkflowTypeCode type, int part) =>
        Catalog[type].Fields.Where(f => f.Part == part).ToList();

    /// <summary>The parts of the type in order; a single requester part for ordinary types.</summary>
    public static IReadOnlyList<WorkflowPartDefinition> GetParts(WorkflowTypeCode type) =>
        MultiPartCatalog.TryGetValue(type, out var parts) ? parts : SinglePart;

    public static bool IsMultiPart(WorkflowTypeCode type) => GetParts(type).Count > 1;

    /// <summary>
    /// The part (2 or later) a step is set to fill, or null when it fills nothing or the number is
    /// not a fillable part of this type.
    /// </summary>
    public static WorkflowPartDefinition? GetStepPart(WorkflowTypeCode type, int? fillsPart) =>
        fillsPart is int number && number > 1 ? GetParts(type).FirstOrDefault(p => p.Part == number) : null;

    /// <summary>
    /// The parts that cannot be filled on the given path: every part after the first needs exactly one
    /// step assigned to it, and that step must be alone at its order (a parallel group would leave it
    /// unclear who fills the part, and an escalation expands into several steps). Empty when the path
    /// suits the type. Checked at submission so a request never starts on a path that cannot complete
    /// its form — the admin must fix the workflow instead (spec §24).
    /// </summary>
    public static IReadOnlyList<WorkflowPartDefinition> UnfillableParts(WorkflowTypeCode type, IReadOnlyList<WorkflowResolvedStep> steps) =>
        GetParts(type)
            .Where(p => p.Part > 1)
            .Where(p =>
            {
                var assigned = steps.Where(s => s.FillsPart == p.Part).ToList();
                return assigned.Count != 1 || steps.Count(s => s.Order == assigned[0].Order) != 1;
            })
            .ToList();

    public static IReadOnlyList<WorkflowTypeCode> AllTypes { get; } = Enum.GetValues<WorkflowTypeCode>();

    /// <summary>
    /// Validates a submitted set of field values against the type's fixed schema.
    /// Returns the human-readable labels of any missing required fields of the requester's part (part 1).
    /// </summary>
    public static IReadOnlyList<string> ValidateRequiredFields(WorkflowTypeCode type, IReadOnlyDictionary<string, string?> values)
    {
        var missing = new List<string>();
        foreach (var field in GetFields(type, 1))
        {
            if (!field.Required)
                continue;
            if (!values.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
                missing.Add(field.Label);
        }
        return missing;
    }

    /// <summary>
    /// Full server-side validation of a submitted set of field values (spec §26.1): required
    /// fields, number / date / select formats, keys that are not part of the type's schema, and
    /// the leave date range. Returns one <see cref="WorkflowFieldError"/> per problem, each naming
    /// the field and the reason. An empty list means the values are acceptable.
    ///
    /// Formats match what the form stores: dates as <c>yyyy-MM-dd</c>, numbers in invariant
    /// culture, and select values exactly equal to one of the field's options.
    /// </summary>
    public static IReadOnlyList<WorkflowFieldError> ValidateValues(WorkflowTypeCode type, IReadOnlyDictionary<string, string?> values) =>
        ValidatePartValues(type, 1, values);

    /// <summary>
    /// Same checks as <see cref="ValidateValues"/> for one part of the form: only that part's fields
    /// are required and accepted, so a value for a field of another part is an error (a requester
    /// cannot fill the approver's part, and the reverse). Part 1 is what the requester submits.
    /// </summary>
    public static IReadOnlyList<WorkflowFieldError> ValidatePartValues(WorkflowTypeCode type, int part, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<WorkflowFieldError>();
        var fields = GetFields(type, part);

        foreach (var field in fields)
        {
            values.TryGetValue(field.Key, out var raw);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (field.Required)
                    errors.Add(new WorkflowFieldError(field.Key, field.Label, "تکمیل این فیلد الزامی است."));
                continue;
            }

            var value = raw.Trim();
            switch (field.FieldType)
            {
                case WorkflowFieldType.Number:
                    if (!TryParseNumber(value, out var number))
                        errors.Add(new WorkflowFieldError(field.Key, field.Label, "باید یک عدد معتبر باشد."));
                    else if (number < 0)
                        errors.Add(new WorkflowFieldError(field.Key, field.Label, "نمی‌تواند منفی باشد."));
                    break;
                case WorkflowFieldType.Date:
                    if (!TryParseDate(value, out _))
                        errors.Add(new WorkflowFieldError(field.Key, field.Label, "تاریخ معتبر نیست."));
                    break;
                case WorkflowFieldType.Select:
                    if (field.Options is not null && !field.Options.Contains(value, StringComparer.Ordinal))
                        errors.Add(new WorkflowFieldError(field.Key, field.Label, "گزینه انتخاب‌شده معتبر نیست."));
                    break;
            }
        }

        // Fields are fixed in code (spec §11.1): a value for a key outside the schema is never legitimate.
        var knownKeys = fields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in values.Keys.Where(k => !knownKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            errors.Add(new WorkflowFieldError(key, key, "این فیلد برای این نوع درخواست تعریف نشده است."));

        if (part == 1 && DateRangeKeys(type) is { } range
            && values.TryGetValue(range.Start, out var startRaw) && TryParseDate(startRaw?.Trim(), out var start)
            && values.TryGetValue(range.End, out var endRaw) && TryParseDate(endRaw?.Trim(), out var end)
            && end < start)
        {
            var endField = fields.Single(f => f.Key == range.End);
            errors.Add(new WorkflowFieldError(endField.Key, endField.Label, "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد."));
        }

        return errors;
    }

    /// <summary>The start/end date fields of a type whose end may not be before its start, or null.</summary>
    private static (string Start, string End)? DateRangeKeys(WorkflowTypeCode type) => type switch
    {
        WorkflowTypeCode.Leave => ("startDate", "endDate"),
        WorkflowTypeCode.VehicleRequest => ("tripDate", "returnDate"),
        _ => null,
    };

    private static bool TryParseNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);

    private static bool TryParseDate(string? value, out DateTime date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
