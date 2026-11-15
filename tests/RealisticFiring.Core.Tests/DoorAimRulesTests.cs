using Xunit;
namespace RealisticFiring.Core.Tests;

public class DoorAimRulesTests
{
    private const float Hold = 3f;

    [Fact] public void OnlyAShovelsAimPlacesCoal()
    {
        Assert.True(DoorAimRules.AimsCoal(DoorAimKind.Shovel, 0.5f, Hold));
        // Wood or a rag just laid, or a rake, never steers a keyboard shovelful or a stoker's coal.
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.LayWood, 0.5f, Hold));
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.LayRag, 0.5f, Hold));
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.Rake, 0.5f, Hold));
    }

    [Fact] public void OnlyARakesAimRakes()
    {
        Assert.True(DoorAimRules.AimsRake(DoorAimKind.Rake, 0.5f, Hold));
        Assert.False(DoorAimRules.AimsRake(DoorAimKind.Shovel, 0.5f, Hold));
        Assert.False(DoorAimRules.AimsRake(DoorAimKind.LayWood, 0.5f, Hold));
        Assert.False(DoorAimRules.AimsRake(DoorAimKind.LayRag, 0.5f, Hold));
    }

    [Fact] public void ALayTakesOnlyItsOwnKindsAim()
    {
        Assert.True(DoorAimRules.AimsLay(DoorAimKind.LayWood, DoorAimKind.LayWood, 0.1f, Hold));
        Assert.True(DoorAimRules.AimsLay(DoorAimKind.LayRag, DoorAimKind.LayRag, 0.1f, Hold));
        Assert.False(DoorAimRules.AimsLay(DoorAimKind.LayRag, DoorAimKind.LayWood, 0.1f, Hold));
        Assert.False(DoorAimRules.AimsLay(DoorAimKind.Shovel, DoorAimKind.LayWood, 0.1f, Hold));
        Assert.False(DoorAimRules.AimsLay(DoorAimKind.Rake, DoorAimKind.LayRag, 0.1f, Hold));
        // Something that lays nothing takes no lay aim.
        Assert.False(DoorAimRules.AimsLay(DoorAimKind.Shovel, DoorAimKind.Shovel, 0.1f, Hold));
    }

    [Fact] public void AnAimGoesStaleAfterItsHold()
    {
        Assert.True(DoorAimRules.AimsCoal(DoorAimKind.Shovel, Hold, Hold));
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.Shovel, Hold + 0.01f, Hold));
        Assert.False(DoorAimRules.AimsRake(DoorAimKind.Rake, Hold + 0.01f, Hold));
        Assert.False(DoorAimRules.AimsLay(DoorAimKind.LayWood, DoorAimKind.LayWood, Hold + 0.01f, Hold));
        // One from the future (a time base that went back, as at a load) is not taken.
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.Shovel, -1f, Hold));
        Assert.False(DoorAimRules.AimsCoal(DoorAimKind.Shovel, float.NaN, Hold));
    }

    [Theory]
    [InlineData(DoorAimKind.Shovel, "Shovel", false)]
    [InlineData(DoorAimKind.Rake, "Rake", true)]
    [InlineData(DoorAimKind.LayWood, "Wood", true)]
    [InlineData(DoorAimKind.LayRag, "Rag", true)]
    public void EachKindIsNamedOnTheLabel(DoorAimKind kind, string label, bool needsOpenDoor)
    {
        Assert.Equal(label, DoorAimRules.Label(kind));
        Assert.Equal(needsOpenDoor, DoorAimRules.LabelNeedsOpenDoor(kind));
    }

    [Fact] public void OnlyWoodAndRagsLay()
    {
        Assert.True(DoorAimRules.Lays(DoorAimKind.LayWood));
        Assert.True(DoorAimRules.Lays(DoorAimKind.LayRag));
        Assert.False(DoorAimRules.Lays(DoorAimKind.Shovel));
        Assert.False(DoorAimRules.Lays(DoorAimKind.Rake));
    }
}
