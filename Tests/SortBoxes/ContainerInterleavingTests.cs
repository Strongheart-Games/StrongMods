using Xunit;
using Xunit.Abstractions;

namespace Tests.SortBoxes;

/// <summary>
///   What happens to a sort's writes when a player is in the target box, run against
///   <see cref="ContainerInterleavingModel" /> for the #31 research report.
///   <para>
///     The model encodes one game rule established by decompilation:
///     <c>NetPackageTileEntity.ProcessPackage</c> applies a client's whole item array to the server with no
///     sender check and no merge, and <c>TileEntityComposite.bUserAccessing</c> — the flag whose only writer
///     is the client-side loot window — is therefore always false on a dedicated server, so the guard in
///     <c>TEFeatureStorage.read</c> never protects the server copy.
///   </para>
/// </summary>
public sealed class ContainerInterleavingTests {
  private readonly ITestOutputHelper output;

  public ContainerInterleavingTests(ITestOutputHelper output) {
    this.output = output;
  }

  /// <summary>
  ///   The lost-update case, and the reason an in-use check is not optional. The sort delivers nails into a
  ///   box a player already has open; when that player closes it, their client pushes the copy it took on
  ///   open, and the nails are gone. No error is raised anywhere — the sort logged success.
  /// </summary>
  [Fact]
  public void Items_sorted_into_a_box_a_player_has_open_are_destroyed_when_they_close_it() {
    var box = new ContainerInterleavingModel("hammer");

    box.Apply(ContainerEvent.PlayerOpened);
    box.Apply(ContainerEvent.SortWrote, "nails");
    box.Apply(ContainerEvent.PlayerClosed);

    foreach (var line in box.Log) {
      output.WriteLine(line);
    }

    Assert.DoesNotContain("nails", box.ServerContents);
    Assert.Contains("nails", box.LostBySort);
  }

  /// <summary>
  ///   The items are not merely misplaced. They left the sort box — which the sort emptied — and never
  ///   arrived, so the count in the world drops. This is item destruction, not misfiling.
  /// </summary>
  [Fact]
  public void The_loss_is_destruction_rather_than_misplacement() {
    var box = new ContainerInterleavingModel();

    box.Apply(ContainerEvent.PlayerOpened);
    box.Apply(ContainerEvent.SortWrote, "brass");
    box.Apply(ContainerEvent.SortWrote, "lead");
    box.Apply(ContainerEvent.PlayerClosed);

    output.WriteLine($"sort believed it delivered 2 stacks; {box.LostBySort.Count} were destroyed");

    Assert.Empty(box.ServerContents);
    Assert.Equal(2, box.LostBySort.Count);
  }

  /// <summary>Checking before the write is what avoids it, and the check is one boolean.</summary>
  [Fact]
  public void Checking_immediately_before_the_write_avoids_the_loss_entirely() {
    var box = new ContainerInterleavingModel("hammer");

    box.Apply(ContainerEvent.PlayerOpened);
    if (box.SafeToWrite()) {
      box.Apply(ContainerEvent.SortWrote, "nails");
    } else {
      output.WriteLine("skipped: a player holds this container open");
    }

    box.Apply(ContainerEvent.PlayerClosed);

    Assert.Empty(box.LostBySort);
  }

  /// <summary>
  ///   Checking once at the start of the scan is NOT enough. A scan that yields between containers can have
  ///   the box opened after its check passed, so the check has to be re-taken next to the write — which is
  ///   the difference between RoboticInbox's per-container check and a hypothetical per-scan one.
  /// </summary>
  [Fact]
  public void A_check_taken_before_the_scan_yields_is_already_stale_by_the_write() {
    var box = new ContainerInterleavingModel("hammer");

    var checkedEarly = box.SafeToWrite();
    box.Apply(ContainerEvent.PlayerOpened); // the yield: a packet is processed here
    var trueAtWrite = box.SafeToWrite();
    if (checkedEarly) {
      box.Apply(ContainerEvent.SortWrote, "nails");
    }

    box.Apply(ContainerEvent.PlayerClosed);

    output.WriteLine($"check before the yield said {checkedEarly}; at the write the truth was {trueAtWrite}");

    Assert.True(checkedEarly);
    Assert.False(trueAtWrite);
    Assert.Contains("nails", box.LostBySort);
  }

  /// <summary>
  ///   The other thing a yield lets happen: the container stops existing. A held tile-entity reference does
  ///   not become null, so a write to it succeeds and goes nowhere.
  /// </summary>
  [Fact]
  public void A_container_destroyed_mid_scan_swallows_writes_to_the_stale_reference() {
    var box = new ContainerInterleavingModel("hammer");

    box.Apply(ContainerEvent.ContainerVanished);
    box.Apply(ContainerEvent.SortWrote, "nails");

    foreach (var line in box.Log) {
      output.WriteLine(line);
    }

    Assert.False(box.SafeToWrite());
    Assert.Contains("nails", box.LostBySort);
  }

  /// <summary>
  ///   With nobody in the box the ordinary path is uneventful, which is why the failure is easy to miss in
  ///   testing: it needs a second player doing something ordinary at the same moment.
  /// </summary>
  [Fact]
  public void An_undisturbed_sort_keeps_everything() {
    var box = new ContainerInterleavingModel("hammer");

    Assert.True(box.SafeToWrite());
    box.Apply(ContainerEvent.SortWrote, "nails");

    Assert.Equal(new[] { "hammer", "nails" }, box.ServerContents);
    Assert.Empty(box.LostBySort);
  }

  /// <summary>
  ///   A client push can also arrive while the box is open, not only on close — any client-side modification
  ///   sends the whole array. So the window is not "at close", it is "at any time while open".
  /// </summary>
  [Fact]
  public void A_push_arriving_mid_session_clobbers_just_as_a_close_does() {
    var box = new ContainerInterleavingModel("hammer");

    box.Apply(ContainerEvent.PlayerOpened);
    box.Apply(ContainerEvent.SortWrote, "nails");
    box.Apply(ContainerEvent.ClientPushedContents);

    Assert.Contains("nails", box.LostBySort);
    Assert.True(box.IsOpen);
  }
}
