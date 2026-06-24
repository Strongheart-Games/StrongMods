namespace CustomChatCommands {
  public struct ChatCommandSender {
    public readonly ClientInfo ClientInfo;
    public readonly int EntityId;
    public readonly string MainName;

    private bool resolvedEntityPlayer;
    private EntityPlayer entityPlayer;

    public ChatCommandSender(ClientInfo clientInfo, int entityId, string mainName) {
      ClientInfo = clientInfo;
      EntityId = entityId;
      MainName = mainName;
      resolvedEntityPlayer = false;
      entityPlayer = null;
    }

    public ChatCommandSender(ModEvents.SChatMessageData data) : this(data.ClientInfo, data.SenderEntityId,
      data.MainName) {
    }

    public EntityPlayer GetEntityPlayer() {
      if (resolvedEntityPlayer) {
        return entityPlayer;
      }

      GameManager.Instance.World.Players.dict.TryGetValue(EntityId, out entityPlayer);
      resolvedEntityPlayer = true;
      return entityPlayer;
    }
  }
}
