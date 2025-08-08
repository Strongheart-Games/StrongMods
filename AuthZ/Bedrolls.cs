namespace AuthZ {
  public class Bedrolls {
    public const string NoBedrollBuff = "buff_no_bedroll_strong";

    public static void UpdateBedrollWarning(EntityAlive entityAlive) {
      if (entityAlive is EntityPlayer player) {
        UpdateBedrollWarning(player);
      }
    }

    public static void UpdateBedrollWarning(EntityPlayer entityPlayer) {
      PersistentPlayerData data = GameManager.Instance.GetPersistentPlayerList()?.GetPlayerDataFromEntityID(entityPlayer.entityId);
      if (data is null) {
        return;
      }

      if (data.HasBedrollPos || entityPlayer.Progression.Level < 2) {
        if (entityPlayer.Buffs.HasBuff(NoBedrollBuff)) {
          entityPlayer.Buffs.RemoveBuff(NoBedrollBuff);
        }
      } else {
        if (!entityPlayer.Buffs.HasBuff(NoBedrollBuff)) {
          entityPlayer.Buffs.AddBuff(NoBedrollBuff);
        }
      }
    }
  }
}
