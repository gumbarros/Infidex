using Infidex.Core;
using Infidex.Indexing;
using Infidex.Scoring;
using Infidex.Tokenization;

namespace Infidex.Tests;

[TestClass]
public class DebugLoggingTests
{
    private static VectorModel CreateModel(bool enableDebugLogging)
    {
        var tokenizer = new Tokenizer([3], 2, 0, TextNormalizer.CreateDefault(), TokenizerSetup.CreateDefault());
        var model = new VectorModel(tokenizer)
        {
            EnableDebugLogging = enableDebugLogging
        };

        model.IndexDocument(new Document(1L, "hello world"));
        model.IndexDocument(new Document(2L, "goodbye world"));
        model.BuildInvertedLists();
        model.BuildOptimizedIndexes();

        return model;
    }

    [TestMethod]
    public void SearchWithMaxScore_DebugLoggingDisabled_DoesNotWriteToConsole()
    {
        bool originalGlobalLogging = FusionScorer.EnableDebugLogging;
        FusionScorer.EnableDebugLogging = false;

        using var model = CreateModel(enableDebugLogging: false);
        using var writer = new StringWriter();
        TextWriter originalConsole = Console.Out;

        try
        {
            Console.SetOut(writer);
            _ = model.SearchWithMaxScore("hello", 10);
        }
        finally
        {
            Console.SetOut(originalConsole);
            FusionScorer.EnableDebugLogging = originalGlobalLogging;
        }

        Assert.AreEqual(string.Empty, writer.ToString());
    }

    [TestMethod]
    public void SearchWithMaxScore_DebugLoggingEnabled_WritesToConsole()
    {
        bool originalGlobalLogging = FusionScorer.EnableDebugLogging;
        FusionScorer.EnableDebugLogging = false;

        using var model = CreateModel(enableDebugLogging: true);
        using var writer = new StringWriter();
        TextWriter originalConsole = Console.Out;

        try
        {
            Console.SetOut(writer);
            _ = model.SearchWithMaxScore("hello", 10);
        }
        finally
        {
            Console.SetOut(originalConsole);
            FusionScorer.EnableDebugLogging = originalGlobalLogging;
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(writer.ToString()));
    }
}
