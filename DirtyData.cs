using System.Globalization;

namespace ValloxLogs;

public class DirtyData
{
    public string LogItemId;
    public string Value;

    public DateTime Date;

    public DirtyData(byte[] bytes)
    {
        if (bytes[0] == 255)
            return;

        switch (bytes[0])
        {
            case 0:
                LogItemId = "ExtractAirTemp";
                break;

            case 1:
                LogItemId = "ExaustAirTemp";
                break;

            case 2:
                LogItemId = "OutdoorAirTemp";
                break;

            case 3:
                LogItemId = "SupplyAirTemp";
                break;

            case 4:
                LogItemId = "CO2";
                break;

            case 5:
                LogItemId = "Humidity";
                break;

            default:
                LogItemId = "-";
                break;
        }

        Date = new DateTime(2000 + bytes[5], bytes[4], bytes[3], bytes[2], bytes[1], 0);
        double ValueTemp = (short)(bytes[7] << 8 | bytes[6]);
        Value = LogItemId.Contains("Temp")
            ? Math.Round(ValueTemp / 100.0 - 273.15, 1).ToString(CultureInfo.InvariantCulture)
            : ValueTemp.ToString(CultureInfo.InvariantCulture);
        Value = Value.Replace(".", ",");

    }

}