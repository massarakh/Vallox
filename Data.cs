using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ValloxLogs;

public class Data
{
    private int _id;
    [Key]
    public int ID
    {
        get { return _id; }
    }
    [Column("DateTime")]
    public DateTime Date { get; set; }
    public decimal ExtractAirTemp { get; set; }
    public decimal ExaustAirTemp { get; set; }
    public decimal OutdoorAirTemp { get; set; }
    public decimal SupplyAirTemp { get; set; }
    public int CO2 { get; set; }
    public int Humidity { get; set; }
}