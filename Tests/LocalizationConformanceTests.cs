using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace Tests;

public sealed class LocalizationConformanceTests {
  [Fact]
  public void Localization_rows_match_their_file_header_column_count() {
    string repositoryRoot = AssemblyMetadata.Get("RepoRoot");
    string[] localizationFiles = Directory.GetDirectories(repositoryRoot)
      .Select(directory => Path.Combine(directory, "Config", "Localization.csv"))
      .Where(File.Exists)
      .ToArray();

    Assert.NotEmpty(localizationFiles);
    foreach (string file in localizationFiles) {
      using var reader = new TextFieldParser(file) {
        TextFieldType = FieldType.Delimited,
        HasFieldsEnclosedInQuotes = true
      };
      reader.SetDelimiters(",");

      string[] header = reader.ReadFields();
      Assert.NotEmpty(header);
      while (!reader.EndOfData) {
        string[] row = reader.ReadFields();
        Assert.True(row.Length == header.Length,
          $"{file} line {reader.LineNumber} has {row.Length} columns; its header has {header.Length}.");
      }
    }
  }

  [Fact]
  public void Strong_utils_buff_localization_references_are_defined() {
    string repositoryRoot = AssemblyMetadata.Get("RepoRoot");
    var localizationKeys = new HashSet<string>(LocalizationKeys(
      Path.Combine(repositoryRoot, "StrongUtils", "Config", "Localization.csv")));
    XDocument buffs = XDocument.Load(Path.Combine(repositoryRoot, "StrongUtils", "Config", "buffs.xml"));
    string[] referencedKeys = buffs.Descendants("buff")
      .Attributes()
      .Where(attribute => attribute.Name.LocalName is "name_key" or "description_key" or "tooltip_key")
      .Select(attribute => attribute.Value)
      .ToArray();

    foreach (string key in referencedKeys) {
      Assert.Contains(key, localizationKeys);
    }
  }

  [Fact]
  public void Player_spawned_trader_block_description_keys_are_defined() {
    string repositoryRoot = AssemblyMetadata.Get("RepoRoot");
    var localizationKeys = new HashSet<string>(LocalizationKeys(
      Path.Combine(repositoryRoot, "PlayerSpawnedTraders", "Config", "Localization.csv")));
    XDocument blocks = XDocument.Load(Path.Combine(repositoryRoot, "PlayerSpawnedTraders", "Config", "blocks.xml"));
    string[] referencedKeys = blocks.Descendants("property")
      .Where(property => (string)property.Attribute("name") == "DescriptionKey")
      .Select(property => (string)property.Attribute("value"))
      .ToArray();

    foreach (string key in referencedKeys) {
      Assert.Contains(key, localizationKeys);
    }
  }

  private static IEnumerable<string> LocalizationKeys(string file) {
    using var reader = new TextFieldParser(file) {
      TextFieldType = FieldType.Delimited,
      HasFieldsEnclosedInQuotes = true
    };
    reader.SetDelimiters(",");
    reader.ReadFields();

    while (!reader.EndOfData) {
      yield return reader.ReadFields()[0];
    }
  }
}
