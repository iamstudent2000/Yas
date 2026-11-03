namespace YasPortal.Domain.PhoneBook;

/// <summary>One line of a pasted list of contacts, checked and cleaned (numbers with Latin digits). <see cref="Error"/> is a Persian reason, or null.</summary>
public sealed record ContactImportLine(int LineNumber, string Group, string FullName, string? JobTitle, string? Extension, string? Phone,
    string? Mobile, string? Email, string? Note, string? Error);

/// <summary>
/// Reads a pasted list, one contact per line:
/// <c>group | name | job title | extension | phone | mobile | email | note</c>. Only the group and the name are always needed, and
/// at least one of extension, phone or mobile; the other parts may be left empty, and the trailing ones left out.
/// Blank lines and lines starting with # are skipped. Nothing is saved here.
/// </summary>
public static class PhoneBookImportParser
{
    public const string Format = "گروه | نام | سمت | داخلی | تلفن | موبایل | ایمیل | توضیح";

    public static IReadOnlyList<ContactImportLine> Parse(string? text)
    {
        var result = new List<ContactImportLine>();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#'))
                continue;
            var number = i + 1;
            var parts = raw.Split('|').Select(x => x.Trim()).ToArray();
            string? Part(int index) => index < parts.Length && parts[index].Length > 0 ? parts[index] : null;

            var group = parts[0];
            var name = Part(1) ?? "";
            // the note is the last column and may itself contain "|"
            var note = parts.Length > 7 ? string.Join(" | ", parts.Skip(7)).Trim() : null;
            if (note is { Length: 0 })
                note = null;

            if (parts.Length < 3)
            {
                result.Add(new ContactImportLine(number, group, name, null, null, null, null, null, null, "هر خط باید «" + Format + "» باشد."));
                continue;
            }
            if (group.Length == 0 || group.Length > ContactGroup.NameMax)
            {
                result.Add(new ContactImportLine(number, group, name, null, null, null, null, null, null, "نام گروه لازم است و نباید خیلی بلند باشد."));
                continue;
            }

            string? error = null;
            Contact? contact = null;
            try
            {
                contact = new Contact(Guid.NewGuid(), name, Part(2), Part(3), Part(4), Part(5), Part(6), note, 0);
            }
            catch (ArgumentException ex)
            {
                error = Describe(ex.ParamName);
            }
            result.Add(contact is null
                ? new ContactImportLine(number, group, name, Part(2), Part(3), Part(4), Part(5), Part(6), note, error)
                : new ContactImportLine(number, group, contact.FullName, contact.JobTitle, contact.Extension, contact.Phone, contact.Mobile,
                    contact.Email, contact.Note, null));
        }
        return result;
    }

    /// <summary>A Persian reason for the parameter a <see cref="Contact"/> rejected.</summary>
    public static string Describe(string? param) => param switch
    {
        "fullName" => $"نام لازم است و حداکثر {Contact.NameMax} نویسه باشد.",
        "jobTitle" => $"سمت حداکثر {Contact.TitleMax} نویسه باشد.",
        "number" => "دست‌کم یکی از داخلی، تلفن یا موبایل لازم است.",
        "extension" or "phone" or "mobile" => $"شماره فقط می‌تواند ارقام و + - ( ) . / # * داشته باشد و حداکثر {Contact.NumberMax} نویسه باشد.",
        "email" => "ایمیل معتبر نیست.",
        "note" => $"توضیح حداکثر {Contact.NoteMax} نویسه باشد.",
        _ => "اطلاعات مخاطب معتبر نیست.",
    };
}
