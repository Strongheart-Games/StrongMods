using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Tests.Fixtures;

/// <summary>
///   The game's XML patch entry points — the names in <c>WorldStaticData.xmlsToLoad</c>. Only a file whose
///   name matches one of these is a patch entry point: a mod's patch is found at
///   <c>{mod}/Config/{name}.xml</c> (BreadthFirstXmlPatcher.cs, replicating vanilla), so anything else under
///   <c>Config\</c> is processed only if an entry-point file pulls it in with <c>&lt;include&gt;</c>, and is
///   silently dead otherwise (#62).
///   Names are path-qualified where the game qualifies them — <c>XUi_InGame/windows</c>, not <c>windows</c> —
///   which is also why two vanilla files can share a base name without colliding.
///   <para>
///     Read from IL rather than from the loaded type on purpose: <c>WorldStaticData</c>'s initializer pulls in
///     Unity engine types (<c>ProfilerMarker</c> and friends) that cannot be satisfied headlessly, so touching
///     the field would throw. The IL is version-accurate for whichever unit is under test, which matters — the
///     list changes between game versions (<c>sandbox_overrides</c> arrived in 3.0).
///   </para>
/// </summary>
public static class EntryPoints {
  /// <summary>Entry-point names in declaration order, read from the unit under test.</summary>
  public static IReadOnlyList<string> Read(string managedDir) {
    using AssemblyDefinition assembly =
      AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "Assembly-CSharp.dll"));
    TypeDefinition worldStaticData = assembly.MainModule.GetType("WorldStaticData");
    MethodDefinition initializer = worldStaticData.Methods.First(m => m.Name == ".cctor");

    // Each entry is a `newobj XmlLoadInfo(string _xmlName, …, string _loadStepLocalizationKey)`. Both strings
    // are pushed before the call, so the FIRST ldstr since the previous entry is the name and any second is
    // the localization key ("materials" then "loadActionMaterials").
    var names = new List<string>();
    var pending = new List<string>();
    foreach (Instruction instruction in initializer.Body.Instructions) {
      if (instruction.OpCode == OpCodes.Ldstr) {
        pending.Add((string)instruction.Operand);
      } else if (instruction.OpCode == OpCodes.Newobj &&
                 ((MethodReference)instruction.Operand).DeclaringType.Name.Contains("XmlLoadInfo")) {
        if (pending.Count > 0) {
          names.Add(pending[0]);
        }

        pending.Clear();
      }
    }

    return names;
  }
}
