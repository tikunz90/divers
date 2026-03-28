using System;
using System.IO;
using System.Linq;

namespace CalibRecalibrator
{
    /// <summary>
    /// Parses the measured residual data file.
    /// </summary>
    public class MeasuredDataParser
    {
        public double Width { get; private set; }  // mm
        public double Height { get; private set; } // mm
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public double[,] XResiduals { get; private set; } = new double[0, 0];
        public double[,] YResiduals { get; private set; } = new double[0, 0];

        public void Parse(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            
            if (lines.Length < 2)
                throw new InvalidDataException("Measured data file must have at least 2 header lines");

            // Parse first line: X:<width_mm>;<cols>
            string[] xParts = lines[0].Split(new[] { ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (xParts.Length < 2 || xParts[0] != "X")
                throw new InvalidDataException($"Invalid X header format: {lines[0]}");
            
            Width = double.Parse(xParts[1]);
            Cols = int.Parse(xParts[2]);

            // Parse second line: Y:<height_mm>;<rows>
            string[] yParts = lines[1].Split(new[] { ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (yParts.Length < 2 || yParts[0] != "Y")
                throw new InvalidDataException($"Invalid Y header format: {lines[1]}");
            
            Height = double.Parse(yParts[1]);
            Rows = int.Parse(yParts[2]);

            XResiduals = new double[Rows, Cols];
            YResiduals = new double[Rows, Cols];

            // Parse data points in row-major order
            int expectedPoints = Rows * Cols;
            int actualPoints = lines.Length - 2;
            
            if (actualPoints < expectedPoints)
                throw new InvalidDataException($"Expected {expectedPoints} data points, found {actualPoints}");

            for (int i = 0; i < expectedPoints; i++)
            {
                string line = lines[i + 2].Trim();
                
                // Remove parentheses and split by semicolon
                line = line.TrimStart('(').TrimEnd(new[] { ')', ';' });
                string[] parts = line.Split(';');
                
                if (parts.Length < 3)
                    throw new InvalidDataException($"Invalid data point format at line {i + 3}: {lines[i + 2]}");

                double xRes = double.Parse(parts[0]);
                double yRes = double.Parse(parts[1]);
                // Z component is in parts[2] but we don't use it

                // Convert linear index to row, col
                int row = i / Cols;
                int col = i % Cols;

                XResiduals[row, col] = xRes;
                YResiduals[row, col] = yRes;
            }
        }

        public (double x, double y) GetGridPoint(int row, int col)
        {
            double px = Width / (Cols - 1);
            double py = Height / (Rows - 1);
            
            double x = -Width / 2.0 + col * px;
            double y = -Height / 2.0 + row * py;
            
            return (x, y);
        }
    }
}
