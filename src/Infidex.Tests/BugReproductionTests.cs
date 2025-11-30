using Infidex.Core;
using Infidex.Scoring;
using Infidex.Coverage;
using Infidex.Tokenization;

namespace Infidex.Tests;

[TestClass]
public class BugReproductionTests
{
    [TestMethod]
    public void PrefixPreference_MatrixRev_PreferRevisitedOverReloaded()
    {
        // "the matrix rev"
        // "The Matrix Reloaded" vs "The Matrix Revisited"
        // "Revisited" starts with "Rev". "Reloaded" does not.
        
        string query = "the matrix rev";
        string docReloaded = "The Matrix Reloaded";
        string docRevisited = "The Matrix Revisited";
        
        var tokenizer = new Tokenizer([3], 2, 0, TextNormalizer.CreateDefault(), TokenizerSetup.CreateDefault());
        var setup = CoverageSetup.CreateDefault();
        var engine = new CoverageEngine(tokenizer, setup);
        
        // Calculate features for Reloaded
        var featsReloaded = engine.CalculateFeatures(query, docReloaded, 0, 1);
        
        // Calculate features for Revisited
        var featsRevisited = engine.CalculateFeatures(query, docRevisited, 0, 2);
        
        // Score
        var scoreReloaded = FusionScorer.Calculate(query, docReloaded, featsReloaded, 0.5f, 3, new[]{' '});
        var scoreRevisited = FusionScorer.Calculate(query, docRevisited, featsRevisited, 0.5f, 3, new[]{' '});
        
        Console.WriteLine($"Reloaded: {scoreReloaded.score}");
        Console.WriteLine($"Revisited: {scoreRevisited.score}");
        
        Assert.IsTrue(scoreRevisited.score > scoreReloaded.score, 
            $"Revisited ({scoreRevisited.score}) should score higher than Reloaded ({scoreReloaded.score})");
            
        // Check why
        Assert.IsTrue(featsRevisited.FusionSignals.LexicalPrefixLast, "Revisited should have LexicalPrefixLast");
        Assert.IsFalse(featsReloaded.FusionSignals.LexicalPrefixLast, "Reloaded should NOT have LexicalPrefixLast");
    }
}
