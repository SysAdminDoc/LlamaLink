using System.Threading.Tasks;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class WebSearchTests
{
    [TestMethod]
    public void ParsesDuckDuckGoAbstractAndRelatedTopics()
    {
        var json = """
            {
              "AbstractText": "A concise answer",
              "AbstractURL": "https://example.test/answer",
              "RelatedTopics": [
                {"Text": "Related result", "FirstURL": "https://example.test/related"}
              ]
            }
            """;

        var results = WebSearchService.ParseDuckDuckGo(json, 5);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("DuckDuckGo", results[0].Title);
        Assert.AreEqual("https://example.test/related", results[1].Url);
    }

    [TestMethod]
    public void ParsesSearxSnippetsAndClampsResults()
    {
        var json = """
            {
              "results": [
                {"title":"First", "url":"https://example.test/1", "content":"<b>Useful</b> excerpt"},
                {"title":"Second", "url":"https://example.test/2", "content":"Another excerpt"}
              ]
            }
            """;

        var results = WebSearchService.ParseSearx(json, 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Useful excerpt", results[0].Snippet);
    }

    [TestMethod]
    public async Task DisabledSearchDoesNotMakeNetworkRequests()
    {
        var result = await WebSearchService.SearchAsync(
            "ignored",
            new WebSearchOptions(false, "duckduckgo", ""));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Content, "not enabled");
    }
}
