# Calibration Recalibrator Tool

A C# console application that recalibrates a 33x33 XY compensation table using measured residual grid data.

## Overview

This tool reads a baseline calibration file containing 33x33 XY compensation tables and a measured residual data file, then:
1. Interpolates the measured residuals onto the fixed 33x33 grid using bilinear interpolation
2. Converts residual values from mm to integer counts (scale: 1220 nm per count)
3. Updates the baseline calibration with the residuals
4. Outputs a new calibration file while preserving all other sections (Z, P, coupling matrices, checksums, etc.)

## Building

```bash
cd tools/CalibRecalibrator
dotnet build
```

## Usage

### Basic usage (with default file names):
```bash
dotnet run
```

This assumes `base_calib.txt` and `measured_data.txt` are in the current directory and will create `base_calib_updated.txt`.

### With custom file paths:
```bash
dotnet run -- <baseCalibPath> <measuredDataPath> <outputPath>
```

Example:
```bash
dotnet run -- /path/to/base_calib.txt /path/to/measured_data.txt /path/to/output.txt
```

### From repository root:
```bash
cd /home/runner/work/divers/divers
dotnet run --project tools/CalibRecalibrator/CalibRecalibrator.csproj -- base_calib.txt measured_data.txt base_calib_updated.txt
```

### Show help:
```bash
dotnet run -- --help
```

## Input File Formats

### Base Calibration File (`base_calib.txt`)
- Contains sections including `X at Z=0` and `Y at Z=0`
- Each section followed by `Scale: x 1220 nm`
- Then 33 lines of 33 tab-separated integers
- Other sections (Z, P, etc.) are preserved as-is

### Measured Residual Data File (`measured_data.txt`)
Format:
```
X:<width_mm>;<cols>
Y:<height_mm>;<rows>
(<Xmm>;<Ymm>;<Zmm>);
(<Xmm>;<Ymm>;<Zmm>);
...
```
- First line: `X:<width_mm>;<cols>` - Grid width in mm and number of columns
- Second line: `Y:<height_mm>;<rows>` - Grid height in mm and number of rows
- Subsequent lines: Data points in row-major order, first row at y = -height/2

## Technical Details

### Coordinate Conventions

**Fixed Grid (33x33):**
- Pitch: 10 mm
- Centered origin
- Indices u,v = 0..32 map to x = (u-16)*10, y = (v-16)*10 mm

**Measured Grid:**
- Width W, Cols C; Height H, Rows R
- Pitch: px = W/(C-1), py = H/(R-1)
- Column c = 0..C-1 maps to x = -W/2 + c*px
- Row r = 0..R-1 maps to y = -H/2 + r*py
- Data is row-major with first row at y = -H/2

### Interpolation
- Uses bilinear interpolation for points within the measured grid rectangle [-W/2, +W/2] × [-H/2, +H/2]
- Points outside this rectangle get residual delta = 0 (no change)

### Unit Conversion
- Measured residuals are in mm
- Baseline values are integer counts with scale 1220 nm per count
- Conversion: deltaCounts = round(deltaMm × 819.672131147541)
- Uses MidpointRounding.AwayFromZero for halfway cases

### Update Formula
```
NewX = BaseX + deltaCountsX
NewY = BaseY + deltaCountsY
```

## Validation

The tool validates:
- Input files exist
- Base calibration file contains valid X and Y 33x33 tables
- Measured data file has correct format
- Number of data points matches declared grid dimensions (rows × cols)

## Example Output

```
Calibration Recalibrator Tool
==============================

Base calibration file: base_calib.txt
Measured data file:    measured_data.txt
Output file:           base_calib_updated.txt

Parsing base calibration file...
  ✓ Loaded X table (33x33)
  ✓ Loaded Y table (33x33)
Parsing measured data file...
  ✓ Grid dimensions: 300 mm x 300 mm
  ✓ Grid resolution: 21 x 21 points
  ✓ Validated 441 data points
Interpolating residuals onto 33x33 grid...
  ✓ Applied residuals to X and Y tables
Writing updated calibration to base_calib_updated.txt...
  ✓ File written successfully

Recalibration complete!
```

## Project Structure

```
CalibRecalibrator/
├── CalibRecalibrator.csproj    # Project file
├── Program.cs                  # Main entry point and CLI
├── BaseCalibParser.cs          # Parses base calibration file
├── MeasuredDataParser.cs       # Parses measured residual data
├── BilinearInterpolator.cs     # Bilinear interpolation logic
├── CalibUpdater.cs             # Applies residuals and updates tables
└── README.md                   # This file
```

## Error Handling

The tool will exit with an error message if:
- Input files are not found
- File formats are invalid
- Data dimensions don't match
- Parsing fails for any reason

All errors are displayed to the console with descriptive messages.
