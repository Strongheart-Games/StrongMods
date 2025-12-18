namespace StrongUtils {
  public static class EntityPlayerExtensions {
    private static string s_strongsworn_cvar = "strongsworn";

    public static bool IsStrongSworn(this EntityPlayer player) {
      return player.GetCVar(s_strongsworn_cvar) != 0;
    }

    public static void SetStrongSworn(this EntityPlayer player, bool isStrongSworn) {
      player.SetCVar(s_strongsworn_cvar, isStrongSworn ? 1 : 0);
    }
  }
}
