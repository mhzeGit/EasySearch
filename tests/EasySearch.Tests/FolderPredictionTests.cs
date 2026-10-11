using EasySearch.Core;

namespace EasySearch.Tests;

public sealed class FolderPredictionTests
{
    [Theory]
    [InlineData("Proj", "Projects")]
    [InlineData("Projecs", "Projects")]
    [InlineData("annual r", "Annual Reports")]
    [InlineData("Reports", "Annual Reports")]
    public void Predicts_indexed_folder_names(string typed, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "EasySearchPredictionTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Projects"));
            Directory.CreateDirectory(Path.Combine(root, "Annual Reports"));
            File.WriteAllText(Path.Combine(root, "ProjectFile.txt"), "");
            var index = IndexBuilder.Build(new IndexScope([root], []));
            Assert.Equal(expected, FolderPrediction.Find(index, typed));
            Assert.Null(FolderPrediction.Find(index, ""));
        }
        finally { Directory.Delete(root, true); }
    }
}
