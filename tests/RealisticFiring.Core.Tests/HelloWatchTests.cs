namespace RealisticFiring.Core.Tests;

public class HelloWatchTests
{
    private const uint Due = 45u * 60u;

    [Fact] public void AGuestWhoNeverAnswersIsToldOfOnceWhenDue()
    {
        var watch = new HelloWatch<string>();
        watch.Joined(3, "Ripper_Ty22", Due);
        Assert.Empty(watch.Overdue(Due - 1, hosting: true));
        Assert.Equal(new[] { "Ripper_Ty22" }, watch.Overdue(Due, hosting: true));
        Assert.Empty(watch.Overdue(Due + 600, hosting: true));
    }

    [Fact] public void AHelloAfterTheJoinIsHeard()
    {
        var watch = new HelloWatch<string>();
        watch.Joined(3, "Ripper_Ty22", Due);
        watch.Heard(3);
        Assert.Equal(0, watch.Waiting);
        Assert.Empty(watch.Overdue(Due * 2, hosting: true));
    }

    [Fact] public void AHelloBeforeTheJoinIsHeardToo()
    {
        // Ace's host log, 2026-11-11: "Ripper_Ty22 joined with Realistic
        // Firing 1.17.9, settings matching" came before the Multiplayer
        // mod's join, and the join set him waiting again.
        var watch = new HelloWatch<string>();
        watch.Heard(3);
        watch.Joined(3, "Ripper_Ty22", Due);
        Assert.Equal(0, watch.Waiting);
        Assert.Empty(watch.Overdue(Due * 2, hosting: true));
    }

    [Fact] public void TheServerStoppingWithAGuestUnheardTellsNobody()
    {
        // The host left for the main menu 34 s after the join, before the
        // 45 s were up; the server stopped without reporting the guest gone.
        var watch = new HelloWatch<string>();
        watch.Joined(3, "Ripper_Ty22", Due);
        watch.Clear();
        Assert.Equal(0, watch.Waiting);
        Assert.Empty(watch.Overdue(Due * 2, hosting: true));
    }

    [Fact] public void OutsideAHostedSessionNobodyIsToldOfAndTheWaitIsForgotten()
    {
        // Had the stop gone unreported, the main menu's ticks still ran.
        var watch = new HelloWatch<string>();
        watch.Joined(3, "Ripper_Ty22", Due);
        Assert.Empty(watch.Overdue(Due * 2, hosting: false));
        Assert.Equal(0, watch.Waiting);
        Assert.Empty(watch.Overdue(Due * 3, hosting: true));
    }

    [Fact] public void AHelloHeardInOneSessionDoesNotCoverTheNext()
    {
        var watch = new HelloWatch<string>();
        watch.Heard(3);
        watch.Clear();
        watch.Joined(3, "someone else", Due);
        Assert.Equal(new[] { "someone else" }, watch.Overdue(Due, hosting: true));
    }

    [Fact] public void AGuestWhoLeftFreesTheirIdForTheNextToJoin()
    {
        var watch = new HelloWatch<string>();
        watch.Heard(3);
        watch.Left(3);
        watch.Joined(3, "a new guest", Due);
        Assert.Equal(new[] { "a new guest" }, watch.Overdue(Due, hosting: true));
        // And one who leaves while awaited is not told of.
        watch.Joined(4, "gone", Due);
        watch.Left(4);
        Assert.Empty(watch.Overdue(Due * 2, hosting: true));
    }

    [Fact] public void EachGuestIsWatchedOnTheirOwnTime()
    {
        var watch = new HelloWatch<string>();
        watch.Joined(3, "first", 100u);
        watch.Joined(4, "second", 200u);
        watch.Heard(4);
        watch.Joined(5, "third", 300u);
        Assert.Equal(new[] { "first" }, watch.Overdue(250u, hosting: true));
        Assert.Equal(new[] { "third" }, watch.Overdue(300u, hosting: true));
    }
}
