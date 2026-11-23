namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusWinUsbPipeLayoutTests
{
    private const byte TasollerPlusInterfaceNumber = 0;
    private const byte OtherInterfaceNumber = 1;
    private const byte InputPipeId = 0x84;
    private const byte LedOutputPipeId = 0x03;
    private const byte UnrelatedInputPipeId = 0x81;
    private const byte ReversedInputPipeId = 0x04;
    private const byte ReversedLedOutputPipeId = 0x83;
    private const int BulkPipeType = 2;
    private const int InterruptPipeType = 3;
    private const byte FirstPipeIndex = 0;
    private const byte TwoPipes = 2;

    [TestMethod]
    public void AcceptsTheRequiredPipesAlongsideAnUnrelatedPipe()
    {
        var supported = IsSupported(TasollerPlusInterfaceNumber, (LedOutputPipeId, BulkPipeType), (UnrelatedInputPipeId, BulkPipeType), (InputPipeId, InterruptPipeType));

        Assert.IsTrue(supported);
    }

    [TestMethod]
    [DataRow(ReversedInputPipeId, InterruptPipeType, LedOutputPipeId, BulkPipeType)]
    [DataRow(InputPipeId, BulkPipeType, LedOutputPipeId, BulkPipeType)]
    [DataRow(InputPipeId, InterruptPipeType, ReversedLedOutputPipeId, BulkPipeType)]
    [DataRow(InputPipeId, InterruptPipeType, LedOutputPipeId, InterruptPipeType)]
    public void RejectsAnIncorrectPipeAddressOrTransferType(byte inputId, int inputType, byte outputId, int outputType)
    {
        var supported = IsSupported(TasollerPlusInterfaceNumber, (inputId, inputType), (outputId, outputType));

        Assert.IsFalse(supported);
    }

    [TestMethod]
    public void RejectsTheRequiredPipesOnAnotherInterface()
    {
        var supported = IsSupported(OtherInterfaceNumber, (InputPipeId, InterruptPipeType), (LedOutputPipeId, BulkPipeType));

        Assert.IsFalse(supported);
    }

    [TestMethod]
    public void RejectsTheInterfaceWhenTheInputPipeCannotBeQueried()
    {
        var supported = TasollerPlusWinUsbPipeLayout.IsSupported(TasollerPlusInterfaceNumber, TwoPipes, index => index == FirstPipeIndex ? null : (LedOutputPipeId, BulkPipeType));

        Assert.IsFalse(supported);
    }

    private static bool IsSupported(byte interfaceNumber, params (byte PipeId, int PipeType)[] pipes)
    {
        return TasollerPlusWinUsbPipeLayout.IsSupported(interfaceNumber, (byte)pipes.Length, index => pipes[index]);
    }
}
