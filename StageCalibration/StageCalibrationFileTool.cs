using System;
using System.Collections.Generic;
using System.IO;

namespace StageCalibration
{
    public class StageCalibrationFileTool
    {
        // Load calibration points from a file
        public List<(double X, double Y, double Z)> LoadCalibrationPoints(string filePath)
        {
            var points = new List<(double, double, double)>();
            foreach (var line in File.ReadLines(filePath))
            {
                var parts = line.Split('\t'); // Assuming tab-separated values
                if (parts.Length == 3 && 
                    double.TryParse(parts[0], out var x) && 
                    double.TryParse(parts[1], out var y) && 
                    double.TryParse(parts[2], out var z))
                {
                    points.Add((x, y, z));
                }
            }
            return points;
        }

        // Expand/extrapolate calibration points
        public List<(double X, double Y, double Z)> ExpandGrid(List<(double X, double Y, double Z)> originalPoints, double expansionFactor)
        {
            var expandedPoints = new List<(double, double, double)>();
            // Placeholder for your expansion logic
            foreach (var point in originalPoints)
            {
                expandedPoints.Add((point.X * expansionFactor, point.Y * expansionFactor, point.Z));
            }
            return expandedPoints;
        }

        // Save the calibration points back to the file
        public void SaveCalibrationPoints(string filePath, List<(double X, double Y, double Z)> points)
        {
            using (var writer = new StreamWriter(filePath))
            {
                foreach (var point in points)
                {
                    writer.WriteLine($"{point.X}\t{point.Y}\t{point.Z}");
                }
            }
        }
    }
}