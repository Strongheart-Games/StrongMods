using System;
using System.IO;
using System.Linq;
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
}
