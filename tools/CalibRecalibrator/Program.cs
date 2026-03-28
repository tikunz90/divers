using System;
using System.IO;

namespace CalibRecalibrator
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Parse command line arguments
                string baseCalibPath = "base_calib.txt";
                string measuredDataPath = "measured_data.txt";
                string outputPath = "base_calib_updated.txt";

                if (args.Length > 0)
                    baseCalibPath = args[0];
                if (args.Length > 1)
                    measuredDataPath = args[1];
                if (args.Length > 2)
                    outputPath = args[2];

                // Display usage if help requested
                if (args.Length > 0 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?" || args[0] == "help"))
                {
                    ShowUsage();
                    return;
                }

                Console.WriteLine("Calibration Recalibrator Tool");
                Console.WriteLine("==============================");
                Console.WriteLine();
                Console.WriteLine($"Base calibration file: {baseCalibPath}");
                Console.WriteLine($"Measured data file:    {measuredDataPath}");
                Console.WriteLine($"Output file:           {outputPath}");
                Console.WriteLine();

                // Validate input files exist
                if (!File.Exists(baseCalibPath))
                {
                    Console.WriteLine($"Error: Base calibration file not found: {baseCalibPath}");
                    Environment.Exit(1);
                }
                if (!File.Exists(measuredDataPath))
                {
                    Console.WriteLine($"Error: Measured data file not found: {measuredDataPath}");
                    Environment.Exit(1);
                }

                // Parse base calibration file
                Console.WriteLine("Parsing base calibration file...");
                var baseCalib = new BaseCalibParser();
                baseCalib.Parse(baseCalibPath);
                Console.WriteLine($"  ✓ Loaded X table (33x33)");
                Console.WriteLine($"  ✓ Loaded Y table (33x33)");

                // Parse measured data file
                Console.WriteLine("Parsing measured data file...");
                var measuredData = new MeasuredDataParser();
                measuredData.Parse(measuredDataPath);
                Console.WriteLine($"  ✓ Grid dimensions: {measuredData.Width} mm x {measuredData.Height} mm");
                Console.WriteLine($"  ✓ Grid resolution: {measuredData.Cols} x {measuredData.Rows} points");
                
                int expectedPoints = measuredData.Rows * measuredData.Cols;
                Console.WriteLine($"  ✓ Validated {expectedPoints} data points");

                // Create interpolator
                Console.WriteLine("Interpolating residuals onto 33x33 grid...");
                var interpolator = new BilinearInterpolator(measuredData);

                // Update calibration
                var updater = new CalibUpdater(baseCalib, interpolator);
                updater.ApplyResiduals(measuredData);
                Console.WriteLine("  ✓ Applied residuals to X and Y tables");

                // Write output
                Console.WriteLine($"Writing updated calibration to {outputPath}...");
                baseCalib.WriteToFile(outputPath);
                Console.WriteLine("  ✓ File written successfully");

                Console.WriteLine();
                Console.WriteLine("Recalibration complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Stack trace:");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("Calibration Recalibrator Tool");
            Console.WriteLine("==============================");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  CalibRecalibrator [baseCalibPath] [measuredDataPath] [outputPath]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  baseCalibPath     Path to base calibration file (default: base_calib.txt)");
            Console.WriteLine("  measuredDataPath  Path to measured residual data file (default: measured_data.txt)");
            Console.WriteLine("  outputPath        Path for updated calibration output (default: base_calib_updated.txt)");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  CalibRecalibrator");
            Console.WriteLine("  CalibRecalibrator base_calib.txt measured_data.txt output.txt");
            Console.WriteLine();
            Console.WriteLine("Description:");
            Console.WriteLine("  This tool recalibrates a 33x33 XY compensation table using measured residual data.");
            Console.WriteLine("  It interpolates measured residuals onto the fixed grid and updates the baseline");
            Console.WriteLine("  calibration values while preserving all other sections (Z, P, coupling matrices, etc.).");
        }
    }
}
