using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Noemax.GZip;

namespace StrongMods {
  /// <summary>
  ///   Reorders XML config patching from file-major ("depth-first": every mod's patch for one XML
  ///   file is applied before moving to the next XML file) to mod-major ("breadth-first": every
  ///   XML file is patched by one mod before moving to the next mod).
  ///
  ///   Within a single mod's pass, patches are still applied in the order the files appear in
  ///   WorldStaticData.xmlsToLoad. Mods are visited in the order returned by
  ///   ModManager.GetLoadedMods(), same as vanilla.
  ///
  ///   Design: the mod loop lives inside XmlPatcher.LoadAndPatchConfig, which is called once per
  ///   file from WorldStaticData.loadSingleXml, so the depth-first ordering is baked into the
  ///   innermost layer. Rather than rewriting loadSingleXml (which also handles conditionals,
  ///   ConfigsDump, client caching, and the actual LoadMethod), this class:
  ///
  ///     Phase 1: Loads every eligible base XML into a cache (xmlsToLoad order).
  ///     Phase 2: Applies patches mod-major: for each mod, for each file (xmlsToLoad order).
  ///     Phase 3: Runs the untouched vanilla per-file pipeline (loadSingleXml). A prefix on
  ///              LoadAndPatchConfig hands each file its pre-patched XmlFile from the cache
  ///              instead of loading and patching it again.
  ///
  ///   Any LoadAndPatchConfig caller outside this pipeline (e.g. the client received-configs
  ///   path) misses the cache and falls through to vanilla behavior.
  /// </summary>
  public static class BreadthFirstXmlPatcher {
    /// <summary>
    ///   XmlName (no .xml extension) -> pre-patched file. A key present with a null value marks
    ///   a base XML that failed to load; the LoadAndPatchConfig prefix then replicates vanilla
    ///   failure behavior (error already logged, callback never invoked) instead of falling
    ///   through and re-patching depth-first.
    /// </summary>
    private static readonly Dictionary<string, XmlFile> s_patchedFiles = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetPatchedFile(string xmlName, out XmlFile patchedFile) {
      return s_patchedFiles.TryGetValue(xmlName, out patchedFile);
    }

    private static IEnumerator LoadAllXmlsBreadthFirstCo(
      bool isStartup, WorldStaticData.ProgressDelegate progressDelegate, string[] onlyXmls) {
      WorldStaticData.LoadAllXmlsCoComplete = false;
      var memStream = new MemoryStream();
      var zipStream = new DeflateOutputStream(memStream, 3);

      // Mirror the vanilla eligibility filters up front so phases 1 and 2 only touch files
      // that phase 3 will actually consume.
      var eligible = new List<WorldStaticData.XmlLoadInfo>();
      foreach (WorldStaticData.XmlLoadInfo loadInfo in WorldStaticData.xmlsToLoad) {
        if (onlyXmls != null && !onlyXmls.ContainsCaseInsensitive(loadInfo.XmlName)) {
          continue;
        }

        if (!loadInfo.XmlFileExists()) {
          if (!loadInfo.IgnoreMissingFile) {
            Log.Error("[BreadthFirstXmlPatcher] XML is missing: " + loadInfo.XmlName);
          }

          continue;
        }

        if (isStartup && !loadInfo.LoadAtStartup) {
          continue;
        }

        eligible.Add(loadInfo);
      }

      try {
        var timer = new MicroStopwatch(true);

        // Phase 1: load all base XMLs, in xmlsToLoad order.
        Log.Out($"[BreadthFirstXmlPatcher] Loading base XML");
        foreach (WorldStaticData.XmlLoadInfo loadInfo in eligible) {
          yield return LoadBaseXmlCo(loadInfo.XmlName);
          if (timer.ElapsedMilliseconds > Constants.cMaxLoadTimePerFrameMillis) {
            yield return null;
            timer.ResetAndRestart();
          }
        }

        // Phase 2: apply patches breadth-first — all of one mod's patches (in xmlsToLoad
        // order) before moving on to the next mod.
        foreach (Mod mod in ModManager.GetLoadedMods()) {
          if (!mod.GameConfigMod) {
            continue;
          }
          Log.Out($"[BreadthFirstXmlPatcher] Applying XML patches from mod '{mod.Name}'");
          foreach (WorldStaticData.XmlLoadInfo loadInfo in eligible) {
            if (!s_patchedFiles.TryGetValue(loadInfo.XmlName, out XmlFile targetFile) || targetFile == null) {
              continue; // Base load failed; already logged in phase 1.
            }

            var patchFilePath = $"{mod.Path}/Config/{loadInfo.XmlName}.xml";
            if (!SdFile.Exists(patchFilePath)) {
              continue;
            }

            try {
              XmlFile patchFile = XmlPatcher.ReadPatchXmlWithFixedModFolders(mod, patchFilePath);
              if (patchFile != null) {
                XmlPatcher.PatchXml(targetFile, patchFile.XmlDoc.Root, patchFile, mod);
              }
            } catch (Exception ex) {
              Log.Error($"[BreadthFirstXmlPatcher] Patching '{loadInfo.XmlName}.xml' from mod '{mod.Name}' failed:");
              Log.Exception(ex);
            }

            if (timer.ElapsedMilliseconds > Constants.cMaxLoadTimePerFrameMillis) {
              yield return null;
              timer.ResetAndRestart();
            }
          }
        }

        // Phase 3: the vanilla per-file pipeline. loadSingleXml runs unmodified (conditionals,
        // ConfigsDump, client cache compression, LoadMethod, ExecuteAfterLoad); its call to
        // LoadAndPatchConfig is intercepted by our prefix and served from the cache.
        foreach (WorldStaticData.XmlLoadInfo loadInfo in eligible) {
          if (progressDelegate != null && loadInfo.LoadStepLocalizationKey != null) {
            progressDelegate(Localization.Get(loadInfo.LoadStepLocalizationKey), 0.0f);
          }

          yield return WorldStaticData.loadSingleXml(loadInfo, memStream, zipStream);
        }
      } finally {
        // Never leave stale entries behind for later LoadAndPatchConfig callers (reloads,
        // client paths), even if the coroutine is stopped mid-flight.
        s_patchedFiles.Clear();
      }

      WorldStaticData.LoadAllXmlsCoComplete = true;
    }

