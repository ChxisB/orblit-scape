using static Scape.Core.Tests.WireSupport;

namespace Scape.Core.Tests;

public class MoveItemMessagesTests
{
    [Fact]
    public void AMoveKeepsBothContainersAndBothSlots()
    {
        var back = RoundTrip(new MoveItem(ContainerId.Inventory, 27, ContainerId.Equipment, 3));

        Assert.Equal((ContainerId.Inventory, 27, ContainerId.Equipment, 3), (back.From, back.FromSlot, back.To, back.ToSlot));
    }

    [Fact]
    public void TheBankIsNotMovedWithIt()
    {
        Assert.ThrowsAny<ArgumentException>(() => new MoveItem(ContainerId.Bank, 0, ContainerId.Inventory, 0));
        Assert.Equal(BadMessage.Malformed, WhyClient([18, 2, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void AContainerThatDoesNotExistIsMalformedNotACrash()
    {
        Assert.Equal(BadMessage.Malformed, WhyClient([18, 9, 0, 0, 0, 0, 0]));
        Assert.Equal(BadMessage.Malformed, WhyClient([18, 0, 1, 0, 0]));
    }

    // A cast can make a container id outside the enum.
    [Fact]
    public void AMoveCannotBeBuiltWithAContainerOutsideTheEnum()
    {
        Assert.ThrowsAny<ArgumentException>(() => new MoveItem((ContainerId)9, 0, ContainerId.Inventory, 0));
        Assert.ThrowsAny<ArgumentException>(() => new MoveItem(ContainerId.Inventory, 0, (ContainerId)9, 0));
    }
}
