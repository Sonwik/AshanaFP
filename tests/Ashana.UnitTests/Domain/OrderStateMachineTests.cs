using Ashana.Domain.Orders;

namespace Ashana.UnitTests.Domain;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.Placed, OrderStatus.Cooking)]
    [InlineData(OrderStatus.Placed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Ready)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Ready, OrderStatus.PickedUp)]
    [InlineData(OrderStatus.Ready, OrderStatus.NoShow)]
    public void Allowed_moves_are_accepted(OrderStatus from, OrderStatus to) =>
        Assert.True(OrderStateMachine.CanMove(from, to));

    [Theory]
    [InlineData(OrderStatus.Placed, OrderStatus.Ready)]       // cannot skip cooking
    [InlineData(OrderStatus.Placed, OrderStatus.PickedUp)]
    [InlineData(OrderStatus.Placed, OrderStatus.NoShow)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Placed)]     // no way back
    [InlineData(OrderStatus.Ready, OrderStatus.Cooking)]
    [InlineData(OrderStatus.Ready, OrderStatus.Cancelled)]    // the food is ready: hand it over or mark a no-show
    [InlineData(OrderStatus.PickedUp, OrderStatus.Placed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Cooking)]
    [InlineData(OrderStatus.NoShow, OrderStatus.PickedUp)]
    public void Illegal_moves_are_rejected(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStateMachine.CanMove(from, to));

    [Theory]
    [InlineData(OrderStatus.PickedUp)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.NoShow)]
    public void Final_states_have_no_way_out(OrderStatus status)
    {
        Assert.Empty(OrderStateMachine.AllowedTargets(status));
        Assert.True(status.IsFinal());
        Assert.True(OrderStateMachine.NextKitchenStep(status).IsNone);
    }

    [Theory]
    [InlineData(OrderStatus.Placed, OrderStatus.Cooking)]
    [InlineData(OrderStatus.Cooking, OrderStatus.Ready)]
    [InlineData(OrderStatus.Ready, OrderStatus.PickedUp)]
    public void Kitchen_button_follows_the_happy_path(OrderStatus from, OrderStatus expected) =>
        Assert.Equal(Option.Some(expected), OrderStateMachine.NextKitchenStep(from));

    [Fact]
    public void Every_status_has_a_readable_label()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(status.Label()));
        }

        Assert.Equal("Not picked up", OrderStatus.NoShow.Label());
        Assert.Equal("Picked up", OrderStatus.PickedUp.Label());
    }
}
