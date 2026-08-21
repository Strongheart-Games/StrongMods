using System;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>Violation counters use authenticated-player identity, not display identity.</summary>
[Collection(ModLogicCollection.Name)]
public sealed class AuthZViolationLogTests {
  private readonly Type clientInfoType;
  private readonly Type violationLogType;

  public AuthZViolationLogTests() {
    ModLogicHost host = ModLogicHost.For("AuthZ");
    clientInfoType = host.GameType("ClientInfo");
    violationLogType = host.ModType("AuthZ.ViolationLog");
  }

  [Fact]
  public void Disconnect_keeps_player_state_after_display_identity_changes() {
    object sender = Client(42, "first-name");
    AddState("<no authenticated player>");
    ModLogicHost.SetInstance(sender, "playerName", "renamed");

    Assert.Equal(1, (int)ModLogicHost.CallStatic(violationLogType, "TotalFor", sender));
    ModLogicHost.CallStatic(violationLogType, "Forget", sender);
    Assert.Equal(1, (int)ModLogicHost.CallStatic(violationLogType, "TotalFor", sender));
    Assert.Equal(1, (int)ModLogicHost.CallStatic(violationLogType, "TotalFor", Client(7, "next-client")));
  }

  private void AddState(string playerKey) {
    var totals = violationLogType.GetField("s_totalsByPlayer", System.Reflection.BindingFlags.NonPublic |
      System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    ModLogicHost.Call(totals, "Add", playerKey, 1);
    var countersField = violationLogType.GetField("s_countersByPlayer", System.Reflection.BindingFlags.NonPublic |
      System.Reflection.BindingFlags.Static)!;
    object counters = Activator.CreateInstance(countersField.FieldType.GetGenericArguments()[1])!;
    ModLogicHost.Call(countersField.GetValue(null)!, "Add", playerKey, counters);
  }

  private object Client(int clientNumber, string playerName) {
    object sender = ModLogicHost.Uninitialized(clientInfoType);
    ModLogicHost.SetInstance(sender, "ClientNumber", clientNumber);
    ModLogicHost.SetInstance(sender, "playerName", playerName);
    return sender;
  }
}
