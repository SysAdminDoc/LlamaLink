using System.Linq;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class GrammarBuilderTests
{
    [TestMethod]
    public void ParsesAndBuildsRulesWithoutLosingDefinitions()
    {
        var rules = GrammarBuilder.Parse("# comment\nroot ::= value\nvalue ::= \"ok\"");

        Assert.AreEqual(2, rules.Count);
        Assert.AreEqual("root ::= value\nvalue ::= \"ok\"", GrammarBuilder.Build(rules.ToArray()).Replace("\r\n", "\n"));
    }

    [TestMethod]
    public void RejectsDuplicateRulesAndMissingRoot()
    {
        Assert.ThrowsException<System.ArgumentException>(() => GrammarBuilder.Build(new[]
        {
            new GrammarBuilderRule("value", "\"ok\""),
        }));
        Assert.ThrowsException<System.ArgumentException>(() => GrammarBuilder.Build(new[]
        {
            new GrammarBuilderRule("root", "value"),
            new GrammarBuilderRule("root", "value"),
        }));
    }
}
