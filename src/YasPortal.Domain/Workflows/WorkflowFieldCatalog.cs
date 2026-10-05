using System.Globalization;

namespace YasPortal.Domain.Workflows;

/// <summary>A single fixed field on a workflow type's form.</summary>
public sealed record WorkflowFieldDefinition(string Key, string Label, WorkflowFieldType FieldType, bool Required, IReadOnlyList<string>? Options = null);

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
            [WorkflowTypeCode.WorkReport] = ("گزارش کار", new[]
            {
                new WorkflowFieldDefinition("reportDate", "تاریخ گزارش", WorkflowFieldType.Date, true),
                new WorkflowFieldDefinition("summary", "خلاصه فعالیت‌ها", WorkflowFieldType.TextArea, true),
                new WorkflowFieldDefinition("hoursSpent", "ساعت صرف‌شده", WorkflowFieldType.Number, false),
            }),
        };

    public static string GetDisplayName(WorkflowTypeCode type) => Catalog[type].Name;

    public static IReadOnlyList<WorkflowFieldDefinition> GetFields(WorkflowTypeCode type) => Catalog[type].Fields;

    public static IReadOnlyList<WorkflowTypeCode> AllTypes { get; } = Enum.GetValues<WorkflowTypeCode>();

    /// <summary>
    /// Validates a submitted set of field values against the type's fixed schema.
    /// Returns the human-readable labels of any missing required fields.
    /// </summary>
    public static IReadOnlyList<string> ValidateRequiredFields(WorkflowTypeCode type, IReadOnlyDictionary<string, string?> values)
    {
        var missing = new List<string>();
        foreach (var field in GetFields(type))
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
    public static IReadOnlyList<WorkflowFieldError> ValidateValues(WorkflowTypeCode type, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<WorkflowFieldError>();
        var fields = GetFields(type);

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

        if (type == WorkflowTypeCode.Leave
            && values.TryGetValue("startDate", out var startRaw) && TryParseDate(startRaw?.Trim(), out var start)
            && values.TryGetValue("endDate", out var endRaw) && TryParseDate(endRaw?.Trim(), out var end)
            && end < start)
        {
            var endField = fields.Single(f => f.Key == "endDate");
            errors.Add(new WorkflowFieldError(endField.Key, endField.Label, "تاریخ پایان نمی‌تواند قبل از تاریخ شروع باشد."));
        }

        return errors;
    }

    private static bool TryParseNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);

    private static bool TryParseDate(string? value, out DateTime date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
