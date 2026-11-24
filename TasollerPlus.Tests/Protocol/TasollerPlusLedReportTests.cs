using TasollerPlus.Protocol;

namespace TasollerPlus.Tests.Protocol;

[TestClass]
public sealed class TasollerPlusLedReportTests
{
    private const int SliderColorCount = 31;
    private const int AirTowerColorCount = 3;
    private const byte FirstSliderChannel = 0x01;
    private const byte FirstLeftAirChannel = 0x5E;
    private const byte FirstRightAirChannel = 0x67;
    private const string ExpectedHeaderHex = "444C02";
    private const string ExpectedSliderHex = "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F404142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D";
    private const string ExpectedLeftAirHex = "5E5F60616263646566";
    private const string ExpectedRightAirHex = "6768696A6B6C6D6E6F";

    [TestMethod]
    public void EncodeWritesSliderAndAirTowerColorsInLedReportOrder()
    {
        var slider = CreateColors(SliderColorCount, FirstSliderChannel);
        var leftAir = CreateColors(AirTowerColorCount, FirstLeftAirChannel);
        var rightAir = CreateColors(AirTowerColorCount, FirstRightAirChannel);
        var expectedReport = Convert.FromHexString(ExpectedHeaderHex + ExpectedSliderHex + ExpectedLeftAirHex + ExpectedRightAirHex);

        var actualReport = TasollerPlusLedReport.Encode(slider, leftAir, rightAir);

        CollectionAssert.AreEqual(expectedReport, actualReport);
    }

    private static RgbColor[] CreateColors(int count, byte firstChannel)
    {
        var colors = new RgbColor[count];
        var channel = firstChannel;
        for (var index = 0; index < colors.Length; index++)
        {
            var red = channel++;
            var green = channel++;
            var blue = channel++;
            colors[index] = new RgbColor(red, green, blue);
        }

        return colors;
    }
}
