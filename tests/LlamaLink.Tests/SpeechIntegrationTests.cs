using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class SpeechIntegrationTests
{
    [TestMethod]
    public void BuildsPointerFreeRecorderCommand()
    {
        var args = SpeechCommandBuilder.BuildRecorderArguments("Microphone (USB)", @"C:\temp\speech.wav");

        CollectionAssert.Contains(args.ToArray(), "dshow");
        CollectionAssert.Contains(args.ToArray(), "audio=Microphone (USB)");
        Assert.AreEqual(@"C:\temp\speech.wav", args[^1]);
    }

    [TestMethod]
    public void ParsesWhisperTimestampedOutput()
    {
        var transcript = SpeechCommandBuilder.ParseWhisperTranscript(
            "[00:00:00.000 --> 00:00:01.200] hello\n[00:00:01.200 --> 00:00:02.500] world");

        Assert.AreEqual("hello world", transcript);
    }

    [TestMethod]
    public void BuildsWhisperAndPiperArguments()
    {
        CollectionAssert.AreEquivalent(
            new[] { "-m", "model.bin", "-f", "record.wav", "--no-timestamps", "--print-progress", "false" },
            SpeechCommandBuilder.BuildWhisperArguments("model.bin", "record.wav").ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "--model", "voice.onnx", "--output_file", "reply.wav" },
            SpeechCommandBuilder.BuildPiperArguments("voice.onnx", "reply.wav").ToArray());
    }
}
