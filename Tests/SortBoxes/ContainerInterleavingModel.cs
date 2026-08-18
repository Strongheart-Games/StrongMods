using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.SortBoxes;

/// <summary>
///   The events that can reach a container between a sort starting and finishing, in the order the server
///   sees them. Every one of these is a real message or callback in 7 Days to Die, and all of them arrive on
///   the main thread — the hazard is interleaving, not parallelism.
/// </summary>
public enum ContainerEvent {
  /// <summary>A client took the access lock. <c>TEFeatureStorage.OnLockedServer</c>.</summary>
  PlayerOpened,

  /// <summary>
  ///   A client pushed its whole item array to the server. <c>NetPackageTileEntity.ProcessPackage</c> calls
  ///   <c>tileEntity.read(..., StreamModeRead.FromClient)</c>, which overwrites the server's array wholesale
  ///   — it is not a delta, and it is not validated against the sender.
  /// </summary>
  ClientPushedContents,

  /// <summary>A client released the access lock. <c>TEFeatureStorage.OnUnlockedServer</c>.</summary>
  PlayerClosed,

  /// <summary>The sort wrote items into this container.</summary>
  SortWrote,

  /// <summary>The block was destroyed or the chunk unloaded; the tile entity reference is now stale.</summary>
  ContainerVanished,
}

/// <summary>
///   A minimal model of one container's server-side state under the real replication rules, built for the #31
///   research report. It exists to make the "what breaks if a player is in the box" question answerable by
///   running it rather than by reasoning about it.
///   <para>
///     The single rule that drives every outcome: a client's push REPLACES the server's item array with the
///     client's own copy, which the client built when it opened the box. Anything the server wrote in between
///     is gone. That is why an advisory in-use check matters even though nothing here is multi-threaded.
///   </para>
///   Not shipping code, and not in StrongBoxes.
/// </summary>
public sealed class ContainerInterleavingModel {
  private readonly List<string> log = new();

  public ContainerInterleavingModel(params string[] initialContents) {
    ServerContents = initialContents.ToList();
  }

  /// <summary>What the server believes the container holds.</summary>
  public List<string> ServerContents { get; private set; }

  /// <summary>The snapshot the client took when it opened the box, and will push back on close.</summary>
  public List<string> ClientCopy { get; private set; }

  public bool IsOpen { get; private set; }

  public bool Exists { get; private set; } = true;

  /// <summary>Items the sort believed it had delivered but which no longer exist anywhere.</summary>
  public List<string> LostBySort { get; } = new();

  public IReadOnlyList<string> Log => log;

  public void Apply(ContainerEvent e, string item = null) {
    switch (e) {
      case ContainerEvent.PlayerOpened:
        IsOpen = true;
        ClientCopy = ServerContents.ToList();
        log.Add($"open: client snapshotted [{string.Join(",", ClientCopy)}]");
        break;

      case ContainerEvent.SortWrote:
        if (!Exists) {
          log.Add($"sort wrote '{item}' to a container that no longer exists — item destroyed");
          LostBySort.Add(item);
          break;
        }

        ServerContents.Add(item);
        log.Add($"sort wrote '{item}'; server now [{string.Join(",", ServerContents)}]");
        break;

      case ContainerEvent.ClientPushedContents:
      case ContainerEvent.PlayerClosed:
        if (e == ContainerEvent.PlayerClosed) {
          IsOpen = false;
        }

        if (ClientCopy == null) {
          log.Add("close with no client copy — nothing overwritten");
          break;
        }

        List<string> clobbered = ServerContents.Where(i => !ClientCopy.Contains(i)).ToList();
        ServerContents = ClientCopy.ToList();
        foreach (var lost in clobbered) {
          LostBySort.Add(lost);
        }

        log.Add(clobbered.Count == 0
          ? $"client push: server now [{string.Join(",", ServerContents)}]"
          : $"client push OVERWROTE [{string.Join(",", clobbered)}]; " +
            $"server now [{string.Join(",", ServerContents)}]");
        break;

      case ContainerEvent.ContainerVanished:
        Exists = false;
        log.Add("container destroyed / chunk unloaded — held reference is now stale");
        break;

      default:
        throw new ArgumentOutOfRangeException(nameof(e), e, null);
    }
  }

  /// <summary>
  ///   The discipline the report recommends, expressed as a predicate: a sort may write to a container only
  ///   while nobody holds it open and it still exists. Checking this immediately before each write — not once
  ///   at the start of the scan — is what keeps a yielding sort correct.
  /// </summary>
  public bool SafeToWrite() => Exists && !IsOpen;
}
