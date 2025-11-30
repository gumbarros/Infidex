using Infidex.Core;
using Infidex.Scoring;
using Infidex.Coverage;
using Infidex.Tokenization;
using System.Reflection;

namespace Infidex.Tests;

[TestClass]
public class BugReproductionTests
{
    [TestMethod]
    public void PrefixPreference_MatrixRev_PreferRevisitedOverReloaded()
    {
        string query = "the matrix rev";
        string docReloaded = "The Matrix Reloaded";
        string docRevisited = "The Matrix Revisited";
        
        var tokenizer = new Tokenizer([3], 2, 0, TextNormalizer.CreateDefault(), TokenizerSetup.CreateDefault());
        var setup = CoverageSetup.CreateDefault();
        var engine = new CoverageEngine(tokenizer, setup);

        // IDFs observed: "the"~1.57, "matrix"~9.54, "rev"~9.51
        var idfCache = new Dictionary<string, float>
        {
            { "the", 1.574f },
            { "matrix", 9.544f },
            { "rev", 9.515f }
        };
        
        engine.SetWordIdfCache(idfCache);
        
        // Calculate features
        var featsReloaded = engine.CalculateFeatures(query, docReloaded, 0, 1);
        var featsRevisited = engine.CalculateFeatures(query, docRevisited, 0, 2);
        
        // Score
        var scoreReloaded = FusionScorer.Calculate(query, docReloaded, featsReloaded, 0.5f, 3, new[]{' '});
        var scoreRevisited = FusionScorer.Calculate(query, docRevisited, featsRevisited, 0.5f, 3, new[]{' '});
        
        Console.WriteLine($"Reloaded: {scoreReloaded.score}");
        Console.WriteLine($"Revisited: {scoreRevisited.score}");
        
        // Debug info
        Console.WriteLine("Reloaded Features:");
        if (featsReloaded.TermIdf != null) Console.WriteLine($"  IDFs: {string.Join(", ", featsReloaded.TermIdf)}");
        if (featsReloaded.TermCi != null) Console.WriteLine($"  Cis: {string.Join(", ", featsReloaded.TermCi)}");
        Console.WriteLine($"  AvgIDF: {featsReloaded.TotalIdf / featsReloaded.TermsCount}");
        
        Console.WriteLine("Revisited Features:");
        if (featsRevisited.TermIdf != null) Console.WriteLine($"  IDFs: {string.Join(", ", featsRevisited.TermIdf)}");
        if (featsRevisited.TermCi != null) Console.WriteLine($"  Cis: {string.Join(", ", featsRevisited.TermCi)}");
        Console.WriteLine($"  AvgIDF: {featsRevisited.TotalIdf / featsRevisited.TermsCount}");
        
        int scoreReloadedInt = (int)scoreReloaded.score;
        bool reloadedHasDominance = (scoreReloadedInt & 64) != 0;
        
        int scoreRevisitedInt = (int)scoreRevisited.score;
        bool revisitedHasDominance = (scoreRevisitedInt & 64) != 0;
        
        Console.WriteLine($"Reloaded Dominance: {reloadedHasDominance}");
        Console.WriteLine($"Revisited Dominance: {revisitedHasDominance}");
        
        Assert.IsTrue(scoreRevisited.score > scoreReloaded.score, 
            $"Revisited ({scoreRevisited.score}) should score higher than Reloaded ({scoreReloaded.score}). " +
            $"Currently failing due to Dominance Flip (Reloaded has dominance, Revisited does not).");
    }
    
}
