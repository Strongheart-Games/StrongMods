using System;
using System.IO;
using Tests.Fixtures;
using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   StrongMods' case-sensitivity feature, run for real in a <see cref="ModLogicHost" /> against a scratch
///   directory. Both types touch only the BCL and <c>Log</c>, which the stubbed universe satisfies.
///   <para>
///     The scratch tree is deliberately shaped like the Linux server that reported #89 —
///     <c>&lt;root&gt;/servers/pzdev</c>, spelled in lower case throughout. That shape is not decoration: #90
///     only reproduces when the install path is already all lower case, so a mixed-case temp path would hide
///     it. Lower-casing the string works on Windows too, where every spelling resolves anyway.
///   </para>
/// </summary>
[Collection(ModLogicCollection.Name)]
public sealed class CaseSensitiveFilesystemTests : IDisposable {
  private readonly Type caseSensitiveFilesystem;
  private readonly Type config;
  private readonly string root;
  private readonly string gameDir;
  private readonly string modDir;
  private readonly string userDataMods;

  public CaseSensitiveFilesystemTests() {
    ModLogicHost host = ModLogicHost.For("StrongMods");
    caseSensitiveFilesystem = host.ModType("StrongMods.CaseSensitiveFilesystem");
    config = host.ModType("StrongMods.Config");

    root = Path.Combine(Path.GetTempPath(), "strongmods-csfs-" + Guid.NewGuid().ToString("N"))
      .ToLowerInvariant();
    gameDir = Path.Combine(root, "servers", "pzdev");
    modDir = Path.Combine(gameDir, "Mods", "MyMod");
    Directory.CreateDirectory(Path.Combine(modDir, "Config"));
    File.WriteAllText(Path.Combine(modDir, "ModInfo.xml"), "<xml />");
    File.WriteAllText(Path.Combine(modDir, "Config", "blocks.xml"), "<xml />");

    // A second mods root on a different branch entirely, standing in for ModsBasePath under a user data
    // folder that points at another disk or mount.
    userDataMods = Path.Combine(root, "mnt", "gamedata", "Mods");
    Directory.CreateDirectory(Path.Combine(userDataMods, "OtherMod"));
    File.WriteAllText(Path.Combine(userDataMods, "OtherMod", "ModInfo.xml"), "<xml />");

    // Stands in for Init(), which would need a live ModManager and GameIO to resolve the same values.
    // Deepest first, as ResolveAnchors sorts them.
    ModLogicHost.SetStatic(caseSensitiveFilesystem, "s_anchors", new[] { userDataMods, gameDir });
  }

  public void Dispose() {
    ModLogicHost.SetStatic(caseSensitiveFilesystem, "s_anchors", new string[0]);
    if (Directory.Exists(root)) {
      Directory.Delete(root, true);
    }
  }

  private bool Exists(string path) =>
    (bool)ModLogicHost.CallStatic(caseSensitiveFilesystem, "Exists", path);

  [Fact]
  public void A_correctly_cased_path_below_the_game_directory_exists() {
    Assert.True(Exists(Path.Combine(modDir, "Config", "blocks.xml")));
  }

  /// <summary>The feature's whole purpose, and it must survive the narrowing in #89.</summary>
  [Fact]
  public void A_miscased_segment_below_the_game_directory_is_still_reported_absent() {
    Assert.False(Exists(Path.Combine(modDir, "config", "blocks.xml")));
  }

  /// <summary>
  ///   Casing above the game directory is not the modder's to get wrong — the game resolved that prefix
  ///   itself — and on the host that reported #89 the directory one level up was not even listable.
  /// </summary>
  [Fact]
  public void A_miscased_segment_above_the_game_directory_is_not_checked() {
    var viaMiscasedPrefix = Path.Combine(root, "SERVERS", "pzdev", "Mods", "MyMod", "Config", "blocks.xml");

    Assert.True(Exists(viaMiscasedPrefix));
  }

