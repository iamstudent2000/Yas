using YasPortal.Domain.PhoneBook;
using Xunit;

namespace YasPortal.Tests;

public sealed class PhoneBookTests
{
    private static Contact C(Guid group, string name, string? ext = "100", string? phone = null, string? mobile = null,
        string? title = null, string? email = null, string? note = null, int order = 1) =>
        new(group, name, title, ext, phone, mobile, email, note, order);

    [Fact]
    public void Contact_needs_a_name_a_group_and_one_number()
    {
        var g = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => C(g, "  "));
        Assert.Throws<ArgumentException>(() => C(Guid.Empty, "علی"));
        var ex = Assert.Throws<ArgumentException>(() => C(g, "علی", ext: null));
        Assert.Equal("number", ex.ParamName);
        Assert.Equal("100", C(g, "علی").Extension);
    }

    [Fact]
    public void Numbers_are_stored_with_latin_digits_and_reject_letters()
    {
        var g = Guid.NewGuid();
        var c = C(g, "علی", ext: "۱۲۳", phone: "۰۲۱-۸۸۸۸۸۸۸۸", mobile: "٠٩١٢٣٤٥٦٧٨٩");
        Assert.Equal("123", c.Extension);
        Assert.Equal("021-88888888", c.Phone);
        Assert.Equal("09123456789", c.Mobile);
        Assert.Throws<ArgumentException>(() => C(g, "علی", phone: "abc"));
        Assert.Throws<ArgumentException>(() => C(g, "علی", phone: "12ab"));
    }

    [Theory]
    [InlineData("a@b.ir", true)]
    [InlineData("user@mail.example.com", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.ir", false)]
    [InlineData("@c.ir", false)]
    [InlineData("a@@c.ir", false)]
    public void Email_is_checked_loosely(string email, bool valid)
    {
        var g = Guid.NewGuid();
        if (valid) Assert.Equal(email, C(g, "علی", email: email).Email);
        else Assert.Equal("email", Assert.Throws<ArgumentException>(() => C(g, "علی", email: email)).ParamName);
    }

    [Fact]
    public void Group_name_is_required()
    {
        Assert.Throws<ArgumentException>(() => new ContactGroup(" ", 1));
        Assert.Equal("مالی", new ContactGroup(" مالی ", 1).Name);
    }

    [Fact]
    public void Normalize_unifies_digits_and_arabic_letters()
    {
        Assert.Equal("0912 علی", PhoneBookText.Normalize("۰۹۱۲ علي"));
        Assert.Equal("کتاب", PhoneBookText.Normalize("كتاب"));
        Assert.Equal("می خواهم", PhoneBookText.Normalize("می‌خواهم"));
        Assert.Equal("", PhoneBookText.Normalize(null));
    }

    [Fact]
    public void Search_finds_every_word_in_any_field_with_either_digit_style()
    {
        var g = new ContactGroup("مالی", 1);
        var c = C(g.Id, "علی رضایی", ext: "214", mobile: "09121234567", title: "مدیر مالی");
        Assert.True(PhoneBookText.ContainsAllWords(PhoneBookTree.SearchText(c), "رضایی مدیر"));
        Assert.True(PhoneBookText.ContainsAllWords(PhoneBookTree.SearchText(c), "۲۱۴"));
        Assert.True(PhoneBookText.ContainsAllWords(PhoneBookTree.SearchText(c), "0912"));
        Assert.False(PhoneBookText.ContainsAllWords(PhoneBookTree.SearchText(c), "رضایی حسابدار"));
        Assert.True(PhoneBookText.ContainsAllWords("x", "   "));
    }

    [Fact]
    public void Build_hides_inactive_groups_and_contacts_unless_asked_and_orders_them()
    {
        var a = new ContactGroup("ب", 2); var b = new ContactGroup("الف", 1); var off = new ContactGroup("خاموش", 3); off.Deactivate();
        var c1 = C(a.Id, "زهرا", order: 2); var c2 = C(a.Id, "امیر", order: 1); var c3 = C(a.Id, "نادر"); c3.Deactivate(); var c4 = C(off.Id, "ناشناس");
        var orphan = C(Guid.NewGuid(), "بی‌گروه");

        var tree = PhoneBookTree.Build([a, b, off], [c1, c2, c3, c4, orphan]);
        Assert.Equal(new[] { "الف", "ب" }, tree.Groups.Select(x => x.Group.Name));
        Assert.Equal(new[] { "امیر", "زهرا" }, tree.Groups[1].Contacts.Select(x => x.FullName));
        Assert.Equal(2, tree.TotalContacts);

        var all = PhoneBookTree.Build([a, b, off], [c1, c2, c3, c4, orphan], includeInactive: true);
        Assert.Equal(3, all.Groups.Count);
        Assert.Equal(4, all.TotalContacts);
    }

    [Fact]
    public void General_search_keeps_matching_contacts_and_whole_groups_whose_name_matches()
    {
        var fin = new ContactGroup("امور مالی", 1); var it = new ContactGroup("فناوری", 2);
        var tree = PhoneBookTree.Build([fin, it], [C(fin.Id, "علی رضایی", ext: "214"), C(fin.Id, "سارا احمدی", ext: "215"), C(it.Id, "رضا کریمی", ext: "300")]);

        var byName = tree.Filter("رضایی");
        Assert.Single(byName.Groups);
        Assert.Equal(["علی رضایی"], byName.Groups[0].Contacts.Select(x => x.FullName));

        var byGroup = tree.Filter("مالی");
        Assert.Single(byGroup.Groups);
        Assert.Equal(2, byGroup.Groups[0].Contacts.Count);   // the group name matches, so all its contacts stay

        var byNumber = tree.Filter("۳۰۰");
        Assert.Equal("فناوری", byNumber.Groups.Single().Group.Name);

        Assert.Empty(tree.Filter("ناموجود").Groups);
        Assert.Same(tree, tree.Filter("  "));
    }

    [Fact]
    public void Group_search_filters_only_that_groups_contacts()
    {
        var g = new ContactGroup("مالی", 1);
        var tree = PhoneBookTree.Build([g], [C(g.Id, "علی", ext: "1"), C(g.Id, "سارا", ext: "2")]);
        var contacts = tree.Groups[0].Contacts;
        Assert.Equal(new[] { "سارا" }, PhoneBookTree.FilterContacts(contacts, "سارا").Select(x => x.FullName));
        Assert.Equal(2, PhoneBookTree.FilterContacts(contacts, "").Count);
        // the group name is not searched here (everything in the group already belongs to it)
        Assert.Empty(PhoneBookTree.FilterContacts(contacts, "مالی"));
    }
}
