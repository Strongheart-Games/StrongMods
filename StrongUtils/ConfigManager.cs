using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace StrongUtils {
  public class ConfigManager {
    private static readonly object s_lock = new();

    private readonly string _configDirectory;
    private readonly Dictionary<string, FileSystemWatcher> _fileWatchers;
    private readonly Dictionary<string, ConfigFileInfo> _registeredFiles;

    private ConfigManager(string configDirectory) {
      if (string.IsNullOrWhiteSpace(configDirectory)) {
        throw new ArgumentException("Config directory cannot be null or empty.", nameof(configDirectory));
      }

      _configDirectory = Path.GetFullPath(configDirectory);
      _registeredFiles = new Dictionary<string, ConfigFileInfo>();
      _fileWatchers = new Dictionary<string, FileSystemWatcher>();

      if (!Directory.Exists(_configDirectory)) {
        Directory.CreateDirectory(_configDirectory);
      }
    }

    public static ConfigManager Instance { get; private set; }

    public static void Init(string configDirectory) {
      lock (s_lock) {
        if (Instance != null) {
          throw new InvalidOperationException("ConfigManager has already been initialized.");
        }

        Instance = new ConfigManager(configDirectory);
      }
    }

    public void RegisterConfigFile(
      string filename,
      string defaultContents = "<config></config>",
      Action<XElement> updateMethod = null) {
      if (string.IsNullOrWhiteSpace(filename)) {
        throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
      }

      if (Path.IsPathRooted(filename)) {
        throw new ArgumentException("Filename must be relative, not an absolute path.", nameof(filename));
      }

      if (defaultContents == null) {
        throw new ArgumentNullException(nameof(defaultContents));
      }

      var fullPath = Path.Combine(_configDirectory, filename);
      var normalizedFilename = NormalizeFilename(filename);

      if (_registeredFiles.ContainsKey(normalizedFilename)) {
        Log.Warning($"[ConfigManager] Ignoring request to re-register config file '{filename}'.");
        return;
      }

      _registeredFiles[normalizedFilename] = new ConfigFileInfo {
        FullPath = fullPath,
        UpdateMethod = updateMethod
      };

      if (!File.Exists(fullPath)) {
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(directoryPath)) {
          Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(fullPath, defaultContents);
      }

      if (updateMethod is not null) {
        SetupFileWatcher(normalizedFilename, fullPath, updateMethod);
      }
    }

    public XElement ReadConfigFile(string filename) {
      if (string.IsNullOrWhiteSpace(filename)) {
        throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
      }

      var fullPath = Path.Combine(_configDirectory, filename);
      return XElement.Load(fullPath);
    }

    public void WriteConfigFile(string filename, XElement newContents) {
      if (string.IsNullOrWhiteSpace(filename)) {
        throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
      }

      if (Path.IsPathRooted(filename)) {
        throw new ArgumentException("Filename must be relative, not an absolute path.", nameof(filename));
      }

      if (newContents == null) {
        throw new ArgumentNullException(nameof(newContents));
      }

      var normalizedFilename = NormalizeFilename(filename);

      if (!_registeredFiles.TryGetValue(normalizedFilename, out ConfigFileInfo configInfo)) {
        throw new InvalidOperationException($"Config file '{filename}' is not registered.");
      }

      newContents.Save(configInfo.FullPath);
    }

    public void AppendConfig(string filename, XElement elementToAppend) {
      if (string.IsNullOrWhiteSpace(filename)) {
        throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
      }

      if (Path.IsPathRooted(filename)) {
        throw new ArgumentException("Filename must be relative, not an absolute path.", nameof(filename));
      }

      if (elementToAppend == null) {
        throw new ArgumentNullException(nameof(elementToAppend));
      }

      var normalizedFilename = NormalizeFilename(filename);

      if (!_registeredFiles.TryGetValue(normalizedFilename, out ConfigFileInfo configInfo)) {
        throw new InvalidOperationException($"Config file '{filename}' is not registered.");
      }

      var root = XElement.Load(configInfo.FullPath);
      root.Add(elementToAppend);
      root.Save(configInfo.FullPath);
    }

    public void RemoveConfig(string filename, XElement elementToRemove) {
      if (string.IsNullOrWhiteSpace(filename)) {
        throw new ArgumentException("Filename cannot be null or empty.", nameof(filename));
      }

      if (Path.IsPathRooted(filename)) {
        throw new ArgumentException("Filename must be relative, not an absolute path.", nameof(filename));
      }

      if (elementToRemove == null) {
        throw new ArgumentNullException(nameof(elementToRemove));
      }

      var normalizedFilename = NormalizeFilename(filename);

      if (!_registeredFiles.TryGetValue(normalizedFilename, out ConfigFileInfo configInfo)) {
        throw new InvalidOperationException($"Config file '{filename}' is not registered.");
      }

      var root = XElement.Load(configInfo.FullPath);
      XElement matchingElement = root.Elements().FirstOrDefault(e => XNode.DeepEquals(e, elementToRemove));

      if (matchingElement == null) {
        return;
      }

      matchingElement.Remove();
      root.Save(configInfo.FullPath);
    }

    public void Dispose() {
      foreach (FileSystemWatcher watcher in _fileWatchers.Values) {
        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
      }

      _fileWatchers.Clear();
      _registeredFiles.Clear();

      lock (s_lock) {
        Instance = null;
      }
    }

    private void SetupFileWatcher(string normalizedFilename, string fullPath, Action<XElement> updateMethod) {
      var directory = Path.GetDirectoryName(fullPath);
      var fileName = Path.GetFileName(fullPath);

      var watcher = new FileSystemWatcher(directory, fileName) {
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
        EnableRaisingEvents = true
      };

      watcher.Changed += (sender, e) => {
        try {
          var contents = XElement.Load(e.FullPath);
          updateMethod(contents);
        } catch (Exception ex) {
          Console.Error.WriteLine($"Error in update callback for '{normalizedFilename}': {ex.Message}");
        }
      };

      _fileWatchers[normalizedFilename] = watcher;
    }

    private static string NormalizeFilename(string filename) {
      return filename.Replace('\\', '/').ToLowerInvariant();
    }

    private class ConfigFileInfo {
      public string FullPath { get; set; }
      public Action<XElement> UpdateMethod { get; set; }
    }
  }
}
