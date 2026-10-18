namespace YasPortal.Domain.Security;

/// <summary>
/// The system-wide password rules an administrator can configure. There is exactly one row
/// (<see cref="SingletonId"/>); it is created with the defaults the first time it is read.
/// The rules apply whenever a password is set or changed (admin-set and self-service).
/// Existing passwords are not re-checked until they are next changed.
/// </summary>
public sealed class PasswordPolicy
{
    public const int SingletonId = 1;
    public const int LowestMinLength = 4;
    public const int HighestMinLength = 64;
    public const int HardMaxLength = 128;

    private PasswordPolicy()
    {
    }

    public static PasswordPolicy CreateDefault() => new();

    public int Id { get; private set; } = SingletonId;
    public int MinLength { get; private set; } = 8;
    public int MaxLength { get; private set; } = HardMaxLength;
    public bool RequireUppercase { get; private set; }
    public bool RequireLowercase { get; private set; }
    public bool RequireDigit { get; private set; } = true;
    public bool RequireSymbol { get; private set; }
    /// <summary>Reject a password that contains the user's username.</summary>
    public bool DisallowUsername { get; private set; } = true;
    /// <summary>Reject a handful of very common passwords and simple keyboard runs.</summary>
    public bool DisallowCommonPasswords { get; private set; } = true;
    /// <summary>Reject a new password that is identical to the current one (change-password only).</summary>
    public bool DisallowReuseOfCurrent { get; private set; } = true;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void Update(int minLength, int maxLength, bool requireUppercase, bool requireLowercase, bool requireDigit,
        bool requireSymbol, bool disallowUsername, bool disallowCommonPasswords, bool disallowReuseOfCurrent)
    {
        if (minLength < LowestMinLength || minLength > HighestMinLength)
            throw new ArgumentOutOfRangeException(nameof(minLength), $"Minimum length must be between {LowestMinLength} and {HighestMinLength}.");
        if (maxLength < minLength || maxLength > HardMaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Maximum length must be at least the minimum length and at most " + HardMaxLength + ".");
        MinLength = minLength;
        MaxLength = maxLength;
        RequireUppercase = requireUppercase;
        RequireLowercase = requireLowercase;
        RequireDigit = requireDigit;
        RequireSymbol = requireSymbol;
        DisallowUsername = disallowUsername;
        DisallowCommonPasswords = disallowCommonPasswords;
        DisallowReuseOfCurrent = disallowReuseOfCurrent;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "123456", "1234567", "12345678", "123456789", "1234567890",
        "qwerty", "qwerty123", "qwertyuiop", "abc123", "abcd1234", "letmein", "welcome", "admin", "admin123", "iloveyou",
        "111111", "000000", "123123", "654321", "1q2w3e4r", "asdfghjkl", "changeme", "yasportal"
    };

    /// <summary>Persian bullet lines describing the active rules, for showing next to a password field.</summary>
    public IReadOnlyList<string> Describe()
    {
        var lines = new List<string> { $"حداقل {MinLength} و حداکثر {MaxLength} نویسه" };
        if (RequireUppercase) lines.Add("حداقل یک حرف بزرگ انگلیسی (A-Z)");
        if (RequireLowercase) lines.Add("حداقل یک حرف کوچک انگلیسی (a-z)");
        if (RequireDigit) lines.Add("حداقل یک رقم");
        if (RequireSymbol) lines.Add("حداقل یک نماد (مثل ! @ # $)");
        if (DisallowUsername) lines.Add("شامل نام کاربری نباشد");
        if (DisallowCommonPasswords) lines.Add("از رمزهای رایج و ساده نباشد");
        return lines;
    }

    /// <summary>Returns the Persian reason for every rule the password breaks; empty when it is acceptable.</summary>
    public IReadOnlyList<string> Validate(string? password, string? username = null)
    {
        var errors = new List<string>();
        password ??= "";
        if (password.Length < MinLength)
            errors.Add($"رمز عبور باید حداقل {MinLength} نویسه باشد.");
        if (password.Length > MaxLength)
            errors.Add($"رمز عبور باید حداکثر {MaxLength} نویسه باشد.");
        if (RequireUppercase && !password.Any(c => c is >= 'A' and <= 'Z'))
            errors.Add("رمز عبور باید حداقل یک حرف بزرگ انگلیسی داشته باشد.");
        if (RequireLowercase && !password.Any(c => c is >= 'a' and <= 'z'))
            errors.Add("رمز عبور باید حداقل یک حرف کوچک انگلیسی داشته باشد.");
        if (RequireDigit && !password.Any(char.IsDigit))
            errors.Add("رمز عبور باید حداقل یک رقم داشته باشد.");
        if (RequireSymbol && !password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            errors.Add("رمز عبور باید حداقل یک نماد داشته باشد.");
        if (DisallowUsername && !string.IsNullOrWhiteSpace(username) && username.Trim().Length >= 3
            && password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add("رمز عبور نباید شامل نام کاربری باشد.");
        if (DisallowCommonPasswords && Common.Contains(password))
            errors.Add("این رمز عبور بسیار رایج است؛ رمز دیگری انتخاب کنید.");
        return errors;
    }
}
