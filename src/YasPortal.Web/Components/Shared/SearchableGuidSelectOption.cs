namespace YasPortal.Web.Components.Shared;

public sealed record SearchableGuidSelectOption(
    Guid Id,
    string Text,
    string? SecondaryText = null);
