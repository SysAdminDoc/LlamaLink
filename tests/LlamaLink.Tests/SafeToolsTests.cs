using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class SafeToolsTests
{
    [TestMethod]
    public void EvaluatesOnlySupportedArithmetic()
    {
        Assert.AreEqual(14, SafeCalculator.Evaluate("2 + 3 * 4"), 0.0001);
        Assert.AreEqual(512, SafeCalculator.Evaluate("2 ^ 3 ^ 2"), 0.0001);
        Assert.ThrowsException<FormatException>(() => SafeCalculator.Evaluate("2 + foo"));
    }

    [TestMethod]
    public async Task RestrictsFileReadToConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LlamaLinkSafeTools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "note.txt"), "safe content");
            var allowed = await SafeToolExecutor.ExecuteAsync(
                new ToolCallRequest("1", "read_file", "{\"path\":\"note.txt\"}"), root);
            var denied = await SafeToolExecutor.ExecuteAsync(
                new ToolCallRequest("2", "read_file", """{"path":"..\\outside.txt"}"""), root);

            Assert.IsTrue(allowed.Success);
            Assert.AreEqual("safe content", allowed.Content);
            Assert.IsFalse(denied.Success);
            StringAssert.Contains(denied.Content, "outside");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AccumulatesStreamedToolCallFragments()
    {
        var accumulator = new ToolCallAccumulator();
        accumulator.Add(new[]
        {
            new BackendToolCallFragment("0", "call-1", "calcul", "{\"expression\":"),
            new BackendToolCallFragment("0", "", "ator", "\"2+2\"}"),
        });

        var calls = accumulator.Complete();

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual("calculator", calls[0].Name);
        StringAssert.Contains(calls[0].ArgumentsJson, "2+2");
    }

    [TestMethod]
    public void ExposesOnlyOptedInToolDefinitions()
    {
        var definitions = SafeToolRegistry.GetDefinitions(fileRead: true, calculator: false, pythonEvaluation: true);

        CollectionAssert.AreEquivalent(new[] { "read_file", "python_eval" }, definitions.Select(d => d.Name).ToArray());
    }

    [TestMethod]
    public async Task PythonToolRunsOnlyNumericExpressions()
    {
        var allowed = await SafeToolExecutor.ExecuteAsync(
            new ToolCallRequest("1", "python_eval", "{\"code\":\"2 + 3 * 4\"}"),
            Path.GetTempPath());
        var denied = await SafeToolExecutor.ExecuteAsync(
            new ToolCallRequest("2", "python_eval", "{\"code\":\"__import__('os')\"}"),
            Path.GetTempPath());

        Assert.IsTrue(allowed.Success);
        Assert.AreEqual("14", allowed.Content);
        Assert.IsFalse(denied.Success);
    }
}
