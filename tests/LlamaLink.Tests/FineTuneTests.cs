using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public class FineTuneTests
{
    [TestMethod]
    public void BuildArguments_UsesLlamaCppLoraOptions()
    {
        var settings = new FineTuneSettings(
            "finetune.exe",
            "base model.gguf",
            "train data.txt",
            "adapter.bin",
            ContextSize: 1024,
            BatchSize: 4,
            MicroBatchSize: 2,
            AdamIterations: 100,
            Threads: 8);

        var arguments = FineTuneCommandBuilder.BuildArguments(settings);

        CollectionAssert.AreEqual(
            new[]
            {
                "--model-base", "base model.gguf",
                "--train-data", "train data.txt",
                "--lora-out", "adapter.bin",
                "--ctx", "1024",
                "--batch", "4",
                "--ubatch", "2",
                "--adam-iter", "100",
                "--threads", "8",
            },
            arguments.ToArray());
    }

    [TestMethod]
    public void BuildArguments_ClampsUnsafeTrainingValues()
    {
        var settings = new FineTuneSettings(
            "finetune.exe", "base.gguf", "train.txt", "adapter.bin",
            ContextSize: 1, BatchSize: 0, MicroBatchSize: -1, AdamIterations: 2_000_000, Threads: 0);

        var arguments = FineTuneCommandBuilder.BuildArguments(settings);

        CollectionAssert.AreEqual(
            new[] { "64", "1", "1", "1000000", "1" },
            new[] { arguments[7], arguments[9], arguments[11], arguments[13], arguments[15] });
    }

    [TestMethod]
    public async Task RunAsync_RejectsMissingInputsBeforeLaunching()
    {
        var result = await FineTuneRunner.RunAsync(new FineTuneSettings(
            "missing-finetune.exe", "missing-model.gguf", "missing-data.txt", "adapter.bin"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "executable");
    }

    [TestMethod]
    public async Task RunAsync_RejectsOverwritingBaseModel()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "LlamaLink-FineTune-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var executable = Path.Combine(root, "finetune.exe");
            var model = Path.Combine(root, "base.gguf");
            var data = Path.Combine(root, "train.txt");
            File.WriteAllBytes(executable, new byte[] { 0 });
            File.WriteAllBytes(model, new byte[] { 1 });
            File.WriteAllText(data, "example");

            var result = await FineTuneRunner.RunAsync(new FineTuneSettings(
                executable, model, data, model));

            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Message, "overwrite");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
