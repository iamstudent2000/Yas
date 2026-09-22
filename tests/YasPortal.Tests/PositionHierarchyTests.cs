using Xunit;
using YasPortal.Domain.Organization;

namespace YasPortal.Tests;

public class PositionHierarchyTests
{
    // ceo
    //  └─ unitManager
    //       └─ employee
    private static (Guid ceo, Guid unitManager, Guid employee, Dictionary<Guid, Guid?> parents) BuildChain()
    {
        var ceo = Guid.NewGuid();
        var unitManager = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?>
        {
            [ceo] = null,
            [unitManager] = ceo,
            [employee] = unitManager,
        };
        return (ceo, unitManager, employee, parents);
    }

    [Fact]
    public void Level_one_returns_the_direct_parent()
    {
        var (_, unitManager, employee, parents) = BuildChain();

        var ancestor = PositionHierarchy.GetAncestorPositionId(employee, 1, parents);

        Assert.Equal(unitManager, ancestor);
    }

    [Fact]
    public void Level_two_skips_a_generation()
    {
        var (ceo, _, employee, parents) = BuildChain();

        var ancestor = PositionHierarchy.GetAncestorPositionId(employee, 2, parents);

        Assert.Equal(ceo, ancestor);
    }

    [Fact]
    public void Returns_null_when_the_chain_runs_out_before_reaching_the_level()
    {
        var (ceo, _, _, parents) = BuildChain();

        var ancestor = PositionHierarchy.GetAncestorPositionId(ceo, 1, parents);

        Assert.Null(ancestor);
    }

    [Fact]
    public void Rejects_a_non_positive_level()
    {
        var (_, _, employee, parents) = BuildChain();

        Assert.Throws<ArgumentOutOfRangeException>(() => PositionHierarchy.GetAncestorPositionId(employee, 0, parents));
    }

    [Fact]
    public void Closest_ancestor_returns_the_exact_level_when_the_chain_is_long_enough()
    {
        var (ceo, unitManager, employee, parents) = BuildChain();

        Assert.Equal(unitManager, PositionHierarchy.GetClosestAncestorPositionId(employee, 1, parents));
        Assert.Equal(ceo, PositionHierarchy.GetClosestAncestorPositionId(employee, 2, parents));
    }

    [Fact]
    public void Closest_ancestor_clamps_to_the_highest_available_instead_of_failing()
    {
        var (ceo, unitManager, employee, parents) = BuildChain();

        // Asking for "2 levels up" from the unit manager would run off the top of a
        // 3-level chain — this employee's manager (unitManager) has no manager of their
        // own beyond the CEO, so it should clamp to the CEO rather than return null.
        Assert.Equal(ceo, PositionHierarchy.GetClosestAncestorPositionId(unitManager, 5, parents));
    }

    [Fact]
    public void Closest_ancestor_returns_null_only_when_there_is_no_manager_at_all()
    {
        var (ceo, _, _, parents) = BuildChain();

        Assert.Null(PositionHierarchy.GetClosestAncestorPositionId(ceo, 1, parents));
    }
}
