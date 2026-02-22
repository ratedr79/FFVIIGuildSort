# FFVII Ever Crisis Power Level Analyzer

A C# .NET web application that processes CSV files containing player data from Final Fantasy VII: Ever Crisis to calculate and analyze power levels.

## Features

- **CSV Upload & Processing**: Upload player data CSV files for automatic power level calculation
- **Power Level Calculation**: Advanced algorithm that considers multiple stats and equipment levels
- **Player Ranking**: Automatic ranking of players by calculated power levels
- **Analysis Dashboard**: Comprehensive statistics including power distribution and top players
- **Export Results**: Download processed data with calculated power levels
- **Sample Data**: Download sample CSV format for reference

## Power Level Formula

The power level calculation uses a weighted formula considering:

### Base Stats (weighted)
- HP: 0.1x
- Attack: 2.0x
- Defense: 1.5x
- Magic: 2.2x
- Magic Defense: 1.5x
- Speed: 1.8x
- Critical Rate: 0.05x
- Evasion: 0.03x

### Equipment Multipliers
- Weapon Level: +10% per level
- Armor Level: +8% per level
- Accessory Level: +5% per level

### Ability Bonuses
- Ability Level: +50 points per level
- Limit Break Level: +100 points per level
- Overlord Level: +200 points per level

### Level Scaling
- Base multiplier: 1.0 + (Level × 0.05)

## Data Files

This app is designed around the monthly guild form export (wide format) and a weapon metadata reference file.

### gb20.csv

- Must contain the column **In-Game Name** (this is the stable key).
- All other columns may change month-to-month.
- Columns that match a weapon name in `weaponData.tsv` are treated as weapons.
- Weapon cell values are expected to be:
  - `Do Not Own`
  - `5 Star` (treated as OB0)
  - `OB1` .. `OB10`

### weaponData.tsv

`data/weaponData.tsv` provides weapon-to-character mapping and identifies ultimate weapons via `GachaType=Ultimate`.

## Power Tiers

- **S-Tier**: 90,000+ power level
- **A-Tier**: 70,000 - 89,999 power level
- **B-Tier**: 50,000 - 69,999 power level
- **C-Tier**: 30,000 - 49,999 power level
- **D-Tier**: Below 30,000 power level

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

### Installation

1. Clone the repository
2. Navigate to the project directory
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Run the application:
   ```bash
   dotnet run
   ```

5. Open your browser and navigate to `https://localhost:7xxx` (port will be shown in console)

### Usage

1. **Download Sample CSV**: Click "Download Sample CSV" to see the expected format
2. **Prepare Your Data**: Create a CSV file with your player data following the format
3. **Upload File**: Select and upload your CSV file
4. **View Results**: Analyze the calculated power levels, rankings, and statistics
5. **Export Results**: Download the processed data with power levels included

## Project Structure

```
FFVIIEverCrisisAnalyzer/
├── Models/
│   └── PlayerData.cs           # Player data model
├── Services/
│   ├── PowerLevelCalculator.cs # Power level calculation logic
│   └── CsvProcessor.cs         # CSV processing and export
├── Pages/
│   ├── Index.cshtml           # Main web interface
│   └── Index.cshtml.cs        # Page model with handlers
├── wwwroot/
│   └── css/
│       └── site.css           # Custom styling
└── Program.cs                 # Application configuration
```

## Technologies Used

- **ASP.NET Core 8.0**: Web framework
- **Razor Pages**: UI framework
- **CsvHelper**: CSV processing library
- **Bootstrap 5**: UI framework
- **Font Awesome**: Icons

## License

This project is for educational and personal use only. Not affiliated with Square Enix or Final Fantasy VII: Ever Crisis.
