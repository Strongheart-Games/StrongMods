using System.Collections.Generic;

namespace DynamicLandClaimCount {
  public class DynamicLandClaimCount {
    public enum EnumOps {
      Add,
      Override
    }

    public static string[] CVars;
    public static EnumOps Op = EnumOps.Add;

    public static int GetLandClaimCount(EntityPlayer player) {
      var count = GameStats.GetInt(EnumGameStats.LandClaimCount);
      if (player is null || CVars is null || CVars.Length == 0 || player.Buffs is null) {
        return count;
      }

      foreach (var cvar in CVars) {
        count = GetLandClaimCount(player, count, cvar);
      }

      return count;
    }

    public static int GetLandClaimCount(EntityPlayer player, int count, string cvar) {
      if (!player.Buffs.HasCustomVar(cvar) || player.GetCVar(cvar) < 0) {
        return count;
      }

      var value = (int)player.GetCVar(cvar);
      return Op switch {
        EnumOps.Add => count + value,
        EnumOps.Override => value,
        _ => count
      };
    }

    public static int GetLandClaimCount(PersistentPlayerData player) {
      EntityPlayer p = player is null ? null : GameManager.Instance.World?.Players?.dict[player.EntityId];
      return GetLandClaimCount(p);
    }

    public static ModEvents.EModEventResult HandleChatMessage(ref ModEvents.SChatMessageData _data) {
      if (_data.Message.ToLower() == "/claims") {
        PersistentPlayerData player = GameManager.Instance.getPersistentPlayerData(_data.ClientInfo);
        WhisperLandClaimCount(player, _data.ClientInfo);
        return ModEvents.EModEventResult.StopHandlersAndVanilla;
      }

      return ModEvents.EModEventResult.Continue;
    }

    public static void WhisperLandClaimCount(PersistentPlayerData player, ClientInfo client = null) {
      if (player is null) {
        return;
      }

      Localization.TryGet("dynamic_land_claim_count_message", out var template);
      var message = template
        .Replace("{used}", player.GetLandProtectionBlocks().Count.ToString())
        .Replace("{total}", GetLandClaimCount(player).ToString());

      // Use the net package's logic to determine whether to send the message to the client or not
      NetPackageChat package = NetPackageManager.GetPackage<NetPackageChat>().Setup(EChatType.Whisper, -1, message,
        new List<int> { player.EntityId }, EMessageSender.None, GeneratedTextManager.BbCodeSupportMode.Supported);
      package.ProcessPackage(GameManager.Instance.World, GameManager.Instance);
    }

    public static void OnXMLChanged() {
      DynamicProperties properties = WorldEnvironment.Properties;
      if (properties is null) {
        return;
      }

      var cvar = "";
      EnumOps op = EnumOps.Add;
      properties.ParseString("dynamic_land_claim_count_cvar", ref cvar);
      properties.ParseEnum("dynamic_land_claim_count_op", ref op);
      // TODO: Wrap in a data object to make the switch-out atomic
      CVars = cvar.Split(',');
      Op = op;
      Log.Out($"[DynamicLandClaimCount] Configuration loaded: (cvars: {string.Join(", ", CVars)}, op: {Op})");
    }
  }
}
