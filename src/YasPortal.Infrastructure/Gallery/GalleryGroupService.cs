using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Gallery;
using YasPortal.Domain.Guides;
using YasPortal.Infrastructure.Guides;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Gallery;

/// <summary>
/// The list of gallery groups kept by the administrators: add, rename and order them, switch them on or off, and delete one that no picture uses.
/// It follows the same rules as the guide groups (and reuses their result type).
/// </summary>
public sealed class GalleryGroupService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>The groups in display order (order number, then name). Switched-off groups are included only when asked for.</summary>
    public async Task<IReadOnlyList<GalleryGroup>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.GalleryGroups.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
    }

    /// <summary>How many pictures sit in each group (pictures without a group are not counted). Used to show the number and to explain why a delete is refused.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> CountImagesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.GalleryImages.AsNoTracking().Where(x => x.GroupId != null).GroupBy(x => x.GroupId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Key, x => x.Count);
    }

    /// <summary>Creates a group (<paramref name="id"/> empty) or renames and re-orders an existing one. The name must be filled in, not too long and not used by another group.</summary>
    public async Task<GuideGroupResult> SaveAsync(Guid? id, string name, int sortOrder, CancellationToken ct = default)
    {
        var clean = UserGuideGroup.NormalizeName(name);
        if (clean is null) return GuideGroupResult.Fail("نام گروه را وارد کنید.");
        if (clean.Length > GalleryGroup.NameMax) return GuideGroupResult.Fail("نام گروه بیش از حد طولانی است.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Names are compared by what a person sees (letter case, digit script, half-spaces and invisible marks do not make a new group).
        // There are only a few groups, so the comparison is done here instead of in the database.
        const string duplicate = "گروهی با این نام وجود دارد.";
        var key = UserGuideGroup.CompareKey(clean);
        var others = await db.GalleryGroups.AsNoTracking().Where(x => x.Id != id).Select(x => x.Name).ToListAsync(ct);
        if (others.Any(n => UserGuideGroup.CompareKey(n) == key)) return GuideGroupResult.Fail(duplicate);
        if (id is null) db.GalleryGroups.Add(new GalleryGroup(clean, sortOrder));
        else
        {
            var group = await db.GalleryGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (group is null) return GuideGroupResult.Fail("گروه پیدا نشد.");
            group.Update(clean, sortOrder);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two people saved the same new name at the same moment: the database's unique index stopped the second one.
            return GuideGroupResult.Fail(duplicate);
        }
        return GuideGroupResult.Success;
    }

    /// <summary>Switches a group on or off. A switched-off group is hidden from visitors together with its pictures.</summary>
    public async Task<GuideGroupResult> SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.GalleryGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null) return GuideGroupResult.Fail("گروه پیدا نشد.");
        if (active) group.Activate(); else group.Deactivate();
        await db.SaveChangesAsync(ct);
        return GuideGroupResult.Success;
    }

    /// <summary>Deletes a group that no picture uses. A group with pictures can only be switched off, so a picture never loses its group by accident.</summary>
    public async Task<GuideGroupResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.GalleryGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (group is null) return GuideGroupResult.Success;
        if (await db.GalleryImages.AnyAsync(x => x.GroupId == id, ct))
            return GuideGroupResult.Fail("این گروه تصویر دارد و حذف نمی‌شود؛ ابتدا تصاویر آن را به گروه دیگری ببرید یا گروه را غیرفعال کنید.");
        db.GalleryGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return GuideGroupResult.Success;
    }

    /// <summary>
    /// Checks the group chosen for a picture: no group is fine; otherwise it must exist, and it must be switched on unless the picture already
    /// belongs to it (a picture may keep a group that was switched off later, but nobody can newly pick one). Null = fine, otherwise the reason.
    /// </summary>
    public async Task<string?> CheckChoiceAsync(Guid? groupId, Guid? currentGroupId, CancellationToken ct = default)
    {
        if (groupId is null || groupId == Guid.Empty) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var group = await db.GalleryGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId, ct);
        if (group is null) return "گروه انتخاب‌شده دیگر وجود ندارد.";
        if (!group.IsActive && groupId != currentGroupId) return "گروه انتخاب‌شده غیرفعال است.";
        return null;
    }
}
