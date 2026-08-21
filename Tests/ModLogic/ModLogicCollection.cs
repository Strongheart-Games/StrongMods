using Xunit;

namespace Tests.ModLogic;

/// <summary>
///   Every mod-logic test runs in one collection, serialized. The logic under test lives on static
///   singletons the game would own for the process lifetime (ConfigManager.Instance, PlayerDamage's
///   history), and a <see cref="Fixtures.ModLogicHost" /> is shared per mod, so two of these classes
///   running concurrently would fight over the same statics. The collection can still run beside other
///   collections because each host loads its game and mod assemblies into a private AssemblyLoadContext.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ModLogicCollection {
  public const string Name = "mod-logic";
}
