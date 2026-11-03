namespace YasPortal.Domain.PhoneBook;

/// <summary>
/// A person or place in the phone book. It is its own record and is not linked to a system user: it can be an outside company,
/// a reception desk or a colleague who never signs in. At least one of extension, phone or mobile is required.
/// </summary>
public sealed class Contact
{
    public const int NameMax = 150;
    public const int TitleMax = 150;
    public const int NumberMax = 30;
    public const int EmailMax = 150;
    public const int NoteMax = 300;

    private Contact()
    {
    }

    public Contact(Guid groupId, string fullName, string? jobTitle, string? extension, string? phone, string? mobile,
        string? email, string? note, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(groupId, fullName, jobTitle, extension, phone, mobile, email, note, sortOrder);
    }

    public Guid Id { get; private set; }
    public Guid GroupId { get; private set; }
    public string FullName { get; private set; } = "";
    public string? JobTitle { get; private set; }
    /// <summary>The internal extension (داخلی).</summary>
    public string? Extension { get; private set; }
    public string? Phone { get; private set; }
    public string? Mobile { get; private set; }
    public string? Email { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(Guid groupId, string fullName, string? jobTitle, string? extension, string? phone, string? mobile,
        string? email, string? note, int sortOrder)
    {
        if (groupId == Guid.Empty)
            throw new ArgumentException("A contact needs a group.", nameof(groupId));
        fullName = (fullName ?? "").Trim();
        if (fullName.Length == 0 || fullName.Length > NameMax)
            throw new ArgumentException($"Name is required and at most {NameMax} characters.", nameof(fullName));
        jobTitle = Clean(jobTitle, TitleMax, nameof(jobTitle));
        extension = CleanNumber(extension, nameof(extension));
        phone = CleanNumber(phone, nameof(phone));
        mobile = CleanNumber(mobile, nameof(mobile));
        if (extension is null && phone is null && mobile is null)
            throw new ArgumentException("At least one of extension, phone or mobile is required.", "number");
        email = Clean(email, EmailMax, nameof(email));
        if (email is not null && !IsPlausibleEmail(email))
            throw new ArgumentException("The email address is not valid.", nameof(email));
        note = Clean(note, NoteMax, nameof(note));

        GroupId = groupId;
        FullName = fullName;
        JobTitle = jobTitle;
        Extension = extension;
        Phone = phone;
        Mobile = mobile;
        Email = email;
        Note = note;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private static string? Clean(string? value, int max, string param)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.Length > max)
            throw new ArgumentException($"At most {max} characters.", param);
        return value;
    }

    // Digits are stored as Latin digits so a number typed with Persian digits is found by a search typed either way.
    private static string? CleanNumber(string? value, string param)
    {
        value = Clean(value, NumberMax, param);
        if (value is null)
            return null;
        value = PhoneBookText.Normalize(value);
        if (!value.Any(char.IsDigit) || value.Any(c => !(char.IsDigit(c) || c is '+' or '-' or ' ' or '(' or ')' or '.' or '/' or '#' or '*')))
            throw new ArgumentException("A number may contain digits and + - ( ) . / # * only.", param);
        return value;
    }

    private static bool IsPlausibleEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 3 && email.IndexOf('.', at) > at + 1
            && !email.Any(char.IsWhiteSpace) && !email.EndsWith('.');
    }
}