    /// <summary>
    ///   Replicates the base-XML load at the top of the vanilla XmlPatcher.LoadAndPatchConfig
    ///   and stores the result (or a null failure marker) in the cache.
    /// </summary>
    private static IEnumerator LoadBaseXmlCo(string xmlName) {
      Exception loadException = null;
      var xmlFile = new XmlFile(
        GameIO.GetGameDir("Data/Config"),
        xmlName + ".xml",
        exception => {
          if (exception != null) {
            loadException = exception;
          }
        });
      while (!xmlFile.Loaded && loadException == null) {
        yield return null;
      }

      if (loadException != null) {
        Log.Error($"[BreadthFirstXmlPatcher] Loading base XML '{xmlFile.Filename}' failed:");
        Log.Exception(loadException);
        s_patchedFiles[xmlName] = null;
      } else {
        s_patchedFiles[xmlName] = xmlFile;
      }
    }

    private static string TrimXmlExtension(string configName) {
      return configName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? configName[..^4] : configName;
    }

    [HarmonyPatch(typeof(WorldStaticData), nameof(WorldStaticData.LoadAllXmlsCo))]
    public static class WorldStaticDataLoadAllXmlsCoPatch {
      // Parameter names must match the original method's exactly; Harmony binds by name.
      public static bool Prefix(
        bool _isStartup,
        WorldStaticData.ProgressDelegate _progressDelegate,
        string[] _onlyXmls,
        ref IEnumerator __result) {
        __result = LoadAllXmlsBreadthFirstCo(_isStartup, _progressDelegate, _onlyXmls);
        return false; // Skip the vanilla coroutine entirely.
      }
    }

    [HarmonyPatch(typeof(XmlPatcher), nameof(XmlPatcher.LoadAndPatchConfig))]
    public static class XmlPatcherLoadAndPatchConfigPatch {
      public static bool Prefix(
        string _configName, Action<XmlFile> _callback, ref IEnumerator __result) {
        var key = TrimXmlExtension(_configName);
        XmlFile prePatched;
        if (!s_patchedFiles.TryGetValue(key, out prePatched)) {
          return true; // Not part of our pipeline — run vanilla load + depth-first patching.
        }

        // Consumed entries are removed so already-loaded files can be collected while later
        // files are still working through phase 3.
        s_patchedFiles.Remove(key);
        __result = YieldPrePatched(prePatched, _callback);
        return false;
      }

      private static IEnumerator YieldPrePatched(XmlFile file, Action<XmlFile> callback) {
        // A null file means the base XML failed to load in phase 1. Vanilla behavior on a
        // failed base load is to log (already done in phase 1) and never invoke the callback,
        // which makes loadSingleXml skip the file.
        if (file != null) {
          callback(file);
        }

        yield break;
      }
    }
  }
}
