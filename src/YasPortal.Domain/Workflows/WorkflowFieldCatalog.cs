namespace YasPortal.Domain.Workflows;

/// <summary>A single fixed field on a workflow type's form.</summary>
public sealed record WorkflowFieldDefinition(string Key, string Label, WorkflowFieldType FieldType, bool Required, IReadOnlyList<string>? Options = null);

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
}
