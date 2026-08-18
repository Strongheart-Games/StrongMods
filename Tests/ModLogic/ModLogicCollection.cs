using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   Every mod-logic test runs in one collection, serialized. The logic under test lives on static
///   singletons the game would own for the process lifetime (ConfigManager.Instance, PlayerDamage's
///   history), and a <see cref="Fixtures.ModLogicHost" /> is shared per mod, so two of these classes
///   running concurrently would fight over the same statics.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModLogicCollection {
  public const string Name = "mod-logic";
}