  /// <summary>
  ///   The game loads mods from TWO roots (<c>ModManager.LoadMods</c>): <c>ModsBasePath</c>, under the user
  ///   data directory and resolved through the platform layer, and <c>ModsBasePathLegacy</c>, under the game
  ///   directory. The first is routinely on a different disk or mount, so anchoring only at the game
  ///   directory would send every mod under it back to walking from the filesystem root.
  /// </summary>
  [Fact]
  public void A_mod_root_outside_the_game_directory_is_also_an_anchor() {
    var viaMiscasedPrefix = Path.Combine(root, "MNT", "gamedata", "Mods", "OtherMod", "ModInfo.xml");

    Assert.True(Exists(viaMiscasedPrefix));
  }

  /// <summary>Narrowing the walk must not cost the check itself, under either root.</summary>
  [Fact]
  public void A_miscased_segment_below_a_non_game_mod_root_is_still_reported_absent() {
    Assert.False(Exists(Path.Combine(userDataMods, "OtherMod", "modinfo.xml")));
  }

  /// <summary>
  ///   The reported crash (#89). Reporting absence would be almost as bad as throwing: ValidateModInfos
  ///   unloads a mod on absence, so a permission quirk would silently disable every mod on the server.
  /// </summary>
  [Fact]
  public void An_ancestor_that_cannot_be_listed_neither_throws_nor_reports_absence() {
    if (!UnlistableDirectory.Supported) {
      return; // running as root, which ignores permission bits; nothing to induce
    }

    using UnlistableDirectory blocked = UnlistableDirectory.At(Path.Combine(gameDir, "Mods"));
    Assert.True(blocked.Induced, "could not make the directory unlistable, so this test proves nothing");

    Assert.True(Exists(Path.Combine(modDir, "ModInfo.xml")));
  }

  /// <summary>
  ///   Exists() is transpiled in where File.Exists and Directory.Exists stood. Those return false for every
  ///   one of these; the game code calling them carries no handler for an exception (#89). Under Mono, which
  ///   is what the game runs, Path.GetFullPath throws for more of them than it does here.
  /// </summary>
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("\t")]
  [InlineData("bad|char.xml")]
  public void Hostile_input_is_reported_absent_rather_than_thrown_on(string path) {
    Assert.False(Exists(path));
  }

  /// <summary>
  ///   #90. GameIO.GetGamePath() returns "&lt;install&gt;/&lt;data dir&gt;/..", and Directory.Exists
  ///   collapses ".." lexically — so inverting the raw string touches only the data-directory segment, the
  ///   one the ".." erases. The probe then asks about the install directory itself, which of course exists,
  ///   and calls every filesystem case-insensitive.
  /// </summary>
  [Fact]
  public void The_opposite_case_spelling_survives_path_normalization() {
    var gamePath = GameIoShapedPath();

    var opposite = (string)ModLogicHost.CallStatic(config, "OppositeCaseSpelling", gamePath);

    Assert.NotNull(opposite);
    var asked = Path.GetFullPath(opposite);
    var original = Path.GetFullPath(gamePath);
    Assert.Equal(original, asked, ignoreCase: true); // still the same directory
    Assert.NotEqual(original, asked); // but genuinely spelled differently
  }

  /// <summary>
  ///   The behavioral half of #90. It can only discriminate on a case-sensitive filesystem — where the
  ///   filesystem ignores case every spelling resolves and both answers are legitimately true — so this one
  ///   does its work on the Linux CI runner, which is also the production platform. The white-box test above
  ///   is what catches the same defect on a Windows dev machine.
  /// </summary>
  [Fact]
  public void The_probe_answers_the_same_for_normalized_and_unnormalized_spellings() {
    var direct = IsCaseInsensitiveAt(gameDir);
    var viaGameIoShape = IsCaseInsensitiveAt(GameIoShapedPath());

    Assert.Equal(direct, viaGameIoShape);
  }

  private bool IsCaseInsensitiveAt(string path) =>
    (bool)ModLogicHost.CallStatic(config, "IsCaseInsensitiveAt", path);

  /// <summary>The game directory spelled the way GameIO.GetGamePath() spells it.</summary>
  private string GameIoShapedPath() {
    Directory.CreateDirectory(Path.Combine(gameDir, "7DaysToDieServer_Data"));
    return gameDir + "/7DaysToDieServer_Data/..";
  }
}
